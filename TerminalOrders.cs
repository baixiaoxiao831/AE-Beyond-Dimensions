using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

// Mod.Call uses only CLR and Terraria types so providers do not need a reference to this mod.
// Providers must invoke calls on the game thread. The provider owns its recipe key,
// ingredient plan, ingredient consumption and crafting, and retains ownership if delivery fails.
public sealed class TerminalOrders : ModPlayer
{
    internal sealed record Provider(Mod Mod,
        Func<Player, string, int, (bool Ready, (int Type, int Required, int Available, int ToCraft)[] Materials, long? Bytes)> Quote,
        Func<Player, string, int, string, bool> Submit);
    private sealed record Pending(string Provider, string RecipeKey, string ResultKey, int Quantity, long Sequence, bool Waiting=false,bool Paused=false);
    internal readonly record struct MaterialStatus(int Type,int Available,int Planned,int Missing);
    internal sealed record StatusEntry(string Ticket,int Type,int Quantity,bool Waiting,bool Paused,MaterialStatus[] Materials,string GroupId="");
    private readonly Dictionary<string,MaterialStatus[]> detail=new();
    internal static Func<bool>? NewGroupsFirst;
    private (string Ticket,int Product,int Type,int Have,int Need)[]? groupedBookmarks;
    internal (string,int,int,int,int)[] GroupedBookmarks()
    {
        if(Main.netMode==NetmodeID.MultiplayerClient)return TeamSupply.GroupRows;
        if(groupedBookmarks!=null)return groupedBookmarks;
        var entries=new List<(string,int,int,int,int)>();
        var missingTypes=BookmarkGoals.Select(g=>g.Type).ToHashSet();
        foreach(var (ticket,order) in pending.Where(p=>p.Value.Waiting).OrderBy(p=>p.Value.Sequence)){
            int product=ResolveResultType(order.ResultKey);
            entries.Add((ticket,product,0,0,0));
            if(order.Paused)continue;
            foreach(var m in detail.GetValueOrDefault(ticket) ?? Array.Empty<MaterialStatus>())
                if(missingTypes.Contains(m.Type) && (m.Missing>0 || m.Available>0))
                    entries.Add((ticket,product,m.Type,m.Available,(int)Math.Min(int.MaxValue,(long)m.Available+m.Missing)));
        }
        return groupedBookmarks=entries.ToArray();
    }
    internal void CancelBookmarkOrder(string id){
        if(Main.netMode==NetmodeID.MultiplayerClient){
            if(TeamSupply.Active && Guid.TryParseExact(id,"N",out var ticket)){
                var msg=TeamNetwork.Packet(TeamOp.CancelGroup,Player.team);msg.Write(ticket.ToByteArray());msg.Send();
            }
        }else ChangeWaiting(id,true);
    }
    internal void ReorderGroups(string[] tickets)
    {
        if(Main.netMode==NetmodeID.MultiplayerClient){
            if(!TeamSupply.Active || tickets.Length>128 || tickets.Any(t=>!Guid.TryParseExact(t,"N",out _)))return;
            var p=TeamNetwork.Packet(TeamOp.ReorderGroups,Player.team);p.Write((byte)tickets.Length);
            foreach(string t in tickets)p.Write(Guid.Parse(t).ToByteArray());p.Send();return;
        }
        var ordered=pending.Where(p=>p.Value.Waiting).OrderBy(p=>p.Value.Sequence).ToArray();
        if(tickets.Length!=ordered.Length || tickets.Distinct().Count()!=tickets.Length ||
            tickets.Any(t=>!pending.TryGetValue(t,out var row)||!row.Waiting))return;
        for(int i=0;i<tickets.Length;i++)
            pending[tickets[i]]=pending[tickets[i]] with {Sequence=ordered[i].Value.Sequence};
        groupedBookmarks=null;bookmarkGoals=null;pendingStockPreview=null;checkDelay=0;
    }
    private (int Type,int Need)[]? bookmarkGoals;
    internal (int Type,int Need)[] BookmarkGoals => Main.netMode==NetmodeID.MultiplayerClient?TeamSupply.Goals:bookmarkGoals ??= pending
        .Where(p=>p.Value.Waiting && !p.Value.Paused)
        .SelectMany(p=>detail.GetValueOrDefault(p.Key) ?? Array.Empty<MaterialStatus>())
        .GroupBy(row=>row.Type).Where(g=>g.Any(row=>row.Missing>0))
        .Select(g=>(g.Key,(int)Math.Min(int.MaxValue,g.Sum(row=>(long)row.Available+row.Missing)))).ToArray();
    internal int PendingCount=>TeamClient.Open?TeamClient.Orders.Count:pending.Count;
    internal StatusEntry[] StatusEntries()=>TeamSupply.Active?TeamSupply.Status:pending.OrderBy(p=>p.Value.Sequence).Select(p=>
        new StatusEntry(p.Key,ResolveResultType(p.Value.ResultKey),p.Value.Quantity,p.Value.Waiting,p.Value.Paused,
            detail.GetValueOrDefault(p.Key) ?? Array.Empty<MaterialStatus>())).ToArray();
    internal void ChangeWaiting(string ticket,bool cancel)
    {
        if(TeamClient.Open){if(int.TryParse(ticket,out var index))TeamClient.Action(cancel?TeamOp.Cancel:TeamOp.Pause,TeamTab.Orders,index);return;}
        if (!Local || !pending.TryGetValue(ticket,out var order) || !order.Waiting || delivering.Contains(ticket)) return;
        groupedBookmarks=null;
        bookmarkGoals=null;
        if (cancel) RemovePending(ticket);
        else pending[ticket]=order with {Paused=!order.Paused};
        checkDelay=0;
    }
    private readonly Dictionary<string,(int Type,int Count)[]> shortages = new();
    internal Dictionary<string,string> Route=new();
    private readonly Dictionary<string,Dictionary<string,string>> routes=new();
    internal Dictionary<string,string>? RouteFor(string ticket)=>routes.GetValueOrDefault(ticket);
    internal Dictionary<int,int> ReservedFor(string ticket)=>ReservedBefore(pending[ticket].Sequence);
    internal Dictionary<int,int> ReservedBefore(long sequence) => TeamSupply.Active?TeamSupply.Status.Where(s=>!s.Paused).SelectMany(s=>s.Materials)
        .Where(m=>m.Available>0).GroupBy(m=>m.Type).ToDictionary(g=>g.Key,g=>(int)Math.Min(int.MaxValue,g.Sum(m=>(long)m.Available))):pending
        .Where(p=>p.Value.Waiting && !p.Value.Paused && p.Value.Sequence<sequence)
        .SelectMany(p=>detail.GetValueOrDefault(p.Key) ?? Array.Empty<MaterialStatus>())
        .Where(m=>m.Available>0).GroupBy(m=>m.Type)
        .ToDictionary(g=>g.Key,g=>(int)Math.Min(int.MaxValue,g.Sum(m=>(long)m.Available)));
    private int checkDelay;
    internal bool HasWaiting => TeamSupply.Active?TeamSupply.Status.Any(s=>!s.Paused):pending.Values.Any(order=>order.Waiting && !order.Paused);

