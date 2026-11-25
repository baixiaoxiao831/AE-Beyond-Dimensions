using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

internal sealed class InstantCraft : ModSystem
{
    private static Dictionary<int,Recipe[]> recipes = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player,Plan> quotes=new();
    private static ProbeMod Owner => ModContent.GetInstance<ProbeMod>();
    internal static bool ExchangeItem(int type)
    {
        if(ItemID.Sets.BossBag[type] || ItemID.Sets.IsFishingCrate[type])return true;
        var item=ContentSamples.ItemsByType[type];
        string name=item.ModItem?.Name ?? ItemID.Search.GetName(type);
        return item.createTile>=0 && name.Contains("Trophy",StringComparison.OrdinalIgnoreCase);
    }
    internal static bool AllowedRecipe(Recipe r) => !ExchangeItem(r.createItem.type) &&
        r.requiredItem.All(item=>!ExchangeItem(item.type) || Alternatives(r,item.type).Length>0);

    public override void PostSetupRecipes()
    {
        recipes=Main.recipe.Take(Recipe.numRecipes).Where(r=>r!=null && !r.Disabled && !r.createItem.IsAir && AllowedRecipe(r))
            .GroupBy(r=>r.createItem.type).ToDictionary(g=>g.Key,g=>g.ToArray());
        foreach(var group in recipes.Values)foreach(var recipe in group)RecipeKey(recipe);
        RegisterProvider();
    }
    public override void Unload() { recipes.Clear();quotes.Clear();recipeKeys.Clear(); }

    internal static bool Open(Player player,int type)
    {
        if (player.dead || Main.netMode!=NetmodeID.SinglePlayer && !TeamClient.Open) return false;
        RegisterProvider();
        return (bool)TerminalOrders.Dispatch(["OpenOrder",Owner,player,type.ToString(),type,1]);
    }

    private static void RegisterProvider() =>
        TerminalOrders.Dispatch(["RegisterOrderProvider",Owner,
            (Func<Player,string,int,(bool,(int,int,int,int)[],long?)>)Quote,
            (Func<Player,string,int,string,bool>)Submit]);

    internal static IEnumerable<Item> Inputs(Player player)
    {
        foreach (var item in (TeamSupply.Active && TeamClient.InventoryTeam==player.team?TeamClient.Stock:player.GetModPlayer<TerminalStorage>().DisplaySlots).Concat(player.inventory.Take(58)))
            if (!item.IsAir && !item.favorited) yield return item;
        if (!player.GetModPlayer<TerminalSearchSettings>().UseEquipment) yield break;
        var equipped=AccessoryProbe.AccessoryProbe.ReadArmor(player).Concat(AccessoryProbe.AccessoryProbe.Read(player))
            .Select(s=>s.Functional).Concat(player.miscEquips);
        foreach (var item in equipped)
            if (item!=null && !item.IsAir && !item.favorited) yield return item;
    }

    internal static IEnumerable<Item> InputsFor(Player player,Item output)
    {
        var stations=TeamClient.Open || TeamSupply.Active && TeamClient.InventoryTeam==player.team?TeamClient.Stations:player.GetModPlayer<TerminalWorkArea>().Stations;
        if(stations.Count>0 && TerminalWorkArea.IsStationOutput(output))
            foreach(var station in stations)
                if(!station.IsAir && !station.favorited)yield return station;
        foreach(var item in Inputs(player))yield return item;
    }

    internal static Recipe[] RecipesFor(int type) => recipes.GetValueOrDefault(type) ?? Array.Empty<Recipe>();

    internal static Dictionary<int,int> AvailableCounts() => Inputs(Main.LocalPlayer)
        .GroupBy(item => item.type)
        .ToDictionary(group => group.Key, group => (int)Math.Min(int.MaxValue, group.Sum(item => (long)item.stack)));

