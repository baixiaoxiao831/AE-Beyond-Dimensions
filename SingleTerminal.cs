using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

internal static class SingleTerminal
{
    private static readonly Dictionary<Guid,Point16> last=new();
    private static Player? current;
    private static TeamBank? active;
    internal static byte Color {get;private set;}
    internal static bool Selected=>active!=null;
    internal static void Restore(Player player){
        if(last.TryGetValue(player.GetModPlayer<TeamIdentity>().Key,out var a) &&
           ControllerBinding.TryTile(a.X,a.Y,out var tile) && a==tile)Select(player,a);
    }
    internal static void Select(Player player,Point16 a){
        var id=player.GetModPlayer<TeamIdentity>().Key;
        Color=ControllerBinding.ColorAt(a);
        string key=Color==0?ControllerBinding.Neutral(a):TeamWorld.KeyFor(Color,id);
        if(!TeamWorld.Banks.TryGetValue(key,out var bank))TeamWorld.Banks[key]=bank=new();
        last[id]=a;
        if(current==player && active==bank)return;
        current?.GetModPlayer<TerminalBench>().Refund();
        Capture();TerminalCursor.Clear();
        current=player;active=bank;
        player.GetModPlayer<TerminalStorage>().SelectStock(bank);
        player.GetModPlayer<TerminalWorkArea>().SelectStations(bank);
        player.GetModPlayer<TerminalOrders>().SelectOrders(bank);
    }
    internal static void MergeLegacy(){
        if(current==null || active==null)return;
        var transfer=current.GetModPlayer<LegacyStockTransfer>();
        if(!transfer.HasWork)return;
        var a=last[current.GetModPlayer<TeamIdentity>().Key];
        string key=Color==0?ControllerBinding.Neutral(a):TeamWorld.KeyFor(Color,Guid.Empty);
        lock(LegacyStockTransfer.WorldGate){
            Capture();
            if(transfer.MergeSingle(key,active))current.GetModPlayer<TerminalStorage>().SelectStock(active);
        }
    }
    private static void Capture(){
        if(current==null || active==null)return;
        active.Stock=current.GetModPlayer<TerminalStorage>().Slots.Where(i=>!i.IsAir).ToList();
        current.GetModPlayer<TerminalOrders>().CaptureOrders(active);
    }
    internal static void Save(TagCompound tag){
        if(Main.netMode==NetmodeID.SinglePlayer)Capture();
        tag["lastControllers"]=last.Select(p=>new TagCompound{["id"]=p.Key.ToString("N"),["x"]=(int)p.Value.X,["y"]=(int)p.Value.Y}).ToList();
    }
    internal static void Load(TagCompound tag){
        foreach(var row in tag.GetList<TagCompound>("lastControllers"))
            if(Guid.TryParse(row.GetString("id"),out var id))last[id]=new Point16(row.GetInt("x"),row.GetInt("y"));
    }
    internal static void Clear(){
        current?.GetModPlayer<TerminalStorage>().ReleaseStock();
        current?.GetModPlayer<TerminalWorkArea>().ReleaseStations();
        current?.GetModPlayer<TerminalOrders>().ReleaseOrders();
        current=null;active=null;Color=0;last.Clear();
    }
}
