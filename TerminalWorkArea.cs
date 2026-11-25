using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

public sealed class TerminalWorkArea : ModPlayer
{
    internal List<Item> Stations { get; set; } = new();
    internal Item[] ShimmerInput { get; private set; } = new[] { new Item() };
    internal List<Item> Products { get; private set; } = new();

    private List<Item>? characterStations;
    internal void SelectStations(TeamBank bank){characterStations??=Stations;Stations=bank.Stations;}
    internal void ReleaseStations(){if(characterStations!=null){Stations=characterStations;characterStations=null;}}
    public override void Initialize()
    {
        Stations = new();
        ShimmerInput = new[] { new Item() };
        Products = new();
    }

    public override void SaveData(TagCompound tag)
    {
        tag["stations"] = (characterStations ?? Stations).Select(ItemIO.Save).ToList();
        tag["products"] = Products.Select(ItemIO.Save).ToList();
        if (!ShimmerInput[0].IsAir) tag["input"] = ItemIO.Save(ShimmerInput[0]);
    }

    public override void LoadData(TagCompound tag)
    {
        Stations = tag.GetList<TagCompound>("stations").Select(ItemIO.Load).Where(i => !i.IsAir).ToList();
        Products = tag.GetList<TagCompound>("products").Select(ItemIO.Load).Where(i => !i.IsAir).ToList();
        ShimmerInput[0] = tag.TryGet("input", out TagCompound input) ? ItemIO.Load(input) : new Item();
    }

    internal bool StoreStation(Item source)
        => StoreStation(source, out _);

    internal bool StoreStation(Item source, out bool duplicate)
    {
        duplicate = false;
        if (source.IsAir || source.favorited)
            return false;
        var capabilities = Resolve(source,new HashSet<int>(),null,out bool composite);
        if (capabilities.Count == 0) return false;
        var remaining = new HashSet<int>(capabilities);
        var replaced = new List<Item>();
        foreach (var installed in Stations)
        {
            var existing = ResolveStation(installed);
            remaining.ExceptWith(existing);
            if (existing.IsSupersetOf(capabilities) && (installed.type == source.type || !composite || !existing.SetEquals(capabilities)))
            {
                duplicate = true;
                return false;
            }
            if (existing.Count > 0 && existing.IsSubsetOf(capabilities) &&
                (existing.Count < capabilities.Count || (composite && !ResolveComposite(installed))))
                replaced.Add(installed);
        }
        duplicate = remaining.Count == 0 && replaced.Count == 0;
        if (duplicate) return false;
        Stations.Add(ItemLoader.TransferWithLimit(source, 1));
        if (source.stack <= 0) source.TurnToAir();
        var storage = Player.GetModPlayer<TerminalStorage>();
        foreach (var old in replaced)
        {
            Stations.Remove(old);
            storage.StoreLooseItem(old);
        }
        return true;
    }

    // Count a private tile ID as a distinct capability only when a recipe requires it.
    // A mod's decorative alternative can then match the vanilla station it provides.
    private static HashSet<int> StationCapabilities(int tile, HashSet<int> required)
    {
        var result = new HashSet<int> { tile };
        result.UnionWith(NativeAdjacency(tile));
        if (TileLoader.GetTile(tile)?.AdjTiles is { } aliases) result.UnionWith(aliases);
        foreach (var hook in ModContent.GetContent<GlobalTile>())
            if (hook.AdjTiles(tile) is { } extra) result.UnionWith(extra);
        var flags = new HashSet<int>();
        foreach (int id in result)
        {
            if ((uint)id >= TileLoader.TileCount) continue;
            if (id == TileID.AlchemyTable) flags.Add(Alchemy);
            if (TileID.Sets.CountsAsWaterSource[id] || id == TileID.Sinks) flags.Add(Water);
            if (TileID.Sets.CountsAsHoneySource[id]) flags.Add(Honey);
            if (TileID.Sets.CountsAsLavaSource[id]) flags.Add(Lava);
            if (TileID.Sets.CountsAsShimmerSource[id]) flags.Add(Shimmer);
        }
        result.IntersectWith(required);
        result.UnionWith(flags);
        return result;
    }

    private const int Alchemy = -1, Water = -2, Honey = -3, Lava = -4, Shimmer = -5;