    private sealed record Step(Recipe Recipe,int Batches,int[][] Choices);
    internal sealed record Node(int Type,int Count,int Parent,bool Missing,Recipe? Recipe=null);
    internal static int[] Alternatives(Recipe recipe,int id) => new[]{id}.Concat(recipe.acceptedGroups
        .Select(group=>RecipeGroup.recipeGroups[group].ValidItems).Where(members=>members.Contains(id))
        .SelectMany(members=>members.OrderBy(type=>type))).Distinct().Where(type=>!ExchangeItem(type)).ToArray();

    private static readonly Dictionary<Recipe,string> recipeKeys=new();
    internal static string RecipeKey(Recipe r)
    {
        if(recipeKeys.TryGetValue(r,out var key))return key;
        var signature=string.Join("|",r.Mod?.Name ?? "Terraria",TerminalOrders.ResultKey(r.createItem.type),r.createItem.stack,
            string.Join(",",r.requiredItem.Select(i=>TerminalOrders.ResultKey(i.type)+"*"+i.stack)),
            string.Join(",",r.requiredTile.Select(t=>TileID.Search.GetName(t))),
            string.Join(",",r.acceptedGroups.Select(g=>RecipeGroup.recipeGroupIDs.FirstOrDefault(p=>p.Value==g).Key)),
            string.Join(",",r.Conditions.Select(c=>c.Description.Key)));
        // Otherwise identical recipes can have different crafting hooks.
        int ordinal=RecipesFor(r.createItem.type).TakeWhile(other=>other!=r).Count(other=>recipeKeys.TryGetValue(other,out var old) && old.StartsWith(signature+"#",StringComparison.Ordinal));
        return recipeKeys[r]=signature+"#"+ordinal;
    }
    internal static List<(int Recipe,int Slot,int Choice)> EncodeRoute(Dictionary<string,string> route){
        var pins=new List<(int,int,int)>();
        if(route.Count==0)return pins;
        var byKey=recipeKeys.ToDictionary(pair=>pair.Value,pair=>pair.Key);
        foreach(var (key,value) in route){
            if(byKey.TryGetValue(value,out var selected)){pins.Add((selected.RecipeIndex,-1,0));continue;}
            int slash=key.LastIndexOf('/');
            if(slash>0 && int.TryParse(key[(slash+1)..],out int slot) && byKey.TryGetValue(key[..slash],out var recipe))
                pins.Add((recipe.RecipeIndex,slot,TerminalOrders.ResolveResultType(value)));
        }
        return pins;
    }
    internal static List<Terraria.ModLoader.IO.TagCompound> ReadRoute(System.IO.BinaryReader reader){
        var result=new Dictionary<string,string>();
        int count=reader.ReadUInt16();
        if(count>5000)throw new System.IO.InvalidDataException("Too many recipe choices");
        for(int i=0;i<count;i++){
            int index=reader.ReadInt32(),slot=reader.ReadInt32(),type=reader.ReadInt32();
            if((uint)index>=Recipe.numRecipes)continue;
            var recipe=Main.recipe[index];
            if(!recipeKeys.TryGetValue(recipe,out var key))continue;
            if(slot==-1)result[TerminalOrders.ResultKey(recipe.createItem.type)]=key;
            else if((uint)slot<recipe.requiredItem.Count && Alternatives(recipe,recipe.requiredItem[slot].type).Contains(type))
                result[key+"/"+slot]=TerminalOrders.ResultKey(type);
        }
        return result.Select(pair=>new Terraria.ModLoader.IO.TagCompound{["key"]=pair.Key,["choice"]=pair.Value}).ToList();
    }
    private sealed class Plan
    {
        internal Dictionary<int,int> Used=new(), Made=new(), Extra=new(), Missing=new();
        internal List<Step> Steps=new();
        internal List<Node> Tree=new();
        internal bool Blocked;
        internal Plan Copy()=>new(){Used=new(Used),Made=new(Made),Extra=new(Extra),Missing=new(Missing),Steps=new(Steps),Tree=new(Tree),Blocked=Blocked};
        internal long Cost=>Missing.Values.Sum(n=>(long)n)+(Blocked?1L<<40:0);
    }
    private static void Add(Dictionary<int,int> dict,int id,int n)
    { if(n!=0) dict[id]=checked(dict.GetValueOrDefault(id)+n); }

