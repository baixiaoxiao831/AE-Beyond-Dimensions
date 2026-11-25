using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace AEBeyondDimensions;

public sealed class TerminalStorage : ModPlayer
{
    internal bool Unlocked { get; private set; }
    internal void Unlock() => Unlocked = true;
    internal bool CanAccess(bool notify)
    {
        if(Main.netMode==NetmodeID.MultiplayerClient)return TeamClient.Open;
        if (Unlocked && SingleTerminal.Selected) return true;
        if (notify && Local) Main.NewText(TerminalText.Get("制作并放置 ME 控制器，右键激活后解锁终端。"));
        return false;
    }
    private const int BlockSize = 54;
    private Item[] slots = Array.Empty<Item>();
    private Dictionary<int,Item> batchSlots = new();
    private int[] latestTypes = Array.Empty<int>();
    private long receivedAt;
    private bool batchDisplayed;
    internal enum SortOrder { Recent, Quantity, Name }
    internal enum TerminalLength { Short, Long, Full }
    internal SortOrder Order { get; set; }
    internal TerminalLength Length { get; private set; }
    internal void CycleLength() => Length = Length switch {
        TerminalLength.Short => TerminalLength.Long,
        TerminalLength.Long => TerminalLength.Full,
        _ => TerminalLength.Short
    };
    private readonly Dictionary<int,long> lastDeposit = new();
    private long sequence;
    private int descendingMask=3;
    internal bool Descending => (descendingMask & (1 << (int)Order)) != 0;
    internal void ToggleDirection() => descendingMask ^= 1 << (int)Order;
    internal void NoteDeposit(int type) => lastDeposit[type]=++sequence;
    private Item[]? characterSlots;
    internal Item[] CharacterSlots=>characterSlots ?? slots;
    internal void SelectStock(TeamBank bank){
        characterSlots??=slots;
        slots=bank.Stock.ToArray();
        EnsureSpace(Math.Max(BlockSize,slots.Length));
        ClearReceipts();
    }
    internal void ReleaseStock(){
        if(characterSlots!=null){slots=characterSlots;characterSlots=null;ClearReceipts();}
    }
    private void ClearReceipts(){batchSlots.Clear();latestTypes=Array.Empty<int>();batchDisplayed=false;receivedAt++;}
    internal bool Serving {get;private set;}
    internal void WithBank(TeamBank bank,Action action){
        var saved=slots;var work=Player.GetModPlayer<TerminalWorkArea>();
        var stations=work.Stations;
        slots=bank.Stock.ToArray();work.Stations=bank.Stations;Serving=true;
        try{action();}finally{
            bank.Stock=slots.Where(i=>!i.IsAir).ToList();slots=saved;
            work.Stations=stations;Serving=false;
        }
    }
    internal Item[] Slots => slots;
    internal Item[] DisplaySlots=>TeamClient.Open?TeamClient.Stock:slots;
    internal IReadOnlyList<int> LatestTypes => latestTypes;
    internal long ReceiptStamp => receivedAt;
    internal bool HasPinnedItems => batchSlots.Keys.Any(IsBatch);

    internal void MarkDeliveryDisplayed() => batchDisplayed = true;

    private Item? firstHeld, firstTarget;
    private int firstSlot;
    private long firstTime;

    internal bool HandleMatchingClick(int index)
    {
        var held=Main.mouseItem;
        var target=Player.inventory[index];
        if(!Local || held.IsAir || held.type!=target.type || !ItemSlot.ShiftInUse ||
            !Main.mouseLeft || !Main.mouseLeftRelease) return false;
        Main.mouseLeftRelease=false;
        long now=Stopwatch.GetTimestamp();
        if(firstSlot==index && ReferenceEquals(firstHeld,held) && ReferenceEquals(firstTarget,target) &&
            Stopwatch.GetElapsedTime(firstTime,now).TotalMilliseconds<500) {
            firstHeld=firstTarget=null;
            for(int i=0;i<58;i++)
                if(Player.inventory[i].type==held.type) Deposit(i);
            if(!held.favorited){
                StoreTransfer(held);
                Main.mouseItem=new Item();
                TerminalCursor.Clear();
                Recipe.FindRecipes();
            }
        } else {
            firstSlot=index; firstHeld=held; firstTarget=target; firstTime=now;
        }
        return true;
    }

