using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

// Materials held for exactly one execution. Keep full item instances for craft hooks and refunds.
public sealed class TerminalBench : ModPlayer
{
    internal Recipe? Selected;
    internal List<Item>[] Held = Array.Empty<List<Item>>();
    internal int Page;
    internal bool Filled;
    private bool taking;
    private bool Serving=>Player.GetModPlayer<TerminalStorage>().Serving;
    private Recipe? remoteRecipe;
    private List<Item>[] remoteHeld=Array.Empty<List<Item>>();
    private readonly List<Item> remoteOwned=new();
    private bool remoteFilled;
    internal Recipe? ViewRecipe=>TeamClient.Open?remoteRecipe:Selected;
    internal List<Item>[] ViewHeld=>TeamClient.Open?remoteHeld:Held;
    internal bool ViewFilled=>TeamClient.Open?remoteFilled:Filled;
    internal IEnumerable<Item> OwnedParts=>Parts.Where(i=>!i.IsAir && !borrowed.ContainsKey(i));
    internal void ClearRemote(){remoteRecipe=null;remoteFilled=false;remoteHeld=Array.Empty<List<Item>>();remoteOwned.Clear();previews.Clear();Page=0;}
    internal void SendBench(int who,int token){
        // Only appearance and quantities leave the server. Held item instances stay authoritative.
        var msg=TeamNetwork.Packet(TeamOp.BenchView,token);
        msg.Write(Selected?.RecipeIndex??-1);msg.Write(Filled);msg.Write(Held.Length);
        foreach(var group in Held){
            var rows=group.GroupBy(i=>(i.type,Borrowed:borrowed.ContainsKey(i))).ToArray();
            msg.Write(rows.Length);
            foreach(var row in rows){msg.Write(row.Key.type);msg.Write(row.Sum(i=>i.stack));msg.Write(row.Key.Borrowed);}
        }
        msg.Send(who);
    }
    internal void ReceiveBench(System.IO.BinaryReader r,bool apply){
        int index=r.ReadInt32();bool filled=r.ReadBoolean();int n=r.ReadInt32();
        var groups=new List<Item>[n];var owned=new List<Item>();
        for(int i=0;i<n;i++){groups[i]=new();int count=r.ReadInt32();
            for(int j=0;j<count;j++){var item=new Item(r.ReadInt32());item.stack=r.ReadInt32();bool lent=r.ReadBoolean();
                groups[i].Add(item);if(!lent)owned.Add(item);}
        }
        if(!apply)return;
        var next=(uint)index<Recipe.numRecipes?Main.recipe[index]:null;
        if(remoteRecipe!=next)Page=0;
        remoteRecipe=next;remoteFilled=filled;remoteHeld=groups;remoteOwned.Clear();remoteOwned.AddRange(owned);previews.Clear();
    }
    // Installed stations stay active until the craft commits.
    private Dictionary<Item,Item> borrowed=new(ReferenceEqualityComparer.Instance);
    private bool BorrowedAvailable() => borrowed.All(pair=>Player.GetModPlayer<TerminalWorkArea>().Stations.Contains(pair.Value)
        && pair.Key.type==pair.Value.type && pair.Value.stack>=pair.Key.stack && !pair.Value.favorited);
    private IEnumerable<Item> Parts => Held.SelectMany(group => group);
    private bool Available(Recipe r) => !r.Disabled && InstantCraft.AllowedRecipe(r) &&
        Player.GetModPlayer<TerminalWorkArea>().WithStations(() =>
            r.requiredTile.All(t => t < 0 || (t < Player.adjTile.Length && Player.adjTile[t])) &&
            RecipeLoader.RecipeAvailable(r));

