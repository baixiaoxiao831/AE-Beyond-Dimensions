using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

internal enum TeamOp : byte { Open, Close, Page, Deposit, Withdraw, Order, Cancel, Snapshot, Closed, BagSlot, Accepted, NativeAction, Pause, Subscribe, Access, Detail, DetailEnd, AmmoRequest, AmmoGrant, AmmoUsed, AmmoReturn, Collect, CollectResult, AmmoBalance, ChestLoot, SweepDeposit, WrongController, ImportOffer, ImportBegin, ImportRequest, ImportPart, ImportReceipt, CancelGroup, ReorderGroups, SharedAction, BenchView, EquippedSlot, ForgeItem, ExternalImport, ImportResult, MagnetPrefs, InventoryPreview, ForgeReceipt, ViewPatch, ViewCommit }
internal enum TeamTab : byte { Stock, Stations, Orders }
internal static class TeamNetwork
{
    internal const int PageSize=27;
    private sealed class Session
    {
        internal string Key="";
        internal int Token, Sequence, Page;
        internal TeamTab Tab;
    }
    internal static Guid Owner(int who)=>owners[who];
    private static readonly Dictionary<int,Guid> owners=new();
    private static readonly Dictionary<int,Session> sessions=new();
    private static readonly Dictionary<int,string> watching=new();
    private static readonly Dictionary<string,TeamBank> supplyChanges=new();
    internal static bool Owns(int who,string key)=>owners.TryGetValue(who,out var id) && TeamWorld.KeyFor(Main.player[who].team,id)==key;
    internal static bool TryBank(Player p,int team,out string key,out TeamBank bank){
        key="";bank=null;
        if(!p.active || p.dead || p.team!=team || !owners.TryGetValue(p.whoAmI,out var id) || !TeamWorld.Unlocked.Contains(id))return false;
        key=TeamWorld.KeyFor(team,id);
        return TeamWorld.Banks.TryGetValue(key,out bank);
    }
    internal static void Forget(int who){TeamSupply.ForgetMagnet(who);SharedActions.Release(who);LegacyStockTransfer.Forget(who);lastViews.Remove(who);owners.Remove(who);sessions.Remove(who);lastViews.Remove(who);watching.Remove(who);TeamSupply.Release(who,false);}
    internal static void Reset(){lastViews.Clear();SharedActions.Reset();owners.Clear();sessions.Clear();watching.Clear();supplyChanges.Clear();TeamSupply.Reset();TeamClient.Reset();TeamClient.CompletedView=0;}
    internal static ModPacket Packet(TeamOp op,int token){
        var packet=ModContent.GetInstance<ProbeMod>().GetPacket();
        packet.Write((byte)op);packet.Write(token);
        return packet;
    }
    private static void Close(int who,int token,bool locked=false){
        var packet=Packet(TeamOp.Closed,token);packet.Write(locked);packet.Send(who);
        SharedActions.Release(who);sessions.Remove(who);lastViews.Remove(who);
    }
    internal static void Handle(BinaryReader reader,int who){
        var op=(TeamOp)reader.ReadByte();int token=reader.ReadInt32();
        if(Main.netMode==NetmodeID.MultiplayerClient){
            if(op is TeamOp.ImportOffer or TeamOp.ImportRequest or TeamOp.ImportReceipt){
                Main.LocalPlayer.GetModPlayer<LegacyStockTransfer>().Client(op,token,reader);return;
            }
            if(TeamSupply.ClientPacket(op,token,reader))return;
            TeamClient.Receive(op,token,reader);return;
        }
        if(Main.netMode!=NetmodeID.Server || (uint)who>=Main.maxPlayers)return;
        var player=Main.player[who];
        if(op is TeamOp.ImportBegin or TeamOp.ImportPart){
            TeamBank importBank=null;string bankKey="";
            if(sessions.TryGetValue(who,out var visit) && visit.Token==token &&
               TryBank(player,player.team,out var k,out var b) && k==visit.Key){importBank=b;bankKey=k;}
            LegacyStockTransfer.Server(op,who,token,owners.GetValueOrDefault(who),bankKey,importBank,reader);return;
        }
        if(op==TeamOp.ForgeReceipt){
            var ticket=new Guid(reader.ReadBytes(16));
            if(owners.TryGetValue(who,out var owner))ForgeTransfers.Confirm(owner,ticket);
            return;
        }
        if(op==TeamOp.ReorderGroups){
            int n=reader.ReadByte();var ids=Enumerable.Range(0,n).Select(_=>new Guid(reader.ReadBytes(16))).ToArray();
            if(!TryBank(player,token,out var key,out var bank))return;
            var byId=bank.Orders.ToDictionary(bank.GroupId);
            if(n==byId.Count && ids.Distinct().Count()==n && ids.All(byId.ContainsKey)){
                bank.Orders.Clear();bank.Orders.AddRange(ids.Select(id=>byId[id]));Broadcast(key,bank);
            }else SendDetails(who,bank);
            return;
        }
        if(op==TeamOp.CancelGroup){
            var ticket=new Guid(reader.ReadBytes(16));
            if(TryBank(player,token,out var key,out var bank)){
                var order=bank.Orders.Find(o=>bank.GroupId(o)==ticket);
                if(order!=null){
                    bank.Orders.Remove(order);bank.Details.Remove(order);bank.Paused.Remove(order);bank.OrderData.Remove(order);
                    Broadcast(key,bank);
                }
            }
            return;
        }
        if(op==TeamOp.ExternalImport){
            ModContent.GetInstance<ExternalStorageImport>().Server(player,token,reader.ReadByte(),reader.ReadInt32());return;
        }
        if(op==TeamOp.MagnetPrefs){
            TeamSupply.SetMagnet(player,token,reader.ReadInt32());return;
        }
        if(op==TeamOp.ChestLoot){
            int index=reader.ReadInt32(),x=reader.ReadInt32(),y=reader.ReadInt32();
            if(TryBank(player,token,out var key,out var bank))TerminalChestLoot.MoveChest(player,index,x,y,key,bank);
            return;
        }
        if(op==TeamOp.SweepDeposit){
            int slot=reader.ReadByte(),type=reader.ReadInt32(),count=reader.ReadInt32(),prefix=reader.ReadInt32();
            if(slot>=58 || !sessions.TryGetValue(who,out var visit) || visit.Token!=token ||
               !TryBank(player,player.team,out var key,out var bank) || visit.Key!=key)return;
            var item=player.inventory[slot];
            if(item.IsAir || item.favorited || item.type!=type || item.stack!=count || item.prefix!=prefix)return;
            bank.Store(item.Clone());player.inventory[slot]=new();
            var delta=Packet(TeamOp.BagSlot,token);delta.Write((byte)slot);ItemIO.Send(player.inventory[slot],delta,true);delta.Send(who);
            NetMessage.SendData(MessageID.SyncEquipment,-1,who,null,who,slot);
            SupplyChanged(key,bank);return;
        }
        if(!player.active || player.dead)return;
        if(op==TeamOp.Subscribe){
            var identity=new Guid(reader.ReadBytes(16));
            if(identity==Guid.Empty || owners.TryGetValue(who,out var known) && known!=identity)return;
            owners[who]=identity;Subscribe(player,identity);return;
        }
        if(op is TeamOp.AmmoRequest or TeamOp.AmmoUsed or TeamOp.AmmoReturn or TeamOp.Collect){
            if(owners.TryGetValue(who,out var owner) && TeamWorld.Unlocked.Contains(owner) &&
               token==player.team && TeamWorld.Banks.TryGetValue(TeamWorld.KeyFor(player.team,owner),out var bank))
                TeamSupply.ServerPacket(op,player,bank,TeamWorld.KeyFor(player.team,owner),reader);
            return;
        }
        if(op==TeamOp.Open){
            var id=new Guid(reader.ReadBytes(16));var x=reader.ReadInt32();var y=reader.ReadInt32();long clientView=reader.ReadInt64();
            if(id==Guid.Empty || owners.TryGetValue(who,out var old) && old!=id)return;
            owners[who]=id;
            if(x!=-1 || y!=-1){
                if(!WorldGen.InWorld(x,y,1) || Vector2.DistanceSquared(player.Center,new Vector2(x*16+8,y*16+8))>160*160 ||
                   !ControllerBinding.TryTile(x,y,out var anchor) || !ControllerBinding.OpenFor(anchor,player.team)){
                    SharedActions.Release(who);sessions.Remove(who);lastViews.Remove(who);Packet(TeamOp.WrongController,token).Send(who);return;
                }
                TeamWorld.Unlocked.Add(id);
            }
            if(!TeamWorld.Unlocked.Contains(id)){Close(who,token,true);return;}
            string key=TeamWorld.KeyFor(player.team,id);
            SharedActions.Release(who);
            var opened=new Session{Key=key,Token=token};
            sessions[who]=opened;
            if(!TeamWorld.Banks.TryGetValue(key,out var bank))TeamWorld.Banks[key]=bank=new();
            if(!watching.TryGetValue(who,out var watchedKey) || watchedKey!=key)Subscribe(player,id);
            SendView(who,opened,bank,clientView:clientView);ForgeTransfers.Resend(id,who,token);LegacyStockTransfer.Offer(who,token,key);return;
        }
        if(op==TeamOp.Close){
            if(sessions.TryGetValue(who,out var closing) && closing.Token==token){SharedActions.Release(who);sessions.Remove(who);}
            return;
        }
        int seq=reader.ReadInt32(),revision=reader.ReadInt32();
        bool native=op==TeamOp.NativeAction;
        if(native)op=(TeamOp)reader.ReadByte();
        TeamTab target=default;int offset=0,slotIndex=0,itemType=0,quantity=0,itemPrefix=0;
        if(native || op==TeamOp.Page){target=(TeamTab)reader.ReadByte();offset=reader.ReadInt32();}
        SharedAction action=default;bool equipped=false,first=false;List<TagCompound> pins=null;
        switch(op){
            case TeamOp.SharedAction:action=(SharedAction)reader.ReadByte();slotIndex=reader.ReadInt32();equipped=reader.ReadBoolean();break;
            case TeamOp.Deposit:
                slotIndex=reader.ReadByte();itemType=reader.ReadInt32();quantity=reader.ReadInt32();itemPrefix=reader.ReadInt32();break;
            case TeamOp.Withdraw:case TeamOp.Cancel:case TeamOp.Pause:
                slotIndex=reader.ReadByte();break;
            case TeamOp.Order:
                itemType=reader.ReadInt32();quantity=reader.ReadInt32();equipped=reader.ReadBoolean();first=reader.ReadBoolean();pins=InstantCraft.ReadRoute(reader);break;
        }
        if(!sessions.TryGetValue(who,out var s) || s.Token!=token)return;
        if(s.Key!=TeamWorld.KeyFor(player.team,owners[who])){Close(who,token);return;}
        var data=TeamWorld.Banks[s.Key];
        if(seq!=s.Sequence+1){SendView(who,s,data);return;}
        s.Sequence=seq;
        if(!native && op==TeamOp.Page){
            if(target<=TeamTab.Orders){s.Tab=target;s.Page=Math.Clamp(offset,0,Math.Max(0,(RowsFor(target,data).Count-1)/PageSize));}
            SendView(who,s,data);return;
        }
        if(revision!=data.Revision){SendView(who,s,data);return;}
        if(native){
            if(target>TeamTab.Orders || offset<0 || offset>RowsFor(target,data).Count/PageSize ||
                op is not (TeamOp.Deposit or TeamOp.Withdraw or TeamOp.Cancel or TeamOp.Pause)){SendView(who,s,data);return;}
            s.Tab=target;s.Page=offset;
        }
        var before=player.inventory.Take(58).Select(i=>(i.type,i.stack,i.prefix)).ToArray();
        bool changed=false;
        if(op==TeamOp.SharedAction){
            player.GetModPlayer<TerminalSearchSettings>().UseEquipment=equipped;
            void Perform()=>changed=SharedActions.Run(player,data,s.Key,token,action,slotIndex);
            if(equipped && action is SharedAction.FillBench or SharedAction.CraftBench)SharedActions.WithEquipmentSync(player,Perform);
            else Perform();
        }else if(op==TeamOp.Deposit){
            int slot=slotIndex,type=itemType,stack=quantity,prefix=itemPrefix;
            if(slot<58 && (s.Tab==TeamTab.Stock || s.Tab==TeamTab.Stations)){
                var item=player.inventory[slot];
                if(!item.IsAir && !item.favorited && item.type==type && item.stack==stack && item.prefix==prefix){
                    if(s.Tab==TeamTab.Stock){
                        data.Store(item.Clone());player.inventory[slot]=new();changed=true;
                    }else if(TerminalWorkArea.IsStationOutput(item) && !data.Stations.Any(i=>i.type==type)){
                        data.Stations.Add(ItemLoader.TransferWithLimit(item,1));changed=true;
                    }
                }
            }
        }else if(op==TeamOp.Withdraw || op==TeamOp.Cancel || op==TeamOp.Pause){
            int index=slotIndex+s.Page*PageSize;
            var list=Rows(s,data);
            if(index>=s.Page*PageSize && index<(s.Page+1)*PageSize && index<list.Count){
                if(op==TeamOp.Cancel && s.Tab==TeamTab.Orders){data.Paused.Remove(list[index]);data.Details.Remove(list[index]);data.OrderData.Remove(list[index]);list.RemoveAt(index);changed=true;}
                if(op==TeamOp.Pause && s.Tab==TeamTab.Orders){if(!data.Paused.Add(list[index]))data.Paused.Remove(list[index]);changed=true;}
                if(op==TeamOp.Withdraw && (s.Tab==TeamTab.Stock || s.Tab==TeamTab.Stations)){
                    var item=list[index];int previous=item.stack;
                    for(int slot=0;slot<50 && !item.IsAir;slot++){
                        var dst=player.inventory[slot];
                        if(dst.type==item.type && dst.stack<dst.maxStack)ItemLoader.TryStackItems(dst,item,out _);
                    }
                    for(int slot=0;slot<50 && !item.IsAir;slot++)if(player.inventory[slot].IsAir)
                        player.inventory[slot]=ItemLoader.TransferWithLimit(item,item.stack);
                    changed=item.stack!=previous;
                    if(item.IsAir)list.RemoveAt(index);
                }
            }
        }else if(op==TeamOp.Order){
            int type=itemType;
            if(type>0 && type<ItemLoader.ItemCount && quantity is >=1 and <=999 &&
                InstantCraft.RecipesFor(type).Length>0 && data.Orders.Count<128){
                var order=new Item(type);order.stack=quantity;
                player.GetModPlayer<TerminalSearchSettings>().UseEquipment=equipped;
                if(pins.Count>0){data.GroupId(order);data.OrderData[order]["route"]=pins;}
                bool made=false;
                if(data.Orders.Count==0){
                    if(equipped)SharedActions.WithEquipmentSync(player,()=>made=TryCraft(player,data,order,true));
                    else made=TryCraft(player,data,order,true);
                }
                if(made)data.OrderData.Remove(order);
                else if(first)data.Orders.Insert(0,order);
                else data.Orders.Add(order);
                Packet(TeamOp.Accepted,token).Send(who);
                changed=true;
            }
        }
        for(int i=0;i<before.Length;i++){
            var item=player.inventory[i];
            if(before[i]==(item.type,item.stack,item.prefix))continue;
            var delta=Packet(TeamOp.BagSlot,token);delta.Write((byte)i);ItemIO.Send(item,delta,true);delta.Send(who);
            NetMessage.SendData(MessageID.SyncEquipment,-1,who,null,who,i);
        }
        if(changed)Broadcast(s.Key,data,op is TeamOp.Order or TeamOp.Cancel or TeamOp.Pause);else SendView(who,s,data);
    }
    private static bool TryCraft(Player player,TeamBank data,Item order,bool bag,Dictionary<int,int>? reserved=null){
        try{return InstantCraft.CraftShared(player,data,order,bag,reserved);}
        catch(Exception e){ModContent.GetInstance<ProbeMod>().Logger.Warn("Team recipe could not be executed",e);return false;}
    }
    private static List<Item> Rows(Session s,TeamBank b)=>RowsFor(s.Tab,b);
    private static List<Item> RowsFor(TeamTab tab,TeamBank b)=>tab switch{
        TeamTab.Stations=>b.Stations,TeamTab.Orders=>b.Orders,_=>b.Stock
    };
    internal readonly record struct ViewRow(int Type,int Stack,int Prefix,bool Flag);
    private sealed record ViewState(ViewRow[][] Tabs);
    private readonly record struct Patch(int Tab,int Start,int Remove,int Count);
    private sealed record Viewer(string Key,int Token,bool Preview,ViewState State,long Stamp);
    private static long nextViewStamp;
    private static readonly Dictionary<int,Viewer> lastViews=new();