    internal IEnumerable<(int Type,int Quantity,string Ticket,(int Type,int Count)[] Missing)> MissingGroups => TeamSupply.Active?TeamSupply.Status.Where(s=>!s.Paused).Select((s)=>(s.Type,s.Quantity,(int.Parse(s.Ticket)*40503%65536).ToString("X4"),s.Materials.Where(m=>m.Missing>0).Select(m=>(m.Type,m.Missing)).ToArray())):
        pending.Where(p=>p.Value.Waiting && !p.Value.Paused).OrderBy(p=>p.Value.Sequence)
            .Select(p=>(ResolveResultType(p.Value.ResultKey),p.Value.Quantity,p.Key,
                shortages.GetValueOrDefault(p.Key) ?? Array.Empty<(int,int)>()));

    internal bool QueueMissing()
    {
        if(TeamClient.Open)return TeamClient.Order(ResultType,Quantity);
        if (!Local || SelectedProvider == null || quotedQuantity != Quantity || Ready) return false;
        var missing=Materials.Select((row,i)=>(row.Type,Count:row.Required-row.Available-ToCraft[i]))
            .Where(row=>row.Count>0).ToArray();
        if (missing.Length==0) return false;
        string ticket=Guid.NewGuid().ToString("N");
        pending.Add(ticket,new Pending(SelectedProvider.Mod.Name,RecipeKey,ResultKey(ResultType),Quantity,nextSequence++,true));
        if(NewGroupsFirst?.Invoke()==true)
            ReorderGroups(pending.Where(p=>p.Value.Waiting).OrderByDescending(p=>p.Key==ticket).ThenBy(p=>p.Value.Sequence).Select(p=>p.Key).ToArray());
        routes[ticket]=new(Route);
        checkDelay=0;
        shortages[ticket]=missing;
        detail[ticket]=Materials.Select((row,i)=>new MaterialStatus(row.Type,row.Available,ToCraft[i],
            Math.Max(0,row.Required-row.Available-ToCraft[i]))).ToArray();
        pendingStockPreview=null;
        groupedBookmarks=null;
        bookmarkGoals=null;
        quotedQuantity=0;
        return true;
    }

