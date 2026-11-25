using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AEBeyondDimensions;

public sealed class TerminalChestLoot : ModSystem
{
    public override void Load()
    {
        On_ChestUI.LootAll += LootAll;
    }

    private delegate void SpawnContents(int x,int y,ref Item item,string context);
    private delegate void RouteContents(SpawnContents original,int x,int y,ref Item item,string context);

    public override void PostSetupContent()
    {
        if (!ModLoader.TryGetMod("ImproveGame",out var improve)) return;
        var check=improve.Code.GetType("ImproveGame.Content.Items.MagickWand")?.GetMethod("CheckChestDestroy",
            System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,new[]{typeof(Player),typeof(int),typeof(int)},null);
        if(check!=null)MonoModHooks.Add(check,(CheckHook)WithWandPlayer);
        var method=improve.Code.GetType("ImproveGame.Helpers.MyUtils")?.GetMethod("SpawnTileBreakItem",
            new[] { typeof(int),typeof(int),typeof(Item).MakeByRefType(),typeof(string) });
        if (method!=null) MonoModHooks.Add(method,(RouteContents)RouteWandContents);
        else Mod.Logger.Warn("ImproveGame chest-break item helper unavailable.");
        // The open-bag button enumerates its rewards, then clears the list and refreshes its UI.
        // Intercept only that button's per-item delivery; selling and normal drops keep their own handlers.
        const System.Reflection.BindingFlags nested=System.Reflection.BindingFlags.NonPublic;
        var delivery=improve.Code.GetType("ImproveGame.UI.OpenBag.OpenBagGUI")?.GetNestedType("<>c",nested)?
            .GetMethod("<MakeMainSlotAndButtons>b__20_6",nested|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance,null,new[]{typeof(Item)},null);
        if(delivery!=null)MonoModHooks.Add(delivery,(ActionDeliver)RouteBagReward);
        else Mod.Logger.Warn("ImproveGame open-bag reward callback unavailable.");
    }

    private delegate void Deliver(object receiver,Item reward);
    private delegate void ActionDeliver(Deliver original,object receiver,Item reward);
    private static void RouteBagReward(Deliver original,object receiver,Item reward)
    {
        var p=Main.LocalPlayer;
        if(TeamSupply.Active && !reward.IsAir && !ItemID.Sets.IsAPickup[reward.type] && p.GetModPlayer<TerminalMagnet>().Available){
            var item=reward.Clone();
            var mode=p.GetModPlayer<TerminalMagnet>().Route;
            if(mode==TerminalMagnet.PickupMode.Overflow)
                item=p.GetItem(p.whoAmI,item,GetItemSettings.InventoryEntityToPlayerInventorySettings);
            if(!item.IsAir)TerminalDelivery.Drop(p,item,mode==TerminalMagnet.PickupMode.Matching);
            return;
        }
        if(!MagnetPickup.TryCollect(reward,p))original(receiver,reward);
    }

    private delegate bool Check(object wand,Player player,int x,int y);
    private delegate bool CheckHook(Check original,object wand,Player player,int x,int y);
    [System.ThreadStatic]private static Player wandPlayer;
    private static bool WithWandPlayer(Check original,object wand,Player player,int x,int y){
        var previous=wandPlayer;
        wandPlayer=player;
        try{return original(wand,player,x,y);}finally{wandPlayer=previous;}
    }
    private static void RouteWandContents(SpawnContents original,int x,int y,ref Item item,string context)
    {
        if(context=="ChestBrokenFromBlastsWand" && Main.netMode==NetmodeID.Server && wandPlayer!=null && TeamSupply.RouteWand(wandPlayer,item)){
            item=new Item();return;
        }
        if (context=="ChestBrokenFromBlastsWand" && Main.netMode==NetmodeID.SinglePlayer &&
            MagnetPickup.TryCollect(item,Main.LocalPlayer))
            item=new Item();
        else original(x,y,ref item,context);
    }

    private static bool UseTerminal(Player player) =>
        player.chest >= 0 &&
        player.chest < Main.chest.Length && Main.chest[player.chest] != null &&
        player.GetModPlayer<TerminalSearchSettings>().LootChestToTerminal &&
        (TeamSupply.Active || Main.netMode==NetmodeID.SinglePlayer && SingleTerminal.Selected && player.GetModPlayer<TerminalStorage>().Unlocked);

    internal static void MoveChest(Player p,int index,int x,int y,string key,TeamBank bank)
    {
        if((uint)index>=Main.chest.Length || p.chest!=index || Main.chest[index] is not { } chest ||
           chest.x!=x || chest.y!=y || !WorldGen.InWorld(x,y,1) || Chest.UsingChest(index)!=p.whoAmI ||
           !p.IsInInteractionRangeToMultiTileHitbox(x,y) || Chest.IsLocked(x,y))return;
        bool moved=false;
        for(int slot=0;slot<chest.item.Length;slot++){
            var item=chest.item[slot];if(item==null || item.IsAir)continue;
            bank.Store(item.Clone());chest.item[slot]=new Item();moved=true;
            NetMessage.SendData(MessageID.SyncChestItem,p.whoAmI,-1,null,index,slot);
        }
        if(moved)TeamNetwork.SupplyChanged(key,bank);
    }

    private static void LootAll(On_ChestUI.orig_LootAll original)
    {
        var player=Main.LocalPlayer;
        if (!UseTerminal(player)) { original(); return; }
        if(Main.netMode==NetmodeID.MultiplayerClient){
            var chest=Main.chest[player.chest];
            var packet=TeamNetwork.Packet(TeamOp.ChestLoot,player.team);
            packet.Write(player.chest);packet.Write(chest.x);packet.Write(chest.y);packet.Send();return;
        }
        var items=Main.chest[player.chest].item;
        var storage=player.GetModPlayer<TerminalStorage>();
        bool moved=false;
        for (int i=0;i<items.Length;i++)
        {
            var item=items[i];
            if (item==null || item.IsAir) continue;
            storage.StoreTransfer(item);
            items[i]=new Item();
            moved=true;
        }
        if (moved) SoundEngine.PlaySound(SoundID.Grab);
    }
}
