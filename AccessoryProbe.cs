using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Default;
using Terraria.UI;

namespace AccessoryProbe;

public record Slot(string Key, Item? Functional, Item? Vanity, bool Visible)
{
    internal Player? Owner { get; init; }
    internal int VanillaIndex { get; init; }
    internal ModAccessorySlot? Source { get; init; }
    internal bool IsArmor { get; init; }

    // ItemSlot processes swaps in place, including the paired functional slot on a vanity right-click.
    // tML exposes individual items publicly, but keeps the full backing array internal.
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "exAccessorySlot")]
    private static extern ref Item[] ModItems(ModAccessorySlotPlayer slots);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "IsNotTheSameAs")]
    private static extern bool CanSwapDistinctItems(Item equipped, Item incoming);

    public bool TryGetBinding(bool vanity, out Item[] inventory, out int context, out int index)
    {
        inventory = Array.Empty<Item>();
        index = -1;
        context = IsArmor ? ItemSlot.Context.EquipArmor : ItemSlot.Context.EquipAccessory;
        if (vanity) context++;
        if (Owner == null || (vanity ? Vanity : Functional) == null)
            return false;

        if (Source == null)
        {
            inventory = Owner.armor;
            index = VanillaIndex + (vanity ? Owner.dye.Length : 0);
        }
        else
        {
            var slots = Owner.GetModPlayer<ModAccessorySlotPlayer>();
            inventory = ModItems(slots);
            index = Source.Type + (vanity ? slots.SlotCount : 0);
            context = -context;
        }
        return (uint)index < (uint)inventory.Length;
    }

    private bool IsUsable(bool vanity, int index) => Source == null
        ? Owner!.IsItemSlotUnlockedAndUsable(index)
        : LoaderManager.Get<AccessorySlotLoader>().ModdedIsSpecificItemSlotUnlockedAndUsable(Source.Type, Owner!, vanity);

    // Validate a whole item returning to this cell when a terminal transfer swaps two items.
    public bool CanAccept(bool vanity, Item incoming)
    {
        if (Owner != Main.LocalPlayer || !TryGetBinding(vanity, out var inventory, out int context, out int index))
            return false;

        using var scope = new Main.CurrentPlayerOverride(Owner!);
        if (incoming.IsAir)
            return true; // A visible but disabled slot still permits withdrawal with an empty cursor.
        if (incoming.stack != 1 || !IsUsable(vanity, index) ||
            ItemSlot.PickItemMovementAction(inventory, context, index, incoming) != 1)
            return false;
        if (!inventory[index].IsAir && !CanSwapDistinctItems(inventory[index], incoming))
            return false;
        return IsArmor || ((!vanity || incoming.FitsAccessoryVanitySlot) &&
            ItemLoader.CanEquipAccessory(incoming, index, context < 0));
    }

    public void HandleInput(bool vanity)
    {
        if (Owner != Main.LocalPlayer || PlayerInput.IgnoreMouseInterface ||
            !TryGetBinding(vanity, out var inventory, out int context, out int index))
            return;

        using var scope = new Main.CurrentPlayerOverride(Owner!);
        Owner!.mouseInterface = true;
        Main.armorHide = true;
        int hoverContext = Math.Abs(context);
        bool usable = IsUsable(vanity, index);
        ItemSlot.OverrideHover(inventory, hoverContext, index);
        if (usable || Main.mouseItem.IsAir)
        {
            ItemSlot.LeftClick(inventory, context, index);
            if (vanity)
                ItemSlot.RightClick(inventory, context, index);
        }
        ItemSlot.MouseHover(inventory, hoverContext, index);
        if (Source != null)
        {
            Source.OnMouseHover((AccessorySlotType)hoverContext);
            if (!usable && Source.IsEnabled())
            {
                Main.HoverItem = new Item();
                Main.hoverItemName = Language.GetTextValue("tModLoader.SharedAccessorySlotConflictTooltip");
            }
        }
    }

    public void HandleVisibility() => HandleVisibility(SoundID.MenuTick);

    internal void HandleVisibility(SoundStyle clickSound)
    {
        if (IsArmor || Owner != Main.LocalPlayer || Functional == null || PlayerInput.IgnoreMouseInterface)
            return;

        using var scope = new Main.CurrentPlayerOverride(Owner!);
        Owner!.mouseInterface = true;
        bool hidden = Source?.HideVisuals ?? Owner.hideVisibleAccessory[VanillaIndex];
        if (Main.mouseLeft && Main.mouseLeftRelease)
        {
            hidden = !hidden;
            if (Source != null)
                Source.HideVisuals = hidden;
            else
                Owner.hideVisibleAccessory[VanillaIndex] = hidden;
            SoundEngine.PlaySound(clickSound);
            if (Source == null && Main.netMode == NetmodeID.MultiplayerClient)
                NetMessage.SendData(MessageID.SyncPlayer, number: Owner.whoAmI);
        }
        Main.HoverItem = new Item();
        Main.hoverItemName = Lang.inter[hidden ? 60 : 59].Value;
    }

    public void DrawContents(SpriteBatch batch, bool vanity, Vector2 center, float inventoryScale, bool isHovered = false)
    {
        var item = vanity ? Vanity : Functional;
        if (item == null || Main.dedServ)
            return;

        using var scope = new Main.CurrentPlayerOverride(Owner ?? Main.CurrentPlayer);
        int context = vanity ? ItemSlot.Context.EquipAccessoryVanity : ItemSlot.Context.EquipAccessory;
        if (TryGetBinding(vanity, out var inventory, out int signedContext, out int index))
        {
            context = signedContext;
            item = inventory[index];
        }
        Vector2 topLeft = center - new Vector2(26f * inventoryScale);
        var mode = (AccessorySlotType)Math.Abs(context);
        if (Source?.PreDraw(mode, item, topLeft, isHovered) != false)
        {
            if (item.IsAir)
            {
                string? texture = vanity ? Source?.VanityTexture : Source?.FunctionalTexture;
                int frame = IsArmor ? VanillaIndex * 6 + (vanity ? 3 : 0) : (vanity ? 2 : 11);
                EmptySlotIcon.Resolve(texture, frame).Draw(batch, center, inventoryScale);
            }
            else
            {
                float itemScale = IsArmor ? inventoryScale * 1.18f : inventoryScale;
                ItemSlot.DrawItemIcon(item, context, batch, center, itemScale, 32f, Color.White);
            }
        }
        Source?.PostDraw(mode, item, topLeft, isHovered);
    }
}