    internal void BeginTerminalVisit()
    {
        firstHeld=firstTarget=null;
        if (!batchDisplayed) return;
        batchSlots.Clear();
        latestTypes = Array.Empty<int>();
        batchDisplayed = false;
    }

    public override void Initialize()
    {
        Unlocked = false;
        slots = Array.Empty<Item>();
        batchSlots = new();
        latestTypes = Array.Empty<int>();
        receivedAt = 0;
        batchDisplayed = false;
        lastDeposit.Clear();
        sequence=0;
        Order=SortOrder.Recent;
        Length=TerminalLength.Full;
        descendingMask=3;
        EnsureSpace(BlockSize);
    }

    private void EnsureSpace(int required)
    {
        if (required <= slots.Length) return;
        int previous = slots.Length;
        Array.Resize(ref slots, checked((required + BlockSize - 1) / BlockSize * BlockSize));
        for (int i = previous; i < slots.Length; i++) slots[i] = new Item();
    }

    internal int EmptyCell()
    {
        int i = Array.FindIndex(slots,item => item.IsAir);
        if (i >= 0) return i;
        i = slots.Length;
        EnsureSpace(i + 1);
        return i;
    }

    public override void SaveData(TagCompound tag)
    {
        var items=new List<TagCompound>();
        var delivered=new List<int>();
        var saved=CharacterSlots;
        for (int i=0;i<saved.Length;i++) {
            if (saved[i].IsAir) continue;
            // A reloaded character starts a new visit. Preserve only receipts still to be shown.
            if (characterSlots==null && !batchDisplayed && IsBatch(i)) delivered.Add(items.Count);
            items.Add(ItemIO.Save(saved[i]));
        }
        tag["descendingMask"]=descendingMask;
        tag["sortOrder"]=(int)Order;
        tag["terminalLength"]=(int)Length;
        tag["depositOrder"]=saved.Where(i=>!i.IsAir).Select(i=>lastDeposit.GetValueOrDefault(i.type)).ToList();
        tag["controllerUnlocked"]=Unlocked;
        tag["items"]=items;
        tag["latestBatch"]=delivered;
    }

    public override void LoadData(TagCompound tag)
    {
        Initialize();
        var items = tag.GetList<TagCompound>("items");
        Unlocked = tag.ContainsKey("controllerUnlocked") ? tag.GetBool("controllerUnlocked") : items.Count>0;
        EnsureSpace(items.Count);
        for (int i=0;i<items.Count;i++) slots[i] = ItemIO.Load(items[i]);
        if(tag.ContainsKey("descendingMask")) descendingMask=tag.GetInt("descendingMask") & 7;
        int order=tag.GetInt("sortOrder");
        Order=Enum.IsDefined(typeof(SortOrder),order) ? (SortOrder)order : SortOrder.Recent;
        if(tag.TryGet("terminalLength",out int length) && Enum.IsDefined(typeof(TerminalLength),length))
            Length=(TerminalLength)length;
        var ranks=tag.GetList<long>("depositOrder");
        // Older inventories have no deposit history, or a zero for each untracked type.
        // Keep their existing slot order for newest-first, giving each type a distinct rank.
        var newestFirst = Enumerable.Range(0,items.Count)
            .Where(i => !slots[i].IsAir)
            .GroupBy(i => slots[i].type)
            .OrderByDescending(group => group.Max(i => i < ranks.Count ? ranks[i] : 0L));
        foreach (var group in newestFirst.Reverse())
            NoteDeposit(group.Key);
        foreach (int i in tag.GetList<int>("latestBatch"))
            if ((uint)i < items.Count && !slots[i].IsAir) batchSlots[i]=slots[i];
        latestTypes=batchSlots.Values.Select(item => item.type).Distinct().ToArray();
    }

    private bool Local => Player == Main.LocalPlayer && Main.netMode == NetmodeID.SinglePlayer;

    // The provider transfers detached, completed outputs on the game thread. Only successful
    // receipt consumes them. Keep independent ItemIO data even for equal types and prefixes.
    public bool TryReceiveOrderOutputs(params Item[] outputs) => ReceiveOutputs(outputs,0);

