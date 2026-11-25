using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace AEBeyondDimensions;

internal static class TeamClient
{
    internal static bool Open {get;private set;}
    private static bool opening;
    internal static bool InSession(int id)=>Open && id==token && Main.LocalPlayer.team==Team;
    private static int pendingBench=-1;
    internal static int InventoryTeam=-1;
    internal static long CompletedView;
    private static int token,seq,sent,revision,product;
    internal static int Team;
    internal static Item[] Stock=Array.Empty<Item>();
    internal static List<Item> Stations=new(),Orders=new();
    internal static (int Type,long Count)[] Preview=Array.Empty<(int,long)>();
    private static readonly bool[] dirtyTabs=new bool[3];
    private static readonly List<Item>[] incoming={new(),new(),new()};
    private static TerminalSystem SystemUI=>ModContent.GetInstance<TerminalSystem>();
    internal static void Reset(){
        if(Open && !Main.dedServ)Main.LocalPlayer.GetModPlayer<TerminalBench>().ClearRemote();
        Open=opening=false;pendingBench=-1;product=seq=sent=0;Orders=new();Preview=Array.Empty<(int,long)>();dirtyTabs[2]=true;
    }
    internal static void Request(int x=-1,int y=-1){
        if(Open || opening)return;
        token++;opening=true;
        var packet=TeamNetwork.Packet(TeamOp.Open,token);
        packet.Write(Main.LocalPlayer.GetModPlayer<TeamIdentity>().Key.ToByteArray());
        packet.Write(x);packet.Write(y);packet.Write(CompletedView);packet.Send();
    }
    internal static bool SelectBench(int index){
        if(Open)return Shared(SharedAction.FillBench,index);
        pendingBench=index;Request();return true;
    }
    internal static bool Select(int type){
        if(Open)return InstantCraft.Open(Main.LocalPlayer,type);
        product=type;Request();return true;
    }
    internal static void Close(){
        if(!Open && !opening)return;
        TeamNetwork.Packet(TeamOp.Close,token).Send();
        SystemUI.EndTeamVisit();
        Reset();token++;
    }
    private static bool Send(TeamOp op,Action<BinaryWriter> fields){
        if(!Open || sent!=seq)return false;
        var packet=TeamNetwork.Packet(op,token);
        packet.Write(++sent);packet.Write(revision);fields(packet);packet.Send();return true;
    }
    internal static void DepositSlot(int index){
        var item=Main.LocalPlayer.inventory[index];
        var packet=TeamNetwork.Packet(TeamOp.SweepDeposit,token);
        packet.Write((byte)index);packet.Write(item.type);packet.Write(item.stack);packet.Write(item.prefix);packet.Send();
    }
    internal static bool Shared(SharedAction action,int index=0)=>Send(TeamOp.SharedAction,w=>{
        w.Write((byte)action);w.Write(index);w.Write(Main.LocalPlayer.GetModPlayer<TerminalSearchSettings>().UseEquipment);
    });
    internal static bool Order(int type,int quantity){
        var p=Main.LocalPlayer;
        var pins=InstantCraft.EncodeRoute(p.GetModPlayer<TerminalOrders>().Route);
        if(pins.Count>5000)return false;
        return Send(TeamOp.Order,w=>{
            w.Write(type);w.Write(quantity);w.Write(p.GetModPlayer<TerminalSearchSettings>().UseEquipment);
            w.Write(TerminalOrders.NewGroupsFirst?.Invoke()==true);w.Write((ushort)pins.Count);
            foreach(var (recipe,slot,choice) in pins){w.Write(recipe);w.Write(slot);w.Write(choice);}
        });
    }
    internal static bool Action(TeamOp op,TeamTab tab,int index){
        if(index<0)return false;
        return Send(TeamOp.NativeAction,w=>{
            w.Write((byte)op);w.Write((byte)tab);w.Write(op==TeamOp.Deposit?0:index/TeamNetwork.PageSize);
            if(op==TeamOp.Deposit){
                var item=Main.LocalPlayer.inventory[index];w.Write((byte)index);w.Write(item.type);w.Write(item.stack);w.Write((int)item.prefix);
            }else w.Write((byte)(index%TeamNetwork.PageSize));
        });
    }
    internal static void Receive(TeamOp op,int id,BinaryReader r){
        if(op==TeamOp.ImportResult){
            byte kind=r.ReadByte();long moved=r.ReadInt64();
            if(id==Main.LocalPlayer.team)ModContent.GetInstance<ExternalStorageImport>().ReceiveResult(kind,moved);
            return;
        }
        if(op==TeamOp.EquippedSlot){SharedActions.ReceiveEquipment(id,r);return;}
        if(op==TeamOp.BenchView){Main.LocalPlayer.GetModPlayer<TerminalBench>().ReceiveBench(r,InSession(id));return;}
        if(op==TeamOp.ForgeItem){
            var ticket=new Guid(r.ReadBytes(16));
            var item=ItemIO.Receive(r,true,true);
            if(InSession(id))Main.LocalPlayer.GetModPlayer<TerminalReforgePlayer>().ReceiveDelivery(ticket,item);
            return;
        }
        if(op==TeamOp.BagSlot){
            int slot=r.ReadByte();var item=ItemIO.Receive(r,true);
            if(slot<58)Main.LocalPlayer.inventory[slot]=item;
            return;
        }
        bool view=op is TeamOp.ViewPatch or TeamOp.ViewCommit;
        bool passive=view && r.ReadBoolean();
        if(!passive && (id!=token || !Open && !opening))return;
        if(op==TeamOp.WrongController){
            Main.NewText(TerminalText.Get("无法打开这台控制器，请确认距离和队伍颜色。"));
            if(Open)SystemUI.EndTeamVisit();Reset();return;
        }
        if(op==TeamOp.Closed){
            if(r.ReadBoolean())Main.NewText(TerminalText.Get("制作并放置 ME 控制器，右键激活后解锁终端。"));
            if(Open)SystemUI.EndTeamVisit();
            Reset();return;
        }
        if(op==TeamOp.Accepted){product=0;return;}
        if(!view)return;
        int team=r.ReadByte();
        if(team!=Main.LocalPlayer.team || passive && Open)return;
        if(op==TeamOp.ViewPatch){
            CompletedView=0;
            int tab=r.ReadByte(),start=r.ReadInt32(),remove=r.ReadInt32(),count=r.ReadByte();
            var rows=incoming[tab];
            if(remove==-1)rows.Clear();
            else if(remove!=count && remove>0)rows.RemoveRange(start,remove);
            var added=new Item[count];
            for(int i=0;i<count;i++){
                var item=new Item(r.ReadInt32());item.stack=r.ReadInt32();item.prefix=r.ReadInt32();
                item.favorited=r.ReadBoolean();added[i]=item;
            }
            if(remove==count){for(int i=0;i<count;i++)rows[start+i]=added[i];}
            else if(count>0)rows.InsertRange(start,added);
            dirtyTabs[tab]=true;
            return;
        }
        int acknowledged=r.ReadInt32(),version=r.ReadInt32();
        CompletedView=r.ReadInt64();
        if(dirtyTabs[0])Stock=incoming[0].ToArray();
        if(dirtyTabs[1])Stations=new(incoming[1]);
        if(dirtyTabs[2])Orders=new(incoming[2]);
        Array.Clear(dirtyTabs);
        Preview=Orders.GroupBy(i=>i.type).Select(g=>(g.Key,g.Sum(i=>(long)i.stack))).ToArray();
        InventoryTeam=team;
        if(passive)return;
        Team=team;seq=acknowledged;sent=Math.Max(sent,seq);revision=version;
        bool first=!Open;Open=true;opening=false;
        if(first)SystemUI.BeginTeamVisit();
        if(pendingBench>=0){
            int index=pendingBench;pendingBench=-1;
            if(Shared(SharedAction.FillBench,index))SystemUI.ShowTeamBench();
        }
        if(product>0){int type=product;product=0;InstantCraft.Open(Main.LocalPlayer,type);}
    }
}