public readonly record struct EmptySlotIcon(Texture2D Texture, Rectangle Frame)
{
    internal static EmptySlotIcon Resolve(string? texturePath, int fallbackFrame)
    {
        if (!string.IsNullOrEmpty(texturePath)
            && ModContent.RequestIfExists<Texture2D>(texturePath, out var asset, AssetRequestMode.AsyncLoad)
            && asset.IsLoaded)
            return new EmptySlotIcon(asset.Value, asset.Value.Bounds);

        var sheet = TextureAssets.Extra[ExtrasID.EquipIcons].Value;
        var frame = sheet.Frame(3, 6, fallbackFrame % 3, fallbackFrame / 3);
        frame.Width -= 2;
        frame.Height -= 2;
        return new EmptySlotIcon(sheet, frame);
    }

    public void Draw(SpriteBatch batch, Vector2 center, float inventoryScale)
    {
        batch.Draw(Texture, center, Frame, Color.White * 0.35f, 0f, Frame.Size() / 2f,
            inventoryScale, SpriteEffects.None, 0f);
    }
}

public static class AccessoryProbe
{
    private static Slot VanillaSlot(Player player, int index, bool armor = false) =>
        new($"Terraria/{index}", player.armor[index], player.armor[index + player.dye.Length],
            armor || !player.hideVisibleAccessory[index]) { Owner = player, VanillaIndex = index, IsArmor = armor };

    public static IReadOnlyList<Slot> ReadArmor(Player player) =>
        new[] { VanillaSlot(player, 0, true), VanillaSlot(player, 1, true), VanillaSlot(player, 2, true) };

    public static IReadOnlyList<Slot> Read(Player player)
    {
        using var scope = new Main.CurrentPlayerOverride(player);
        var result = new List<Slot>();
        for (int i = 3; i < player.dye.Length; i++)
        {
            if (player.IsItemSlotUnlockedAndUsable(i) ||
                (i == 8 && player.CanDemonHeartAccessoryBeShown()) ||
                (i == 9 && player.CanMasterModeAccessoryBeShown()))
                result.Add(VanillaSlot(player, i));
        }
        var loader = LoaderManager.Get<AccessorySlotLoader>();
        var slots = player.GetModPlayer<ModAccessorySlotPlayer>();
        for (int i = 0; i < slots.SlotCount; i++)
        {
            var slot = loader.Get(i, player);
            if (slot.IsHidden() || (!slot.IsEnabled() && !slot.IsVisibleWhenNotEnabled()))
                continue;
            bool functional = slot.DrawFunctionalSlot;
            bool vanity = slot.DrawVanitySlot;
            if (functional || vanity)
                result.Add(new Slot(i < slots.LoadedSlotCount ? slot.FullName : $"Unloaded/{i}",
                    functional ? slot.FunctionalItem : null, vanity ? slot.VanityItem : null, !slot.HideVisuals)
                    { Owner = player, Source = slot });
        }
        return result;
    }
}