    private static ViewState Capture(TeamBank bank){
        ViewRow[] Rows(List<Item> list,bool orders=false)=>list.Select(i=>
            new ViewRow(i.type,i.stack,i.prefix,orders?bank.Paused.Contains(i):i.favorited)).ToArray();
        return new(new[]{Rows(TeamSupply.StockView(bank)),Rows(bank.Stations),Rows(bank.Orders,true)});
    }

    private static List<Patch> Compare(ViewState previous,ViewState next){
        var patches=new List<Patch>();
        for(int tab=0;tab<next.Tabs.Length;tab++){
            var rows=next.Tabs[tab];
            if(previous==null){patches.Add(new(tab,0,-1,rows.Length));continue;}
            var old=previous.Tabs[tab];
            int start=0,limit=Math.Min(old.Length,rows.Length);
            while(start<limit && old[start]==rows[start])start++;
            if(rows.Length==old.Length){
                while(start<limit){
                    int end=start+1;
                    while(end<limit && old[end]!=rows[end])end++;
                    patches.Add(new(tab,start,end-start,end-start));
                    start=end;
                    while(start<limit && old[start]==rows[start])start++;
                }
            }else{
                int tail=0;
                while(tail<limit-start && old[old.Length-tail-1]==rows[rows.Length-tail-1])tail++;
                patches.Add(new(tab,start,old.Length-start-tail,rows.Length-start-tail));
            }
        }
        return patches;
    }
    private static void SendView(int who,Session s,TeamBank bank,bool preview=false,ViewState captured=null,
        Dictionary<ViewState,List<Patch>> shared=null,long clientView=0){
        var current=captured??Capture(bank);
        string key=preview?watching[who]:s.Key;
        lastViews.TryGetValue(who,out var viewer);
        // A completed preview can also be the opening baseline. The stamp covers all three tabs.
        bool reuse=viewer!=null && viewer.Key==key && (viewer.Stamp==clientView ||
            viewer.Token==s.Token && viewer.Preview==preview);
        var previous=reuse?viewer.State:null;
        List<Patch> patches;
        if(previous==null || shared==null)patches=Compare(previous,current);
        else if(!shared.TryGetValue(previous,out patches))shared[previous]=patches=Compare(previous,current);
        int team=Main.player[who].team;
        foreach(var change in patches){
            int offset=0;
            do{
                int count=Math.Min(PageSize,change.Count-offset);
                var packet=Packet(TeamOp.ViewPatch,s.Token);
                packet.Write(preview);packet.Write((byte)team);packet.Write((byte)change.Tab);
                packet.Write(change.Start+offset);packet.Write(offset==0?change.Remove:0);packet.Write((byte)count);
                for(int i=0;i<count;i++){
                    var row=current.Tabs[change.Tab][change.Start+offset+i];
                    packet.Write(row.Type);packet.Write(row.Stack);packet.Write(row.Prefix);packet.Write(row.Flag);
                }
                packet.Send(who);offset+=count;
            }while(offset<change.Count);
        }
        var end=Packet(TeamOp.ViewCommit,s.Token);end.Write(preview);end.Write((byte)team);
        long stamp=++nextViewStamp;
        end.Write(s.Sequence);end.Write(bank.Revision);end.Write(stamp);end.Send(who);
        lastViews[who]=new(key,s.Token,preview,current,stamp);
    }
    private static void Subscribe(Player p,Guid id){
        string key=TeamWorld.KeyFor(p.team,id);
        if(watching.TryGetValue(p.whoAmI,out var old) && old!=key)TeamSupply.Release(p.whoAmI);
        bool unlocked=TeamWorld.Unlocked.Contains(id);
        var reply=Packet(TeamOp.Access,p.team);reply.Write(unlocked);reply.Send(p.whoAmI);
        if(!unlocked)return;
        if(!sessions.ContainsKey(p.whoAmI))lastViews.Remove(p.whoAmI);
        watching[p.whoAmI]=key;
        if(!TeamWorld.Banks.TryGetValue(key,out var bank))TeamWorld.Banks[key]=bank=new();
        if(!sessions.ContainsKey(p.whoAmI))SendView(p.whoAmI,new Session(),bank,true);
        SendDetails(p.whoAmI,bank);
    }
    private static void SendDetails(int who,TeamBank bank){
        int team=Main.player[who].team;
        for(int i=0;i<bank.Orders.Count;i++){
            var order=bank.Orders[i];bool paused=bank.Paused.Contains(order);
            var rows=!paused && bank.Details.TryGetValue(order,out var list)?list:Array.Empty<TerminalOrders.MaterialStatus>();
            for(int start=0;start==0 || start<rows.Length;start+=128){
                var msg=Packet(TeamOp.Detail,team);msg.Write(i);msg.Write(order.type);msg.Write(order.stack);msg.Write(paused);msg.Write(bank.GroupId(order).ToByteArray());msg.Write(start);
                int count=Math.Min(128,rows.Length-start);msg.Write((byte)count);
                for(int n=start;n<start+count;n++){var row=rows[n];msg.Write(row.Type);msg.Write(row.Available);msg.Write(row.Planned);msg.Write(row.Missing);}
                msg.Send(who);
            }
        }
        var end=Packet(TeamOp.DetailEnd,team);end.Write(bank.Orders.Count);end.Send(who);
    }
    internal static void SupplyChanged(string key,TeamBank bank){
        bank.Revision++;
        supplyChanges[key]=bank;
    }
    internal static void Broadcast(string key,TeamBank b,bool orders=true){
        b.Revision++;supplyChanges.Remove(key);PushViews(key,b,orders);
    }
    private static void PushViews(string key,TeamBank b,bool orders){
        TeamSupply.Balance(b);
        var view=Capture(b);var changes=new Dictionary<ViewState,List<Patch>>();
        foreach(var (who,bankKey) in watching){
            if(bankKey!=key || !Main.player[who].active || !Owns(who,key))continue;
            if(!sessions.ContainsKey(who))SendView(who,new Session(),b,true,view,changes);
            if(orders)SendDetails(who,b);
        }
        foreach(var (who,s) in sessions)
            if(s.Key==key && Main.player[who].active && !Main.player[who].dead &&
                TeamWorld.KeyFor(Main.player[who].team,owners[who])==key)SendView(who,s,b,false,view,changes);
    }
    internal static void Tick(){
        TeamSupply.ServerTick();
        if(Main.GameUpdateCount%6==0 && supplyChanges.Count!=0){
            foreach(var (key,bank) in supplyChanges)PushViews(key,bank,false);
            supplyChanges.Clear();
        }
        foreach(var (who,s) in sessions.ToArray()){
            var p=Main.player[who];
            if(!p.active){Forget(who);continue;}
            if(p.dead || TeamWorld.KeyFor(p.team,owners[who])!=s.Key)Close(who,s.Token);
        }
        if(Main.GameUpdateCount%120==0)ProcessOrders();
    }
    private static void ProcessOrders(){
        var checkedBanks=new HashSet<string>();
        foreach(var p in Main.ActivePlayers){
            if(p.dead || !owners.TryGetValue(p.whoAmI,out var id))continue;
            string key=TeamWorld.KeyFor(p.team,id);
            if(!checkedBanks.Add(key) || !TeamWorld.Banks.TryGetValue(key,out var b) || b.Orders.Count==0)continue;
            b.DetailChanged=false;bool completed=false;
            var reserved=new Dictionary<int,int>();
            for(int index=0;index<b.Orders.Count;index++){
                if(b.OrderData.TryGetValue(b.Orders[index],out var saved) &&
                   (!saved.GetBool("waiting") || saved.GetString("provider")!=ModContent.GetInstance<ProbeMod>().Name))continue;
                if(b.Paused.Contains(b.Orders[index]) || !TryCraft(p,b,b.Orders[index],false,reserved))continue;
                b.Details.Remove(b.Orders[index]);b.OrderData.Remove(b.Orders[index]);b.Orders.RemoveAt(index);completed=true;break;
            }
            if(completed || b.DetailChanged)Broadcast(key,b);
        }
    }
}