    private static bool ResolveComposite(Item item)
    {
        Resolve(item,new HashSet<int>(),null,out bool composite);
        return composite;
    }

    internal static bool IsStationOutput(Item item) => item.createTile>=0 && ResolveStation(item).Count>0;

    private static HashSet<int> ResolveStation(Item station)
    {
        if (station.IsAir) return new();
        return Resolve(station, new HashSet<int>(), null, out _);
    }

    private static HashSet<int> Resolve(Item item, HashSet<int> path, HashSet<int>? tiles, out bool composite)
    {
        composite = false;
        var result = item.createTile >= 0 ? StationCapabilities(item.createTile, StationRecipeIndex.Required) : new HashSet<int>();
        if (item.createTile >= 0) tiles?.Add(item.createTile);
        switch (item.type)
        {
            case ItemID.WaterBucket or ItemID.BottomlessBucket: result.Add(Water); break;
            case ItemID.HoneyBucket or ItemID.BottomlessHoneyBucket: result.Add(Honey); break;
            case ItemID.LavaBucket or ItemID.BottomlessLavaBucket: result.Add(Lava); break;
            case ItemID.BottomlessShimmerBucket: result.Add(Shimmer); break;
        }
        if (item.createTile < 0 || !path.Add(item.type)) return result;
        try
        {
            int type = ItemID.Sets.ShimmerCountsAsItem[item.type];
            if (type < 0) type = item.type;
            if (ItemID.Sets.ShimmerTransformToItem[type] > 0 || item.createTile == TileID.MusicBoxes) return result;
            int index = ShimmerTransforms.GetDecraftingRecipeIndex(type);
            if (index < 0) return result;
            var recipe = Main.recipe[index];
            if (recipe.Disabled || recipe.DecraftDisabled) return result;
            var combined = new HashSet<int>();
            var componentTiles = tiles == null ? null : new HashSet<int>();
            HashSet<int>? first = null;
            bool distinctParts = false;
            foreach (var part in recipe.customShimmerResults ?? recipe.requiredItem)
            {
                if (part.IsAir || path.Contains(part.type)) continue;
                var partTiles = componentTiles == null ? null : new HashSet<int>();
                var provides = Resolve(part, path, partTiles, out bool nested);
                if (provides.Count == 0) continue;
                composite |= nested;
                if (partTiles != null) componentTiles!.UnionWith(partTiles);
                first ??= provides;
                distinctParts |= !first.SetEquals(provides);
                combined.UnionWith(provides);
            }
            // Only composite stations inherit dismantled parts. A single bucket or
            // station used to make ordinary furniture does not make it a workstation.
            composite |= distinctParts;
            if (composite)
            {
                result.UnionWith(combined);
                if (componentTiles != null) tiles!.UnionWith(componentTiles);
            }
            return result;
        }
        finally { path.Remove(item.type); }
    }

    private static int[] NativeAdjacency(int tile) => tile switch
    {
        TileID.AdamantiteForge => [TileID.Hellforge, TileID.Furnaces],
        TileID.Hellforge or TileID.GlassKiln => [TileID.Furnaces],
        TileID.MythrilAnvil => [TileID.Anvils],
        TileID.AlchemyTable => [TileID.Bottles, TileID.Tables],
        TileID.Tables2 or TileID.PicnicTable or TileID.BewitchingTable => [TileID.Tables],
        _ => Array.Empty<int>()
    };

