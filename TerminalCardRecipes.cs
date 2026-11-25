using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

public sealed class AdvancedCard : ModItem
{
    public override string Texture => "AEBeyondDimensions/Assets/Magnet/AdvancedCard";
    public override void SetDefaults()
    {
        Item.width = Item.height = 16;
        Item.maxStack = Item.CommonMaxStack;
    }
}

public sealed class MasterControlCard : ModItem
{
    public override string Texture => "AEBeyondDimensions/Assets/Magnet/MasterControlCard";
    public override void SetDefaults() { Item.width=Item.height=16; Item.maxStack=1; }
}

public sealed class TerminalCardRecipes : ModSystem
{
    private const string GoldBars = "AEBeyondDimensions:GoldBars";

    public override void AddRecipeGroups() =>
        RecipeGroup.RegisterGroup(GoldBars, new RecipeGroup(
            () => $"{Language.GetTextValue("LegacyMisc.37")} {Lang.GetItemNameValue(ItemID.GoldBar)}",
            ItemID.GoldBar, ItemID.PlatinumBar));

    public override void AddRecipes()
    {
        if (ModLoader.HasMod("ImproveGame"))
            Recipe.Create(ModContent.ItemType<MasterControlCard>()).AddIngredient<AdvancedCard>()
                .AddIngredient(ItemID.Wire,5).AddTile(TileID.Anvils).Register();
        Recipe.Create(ModContent.ItemType<AdvancedCard>(), 4)
            .AddRecipeGroup(RecipeGroupID.IronBar, 3).AddIngredient(ItemID.Glass, 3)
            .AddIngredient(ItemID.Diamond).AddTile(TileID.Anvils).Register();
        Recipe.Create(ModContent.ItemType<MagnetCard>())
            .AddIngredient<AdvancedCard>().AddRecipeGroup(RecipeGroupID.IronBar, 3)
            .AddIngredient(ItemID.FallenStar).AddTile(TileID.Anvils).Register();
        Recipe.Create(ModContent.ItemType<ShimmerCard>())
            .AddIngredient<AdvancedCard>().AddIngredient(ItemID.ShimmerTorch, 3)
            .AddTile(TileID.Anvils).Register();
        Recipe.Create(ModContent.ItemType<ReforgeCard>())
            .AddIngredient<AdvancedCard>().AddIngredient(ItemID.Chain, 2)
            .AddRecipeGroup(GoldBars, 2).AddTile(TileID.TinkerersWorkbench).Register();
        Recipe.Create(ModContent.ItemType<ReforgeUpgradeCard>())
            .AddIngredient<ReforgeCard>().AddIngredient(ItemID.CrystalShard, 15)
            .AddIngredient(ItemID.SoulofLight, 5).AddIngredient(ItemID.SoulofNight, 5)
            .AddTile(TileID.TinkerersWorkbench).Register();
    }
}