    private sealed class Planner(Player player,int resultType, Dictionary<string,string>? route=null, Dictionary<int,int>? reserved=null,IEnumerable<Item>? shared=null)
    {
        private readonly Dictionary<int,int> stock=(shared??InputsFor(player,ContentSamples.ItemsByType[resultType])).GroupBy(i=>i.type).ToDictionary(g=>g.Key,g=>(int)Math.Min(int.MaxValue,g.Sum(i=>(long)i.stack)));
        private int trials;
        internal Plan Build(int target,int count)=>Make(target,count,new(),new(),-1);
        internal Plan BuildMaterial(int target, int count, int parentType) =>
            Make(target, count, new(), new() { parentType }, -1);
        private readonly Dictionary<Recipe,bool> availability=new();
        private bool Available(Recipe r)
        {
            if(!availability.TryGetValue(r,out bool ok))
                availability[r]=ok=!r.Disabled && r.requiredTile.All(t=>t<0 || player.adjTile[t]) && RecipeLoader.RecipeAvailable(r);
            return ok;
        }
        private int Free(Plan p,int type)=>Math.Max(0,stock.GetValueOrDefault(type)-(reserved?.GetValueOrDefault(type) ?? 0)-p.Used.GetValueOrDefault(type));
        private int Take(Plan p,int id,int n)
        {
            int available=Math.Min(n,Free(p,id));
            Add(p.Used,id,available);
            int surplus=Math.Min(n-available,p.Extra.GetValueOrDefault(id));
            Add(p.Extra,id,-surplus);
            Add(p.Made,id,surplus);
            return available+surplus;
        }
        private Plan Make(int type,int count,Plan start,HashSet<int> path,int parent)
        {
            int node=start.Tree.Count;
            start.Tree.Add(new(type,count,parent,false));
            if (!recipes.TryGetValue(type,out var options) || !path.Add(type)) {
                start.Tree[node]=start.Tree[node] with { Missing=true };
                Add(start.Missing,type,count);return start;
            }
            try {
                if(route!=null && route.TryGetValue(TerminalOrders.ResultKey(type),out var chosenRecipe) && options.FirstOrDefault(r=>RecipeKey(r)==chosenRecipe) is Recipe pinned)options=new[]{pinned};
                Plan best=null;
                foreach(var recipe in options.OrderByDescending(Available)) {
                    if (++trials>20000 || path.Count>100) break;
                    var p=options.Length==1?start:start.Copy();
                    p.Tree[node]=p.Tree[node] with {Recipe=recipe};
                    if (!Available(recipe)){p.Blocked=true;p.Tree[node]=p.Tree[node] with {Missing=true};}
                    int batches=1+(count-1)/recipe.createItem.stack;
                    var choices=new List<int[]>();
                    foreach(var ingredient in recipe.requiredItem) {
                        int id=ingredient.type,left=checked(batches*ingredient.stack);
                        var ids=Alternatives(recipe,id);
                        if(ids.Length>1 && route!=null && route.TryGetValue(RecipeKey(recipe)+"/"+choices.Count,out var material)){
                            int pinnedType=TerminalOrders.ResolveResultType(material);
                            if(ids.Contains(pinnedType))ids=new[]{pinnedType};
                        }
                        var selected=new List<int>();
                        foreach(int alt in ids.Distinct()) {
                            int got=Take(p,alt,left);
                            if(got>0){selected.Add(alt);selected.Add(got);left-=got;p.Tree.Add(new(alt,got,node,false));}
                            if(left==0)break;
                        }
                        if(left>0) {
                            Plan sub=null;int chosen=id;
                            foreach(int alt in ids.Distinct()){
                                var candidate=Make(alt,left,ids.Length==1?p:p.Copy(),path,node);
                                if(sub==null || candidate.Cost<sub.Cost){sub=candidate;chosen=alt;}
                                if(sub.Cost==p.Cost || trials>20000)break;
                            }
                            p=sub;selected.Add(chosen);selected.Add(left);
                            if(p.Missing.GetValueOrDefault(chosen)==0)Add(p.Made,chosen,left);
                        }
                        choices.Add(selected.ToArray());
                    }
                    Add(p.Extra,type,checked(batches*recipe.createItem.stack)-count);
                    p.Steps.Add(new(recipe,batches,choices.ToArray()));
                    if(best==null || p.Cost<best.Cost)best=p;
                    if(best.Cost==start.Cost)break;
                }
                if(best!=null)return best;
                Add(start.Missing,type,count);return start;
            } finally {path.Remove(type);}
        }
    }
    internal static bool CanPlanMaterial(Player player, int type, int missing, int parentType)
    {
        return player.GetModPlayer<TerminalWorkArea>().WithStations(() => {
            var plan = new Planner(player, type).BuildMaterial(type, missing, parentType);
            return !plan.Blocked && plan.Missing.Count == 0;
        });
    }

