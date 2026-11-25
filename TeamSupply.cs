using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

internal static class TeamSupply
{
    private sealed record Allowance(TeamBank Bank,string Key,Item Source,Item Rounds,int Token,int Weapon);
    private static readonly Dictionary<int,Allowance> allowances=new();
    private static int generation;
    private static int team=-1,grant,weaponType,remaining;
    private static bool unlocked,checkingSpace;
    private static Item ammo=new();
    private static ulong retryAmmo;
    private static readonly Dictionary<int,ulong> pickups=new(),fallback=new();
    internal static (string,int,int,int,int)[] GroupRows=Array.Empty<(string,int,int,int,int)>();
    internal static (int Type,int Need)[] Goals=Array.Empty<(int,int)>();
    internal static TerminalOrders.StatusEntry[] Status=Array.Empty<TerminalOrders.StatusEntry>();
    private static readonly List<TerminalOrders.StatusEntry> receiving=new();
    private static readonly Dictionary<int,(int Team,int Mode)> magnets=new();
    private static int lastMagnet=int.MinValue;
    internal static void ForgetMagnet(int who)=>magnets.Remove(who);
    internal static void SetMagnet(Player p,int team,int mode){
        if(!TeamNetwork.TryBank(p,team,out _,out _) || !Enum.IsDefined(typeof(TerminalMagnet.PickupMode),mode)){magnets.Remove(p.whoAmI);return;}
        magnets[p.whoAmI]=(team,mode);
    }
    internal static bool RouteWand(Player p,Item item){
        if(item.IsAir || ItemID.Sets.IsAPickup[item.type] || !magnets.TryGetValue(p.whoAmI,out var prefs) ||
           !TeamNetwork.TryBank(p,prefs.Team,out var key,out var bank))return false;
        var mode=(TerminalMagnet.PickupMode)prefs.Mode;
        if(mode==TerminalMagnet.PickupMode.Matching && (item.IsACoin || item.favorited || !bank.Stock.Any(i=>i.type==item.type)))return false;
        if(mode==TerminalMagnet.PickupMode.Overflow){
            var before=p.inventory.Take(58).Select(i=>(i.type,i.stack,i.prefix)).ToArray();
            SharedActions.Give(p,item);
            for(int i=0;i<before.Length;i++){
                var after=p.inventory[i];
                if(before[i]==(after.type,after.stack,after.prefix))continue;
                var msg=TeamNetwork.Packet(TeamOp.BagSlot,0);msg.Write((byte)i);ItemIO.Send(after,msg,true);msg.Send(p.whoAmI);
                NetMessage.SendData(MessageID.SyncEquipment,-1,p.whoAmI,null,p.whoAmI,i);
            }
        }
        if(!item.IsAir){bank.Store(item);TeamNetwork.SupplyChanged(key,bank);}
        return true;
    }
    internal static bool Active=>Main.netMode==NetmodeID.MultiplayerClient && unlocked && Main.LocalPlayer.team==team;
    internal static List<Item> StockView(TeamBank bank){
        var result=bank.Stock.ToList();
        foreach(var group in allowances.Values.Where(a=>a.Bank==bank).GroupBy(a=>a.Source)){
            int index=bank.Stock.IndexOf(group.Key);
            var shown=group.First().Rounds.Clone();
            shown.stack=(index<0?0:bank.Stock[index].stack)+group.Sum(a=>a.Rounds.stack);
            if(index<0)result.Add(shown);else result[index]=shown;
        }
        return result;
    }
    internal static void Remap(TeamBank bank,IReadOnlyDictionary<Item,Item> copies){
        foreach(var (who,a) in allowances.ToArray())
            if(a.Bank==bank && copies.TryGetValue(a.Source,out var copy))allowances[who]=a with {Source=copy};
    }
    internal static void Balance(TeamBank bank){
        foreach(var (who,a) in allowances){
            if(a.Bank!=bank || !TeamNetwork.Owns(who,a.Key))continue;
            int total=(bank.Stock.Contains(a.Source)?a.Source.stack:0)+allowances.Values.Where(x=>ReferenceEquals(x.Source,a.Source)).Sum(x=>x.Rounds.stack);
            var msg=TeamNetwork.Packet(TeamOp.AmmoBalance,Main.player[who].team);
            msg.Write(a.Token);msg.Write(a.Rounds.stack);msg.Write(total);msg.Send(who);
        }
    }
    internal static void Reset(){
        magnets.Clear();allowances.Clear();team=-1;unlocked=false;ClearClient();
    }
    private static void ClearClient(){
        TeamClient.InventoryTeam=-1;lastMagnet=int.MinValue;ammo=new();remaining=grant=weaponType=0;retryAmmo=0;pickups.Clear();fallback.Clear();
        receiving.Clear();Goals=Array.Empty<(int,int)>();Status=Array.Empty<TerminalOrders.StatusEntry>();GroupRows=Array.Empty<(string,int,int,int,int)>();
    }
    internal static void ClientTick(Player player,Guid identity){
        if(player.team!=team){
            ClearClient();unlocked=false;team=player.team;
            var msg=TeamNetwork.Packet(TeamOp.Subscribe,team);msg.Write(identity.ToByteArray());msg.Send();
        }
        var magnet=player.GetModPlayer<TerminalMagnet>();
        int mode=magnet.Available?(int)magnet.Route:-1;
        if(Active && lastMagnet!=mode){
            lastMagnet=mode;
            var msg=TeamNetwork.Packet(TeamOp.MagnetPrefs,team);msg.Write(mode);msg.Send();
        }
        if(grant!=0 && (player.HeldItem.type!=weaponType || TeamClient.Open)){
            var msg=TeamNetwork.Packet(TeamOp.AmmoReturn,team);msg.Write(grant);msg.Send();
            grant=remaining=0;ammo=new();retryAmmo=0;
        }
    }
    internal static Item Choose(Player p,Item weapon){
        if(!Active || TeamClient.Open || p.HeldItem.type!=weapon.type)return null;
        if(remaining>0 && weaponType==weapon.type && !ammo.IsAir && ItemLoader.CanChooseAmmo(weapon,ammo,p))return ammo;
        if(Main.GameUpdateCount<retryAmmo)return null;
        retryAmmo=Main.GameUpdateCount+20;
        var msg=TeamNetwork.Packet(TeamOp.AmmoRequest,team);msg.Write(weapon.type);msg.Send();return null;
    }
    internal static void Consumed(Player p,Item item){
        if(!Active || p!=Main.LocalPlayer || remaining==0 || !ReferenceEquals(item,ammo))return;
        remaining--;var msg=TeamNetwork.Packet(TeamOp.AmmoUsed,team);msg.Write(grant);msg.Send();
        if(remaining==0){retryAmmo=0;grant=0;}
    }
    internal static bool CanAttract(Item item,Player p)=>!checkingSpace && Active && p==Main.LocalPlayer && !item.GetGlobalItem<TerminalDelivery>().Pending &&
        p.GetModPlayer<TerminalMagnet>().Available && !item.instanced && !ItemID.Sets.IsAPickup[item.type] &&
        fallback.GetValueOrDefault(item.whoAmI)<=Main.GameUpdateCount;
    internal static bool InterceptPickup(Item item,Player p){
        if(!CanAttract(item,p) || !p.Hitbox.Intersects(item.Hitbox))return false;
        var mode=p.GetModPlayer<TerminalMagnet>().Route;
        if(mode==TerminalMagnet.PickupMode.Overflow){
            checkingSpace=true;
            try{if(p.ItemSpace(item).CanTakeItemToPersonalInventory)return false;}
            finally{checkingSpace=false;}
        }
        int index=item.whoAmI;
        if((uint)index>=Main.maxItems || !ReferenceEquals(Main.item[index],item))return false;
        if(pickups.GetValueOrDefault(index)<=Main.GameUpdateCount){
            var msg=TeamNetwork.Packet(TeamOp.Collect,team);msg.Write(index);msg.Write(item.type);msg.Write(item.stack);
            msg.Write(item.prefix);msg.Write((byte)mode);msg.Send();pickups[index]=Main.GameUpdateCount+30;
        }
        return true;
    }
    internal static void Release(int who,bool notify=true){
        if(!allowances.Remove(who,out var old))return;
        if(notify && Main.player[who].active){
            var cancel=TeamNetwork.Packet(TeamOp.AmmoBalance,Main.player[who].team);cancel.Write(old.Token);cancel.Write(0);cancel.Write(0);cancel.Send(who);
        }
        old.Bank.Store(old.Rounds);TeamNetwork.SupplyChanged(old.Key,old.Bank);
    }
    internal static void ServerTick(){
        foreach(var (who,a) in allowances.ToArray()){
            var p=Main.player[who];
            if(!p.active || p.dead || p.HeldItem.type!=a.Weapon || !TeamNetwork.Owns(who,a.Key))Release(who);
        }
    }
    internal static void ServerPacket(TeamOp op,Player p,TeamBank bank,string key,BinaryReader r){
        int who=p.whoAmI;
        if(op==TeamOp.Collect){
            int index=r.ReadInt32(),type=r.ReadInt32(),count=r.ReadInt32(),prefix=r.ReadInt32();var mode=(TerminalMagnet.PickupMode)r.ReadByte();
            if((uint)index>=Main.maxItems)return;
            var item=Main.item[index];
            var reach=p.Hitbox;reach.Inflate(64+16*Math.Clamp(ModContent.GetInstance<TerminalCardConfig>().MagnetRangeTiles,0,256),64+16*Math.Clamp(ModContent.GetInstance<TerminalCardConfig>().MagnetRangeTiles,0,256));
            bool ok=item.active && !item.IsAir && !item.instanced && item.type==type && item.stack==count && item.prefix==prefix &&
                !ItemID.Sets.IsAPickup[type] && item.noGrabDelay==0 && item.shimmerTime==0 && reach.Intersects(item.Hitbox) &&
                (item.playerIndexTheItemIsReservedFor==who || item.playerIndexTheItemIsReservedFor==255) &&
                Enum.IsDefined(mode) && (mode!=TerminalMagnet.PickupMode.Matching || (!item.IsACoin && !item.favorited && bank.Stock.Any(i=>i.type==type))) &&
                ItemLoader.CanPickup(item,p);
            if(ok){bank.Store(item.Clone());item.TurnToAir();item.active=false;NetMessage.SendData(MessageID.SyncItem,-1,-1,null,index);TeamNetwork.SupplyChanged(key,bank);}
            var result=TeamNetwork.Packet(TeamOp.CollectResult,p.team);result.Write(index);result.Write(ok);result.Send(who);return;
        }
        if(op==TeamOp.AmmoRequest){
            int weapon=r.ReadInt32();
            if(p.HeldItem.type!=weapon || p.HeldItem.useAmmo<=0)return;
            if(allowances.ContainsKey(who))return;
            var stack=bank.Stock.FirstOrDefault(i=>!i.IsAir && ItemLoader.CanChooseAmmo(p.HeldItem,i,p));
            if(stack==null)return;
            var shown=stack.Clone();
            shown.stack+=allowances.Values.Where(a=>ReferenceEquals(a.Source,stack)).Sum(a=>a.Rounds.stack);
            var rounds=ItemLoader.TransferWithLimit(stack,Math.Min(stack.stack,8));
            if(stack.IsAir)bank.Stock.Remove(stack);
            int token=++generation;
            allowances[who]=new(bank,key,stack,rounds,token,weapon);
            var msg=TeamNetwork.Packet(TeamOp.AmmoGrant,p.team);msg.Write(token);msg.Write(weapon);msg.Write(rounds.stack);
            ItemIO.Send(shown,msg,true);msg.Send(who);TeamNetwork.SupplyChanged(key,bank);return;
        }
        int id=r.ReadInt32();
        if(!allowances.TryGetValue(who,out var a) || a.Bank!=bank || a.Token!=id)return;
        if(op==TeamOp.AmmoReturn){Release(who);return;}
        if(--a.Rounds.stack<=0)allowances.Remove(who);
        TeamNetwork.SupplyChanged(key,bank);
    }
    internal static bool ClientPacket(TeamOp op,int target,BinaryReader r){
        if(op is not (TeamOp.Access or TeamOp.Detail or TeamOp.DetailEnd or TeamOp.AmmoGrant or TeamOp.CollectResult or TeamOp.AmmoBalance))return false;
        if(op==TeamOp.Access){
            bool allowed=r.ReadBoolean();
            if(target==Main.LocalPlayer.team){team=target;unlocked=allowed;if(!allowed)ClearClient();}
            return true;
        }
        if(target!=Main.LocalPlayer.team){
            switch(op){
                case TeamOp.Detail:
                    r.ReadInt32();r.ReadInt32();r.ReadInt32();r.ReadBoolean();r.ReadBytes(16);r.ReadInt32();r.ReadBytes(r.ReadByte()*16);break;
                case TeamOp.DetailEnd:r.ReadInt32();break;
                case TeamOp.AmmoBalance:r.ReadInt32();r.ReadInt32();r.ReadInt32();break;
                case TeamOp.CollectResult:r.ReadInt32();r.ReadBoolean();break;
                case TeamOp.AmmoGrant:r.ReadInt32();r.ReadInt32();r.ReadInt32();ItemIO.Receive(r,true);break;
            }
            return true;
        }
        if(op==TeamOp.AmmoBalance){
            int id=r.ReadInt32(),backed=r.ReadInt32(),total=r.ReadInt32();
            if(id==grant){if(backed==0){grant=remaining=0;ammo=new();retryAmmo=0;}else ammo.stack=Math.Max(remaining,total-Math.Max(0,backed-remaining));}
            return true;
        }
        if(op==TeamOp.Detail){
            int index=r.ReadInt32(),type=r.ReadInt32(),count=r.ReadInt32();bool paused=r.ReadBoolean();string group=new Guid(r.ReadBytes(16)).ToString("N");int start=r.ReadInt32(),length=r.ReadByte();
            if(index==0 && start==0)receiving.Clear();
            if(start==0)receiving.Add(new(index.ToString(),type,count,true,paused,Array.Empty<TerminalOrders.MaterialStatus>(),group));
            var previous=receiving[index];var rows=previous.Materials;
            Array.Resize(ref rows,start+length);
            for(int i=start;i<rows.Length;i++)rows[i]=new(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
            receiving[index]=previous with {Materials=rows};return true;
        }
        if(op==TeamOp.DetailEnd){int count=r.ReadInt32();Status=receiving.Take(count).ToArray();receiving.Clear();
            var groups=new List<(string,int,int,int,int)>();
            var materials=Status.Where(s=>!s.Paused).SelectMany(s=>s.Materials).GroupBy(m=>m.Type).Where(g=>g.Any(m=>m.Missing>0));
            Goals=materials.Select(g=>(g.Key,(int)Math.Min(int.MaxValue,g.Sum(m=>(long)m.Available+m.Missing)))).ToArray();
            var missing=Goals.Select(g=>g.Type).ToHashSet();
            foreach(var order in Status){
                groups.Add((order.GroupId,order.Type,0,0,0));
                foreach(var m in order.Materials.Where(m=>missing.Contains(m.Type) && (m.Missing>0 || m.Available>0)))
                    groups.Add((order.GroupId,order.Type,m.Type,m.Available,(int)Math.Min(int.MaxValue,(long)m.Available+m.Missing)));
            }
            GroupRows=groups.ToArray();return true;}
        if(op==TeamOp.CollectResult){int index=r.ReadInt32();pickups.Remove(index);if(!r.ReadBoolean())fallback[index]=Main.GameUpdateCount+45;return true;}
        grant=r.ReadInt32();weaponType=r.ReadInt32();remaining=r.ReadInt32();ammo=ItemIO.Receive(r,true);return true;
    }
}
