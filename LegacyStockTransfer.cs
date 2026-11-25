using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.DataStructures;

namespace AEBeyondDimensions;

// Keep a recoverable copy on the character until a loaded world save confirms
// that the destination inventory and its receipt were persisted together.
public sealed class LegacyStockTransfer : ModPlayer
{
    private Guid batch,world;
    private string destination="";
    private List<TagCompound> items=new();
    private bool checkpointed;
    private const int Chunk=24000, Limit=64*1024*1024;
    private sealed record Upload(Guid Batch,int Token,int Total,MemoryStream Bytes);
    private static readonly Dictionary<int,Upload> uploads=new();
    private static readonly HashSet<string> receipts=new(),onDisk=new();
    internal static readonly object WorldGate=new();
    internal bool HasWork=>batch!=Guid.Empty || Player.GetModPlayer<TerminalStorage>().CharacterSlots.Any(i=>!i.IsAir);
    private static string Receipt(Guid owner,Guid batch)=>owner.ToString("N")+"/"+batch.ToString("N");
    internal static void Forget(int who)=>uploads.Remove(who);
    internal static void Clear(){uploads.Clear();receipts.Clear();onDisk.Clear();}
    internal static void SaveWorld(TagCompound tag){
        if(receipts.Count>0)tag["inventoryImports"]=receipts.ToList();
    }
    internal static void LoadWorld(TagCompound tag){
        onDisk.UnionWith(tag.GetList<string>("inventoryImports"));receipts.UnionWith(onDisk);
    }
    public override void SaveData(TagCompound tag){
        if(batch==Guid.Empty)return;
        tag["batch"]=batch.ToString("N");tag["world"]=world.ToString("N");tag["destination"]=destination;tag["items"]=items;
    }
    public override void LoadData(TagCompound tag){
        if(!Guid.TryParse(tag.GetString("batch"),out batch))return;
        world=Guid.Parse(tag.GetString("world"));destination=tag.GetString("destination");
        items=tag.GetList<TagCompound>("items").ToList();checkpointed=true;
    }
    private bool Prepare(Guid currentWorld,string key){
        if(batch==Guid.Empty){
            var old=Player.GetModPlayer<TerminalStorage>().CharacterSlots;
            var saved=old.Where(i=>!i.IsAir).Select(ItemIO.Save).ToList();
            if(saved.Count==0)return false;
            if(Main.ServerSideCharacter || Main.ActivePlayerFileData?.Player!=Player)return false;
            items=saved;world=currentWorld;destination=key;batch=Guid.NewGuid();checkpointed=false;
            for(int i=0;i<old.Length;i++)old[i]=new Item();
        }
        if(world!=currentWorld)return false;
        if(!checkpointed){
            Terraria.Player.SavePlayer(Main.ActivePlayerFileData);checkpointed=true;
        }
        return true;
    }
    private bool Matches(string key){
        if(key==destination)return true;
        var parts=destination.Split('/');
        if(parts.Length!=3 || parts[0]!="controller" || !short.TryParse(parts[1],out var x) || !short.TryParse(parts[2],out var y))return false;
        byte team=ControllerBinding.ColorAt(new Point16(x,y));
        return team>0 && key=="team/"+team;
    }
    private void Complete(){batch=Guid.Empty;items.Clear();checkpointed=false;}
    private static void Commit(string receipt,TeamBank bank,IList<TagCompound> rows){
        // Append complete instances. The normal grid already groups compatible items.
        // Build the entire list before mutating the bank, including modded item data.
        var copies=rows.Select(ItemIO.Load).ToList();
        if(copies.Exists(i=>i.IsAir))throw new InvalidDataException("Empty item in legacy terminal inventory.");
        bank.Stock.AddRange(copies);receipts.Add(receipt);
    }
    internal bool MergeSingle(string key,TeamBank bank){
        if(!Prepare(Main.ActiveWorldFileData.UniqueId,key))return false;
        string receipt=Receipt(Player.GetModPlayer<TeamIdentity>().Key,batch);
        if(onDisk.Contains(receipt)){Complete();return false;}
        if(receipts.Contains(receipt) || !Matches(key))return false;
        Commit(receipt,bank,items);return true;
    }
    internal static void Offer(int who,int token,string key){
        var p=TeamNetwork.Packet(TeamOp.ImportOffer,token);
        p.Write(Main.ActiveWorldFileData.UniqueId.ToByteArray());p.Write(key);p.Send(who);
    }
    internal void Client(TeamOp op,int token,BinaryReader r){
        Guid id=new(r.ReadBytes(16));
        if(op==TeamOp.ImportReceipt){
            bool persisted=r.ReadBoolean();
            if(id==batch && persisted)Complete();
            return;
        }
        string key=r.ReadString();
        if(!TeamClient.InSession(token))return;
        if(op==TeamOp.ImportOffer){
            if(!Prepare(id,key))return;
            var begin=TeamNetwork.Packet(TeamOp.ImportBegin,token);begin.Write(batch.ToByteArray());begin.Send();return;
        }
        if(id!=batch || !Matches(key))return;
        using var bytes=new MemoryStream();
        TagIO.ToStream(new TagCompound{["items"]=items},bytes,false);
        int total=checked((int)bytes.Length);
        if(total>Limit){Mod.Logger.Warn("Legacy inventory exceeds the network transfer limit. Character backup retained.");return;}
        for(int start=0;start<total;start+=Chunk){
            int count=Math.Min(Chunk,total-start);
            var msg=TeamNetwork.Packet(TeamOp.ImportPart,token);
            msg.Write(batch.ToByteArray());msg.Write(total);msg.Write(start);
            msg.Write((ushort)count);msg.Write(bytes.GetBuffer(),start,count);msg.Send();
        }
    }
    internal static void Server(TeamOp op,int who,int token,Guid owner,string key,TeamBank bank,BinaryReader r){
        Guid id=new(r.ReadBytes(16));string receipt=Receipt(owner,id);
        if(op==TeamOp.ImportBegin){
            Forget(who);
            if(bank==null || id==Guid.Empty)return;
            if(receipts.Contains(receipt)){Ack();return;}
            uploads[who]=new(id,token,0,new());
            var request=TeamNetwork.Packet(TeamOp.ImportRequest,token);request.Write(id.ToByteArray());request.Write(key);request.Send(who);
            return;
        }
        int total=r.ReadInt32(),offset=r.ReadInt32(),count=r.ReadUInt16();
        byte[] data=r.ReadBytes(count);
        if(bank==null || !uploads.TryGetValue(who,out var u) || u.Token!=token || u.Batch!=id)return;
        if(total<=0 || total>Limit || (u.Total!=0 && u.Total!=total) || count==0 || count>Chunk ||
           data.Length!=count || offset!=u.Bytes.Length || offset>total-count){Forget(who);return;}
        if(u.Total==0)uploads[who]=u=u with{Total=total};
        u.Bytes.Write(data);
        if(u.Bytes.Length!=total)return;
        Forget(who);
        lock(WorldGate)if(!receipts.Contains(receipt)){
            u.Bytes.Position=0;
            if(!ValidTag(u.Bytes.GetBuffer(),total))return;
            var rows=TagIO.FromStream(u.Bytes,false).GetList<TagCompound>("items");
            if(rows.Count==0)return;
            Commit(receipt,bank,rows);
            TeamNetwork.Broadcast(key,bank,false);
        }
        Ack();
        void Ack(){
            var msg=TeamNetwork.Packet(TeamOp.ImportReceipt,token);msg.Write(id.ToByteArray());msg.Write(onDisk.Contains(receipt));msg.Send(who);
        }
    }