    private static (bool,(int,int,int,int)[],long?) Quote(Player p,string key,int count)
    {
        int type=int.Parse(key);
        var orders=p.GetModPlayer<TerminalOrders>();
        var plan=new Planner(p,type,orders.Route,orders.ReservedBefore(long.MaxValue)).Build(type,count);
        quotes.AddOrUpdate(p,plan);
        var order=p.GetModPlayer<TerminalOrders>();
        order.Tree=plan.Tree;
        order.MissingStations=plan.Steps.SelectMany(step=>step.Recipe.requiredTile)
            .Where(tile=>tile>=0 && !p.adjTile[tile]).Distinct().Select(tile=>{
                var item=ContentSamples.ItemsByType.Values.FirstOrDefault(i=>i.createTile==tile);
                if (tile == TileID.DemonAltar)
                    return (TerminalText.Get("恶魔祭坛或猩红祭坛"), item?.type ?? 0);
                string label = item?.Name ?? Lang.GetMapObjectName(Terraria.Map.MapHelper.TileToLookup(tile,0));
                return (string.IsNullOrWhiteSpace(label) ? TileID.Search.GetName(tile) : label, item?.type ?? 0);
            }).ToArray();
        order.PlanReason=order.MissingStations.Length>0 ? "" : plan.Blocked ? TerminalText.Get("工作站或配方条件未满足") : plan.Steps.Count==0 ? TerminalText.Get("没有可用的合成配方") : "";
        var rows=plan.Used.Keys.Union(plan.Made.Keys).Union(plan.Missing.Keys).OrderBy(id=>id)
            .Select(id=>{int a=plan.Used.GetValueOrDefault(id),b=plan.Made.GetValueOrDefault(id);
                return(id,checked(a+b+plan.Missing.GetValueOrDefault(id)),a,b);}).ToArray();
        // AE2 19.2.20 charges eight bytes per item node, one per requested
        // item unit (the root included), and one per pattern execution.
        long bytes = 8L + count;
        foreach (var step in plan.Steps)
        {
            bytes += step.Batches;
            foreach (var ingredient in step.Recipe.requiredItem)
                bytes += 8L + (long)ingredient.stack * step.Batches;
        }
        return (plan.Cost==0 && plan.Steps.Count>0,rows,bytes);
    }

    private static bool Submit(Player player,string key,int count,string ticket)
    {
        if(!quotes.TryGetValue(player,out var plan) || plan.Cost!=0 || plan.Steps.Count==0)return false;
        quotes.Remove(player);
        return SubmitPlan(player,plan,ticket);
    }