    public override void PostUpdate()
    {
        if (!Local || Player.dead || pending.Count==0 || !Player.GetModPlayer<TerminalStorage>().CanAccess(false)) return;
        if (checkDelay>0) { checkDelay--; return; }
        checkDelay=59;
        foreach(var (ticket,order) in pending.Where(p=>p.Value.Waiting && !p.Value.Paused).OrderBy(p=>p.Value.Sequence).ToArray())
            if(pending.ContainsKey(ticket))CheckWaiting(ticket,order);
    }

    private void CheckWaiting(string ticket,Pending order)
    {
        groupedBookmarks=null;
        bookmarkGoals=null;
        int type=ResolveResultType(order.ResultKey);
        if (!ValidType(type)) return;
        try {
            Player.GetModPlayer<TerminalWorkArea>().WithStations(() => {
                if (order.Provider==Mod.Name) {
                    if (!InstantCraft.TryWaiting(Player,type,order.Quantity,ticket,out var missing,out var materials)) {
                        shortages[ticket]=missing;
                        detail[ticket]=materials;
                    }
                } else if (ModLoader.TryGetMod(order.Provider,out var owner) && providers.TryGetValue(owner,out var provider)) {
                    var plan=provider.Quote(Player,order.RecipeKey,order.Quantity);
                    var reserved=ReservedBefore(order.Sequence);
                    var rows=plan.Materials.Where(row=>ValidType(row.Type)).Select(row=>{
                        int available=Math.Min(Math.Max(0,row.Required-row.ToCraft),Math.Max(0,row.Available-reserved.GetValueOrDefault(row.Type)));
                        return new MaterialStatus(row.Type,available,row.ToCraft,Math.Max(0,row.Required-available-row.ToCraft));
                    }).ToArray();
                    var missing=rows.Where(m=>m.Missing>0).Select(m=>(m.Type,m.Missing)).ToArray();
                    shortages[ticket]=missing;
                    detail[ticket]=rows;
                    if (plan.Ready && missing.Length==0) {
                        pending[ticket]=order with { Waiting=false };
                        if (!provider.Submit(Player,order.RecipeKey,order.Quantity,ticket) && pending.ContainsKey(ticket))
                            pending[ticket]=order;
                    }
                }
                return true;
            });
        } catch(Exception e) {
            Mod.Logger.Warn("Could not process queued order "+ticket,e);
            checkDelay=299;
        }
    }
    private static readonly Dictionary<Mod, Provider> providers = new();
    private Dictionary<string, Pending> pending = new();
    private long nextSequence;
    private (int Type, long Count)[]? pendingStockPreview;
    private HashSet<string> delivering = new();
    internal Provider? SelectedProvider { get; private set; }
    internal string RecipeKey { get; private set; } = "";
    internal int ResultType { get; private set; }
    internal int Quantity { get; set; } = 1;
    internal bool Ready { get; private set; }
    internal long? QuoteBytes { get; private set; }
    internal string Status { get; private set; } = "";
    internal List<InstantCraft.Node> Tree { get; set; } = new();
    internal (string Name,int Item)[] MissingStations { get; set; } = Array.Empty<(string,int)>();
    internal string PlanReason { get; set; } = "";
    internal (int Type, int Required, int Available)[] Materials { get; private set; } = Array.Empty<(int,int,int)>();
    internal int[] ToCraft { get; private set; } = Array.Empty<int>();
    private int quotedQuantity;
    private string? viewedTicket;

    internal IReadOnlyList<(int Type, long Count)> PendingStockPreview => TeamClient.Open?TeamClient.Preview:pendingStockPreview ??=
        pending.Values.OrderBy(order => order.Sequence).GroupBy(order => order.ResultKey)
            .Select(group => (Type: ResolveResultType(group.Key), Count: group.Sum(order => (long)order.Quantity)))
            .Where(row => ValidType(row.Type)).ToArray();