    // TagIO reads list and array lengths from the stream. Validate their bounds
    // and nesting before allowing remotely supplied data to allocate objects.
    private static bool ValidTag(byte[] bytes,int length){
        int cursor=0,budget=1_000_000;
        bool Skip(long n){
            if(n<0 || n>length-cursor)return false;
            cursor+=(int)n;return true;
        }
        bool Text()=>Skip(2) && Skip(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor-2)));
        bool Payload(int type,int depth){
            if(--budget<0 || depth>128)return false;
            switch(type){
                case 1:return Skip(1);
                case 2:return Skip(2);
                case 3:case 5:return Skip(4);
                case 4:case 6:return Skip(8);
                case 7:case 11:case 12:
                    if(!Skip(4))return false;
                    int size=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cursor-4));
                    return Skip((long)size*(type==7?1:type==11?4:8));
                case 8:return Text();
                case 9:
                    if(!Skip(5))return false;
                    int element=bytes[cursor-5],count=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(cursor-4));
                    if(count<0 || count>budget || count>length-cursor)return false;
                    for(int i=0;i<count;i++)if(!Payload(element,depth+1))return false;
                    return true;
                case 10:
                    while(Skip(1)){
                        int tag=bytes[cursor-1];if(tag==0)return true;
                        if(!Text() || !Payload(tag,depth+1))return false;
                    }
                    return false;
                default:return false;
            }
        }
        return Skip(1) && bytes[0]==10 && Text() && Payload(10,0) && cursor==length;
    }
}
