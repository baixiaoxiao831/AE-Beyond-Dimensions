using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

internal sealed class TeamBank
{
    internal List<Item> Stock=new(), Stations=new();
    internal readonly List<Item> Orders=new();
    internal readonly HashSet<Item> Paused=new();
    internal readonly Dictionary<Item,TerminalOrders.MaterialStatus[]> Details=new();
    internal bool DetailChanged;
    internal void UpdateDetail(Item order,TerminalOrders.MaterialStatus[] rows){
        if(Details.TryGetValue(order,out var previous) && previous.SequenceEqual(rows))return;
        Details[order]=rows;DetailChanged=true;
    }
    internal readonly Dictionary<Item,TagCompound> OrderData=new();
    internal Guid GroupId(Item order){
        if(!OrderData.TryGetValue(order,out var data)){
            data=new TagCompound{["provider"]=ModContent.GetInstance<ProbeMod>().Name,["recipe"]=order.type.ToString(),
                ["result"]=TerminalOrders.ResultKey(order.type),["waiting"]=true};
            OrderData.Add(order,data);
        }
        if(Guid.TryParseExact(data.GetString("ticket"),"N",out var ticket))return ticket;
        ticket=Guid.NewGuid();data["ticket"]=ticket.ToString("N");return ticket;
    }
    internal int Revision;
    internal void Store(Item item)
    {
        if(item.IsAir)return;
        foreach(var existing in Stock){
            if(existing.type!=item.type || existing.stack>=existing.maxStack)continue;
            ItemLoader.TryStackItems(existing,item,out _);
            if(item.IsAir || item.stack<=0)return;
        }
        Stock.Add(item);
    }
    internal TagCompound Save()=>new(){
        ["stock"]=TeamSupply.StockView(this).Concat(SharedActions.Reserved(this)).Select(ItemIO.Save).ToList(),
        ["stations"]=Stations.Select(ItemIO.Save).ToList(),
        ["paused"]=Orders.Select((item,index)=>(item,index)).Where(row=>Paused.Contains(row.item)).Select(row=>row.index).ToList(),
        ["orders"]=Orders.Select(ItemIO.Save).ToList(),
        ["orderData"]=Orders.Select(order=>OrderData.GetValueOrDefault(order) ?? new TagCompound()).ToList()
    };
    internal static TeamBank Load(TagCompound tag){
        List<Item> Read(string key)=>tag.GetList<TagCompound>(key).Select(ItemIO.Load).Where(i=>!i.IsAir).ToList();
        var bank=new TeamBank{Stock=Read("stock"),Stations=Read("stations")};
        bank.Orders.AddRange(Read("orders"));
        foreach(int index in tag.GetList<int>("paused"))if((uint)index<bank.Orders.Count)bank.Paused.Add(bank.Orders[index]);
        var metadata=tag.GetList<TagCompound>("orderData");
        for(int i=0;i<bank.Orders.Count && i<metadata.Count;i++)if(metadata[i].Count>0)bank.OrderData[bank.Orders[i]]=metadata[i];
        return bank;
    }
}

// A private character key is exchanged only with the server, never with other viewers.
public sealed class TeamIdentity : ModPlayer
{
    internal Guid Key=Guid.NewGuid();
    public override void Initialize()=>Key=Guid.NewGuid();
    public override void SaveData(TagCompound tag)=>tag["key"]=Key.ToString("N");
    public override void LoadData(TagCompound tag){if(Guid.TryParse(tag.GetString("key"),out var value) && value!=Guid.Empty)Key=value;}
    public override void PostUpdate(){
        if(Main.netMode==NetmodeID.MultiplayerClient && Player==Main.LocalPlayer && !Player.dead)TeamSupply.ClientTick(Player,Key);
    }
    public override void OnEnterWorld(){if(Main.netMode==NetmodeID.SinglePlayer)SingleTerminal.Restore(Player);}
    public override void PlayerDisconnect()=>TeamNetwork.Forget(Player.whoAmI);
}

public sealed class TeamWorld : ModSystem
{
    internal static readonly Dictionary<string,TeamBank> Banks=new();
    internal static readonly HashSet<Guid> Unlocked=new();
    internal static string KeyFor(int team,Guid owner)=>team is >=1 and <=5?"team/"+team:"player/"+owner.ToString("N");

    public override void ClearWorld(){SingleTerminal.Clear();LegacyStockTransfer.Clear();ForgeTransfers.Clear();Banks.Clear();Unlocked.Clear();TeamNetwork.Reset();}
    public override void SaveWorldData(TagCompound tag){
        lock(LegacyStockTransfer.WorldGate){
            SingleTerminal.Save(tag);LegacyStockTransfer.SaveWorld(tag);ForgeTransfers.Save(tag);
            if(Banks.Count>0)tag["terminals"]=Banks.Select(p=>new TagCompound{["key"]=p.Key,["bank"]=p.Value.Save()}).ToList();
            if(Unlocked.Count>0)tag["terminalUsers"]=Unlocked.Select(id=>id.ToString("N")).ToList();
    }
    }
    public override void LoadWorldData(TagCompound tag){
        SingleTerminal.Load(tag);LegacyStockTransfer.LoadWorld(tag);ForgeTransfers.Load(tag);
        foreach(var row in tag.GetList<TagCompound>("terminals"))Banks[row.GetString("key")]=TeamBank.Load(row.GetCompound("bank"));
        foreach(var key in tag.GetList<string>("terminalUsers"))if(Guid.TryParse(key,out var id))Unlocked.Add(id);
    }
    public override void PostUpdateWorld(){
        if(Main.netMode==NetmodeID.Server)TeamNetwork.Tick();
    }
}