    private List<(Item Source,int Amount)>[]? Allocate(Recipe recipe, bool partial = false)
    {
        if (!partial && !Available(recipe)) return null;
        var sources = (TeamClient.Open?(IEnumerable<Item>)remoteOwned:OwnedParts).Concat(InstantCraft.InputsFor(Player,recipe.createItem))
            .Distinct().Where(i => !i.IsAir).ToArray();
        var free = sources.Select(i => i.stack).ToArray();
        var ingredients = recipe.requiredItem;
        var candidates = ingredients.Select(i => {
            var types = InstantCraft.Alternatives(recipe,i.type);
            return Enumerable.Range(0,sources.Length).Where(s => types.Contains(sources[s].type)).ToArray();
        }).ToArray();
        var reserved = ingredients.Select(_ => new Dictionary<int,int>()).ToArray();
        // Reassign earlier group choices when a later ingredient needs the same stack.
        int Assign(int i,int need,HashSet<int> path) {
            if(!path.Add(i)) return 0;
            int left=need;
            foreach(int s in candidates[i]) {
                int n=Math.Min(left,free[s]); free[s]-=n;
                reserved[i][s]=reserved[i].GetValueOrDefault(s)+n;left-=n;
                if(left==0)break;
            }
            foreach(int s in candidates[i]) {
                if(left==0)break;
                for(int j=0;j<reserved.Length && left>0;j++){
                    int held=reserved[j].GetValueOrDefault(s);
                    if(held<=0 || path.Contains(j))continue;
                    int n=Assign(j,Math.Min(left,held),path);
                    reserved[j][s]-=n;reserved[i][s]=reserved[i].GetValueOrDefault(s)+n;left-=n;
                }
            }
            path.Remove(i);return need-left;
        }
        for(int i=0;i<ingredients.Count;i++)
            if(Assign(i,ingredients[i].stack,new())!=ingredients[i].stack && !partial)return null;
        return reserved.Select(group=>group.Where(k=>k.Value>0).Select(k=>(sources[k.Key],k.Value)).ToList()).ToArray();
    }
    private readonly Dictionary<int,(int Missing,bool Craftable)[]> previews = new();
    private ulong previewTick;
    internal (int Missing,bool Craftable)[] MaterialStatus(int index)
    {
        if ((uint)index >= Recipe.numRecipes || Main.netMode != NetmodeID.SinglePlayer && !TeamSupply.Active || Player.dead)
            return Array.Empty<(int,bool)>();
        if (Main.GameUpdateCount - previewTick >= 10) { previews.Clear(); previewTick = Main.GameUpdateCount; }
        if (previews.TryGetValue(index,out var cached)) return cached;
        var recipe = Main.recipe[index];
        if(!InstantCraft.AllowedRecipe(recipe))return Array.Empty<(int,bool)>();
        var allocation = Allocate(recipe, partial:true)!;
        var result = new (int Missing,bool Craftable)[recipe.requiredItem.Count];
        for (int i=0; i<result.Length; i++)
        {
            var ingredient = recipe.requiredItem[i];
            int missing = ingredient.stack - allocation[i].Sum(p => p.Amount);
            if (missing <= 0) continue;
            var types = InstantCraft.Alternatives(recipe,ingredient.type);
            bool craftable = types.Any(type => InstantCraft.CanPlanMaterial(Player, type, missing, recipe.createItem.type));
            result[i] = (missing,craftable);
        }
        return previews[index] = result;
    }