    // Background planning must not replace the recipe tree or quote being reviewed.
    internal static bool TryWaiting(Player player,int type,int quantity,string ticket,
        out (int Type,int Count)[] missing,out TerminalOrders.MaterialStatus[] materials)
    {
        var orders=player.GetModPlayer<TerminalOrders>();
        var plan=new Planner(player,type,orders.RouteFor(ticket),orders.ReservedFor(ticket)).Build(type,quantity);
        missing=plan.Missing.Where(pair=>pair.Value>0)
            .OrderBy(pair=>pair.Key).Select(pair=>(pair.Key,pair.Value)).ToArray();
        materials=MaterialRows(plan);
        return plan.Cost==0 && plan.Steps.Count>0 && SubmitPlan(player,plan,ticket);
    }

    private static TerminalOrders.MaterialStatus[] MaterialRows(Plan plan)=>plan.Used.Keys.Union(plan.Made.Keys).Union(plan.Missing.Keys).OrderBy(id=>id)
            .Select(id=>new TerminalOrders.MaterialStatus(id,plan.Used.GetValueOrDefault(id),
                plan.Made.GetValueOrDefault(id),plan.Missing.GetValueOrDefault(id))).ToArray();

    internal static bool CraftShared(Player player,TeamBank bank,Item order,bool useBag, Dictionary<int,int>? reserved=null)
    {
        // Execute mod hooks on working copies. Publish inventory, station and bag changes only after the whole recipe succeeds.
        using var current=new Main.CurrentPlayerOverride(player);
        var work=player.GetModPlayer<TerminalWorkArea>();
        bool upgrades=TerminalWorkArea.IsStationOutput(order);
        IEnumerable<Item> Sources(List<Item> stock,List<Item> stations,IEnumerable<Item> bagItems){
            var inputs=stock.Concat(bagItems).Where(i=>!i.IsAir && !i.favorited);
            return upgrades?stations.Where(i=>!i.IsAir && !i.favorited).Concat(inputs):inputs;
        }
        var equipment=useBag && player.GetModPlayer<TerminalSearchSettings>().UseEquipment?
            SharedActions.Equipment(player):Array.Empty<(int Kind,int Index,Item[] Array)>();
        var originals=(useBag?player.inventory.Take(58):Array.Empty<Item>()).Concat(equipment.Select(s=>s.Array[s.Index])).ToArray();
        var available=Sources(bank.Stock,bank.Stations,originals);
        var route=new Dictionary<string,string>();
        if(bank.OrderData.TryGetValue(order,out var metadata))
            foreach(var pin in metadata.GetList<Terraria.ModLoader.IO.TagCompound>("route"))route[pin.GetString("key")]=pin.GetString("choice");
        var plan=work.WithStations(()=>new Planner(player,order.type,route:route,reserved:reserved,shared:available).Build(order.type,order.stack),bank.Stations);
        if(!useBag)bank.UpdateDetail(order,MaterialRows(plan));
        if(plan.Cost!=0 || plan.Steps.Count==0){
            if(reserved!=null)foreach(var (type,used) in plan.Used)reserved[type]=reserved.GetValueOrDefault(type)+used;
            return false;
        }
        var working=new TeamBank{Stock=bank.Stock.Select(i=>i.Clone()).ToList(),Stations=bank.Stations.Select(i=>i.Clone()).ToList()};
        var copies=bank.Stock.Zip(working.Stock).ToDictionary(pair=>pair.First,pair=>pair.Second);
        var bag=originals.Select(i=>i.Clone()).ToArray();
        var sources=Sources(working.Stock,working.Stations,bag).ToArray();
        bool made=work.WithStations(()=>{
            return SubmitPlan(player,plan,"",sources,working.Stations,(outputs,stationType)=>{
                foreach(var item in outputs){
                    if(stationType==item.type && !working.Stations.Any(s=>!s.IsAir && s.type==item.type))
                        working.Stations.Add(ItemLoader.TransferWithLimit(item,1));
                    working.Store(item);
                }
                return true;
            });
        },working.Stations);
        if(!made)return false;
        working.Stock.RemoveAll(i=>i.IsAir);
        TeamSupply.Remap(bank,copies);
        bank.Stock=working.Stock;bank.Stations=working.Stations;
        for(int i=0;i<bag.Length;i++){
            if(bag[i].stack==originals[i].stack && bag[i].type==originals[i].type)continue;
            if(i<58)player.inventory[i]=bag[i];
            else{var slot=equipment[i-58];slot.Array[slot.Index]=bag[i];}
        }
        return true;
    }