    internal bool ReceiveOutputs(Item[] outputs,int stationOutput)
    {
        if (!Local || outputs == null || outputs.Length == 0) return false;
        var seen = new HashSet<Item>(ReferenceEqualityComparer.Instance);
        foreach (var item in outputs)
            if (item == null || item.IsAir || item.stack > item.maxStack || !seen.Add(item)) return false;
        if (slots.Any(seen.Contains) || Player.inventory.Any(seen.Contains) || Player.armor.Any(seen.Contains) ||
            Player.dye.Any(seen.Contains) || Player.miscEquips.Any(seen.Contains) || Player.miscDyes.Any(seen.Contains) ||
            Player.bank.item.Any(seen.Contains) || Player.bank2.item.Any(seen.Contains) ||
            Player.bank3.item.Any(seen.Contains) || Player.bank4.item.Any(seen.Contains) || seen.Contains(Main.mouseItem) ||
            seen.Contains(Player.GetModPlayer<TerminalReforgePlayer>().ReforgeItem)) return false;

        // Clone before any write so references retained by a provider cannot mutate storage.
        Item[] copies;
        try {
            copies=outputs.Select(item => item.Clone()).ToArray();
        } catch (Exception error) {
            Mod.Logger.Warn("A completed output could not be cloned for terminal storage.",error);
            return false;
        }
        int free = slots.Count(item => item.IsAir);
        EnsureSpace(checked(slots.Length + Math.Max(0,copies.Length-free)));
        batchSlots.Clear();
        batchDisplayed=false;
        latestTypes=copies.Select(item => item.type).Distinct().ToArray();
        int write=0;
        foreach (var copy in copies) {
            if(copy.type==stationOutput) Player.GetModPlayer<TerminalWorkArea>().StoreStation(copy);
            if(copy.IsAir)continue;
            while (write<slots.Length && !slots[write].IsAir) write++;
            EnsureSpace(checked(write+1));
            batchSlots[write]=slots[write]=copy;
            NoteDeposit(copy.type);
            write++;
        }
        foreach (var original in outputs) original.TurnToAir();
        receivedAt=Stopwatch.GetTimestamp();
        try {
            if (Player.GetModPlayer<TerminalSearchSettings>().DeliverToBag)
                foreach (int index in batchSlots.Keys.ToArray()) Withdraw(index,lootAll:true);
            Recipe.FindRecipes();
        } catch (Exception error) {
            Mod.Logger.Warn("Recipe refresh failed after terminal output delivery.",error);
        }
        return true;
    }

    public bool Deposit(int inventoryIndex)
    {
        if(TeamClient.Open)return TeamClient.Action(TeamOp.Deposit,TeamTab.Stock,inventoryIndex);
        if (!(Local || Serving) || (uint)inventoryIndex >= 58) return false;
        var source = Player.inventory[inventoryIndex];
        if (source.IsAir || source.favorited) return false;
        int context=inventoryIndex<50?ItemSlot.Context.InventoryItem:inventoryIndex<54?ItemSlot.Context.InventoryCoin:ItemSlot.Context.InventoryAmmo;
        var announcement = new ItemSlot.ItemTransferInfo(source, context, ItemSlot.Context.ChestItem);
        StoreTransfer(source);
        Player.inventory[inventoryIndex]=new Item();
        if(Serving)return true;
        ItemSlot.AnnounceTransfer(announcement);
        SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
        return true;
    }

    internal void StoreTransfer(Item source)
    {
        Player.GetModPlayer<TerminalWorkArea>().StoreStation(source);
        if (!source.IsAir) StoreLooseItem(source);
    }

    internal void StoreLooseItem(Item source)
    {
        NoteDeposit(source.type);
        if (source.maxStack > 1) {
            for (int i=0;i<slots.Length;i++) {
                var target=slots[i];
                if (target.IsAir || target.type != source.type || target.stack >= target.maxStack || IsBatch(i)) continue;
                ItemLoader.TryStackItems(target,source,out _);
                if (source.stack <= 0) break;
            }
        }
        if (!source.IsAir && source.stack > 0) {
            int empty = EmptyCell();
            slots[empty] = source;
        }
    }