    internal static void UnloadProviders() {providers.Clear();NewGroupsFirst=null;}
    public override void Initialize()
    {
        pending = new();
        pendingStockPreview = null;
        nextSequence = 0;
        delivering = new();
    }
    private bool Local => Main.netMode == NetmodeID.SinglePlayer && Player == Main.LocalPlayer && Player.active;
    private static bool ValidType(int type) => type > 0 && type < ItemLoader.ItemCount;
    internal static string ResultKey(int type) => type < ItemID.Count ? "Terraria/" + ItemID.Search.GetName(type) : ModContent.GetModItem(type).FullName;

    internal static int ResolveResultType(string key)
    {
        const string vanilla = "Terraria/";
        if (key.StartsWith(vanilla, StringComparison.Ordinal))
            return ItemID.Search.TryGetId(key[vanilla.Length..], out int type) ? type : 0;
        return ModContent.TryFind<ModItem>(key, out var item) ? item.Type : 0;
    }

    private bool RemovePending(string ticket)
    {
        if (!pending.Remove(ticket)) return false;
        shortages.Remove(ticket);
        detail.Remove(ticket);
        routes.Remove(ticket);
        checkDelay=0;
        groupedBookmarks=null;
        bookmarkGoals=null;
        pendingStockPreview = null;
        return true;
    }

    internal static object Dispatch(object[] args)
    {
        switch (args) {
            case ["RegisterOrderProvider", Mod mod,
                Func<Player,string,int,(bool,(int,int,int)[])> quote, Func<Player,string,int,string,bool> submit]:
                return providers.TryAdd(mod,new Provider(mod,(player,key,count) => {
                    var (ready,materials) = quote(player,key,count);
                    return (ready,materials.Select(row => (row.Item1,row.Item2,row.Item3,0)).ToArray(),null);
                },submit));
            case ["RegisterOrderProvider", Mod mod,
                Func<Player,string,int,(bool,(int,int,int)[],long)> quote, Func<Player,string,int,string,bool> submit]:
                return providers.TryAdd(mod,new Provider(mod,(player,key,count) => {
                    var (ready,materials,bytes) = quote(player,key,count);
                    return (ready,materials.Select(row => (row.Item1,row.Item2,row.Item3,0)).ToArray(),bytes);
                },submit));
            case ["RegisterOrderProvider", Mod mod,
                Func<Player,string,int,(bool,(int,int,int,int)[],long?)> quote, Func<Player,string,int,string,bool> submit]:
                return providers.TryAdd(mod,new Provider(mod,quote,submit));
            case ["OpenOrder", Mod mod, Player player, string key, var itemType, int count] when itemType is int or short or ushort:
                if (!player.GetModPlayer<TerminalStorage>().CanAccess(true)) return false;
                if (!player.GetModPlayer<TerminalOrders>().Select(mod,key,Convert.ToInt32(itemType),count)) return false;
                if (!Main.dedServ) ModContent.GetInstance<TerminalSystem>().OpenOrders();
                return true;
            case ["CompleteOrder", Mod mod, Player player, string ticket, Item[] outputs]:
                return player.GetModPlayer<TerminalOrders>().Complete(mod,ticket,outputs);
            case ["CancelOrder", Mod mod, Player player, string ticket]:
                return player.GetModPlayer<TerminalOrders>().Cancel(mod,ticket);
            case ["GetPendingOrders", Mod mod, Player player]:
                return player.GetModPlayer<TerminalOrders>().PendingFor(mod);
            default:
                throw new ArgumentException("Unknown terminal order call or invalid arguments.");
        }
    }

    internal bool Select(Mod mod,string key,int type,int count)
    {
        if ((!Local && !(TeamClient.Open && mod==Mod)) || Player.dead || !providers.TryGetValue(mod,out var provider) || string.IsNullOrWhiteSpace(key) || !ValidType(type) || count <= 0) return false;
        Route=new();
        SelectedProvider=provider;
        RecipeKey=key;
        ResultType=type;
        Quantity=count;
        viewedTicket=null;
        InvalidateQuote();
        return true;
    }

    internal void InvalidateQuote()
    {
        quotedQuantity=0;
        Tree.Clear();
        MissingStations=Array.Empty<(string,int)>();
        PlanReason="";
        Materials=Array.Empty<(int,int,int)>();
        ToCraft=Array.Empty<int>();
        QuoteBytes=null;
        Ready=false;
        Status="";
    }

