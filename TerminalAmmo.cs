using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

internal sealed class TerminalAmmo : ModSystem
{
    public override void PostSetupContent()
    {
        if (!Main.dedServ) On_Player.ChooseAmmo += ChooseAmmo;
    }

    private static Item ChooseAmmo(On_Player.orig_ChooseAmmo original, Player player, Item weapon)
    {
        var chosen=original(player,weapon);
        if (chosen is { IsAir: false } || weapon.useAmmo<=0 ||
            Main.netMode==NetmodeID.Server || player!=Main.LocalPlayer || !player.active || player.dead)
            return chosen;

        if(Main.netMode==NetmodeID.MultiplayerClient)return TeamSupply.Choose(player,weapon)??chosen;
        var storage=player.GetModPlayer<TerminalStorage>();
        if (!storage.CanAccess(false)) return chosen;
        foreach (var stack in storage.Slots)
        {
            // Return the real stack: PickAmmo applies the normal mod hooks, conservation
            // rolls, projectile selection and consumption directly to this item.
            if (!stack.IsAir && ItemLoader.CanChooseAmmo(weapon,stack,player))
                return stack;
        }
        return chosen;
    }
}