    public bool Withdraw(int index, bool lootAll = false)
    {
        if (!(Local || Serving) || (uint)index >= slots.Length || slots[index].IsAir) return false;
        bool delivered=IsBatch(index);
        var source=slots[index];
        int before=source.stack;
        int type=source.type;
        if(Serving){SharedActions.Give(Player,source);return source.IsAir || source.stack<before;}
        var transfer=new ItemSlot.ItemTransferInfo(source,ItemSlot.Context.ChestItem,ItemSlot.Context.InventoryItem);
        source.position=Player.Center;
        slots[index]=Player.GetItem(Player.whoAmI,source,lootAll ? GetItemSettings.LootAllSettingsRegularChest :
            GetItemSettings.InventoryEntityToPlayerInventorySettings);
        int moved=before-(slots[index].type == type ? slots[index].stack : 0);
        if (moved <= 0) return false;
        if (delivered) {
            if (slots[index].IsAir) batchSlots.Remove(index);
            else batchSlots[index]=slots[index];
        }
        transfer.TransferAmount=moved;
        ItemSlot.AnnounceTransfer(transfer);
        Recipe.FindRecipes();
        return true;
    }

    private static bool QuickStackCandidate(Item item) => !item.IsAir && !item.favorited &&
        item.type is < ItemID.CopperCoin or > ItemID.PlatinumCoin;

    internal bool MatchesQuickStack(Item item)
    {
        if (!QuickStackCandidate(item)) return false;
        foreach (var stored in slots)
            if (!stored.IsAir && stored.type == item.type) return true;
        return false;
    }

    public void DepositBackpack(bool matchingOnly)
    {
        if(TeamClient.Open){TeamClient.Shared(matchingOnly?SharedAction.QuickStack:SharedAction.DepositAll);return;}
        if (!Local && !Serving) return;
        var initial=matchingOnly ? slots.Where(item => !item.IsAir).Select(item => item.type).ToHashSet() : null;
        for (int n=0;n<40;n++) {
            int i=matchingOnly ? 10+n : 49-n;
            var item=Player.inventory[i];
            if (initial == null || (QuickStackCandidate(item) && initial.Contains(item.type))) Deposit(i);
        }
    }

    public void LootAll()
    {
        if(TeamClient.Open){TeamClient.Shared(SharedAction.LootAll);return;}
        if (!Local && !Serving) return;
        for (int i=0;i<slots.Length;i++) Withdraw(i,lootAll:true);
    }

    public void Restock()
    {
        if(TeamClient.Open){TeamClient.Shared(SharedAction.Restock);return;}
        if (!Local && !Serving) return;
        bool moved=false;
        for (int i=57;i>=0;i--) {
            if (i is >=50 and <54) continue;
            var target=Player.inventory[i];
            if (target.IsAir || target.stack>=target.maxStack || target.type is >=ItemID.CopperCoin and <=ItemID.PlatinumCoin) continue;
            for (int j=0;j<slots.Length && target.stack<target.maxStack;j++) {
                var source=slots[j];
                if (source.IsAir || source.type!=target.type ||
                    !ItemLoader.TryStackItems(target,source,out int taken) || taken<=0) continue;
                moved=true;
                if(!Serving)ItemSlot.AnnounceTransfer(new ItemSlot.ItemTransferInfo(target,ItemSlot.Context.ChestItem,
                    i<50 ? ItemSlot.Context.InventoryItem : ItemSlot.Context.InventoryAmmo,taken));
                if (source.stack<=0) { slots[j]=new Item(); batchSlots.Remove(j); }
            }
        }
        // Vanilla restock also moves spare matching ammunition into eligible empty slots.
        var ammoTypes=Player.inventory.Take(58).Where(item => !item.IsAir && item.ammo>0 &&
            item.maxStack>1 && item.type is < ItemID.CopperCoin or > ItemID.PlatinumCoin).Select(item => item.netID).ToHashSet();
        for (int j=0;j<slots.Length;j++) {
            var source=slots[j];
            if (source.IsAir || !ammoTypes.Contains(source.netID)) continue;
            for (int i=57;i>=0;i--) {
                if (i is >=50 and <54 || !Player.inventory[i].IsAir) continue;
                int context=i<50 ? ItemSlot.Context.InventoryItem : ItemSlot.Context.InventoryAmmo;
                if (ItemSlot.PickItemMovementAction(Player.inventory,context,i,source)==-1) continue;
                Player.inventory[i]=source;
                slots[j]=new Item();
                batchSlots.Remove(j);
                moved=true;
                break;
            }
        }
        if (moved && !Serving) { SoundEngine.PlaySound(SoundID.Grab); Recipe.FindRecipes(); }
    }