    internal bool RefreshQuote()
    {
        InvalidateQuote();
        if ((!Local && !TeamClient.Open) || SelectedProvider == null || Quantity <= 0) return false;
        try {
            var plan=Player.GetModPlayer<TerminalWorkArea>().WithStations(() => SelectedProvider.Quote(Player,RecipeKey,Quantity));
            if (plan.Bytes < 0 || plan.Materials == null ||
                plan.Materials.Any(row => !ValidType(row.Type) || row.Required < 0 || row.Available < 0 || row.ToCraft < 0))
                throw new ArgumentException("Invalid material plan from order provider.");
            Materials=plan.Materials.Select(row => (row.Type,row.Required,row.Available)).ToArray();
            ToCraft=plan.Materials.Select(row => row.ToCraft).ToArray();
            QuoteBytes=plan.Bytes;
            Ready=plan.Ready && plan.Materials.All(row => (long)row.Available + row.ToCraft >= row.Required);
            quotedQuantity=Quantity;
            Status=PlanReason;
            return true;
        } catch(Exception e) {
            Mod.Logger.Warn("Order provider failed to quote "+RecipeKey,e);
            Status=TerminalText.Get("无法取得材料清单");
            return false;
        }
    }

    internal bool Submit()
    {
        if(TeamClient.Open)return TeamClient.Order(ResultType,Quantity);
        if (!Local || SelectedProvider == null || !Ready || quotedQuantity != Quantity) return false;
        var previous=Materials;
        var previousToCraft=ToCraft;
        var previousBytes=QuoteBytes;
        if (!RefreshQuote() || !Ready) return false;
        if (!Materials.SequenceEqual(previous) || !ToCraft.SequenceEqual(previousToCraft) || QuoteBytes != previousBytes) {
            Status=TerminalText.Get("计划已更新，请重新确认");
            return false;
        }
        var provider=SelectedProvider;
        string ticket=Guid.NewGuid().ToString("N");
        pending.Add(ticket,new Pending(provider.Mod.Name,RecipeKey,ResultKey(ResultType),Quantity,nextSequence++));
        detail[ticket]=Materials.Select((row,i)=>new MaterialStatus(row.Type,row.Available,ToCraft[i],0)).ToArray();
        pendingStockPreview=null;
        viewedTicket=ticket;
        Ready=false;
        Status=TerminalText.Get("已提交");
        try {
            bool accepted=Player.GetModPlayer<TerminalWorkArea>().WithStations(() => provider.Submit(Player,RecipeKey,Quantity,ticket));
            // Providers can complete synchronously during Submit.
            if (!accepted && RemovePending(ticket)) {
                Status=TerminalText.Get("提供方未接受订单");
                return false;
            }
        } catch(Exception e) {
            Mod.Logger.Warn("Order provider failed during submit "+ticket,e);
            if(provider.Mod==Mod && RemovePending(ticket)){Status=TerminalText.Get("合成未完成，请重新查看计划");return false;}
            if (pending.ContainsKey(ticket)) Status=TerminalText.Get("等待提供方确认");
        }
        return true;
    }

    private bool Owns(Mod mod,string ticket,out Pending order) =>
        pending.TryGetValue(ticket,out order!) && order.Provider == mod.Name && providers.ContainsKey(mod) && Local;

    private (string Ticket,string RecipeKey,string ResultKey,int Quantity)[] PendingFor(Mod mod)
    {
        if (!Local || !providers.ContainsKey(mod)) return Array.Empty<(string,string,string,int)>();
        return pending.Where(p => p.Value.Provider == mod.Name && !p.Value.Waiting)
            .Select(p => (p.Key,p.Value.RecipeKey,p.Value.ResultKey,p.Value.Quantity)).ToArray();
    }

    internal bool Complete(Mod mod,string ticket,Item[] outputs,int stationOutput=0)
    {
        if (!Owns(mod,ticket,out var order) || outputs.Length == 0 ||
            outputs.Any(i => i == null || i.IsAir || !ValidType(i.type))) return false;
        long count=outputs.Where(i => ResultKey(i.type)==order.ResultKey).Sum(i => (long)i.stack);
        if (count < order.Quantity) return false;
        // Prevent a second delivery from a reentrant item hook. The ticket remains
        // retryable if cloning/validation fails before the storage transaction commits.
        if (!delivering.Add(ticket)) return false;
        try {
            if (!Player.GetModPlayer<TerminalStorage>().ReceiveOutputs(outputs,stationOutput)) return false;
            RemovePending(ticket);
            if (viewedTicket==ticket) Status=Player.GetModPlayer<TerminalSearchSettings>().DeliverToBag ? TerminalText.Get("产物已交付") : TerminalText.Get("产物已入库");
            return true;
        } finally {
            delivering.Remove(ticket);
        }
    }