    internal bool Prepare(int index,bool commit)
    {
        if(Player.dead || (uint)index>=Recipe.numRecipes || taking)return false;
        if(TeamClient.Open){
            if(commit)return TeamClient.Shared(SharedAction.FillBench,index);
            return remoteRecipe==Main.recipe[index] && remoteFilled && Available(remoteRecipe) || Allocate(Main.recipe[index])!=null;
        }
        if(TeamSupply.Active && !commit)return Allocate(Main.recipe[index])!=null;
        if(!Serving && Main.netMode!=NetmodeID.SinglePlayer)return false;
        var recipe=Main.recipe[index];
        if(Selected==recipe && Filled && BorrowedAvailable())return Available(recipe);
        var allocation=Allocate(recipe);
        if(allocation==null || !commit)return allocation!=null;
        var area=Player.GetModPlayer<TerminalWorkArea>();
        var nextBorrowed=new Dictionary<Item,Item>(ReferenceEqualityComparer.Instance);
        var next=allocation.Select(group=>group.Select(part=>{
            var item=part.Source.Clone();item.stack=part.Amount;
            if(area.Stations.Contains(part.Source))nextBorrowed[item]=part.Source;
            return item;
        }).ToList()).ToArray();
        foreach(var group in allocation)foreach(var part in group){
            if(area.Stations.Contains(part.Source))continue;
            part.Source.stack-=part.Amount;if(part.Source.stack==0)part.Source.TurnToAir();
        }
        Refund();
        Selected=recipe;Held=next;borrowed=nextBorrowed;Filled=true;
        return true;
    }
    internal bool HasMaterials => (TeamClient.Open?remoteHeld:Held).Any(g=>g.Any(i=>!i.IsAir));
    internal void TransferToInventory()
    {
        if(TeamClient.Open){TeamClient.Shared(SharedAction.EmptyBench);return;}
        foreach (var group in Held)
        {
            for (int i=group.Count-1;i>=0;i--)
            {
                if(borrowed.Remove(group[i])){group.RemoveAt(i);continue;}
                if(Serving){SharedActions.Give(Player,group[i]);if(group[i].IsAir)group.RemoveAt(i);continue;}
                var remainder=Player.GetItem(Player.whoAmI,group[i],GetItemSettings.LootAllSettingsRegularChest);
                if (remainder.IsAir) group.RemoveAt(i);
                else group[i]=remainder;
            }
        }
        if (!HasMaterials) Refund();
        else if (Selected is { } recipe)
            Filled=Held.Select((g,i)=>g.Sum(p=>p.stack)==recipe.requiredItem[i].stack).All(full=>full);
        if(!Serving)Recipe.FindRecipes();
    }
    internal void Refund()
    {
        if(TeamClient.Open){TeamClient.Shared(SharedAction.RefundBench);return;}
        var store=Player.GetModPlayer<TerminalStorage>();
        foreach(var item in Parts.Where(i=>!i.IsAir && !borrowed.ContainsKey(i)))store.StoreLooseItem(item);
        borrowed.Clear();
        Selected=null;Held=Array.Empty<List<Item>>();Filled=false;Page=0;
    }
    public override void SaveData(TagCompound tag) => tag["reserved"]=Parts.Where(i=>!i.IsAir && !borrowed.ContainsKey(i)).Select(ItemIO.Save).ToList();
    public override void LoadData(TagCompound tag) => Held=new[]{tag.GetList<TagCompound>("reserved").Select(ItemIO.Load).ToList()};
    public override void OnEnterWorld() { if(Main.netMode==NetmodeID.SinglePlayer)Refund(); }
    internal void Take()
    {
        if(TeamClient.Open){if(remoteFilled && remoteRecipe!=null)TeamClient.Shared(SharedAction.CraftBench,remoteRecipe.RecipeIndex);return;}
        var r=Selected;
        if(taking || r==null || !Filled)return;
        if(!BorrowedAvailable()){
            Refund();
            Prepare(r.RecipeIndex,true);
            return;
        }
        if(!Available(r))return;
        var cursor=Serving?new Item():Main.mouseItem;var output=r.createItem.Clone();
        if(!cursor.IsAir && (cursor.type!=output.type || cursor.stack+output.stack>cursor.maxStack || !ItemLoader.CanStack(cursor,output)))return;
        taking=true;
        try {
            var consumed=new List<Item>();var refunds=new List<Item>();
            var stationDebits=new Dictionary<Item,int>(ReferenceEqualityComparer.Instance);
            for(int i=0;i<Held.Length;i++){
                int need=r.requiredItem[i].stack;
                RecipeLoader.ConsumeIngredient(r,r.requiredItem[i].type,ref need,false);
                if(need<0 || need>r.requiredItem[i].stack)return;
                foreach(var part in Held[i]){
                    int n=Math.Min(need,part.stack);need-=n;
                    if(n>0){var copy=part.Clone();copy.stack=n;consumed.Add(copy);}
                    if(borrowed.TryGetValue(part,out var station))stationDebits[station]=stationDebits.GetValueOrDefault(station)+n;
                    else if(part.stack>n){var copy=part.Clone();copy.stack-=n;refunds.Add(copy);}
                }
            }
            RecipeLoader.OnCraft(output,r,consumed,new Item());
            var area=Player.GetModPlayer<TerminalWorkArea>();
            if(!BorrowedAvailable() || stationDebits.Any(p=>p.Value>p.Key.stack))return;
            foreach(var (item,n) in stationDebits){
                item.stack-=n;
                if(item.stack==0)area.Stations.Remove(item);
            }
            borrowed.Clear();
            Held=Array.Empty<List<Item>>();Filled=false;
            var store=Player.GetModPlayer<TerminalStorage>();
            if(TerminalWorkArea.IsStationOutput(output))area.StoreStation(output);
            if(Serving){SharedActions.Give(Player,output);if(!output.IsAir)store.StoreLooseItem(output);}
            else if(cursor.IsAir)Main.mouseItem=output;
            else if(!output.IsAir){
                if(ItemLoader.CanStack(cursor,output))ItemLoader.TryStackItems(cursor,output,out _);
                if(!output.IsAir)store.StoreLooseItem(output);
            }
            foreach(var item in refunds)store.StoreLooseItem(item);
        } finally { taking=false; }
        Prepare(r.RecipeIndex,true);
        if(!Serving)Recipe.FindRecipes();
    }
}