    // External planners can use the installed stations with their normal Terraria recipe checks.
    // Project adjacency only for the quote/submit callback and restore the player's actual surroundings.
    internal T WithStations<T>(Func<T> callback, List<Item>? shared=null)
    {
        var stations=shared??(TeamClient.Open || TeamSupply.Active && TeamClient.InventoryTeam==Player.team?TeamClient.Stations:Stations);
        if (stations.Count == 0) return callback();
        var adjacent = (bool[])Player.adjTile.Clone();
        var environment = (Player.alchemyTable, Player.adjWater, Player.adjHoney, Player.adjLava, Player.adjShimmer);
        try
        {
            var tiles = new HashSet<int>();
            var functions = new HashSet<int>();
            foreach (var station in stations)
            {
                if (station.IsAir) continue;
                functions.UnionWith(Resolve(station, new HashSet<int>(), tiles, out _));
            }
            foreach (int tile in tiles)
            {
                if ((uint)tile >= Player.adjTile.Length) continue;
                Player.adjTile[tile] = true;
                foreach (int other in NativeAdjacency(tile)) Player.adjTile[other] = true;
                if (tile == TileID.AlchemyTable) Player.alchemyTable = true;
                TileLoader.AdjTiles(Player,tile);
            }
            Player.alchemyTable |= functions.Contains(Alchemy);
            Player.adjWater |= functions.Contains(Water);
            Player.adjHoney |= functions.Contains(Honey);
            Player.adjLava |= functions.Contains(Lava);
            Player.adjShimmer |= functions.Contains(Shimmer);
            return callback();
        }
        finally
        {
            adjacent.CopyTo(Player.adjTile, 0);
            (Player.alchemyTable, Player.adjWater, Player.adjHoney, Player.adjLava, Player.adjShimmer) = environment;
        }
    }

    internal bool Transform()
    {
        var input = ShimmerInput[0];
        if (!Player.GetModPlayer<TerminalMagnet>().ShimmerAvailable || input.IsAir || input.favorited || !input.CanShimmer()) return false;
        int equivalent = ItemID.Sets.ShimmerCountsAsItem[input.type];
        if (equivalent < 0) equivalent = input.type;
        // These follow special world-state/NPC/lunar rules instead of the item-table/decraft route.
        if (ItemID.Sets.CoinLuckValue[equivalent] > 0 || equivalent is ItemID.RodofDiscord or ItemID.Clentaminator
            or ItemID.BottomlessBucket or ItemID.BottomlessShimmerBucket or ItemID.LunarBar)
            return false;
        int target = input.createTile == TileID.MusicBoxes ? ItemID.MusicBox : ItemID.Sets.ShimmerTransformToItem[equivalent];
        var products = new List<Item>();
        void Add(int type, int count)
        {
            while (count > 0)
            {
                var item = new Item(type);
                item.stack = Math.Min(item.maxStack,count);
                products.Add(item);
                count -= item.stack;
            }
        }
        int consumed;
        if (target > 0)
        {
            consumed = input.stack;
            Add(target,consumed);
        }
        else
        {
            if (input.makeNPC > 0 || input.type == ItemID.GelBalloon) return false;
            int index = ShimmerTransforms.GetDecraftingRecipeIndex(equivalent);
            if (index < 0) return false;
            var recipe = Main.recipe[index];
            int batches = input.stack / recipe.createItem.stack;
            if (batches == 0) return false;
            consumed = batches * recipe.createItem.stack;
            foreach (var ingredient in recipe.customShimmerResults ?? recipe.requiredItem)
            {
                if (ingredient.IsAir) break;
                int amount = checked(batches * ingredient.stack);
                RecipeLoader.ConsumeIngredient(recipe, ingredient.type, ref amount, isDecrafting:true);
                Add(ingredient.type,amount);
            }
        }
        Products.AddRange(products);
        input.stack -= consumed;
        if (input.stack == 0) input.TurnToAir();
        return true;
    }

    internal void TransferProducts(bool toStorage)
    {
        var storage = Player.GetModPlayer<TerminalStorage>();
        for (int i = 0; i < Products.Count;)
        {
            var item = Products[i];
            if (toStorage)
            {
                if(TeamSupply.Active)TerminalDelivery.Drop(Player,item,false);
                else{
                    int free=storage.EmptyCell();
                    storage.Slots[free]=item;
                    storage.NoteDeposit(item.type);
                }
            }
            else
            {
                Products[i] = Player.GetItem(Player.whoAmI,item,GetItemSettings.LootAllSettingsRegularChest);
                if (!Products[i].IsAir) { i++; continue; }
            }
            Products.RemoveAt(i);
        }
        Recipe.FindRecipes();
    }
}

internal sealed class StationRecipeIndex : ModSystem
{
    private static HashSet<int>? required;
    internal static HashSet<int> Required => required ??= Main.recipe.Take(Recipe.numRecipes)
        .Where(r => r != null && !r.Disabled).SelectMany(r => r.requiredTile).Where(id => id >= 0).ToHashSet();
    public override void PostSetupRecipes() => required = null;
    public override void Unload() => required = null;
}
