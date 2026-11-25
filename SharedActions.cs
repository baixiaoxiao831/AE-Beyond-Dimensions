using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

internal enum SharedAction : byte { DepositAll, QuickStack, Restock, LootAll, FillBench, RefundBench, EmptyBench, CraftBench, Reforge }

internal static class SharedActions
{
    private sealed record Reservation(Player Player,TeamBank Bank,string Key);
    private static readonly Dictionary<int,Reservation> reservations=new();
    internal static IEnumerable<Item> Reserved(TeamBank bank)=>reservations.Values.Where(r=>r.Bank==bank)
        .SelectMany(r=>r.Player.GetModPlayer<TerminalBench>().OwnedParts);
    internal static void Reset()=>reservations.Clear();
    internal static void Release(int who){
        if(!reservations.Remove(who,out var r))return;
        r.Player.GetModPlayer<TerminalStorage>().WithBank(r.Bank,()=>r.Player.GetModPlayer<TerminalBench>().Refund());
        TeamNetwork.SupplyChanged(r.Key,r.Bank);
    }
    internal static void Give(Player p,Item item){
        foreach(var target in p.inventory.Take(58))
            if(!item.IsAir && target.type==item.type && target.stack<target.maxStack)ItemLoader.TryStackItems(target,item,out _);
        int start=item.IsACoin?50:item.ammo>0?54:0;
        if(start!=0)for(int i=start;i<start+4 && !item.IsAir;i++)if(p.inventory[i].IsAir)
            p.inventory[i]=ItemLoader.TransferWithLimit(item,Math.Min(item.stack,item.maxStack));
        for(int slot=0;slot<50 && !item.IsAir;slot++)if(p.inventory[slot].IsAir)
            p.inventory[slot]=ItemLoader.TransferWithLimit(item,Math.Min(item.stack,item.maxStack));
    }
    internal static (int Kind,int Index,Item[] Array)[] Equipment(Player p){
        var slots=new List<(int,int,Item[])>();
        foreach(var slot in AccessoryProbe.AccessoryProbe.ReadArmor(p).Concat(AccessoryProbe.AccessoryProbe.Read(p)))
            if(slot.TryGetBinding(false,out var array,out _,out var i))slots.Add((array==p.armor?0:1,i,array));
        for(int i=0;i<p.miscEquips.Length;i++)slots.Add((2,i,p.miscEquips));
        return slots.ToArray();
    }
    internal static void WithEquipmentSync(Player p,Action run){
        var slots=Equipment(p);
        var before=slots.Select(s=>(s.Array[s.Index].type,s.Array[s.Index].stack,s.Array[s.Index].prefix)).ToArray();
        try{run();}finally{
            for(int n=0;n<slots.Length;n++){
                var (kind,i,array)=slots[n];var item=array[i];
                if((item.type,item.stack,item.prefix)==before[n])continue;
                var packet=TeamNetwork.Packet(TeamOp.EquippedSlot,p.whoAmI);
                packet.Write((byte)kind);packet.Write(i);ItemIO.Send(item,packet,true,true);packet.Send();
            }
        }
    }
    internal static void ReceiveEquipment(int who,BinaryReader r){
        int kind=r.ReadByte(),index=r.ReadInt32();var item=ItemIO.Receive(r,true,true);
        if((uint)who>=Main.maxPlayers)return;
        var p=Main.player[who];
        if(kind==0 && (uint)index<p.armor.Length)p.armor[index]=item;
        else if(kind==2 && (uint)index<p.miscEquips.Length)p.miscEquips[index]=item;
        else if(kind==1){
            var arr=ExtraSlots(p.GetModPlayer<Terraria.ModLoader.Default.ModAccessorySlotPlayer>());
            if((uint)index<arr.Length)arr[index]=item;
        }
    }
    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field,Name="exAccessorySlot")]
    private static extern ref Item[] ExtraSlots(Terraria.ModLoader.Default.ModAccessorySlotPlayer slots);
    internal static bool Run(Player player,TeamBank bank,string key,int token,SharedAction action,int index){
        var storage=player.GetModPlayer<TerminalStorage>();
        var bench=player.GetModPlayer<TerminalBench>();
        if(action==SharedAction.Reforge){
            if((uint)index>=bank.Stock.Count)return false;
            var item=bank.Stock[index];
            if(item.IsAir || item.favorited || !item.Prefix(-3))return false;
            return ForgeTransfers.Begin(player.whoAmI,bank,index,token);
        }
        using var current=new Main.CurrentPlayerOverride(player);
        bool accepted=true;
        storage.WithBank(bank,()=>{
            switch(action){
                case SharedAction.DepositAll:storage.DepositBackpack(false);break;
                case SharedAction.QuickStack:storage.DepositBackpack(true);break;
                case SharedAction.Restock:storage.Restock();break;
                case SharedAction.LootAll:storage.LootAll();break;
                case SharedAction.FillBench:accepted=bench.Prepare(index,true);break;
                case SharedAction.RefundBench:bench.Refund();break;
                case SharedAction.EmptyBench:bench.TransferToInventory();break;
                case SharedAction.CraftBench:if(bench.Selected?.RecipeIndex==index)bench.Take();break;
                default:accepted=false;break;
            }
        });
        if(action is SharedAction.FillBench or SharedAction.RefundBench or SharedAction.EmptyBench or SharedAction.CraftBench){
            if(bench.Selected!=null || bench.HasMaterials)reservations[player.whoAmI]=new(player,bank,key);
            else reservations.Remove(player.whoAmI);
            bench.SendBench(player.whoAmI,token);
        }
        return accepted;
    }
}