    private static bool SubmitPlan(Player player,Plan plan,string ticket,Item[]? shared=null,List<Item>? sharedStations=null,
        Func<Item[],int,bool>? deliver=null)
    {
        var result=plan.Steps[^1].Recipe.createItem;
        var sources=(shared??InputsFor(player,result)).Distinct().ToLookup(i=>i.type);
        var debits=new Dictionary<Item,int>(ReferenceEqualityComparer.Instance);
        var created=new Dictionary<int,List<Item>>();
        foreach(var step in plan.Steps) {
            var choices=step.Choices.Select(a=>(int[])a.Clone()).ToArray();
            for(int batch=0;batch<step.Batches;batch++) {
                var consumed=new List<Item>();
                for(int k=0;k<choices.Length;k++) {
                    var ingredient=step.Recipe.requiredItem[k];
                    int amount=ingredient.stack;
                    RecipeLoader.ConsumeIngredient(step.Recipe,ingredient.type,ref amount,false);
                    if(amount<0 || amount>ingredient.stack)return false;
                    int reserved=ingredient.stack;
                    var slots=choices[k];
                    for(int i=0;i<slots.Length && reserved>0;i+=2) {
                        int type=slots[i],allocation=Math.Min(reserved,slots[i+1]);
                        slots[i+1]-=allocation; reserved-=allocation;
                        int needed=Math.Min(allocation,amount);amount-=needed;
                        if(needed==0)continue;
                        if(created.TryGetValue(type,out var products))
                            foreach(var item in products.Where(x=>!x.IsAir)) {
                                int n=Math.Min(needed,item.stack);var copy=item.Clone();copy.stack=n;consumed.Add(copy);
                                item.stack-=n;needed-=n;if(needed==0)break;
                            }
                        if(needed>0)foreach(var item in sources[type]){
                            int n=Math.Min(needed,item.stack-debits.GetValueOrDefault(item));if(n<=0)continue;
                            var copy=item.Clone();copy.stack=n;consumed.Add(copy);
                            debits[item]=debits.GetValueOrDefault(item)+n;needed-=n;if(needed==0)break;
                        }
                        if(needed!=0)return false;
                    }
                }
                var output=step.Recipe.createItem.Clone();
                RecipeLoader.OnCraft(output,step.Recipe,consumed,new Item());
                if(output.IsAir || output.stack>output.maxStack)return false;
                if(!created.TryGetValue(output.type,out var bucket))created[output.type]=bucket=new();
                // Keep individual crafted item data while avoiding one slot per identical batch.
                if(bucket.Count>0 && !bucket[^1].IsAir && ItemLoader.CanStack(bucket[^1],output))
                    ItemLoader.TryStackItems(bucket[^1],output,out _);
                if(!output.IsAir)bucket.Add(output);
            }
        }
        if(debits.Any(pair=>pair.Key.stack<pair.Value))return false;
        var stations=sharedStations??player.GetModPlayer<TerminalWorkArea>().Stations;
        int stationOutput=TerminalWorkArea.IsStationOutput(result)?result.type:0;
        foreach(var (item,n) in debits)item.stack-=n;
        bool complete=false;
        try {
            var outputs=created.Values.SelectMany(x=>x).Where(i=>!i.IsAir).ToArray();
            complete=deliver!=null?deliver(outputs,stationOutput):player.GetModPlayer<TerminalOrders>().Complete(Owner,ticket,outputs,stationOutput);
            return complete;
        } finally {
            foreach(var (item,n) in debits)
                if(!complete)item.stack+=n;else if(item.stack<=0){stations.Remove(item);item.TurnToAir();}
        }
    }
}