    internal bool IsBatch(int index) => !TeamClient.Open && batchSlots.TryGetValue(index,out var expected) &&
        ReferenceEquals(expected,slots[index]) && !expected.IsAir;

    internal Item ReturnCursorItem(int index, long receipt, Item item)
    {
        bool pin = receipt == ReceiptStamp && latestTypes.Contains(item.type);
        if (slots[index].IsAir || IsBatch(index) == pin)
        {
            item = TerminalCursor.RestoreSlot(slots,index,item);
            if (item.IsAir)
            {
                if (pin) batchSlots[index] = slots[index];
                return item;
            }
        }
        index = EmptyCell();
        item = TerminalCursor.RestoreSlot(slots,index,item);
        if (pin) batchSlots[index] = slots[index];
        return item;
    }

    internal void ReconcileDirectClick(int index,int previousCount)
    {
        if (!slots[index].IsAir && slots[index].stack>previousCount) NoteDeposit(slots[index].type);
        if (!batchSlots.ContainsKey(index)) return;
        if (!IsBatch(index)) {
            batchSlots.Remove(index);
            return;
        }
        // A manual chest stack onto the output row must only add the new quantity to ordinary stock.
        if (slots[index].stack > previousCount) {
            int free=EmptyCell();
            slots[free]=ItemLoader.TransferWithLimit(slots[index],slots[index].stack-previousCount);
        }
    }

    internal record Cell(int Index, int Type, int Prefix, long Count, bool Variants, bool Delivered);

    private readonly TerminalItemSearch searchFilter = new();
    internal List<Cell> GetCells(int? detailType,string search,bool? detailBatch=null)
    {
        bool pin=Player.GetModPlayer<TerminalSearchSettings>().PinCraft;
        var displayed=DisplaySlots;
        var entries = displayed.Select((item,index) => (item,index))
            .Where(p => !p.item.IsAir && (detailType == null || p.item.type == detailType)
                && (!pin || !detailBatch.HasValue || detailBatch==IsBatch(p.index))
                && (search.Length==0 || 
                    searchFilter.Matches(p.item,search)));
        var groups = entries.GroupBy(p => (p.item.type, prefix:detailType.HasValue ? p.item.prefix : -1, delivered:pin && IsBatch(p.index)));
        var cells = groups.Select(g => {
            var first = g.First();
            bool variants = !detailType.HasValue && g.Any(p => p.item.prefix != first.item.prefix);
            var best = variants || detailType.HasValue
                ? g.MaxBy(p => TerminalReforgePlayer.Quote(Player,p.item)) : first;
            return new Cell(best.index,g.Key.type,g.Key.prefix,g.Sum(p => (long)p.item.stack),variants,g.Key.delivered);
        });
        if (detailType.HasValue)
            return cells.OrderByDescending(c=>TerminalReforgePlayer.Quote(Player,displayed[c.Index])).ToList();
        if (Order==SortOrder.Name) {
            var comparer=StringComparer.Create(Terraria.Localization.Language.ActiveCulture.CultureInfo,true);
            return (Descending ? cells.OrderByDescending(c=>Lang.GetItemNameValue(c.Type),comparer)
                : cells.OrderBy(c=>Lang.GetItemNameValue(c.Type),comparer)).ToList();
        }
        long Key(Cell c) => Order==SortOrder.Quantity ? c.Count : lastDeposit.GetValueOrDefault(c.Type);
        return (Descending ? cells.OrderByDescending(Key) : cells.OrderBy(Key)).ToList();
    }
}
