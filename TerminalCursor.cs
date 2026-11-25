using System;
using System.Collections.Generic;
using AccessoryProbe;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

internal static class TerminalCursor
{
    // Each starting count is the amount already held when this source contributed.
    // Spending from the cursor consumes the most recent contributions first.
    private sealed record Portion(object Source, int Index, long Batch, int Start, Func<Item, Item> Restore);
    private static readonly List<Portion> portions = new();
    private static Item? pickup;
    internal static int HeldCount => Main.mouseItem.IsAir ? 0 : Main.mouseItem.stack;

    internal static void Clear()
    {
        pickup = null;
        portions.Clear();
    }

    private static void Remember(object source, int index, long batch, int previousCount, Func<Item, Item> returnToSource)
    {
        if (!ReferenceEquals(pickup, Main.mouseItem)) portions.Clear();
        pickup = Main.mouseItem;
        while (portions.Count > 0 && portions[^1].Start >= previousCount)
            portions.RemoveAt(portions.Count - 1);
        if (portions.Count > 0 && ReferenceEquals(portions[^1].Source, source) &&
            portions[^1].Index == index && portions[^1].Batch == batch) return;
        portions.Add(new Portion(source, index, batch, previousCount, returnToSource));
    }

    internal static void RememberSlot(Item[] slots, int index, int previousCount = 0) =>
        Remember(slots, index, 0, previousCount, item => RestoreSlot(slots, index, item));

    internal static void RememberTrash(Player player, int previousCount) =>
        Remember(player, 0, 0, previousCount, item => {
            if (!player.trashItem.IsAir) return item;
            player.trashItem = item;
            return new Item();
        });

    internal static void RememberEquipment(Slot slot, bool vanity) => Remember(slot, 0, 0, 0, item =>
    {
        if (slot.TryGetBinding(vanity, out var items, out _, out int index) && items[index].IsAir &&
            slot.CanAccept(vanity, item))
            return RestoreSlot(items, index, item);
        return item;
    });

    internal static void RememberList(List<Item> slots, int index) => Remember(slots, index, 0, 0, item =>
    {
        slots.Insert(Math.Min(index, slots.Count), item);
        return new Item();
    });

    internal static void RememberStorage(TerminalStorage storage, int index, bool delivered, int previousCount = 0)
    {
        long batch = delivered ? storage.ReceiptStamp : -1;
        if (delivered) storage.MarkDeliveryDisplayed();
        Remember(storage, index, batch, previousCount, item => storage.ReturnCursorItem(index, batch, item));
    }

    private static bool StackOnto(Item target, Item held)
    {
        if (target.type != held.type || target.prefix != held.prefix || target.stack >= target.maxStack ||
            !ItemLoader.TryStackItems(target, held, out int moved) || moved <= 0)
            return false;
        if (held.stack <= 0) held.TurnToAir();
        return true;
    }

    internal static Item RestoreSlot(Item[] slots, int index, Item item)
    {
        if (!slots[index].IsAir)
        {
            StackOnto(slots[index], item);
            return item;
        }
        slots[index] = item;
        return new Item();
    }

    private static Item ReturnRemainder(Item item)
    {
        // The cursor may have entered from vanilla inventory, or its original slot may now be occupied.
        var player = Main.LocalPlayer;
        for (int i = 0; !item.IsAir && i < 50; i++)
            item = RestoreSlot(player.inventory, i, item);
        if (item.IsAir) return item;
        var storage = player.GetModPlayer<TerminalStorage>();
        storage.NoteDeposit(item.type);
        int free = storage.EmptyCell();
        return RestoreSlot(storage.Slots, free, item);
    }

    internal static void ReturnHeld()
    {
        var held = Main.mouseItem;
        if (held.IsAir) return;
        if (ReferenceEquals(held, pickup))
        {
            for (int i = portions.Count - 1; i >= 0; i--)
            {
                var portion = portions[i];
                if (held.stack <= portion.Start) continue;
                if (portion.Start == 0)
                    held = portion.Restore(held);
                else
                {
                    var part = ItemLoader.TransferWithLimit(held, held.stack - portion.Start);
                    ReturnRemainder(portion.Restore(part));
                }
            }
        }
        Main.mouseItem = ReturnRemainder(held);
        Clear();
        SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
    }

    internal static bool HandleOccupiedCursor(Item? target, bool allowStack = false)
    {
        var held = Main.mouseItem;
        if (held.IsAir || target == null || target.IsAir) return false;
        if (Main.mouseLeft && Main.mouseLeftRelease)
        {
            Main.mouseLeftRelease = false;
            if (allowStack && target.maxStack > 1 && target.type == held.type && target.prefix == held.prefix &&
                target.stack >= target.maxStack && ItemLoader.CanStack(target, held))
                return true;
            if (allowStack && StackOnto(target, held))
            {
                SoundEngine.PlaySound(SoundID.Grab);
                Recipe.FindRecipes();
            }
            else ReturnHeld();
            return true;
        }
        return !allowStack || target.maxStack <= 1 || target.type != held.type || target.prefix != held.prefix;
    }
}