    private bool Cancel(Mod mod,string ticket)
    {
        if (!Owns(mod,ticket,out _) || delivering.Contains(ticket)) return false;
        RemovePending(ticket);
        if (viewedTicket==ticket) Status=TerminalText.Get("订单已取消");
        return true;
    }

    private TagCompound? characterOrders;
    internal void SelectOrders(TeamBank bank){
        if(characterOrders==null){characterOrders=new();WriteOrders(characterOrders);}
        var rows=new List<TagCompound>();
        foreach(var item in bank.Orders){
            if(!bank.OrderData.TryGetValue(item,out var row))
                row=new TagCompound{["ticket"]=Guid.NewGuid().ToString("N"),["provider"]=Mod.Name,
                    ["recipe"]=item.type.ToString(),["result"]=ResultKey(item.type),["waiting"]=true};
            row["quantity"]=item.stack;row["paused"]=bank.Paused.Contains(item);row["sequence"]=(long)rows.Count;
            rows.Add(row);
        }
        LoadData(new TagCompound{["pending"]=rows});
    }
    internal void CaptureOrders(TeamBank bank){
        var data=new TagCompound();WriteOrders(data);
        bank.Orders.Clear();bank.OrderData.Clear();bank.Details.Clear();bank.Paused.Clear();
        foreach(var row in data.GetList<TagCompound>("pending")){
            int type=ResolveResultType(row.GetString("result"));if(type<=0)continue;
            var item=new Item(type);item.stack=row.GetInt("quantity");bank.Orders.Add(item);bank.OrderData[item]=row;
            if(row.GetBool("paused"))bank.Paused.Add(item);
        }
    }
    internal void ReleaseOrders(){
        if(characterOrders!=null){LoadData(characterOrders);characterOrders=null;}
    }
    public override void SaveData(TagCompound tag){
        if(characterOrders==null)WriteOrders(tag);
        else if(characterOrders.ContainsKey("pending"))tag["pending"]=characterOrders["pending"];
    }
    private void WriteOrders(TagCompound tag)
    {
        if (pending.Count > 0)
            tag["pending"] = pending.OrderBy(p => p.Value.Sequence).Select(p => new TagCompound {["ticket"]=p.Key,["provider"]=p.Value.Provider,
                ["recipe"]=p.Value.RecipeKey,["result"]=p.Value.ResultKey,["quantity"]=p.Value.Quantity,
                ["route"]=(RouteFor(p.Key) ?? new()).Select(pair=>new TagCompound {["key"]=pair.Key,["choice"]=pair.Value}).ToList(),
                ["waiting"]=p.Value.Waiting,["paused"]=p.Value.Paused,["sequence"]=p.Value.Sequence,
                ["materials"]=(detail.GetValueOrDefault(p.Key) ?? Array.Empty<MaterialStatus>()).Select(m=>new TagCompound {
                    ["item"]=ResultKey(m.Type),["available"]=m.Available,["planned"]=m.Planned,["missing"]=m.Missing}).ToList()}).ToList();
    }

    public override void LoadData(TagCompound tag)
    {
        groupedBookmarks=null;
        bookmarkGoals=null;
        pending.Clear();
        shortages.Clear();
        detail.Clear();
        routes.Clear();
        checkDelay=0;
        pendingStockPreview=null;
        nextSequence=0;
        foreach(var row in tag.GetList<TagCompound>("pending")) {
            string ticket=row.GetString("ticket");
            if (Guid.TryParseExact(ticket,"N",out _) && row.GetInt("quantity")>0)
            {
                long sequence=row.ContainsKey("sequence") ? row.GetLong("sequence") : nextSequence;
                pending[ticket]=new Pending(row.GetString("provider"),row.GetString("recipe"),row.GetString("result"),row.GetInt("quantity"),sequence,row.GetBool("waiting"),row.GetBool("paused"));
                detail[ticket]=row.GetList<TagCompound>("materials").Select(m=>new MaterialStatus(ResolveResultType(m.GetString("item")),
                    m.GetInt("available"),m.GetInt("planned"),m.GetInt("missing"))).Where(m=>ValidType(m.Type)).ToArray();
                routes[ticket]=new();
                foreach(var pin in row.GetList<TagCompound>("route"))routes[ticket][pin.GetString("key")]=pin.GetString("choice");
                shortages[ticket]=detail[ticket].Where(m=>m.Missing>0).Select(m=>(m.Type,m.Missing)).ToArray();
                nextSequence=Math.Max(nextSequence,sequence+1);
            }
        }
    }
}
