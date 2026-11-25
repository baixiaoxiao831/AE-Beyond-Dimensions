using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

internal static class ForgeTransfers
{
    private sealed record Delivery(Guid Ticket,Item Item);
    private static readonly Dictionary<Guid,Delivery> pending=new();

    internal static bool Begin(int who,TeamBank bank,int index,int token){
        Guid owner=TeamNetwork.Owner(who);
        lock(LegacyStockTransfer.WorldGate){
            if(pending.ContainsKey(owner)){Resend(owner,who,token);return false;}
            pending.Add(owner,new(Guid.NewGuid(),bank.Stock[index]));
            bank.Stock.RemoveAt(index);
        }
        Resend(owner,who,token);return true;
    }
    internal static void Resend(Guid owner,int who,int token){
        if(!pending.TryGetValue(owner,out var delivery))return;
        var packet=TeamNetwork.Packet(TeamOp.ForgeItem,token);
        packet.Write(delivery.Ticket.ToByteArray());
        ItemIO.Send(delivery.Item,packet,true,true);packet.Send(who);
    }
    internal static void Confirm(Guid owner,Guid ticket){
        lock(LegacyStockTransfer.WorldGate)
            if(pending.TryGetValue(owner,out var delivery) && delivery.Ticket==ticket)pending.Remove(owner);
    }
    internal static void Save(TagCompound tag){
        if(pending.Count>0)tag["forgeDeliveries"]=pending.Select(pair=>new TagCompound{
            ["owner"]=pair.Key.ToString("N"),["ticket"]=pair.Value.Ticket.ToString("N"),
            ["item"]=ItemIO.Save(pair.Value.Item)}).ToList();
    }
    internal static void Load(TagCompound tag){
        foreach(var row in tag.GetList<TagCompound>("forgeDeliveries"))
            pending[Guid.Parse(row.GetString("owner"))]=new(Guid.Parse(row.GetString("ticket")),ItemIO.Load(row.GetCompound("item")));
    }
    internal static void Clear()=>pending.Clear();
}
