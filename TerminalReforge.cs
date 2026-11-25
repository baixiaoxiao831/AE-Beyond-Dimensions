using System;
using System.Collections.Generic;
using System.Linq;
using AccessoryProbe;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace AEBeyondDimensions;

public sealed class TerminalReforgePlayer : ModPlayer
{
    private Item[] input = null!;
    private HashSet<Guid> deliveries;
    public Item ReforgeItem => input[0];

    public override void Initialize(){input=new[]{new Item()};deliveries=new();}

    public override void SaveData(TagCompound tag)
    {
        if(deliveries.Count>0)tag["forgeReceipts"]=deliveries.Select(id=>id.ToString("N")).ToList();
        if (!ReforgeItem.IsAir)
            tag["reforgeItem"] = ItemIO.Save(ReforgeItem);
    }

    public override void LoadData(TagCompound tag)
    {
        deliveries.Clear();
        foreach(string key in tag.GetList<string>("forgeReceipts"))if(Guid.TryParse(key,out var id))deliveries.Add(id);
        if (tag.TryGet("reforgeItem", out TagCompound saved))
            input[0] = ItemIO.Load(saved);
    }

    public override void OnEnterWorld() => ReturnInput();

    internal void ReturnInput()
    {
        if (ReforgeItem.IsAir) return;
        var remaining = Player.GetItem(Player.whoAmI, ReforgeItem, GetItemSettings.InventoryEntityToPlayerInventorySettings);
        if (!remaining.IsAir){
            if(TeamSupply.Active)TerminalDelivery.Drop(Player,remaining,false);
            else if(Main.netMode==NetmodeID.MultiplayerClient)Player.QuickSpawnItem(Player.GetSource_Misc("AE reforge return"),remaining,remaining.stack);
            else Player.GetModPlayer<TerminalStorage>().StoreLooseItem(remaining);
        }
        input[0] = new Item();
    }

    public int Price => Quote(Player, ReforgeItem);

    internal static int Quote(Player player, Item item)
    {
        if (item.IsAir) return 0;
        int amount = item.value * item.stack;
        bool discount = true;
        if (ItemLoader.ReforgePrice(item, ref amount, ref discount)) {
            if (discount && player.discountAvailable) amount = (int)(amount * 0.8);
            amount = (int)(amount * player.currentShoppingSettings.PriceAdjustment) / 3;
        }
        return amount;
    }

    // True means the reforge page consumed this Shift-click, including a blocked swap.
    public bool TryTransferInventory(int index)
    {
        if ((uint)index >= 50 || !CanMove(Player.inventory[index])) return false;
        Exchange(Player.inventory, index, Player.inventory, index);
        return true;
    }

    internal void ReceiveDelivery(Guid ticket,Item item){
        if(Main.ActivePlayerFileData?.Player!=Player)return;
        if(!deliveries.Contains(ticket)){
            ReceiveFromBank(item);
            deliveries.Add(ticket);
        }
        // The item and receipt must reach the same character save before acknowledging.
        Terraria.Player.SavePlayer(Main.ActivePlayerFileData);
        var ack=TeamNetwork.Packet(TeamOp.ForgeReceipt,0);
        ack.Write(ticket.ToByteArray());ack.Send();
    }
    private void ReceiveFromBank(Item item){

        if(!input[0].IsAir)TerminalDelivery.Drop(Player,input[0],false);
        input[0]=item;Recipe.FindRecipes();
    }
    public bool TryTransferStorage(TerminalStorage storage,int index)
    {
        var slots=storage.Slots;
        if (storage.Player != Player || (uint)index >= slots.Length || !CanMove(slots[index])) return false;
        Exchange(slots,index,slots,index);
        if (!slots[index].IsAir) storage.NoteDeposit(slots[index].type);
        return true;
    }

    public bool TryTransferEquipment(Slot slot, bool vanity)
    {
        if (!slot.TryGetBinding(vanity, out var equipped, out int context, out int index)
            || !CanMove(equipped[index]))
            return false;

        if (Math.Abs(context) == ItemSlot.Context.EquipAccessory && ItemSlot.isEquipLocked(equipped[index].type))
            return true;
        if (slot.CanAccept(vanity, ReforgeItem)) {
            Exchange(equipped, index, equipped, index);
            return true;
        }
        if (!slot.CanAccept(vanity, new Item())) return true;
        int empty = Array.FindIndex(Player.inventory, 0, 50, item => item.IsAir);
        if (empty >= 0)
            Exchange(equipped, index, Player.inventory, empty);
        return true;
    }

    internal bool CanMove(Item item) =>
        Player == Main.LocalPlayer && Player.GetModPlayer<TerminalMagnet>().ReforgeAvailable && Player.itemAnimation == 0 && Player.itemTime == 0
        && !item.IsAir && !item.favorited && item.Prefix(-3);

    private void Exchange(Item[] source, int sourceIndex, Item[] destination, int destinationIndex)
    {
        var incoming = source[sourceIndex];
        var outgoing = input[0];
        source[sourceIndex] = new Item();
        destination[destinationIndex] = outgoing;
        input[0] = incoming;
        Recipe.FindRecipes();
        SoundEngine.PlaySound(SoundID.Grab);
    }

    public void HandleSlot()
    {
        if (Player != Main.LocalPlayer || PlayerInput.IgnoreMouseInterface) return;
        Player.mouseInterface = true;
        ItemSlot.OverrideHover(input, ItemSlot.Context.PrefixItem);
        bool wasEmpty = Main.mouseItem.IsAir;
        if (!TerminalCursor.HandleOccupiedCursor(input[0]))
            ItemSlot.LeftClick(input, ItemSlot.Context.PrefixItem);
        if (wasEmpty && !Main.mouseItem.IsAir) TerminalCursor.RememberSlot(input,0);
        TerminalTooltip.ShowItem(input[0]);
    }

    internal bool TryChoose(Item expected,int prefix)
    {
        var item=ReforgeItem;
        if (Player!=Main.LocalPlayer || !ReforgeChoices.Enabled || item!=expected || item.IsAir ||
            prefix<=0 || prefix>=PrefixLoader.PrefixCount || item.prefix==prefix || !item.Prefix(-3) || !ItemLoader.CanReforge(item)) return false;
        int price=Price;
        if (!Player.CanAfford(price) || ReforgeChoices.Candidate(item,prefix)==null) return false;
        var replacement=item.Clone();
        ItemLoader.PreReforge(replacement);
        replacement.ResetPrefix();
        if (!replacement.CanApplyPrefix(prefix) || !replacement.Prefix(prefix) ||
            replacement.prefix!=prefix || !Player.BuyItem(price)) return false;
        input[0]=replacement;
        FinishReforge(replacement);
        return true;
    }

    public bool TryReforge()
    {
        var item = ReforgeItem;
        if (Player != Main.LocalPlayer || !Player.GetModPlayer<TerminalMagnet>().ReforgeAvailable || item.IsAir || !item.Prefix(-3)) return false;
        int price = Price;
        if (!Player.CanAfford(price) || !ItemLoader.CanReforge(item) || !Player.BuyItem(price)) return false;
        ItemLoader.PreReforge(item);
        item.ResetPrefix();
        item.Prefix(-2);
        FinishReforge(item);
        return true;
    }

    private void FinishReforge(Item item)
    {
        item.position = Player.Center - item.Size * 0.5f;
        ItemLoader.PostReforge(item);
        if (!Main.dedServ)
            PopupText.NewText(PopupTextContext.ItemReforge, item, item.stack, noStack: true);
        if (!Main.dedServ) SoundEngine.PlaySound(SoundID.Item37);
    }
}
