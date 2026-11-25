using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using MagicStorage.Common.Systems;
using MagicStorage.Components;
using ImproveGame.UI.ExtremeStorage;

namespace AEBeyondDimensions;

public sealed class ExternalStorageImport : ModSystem
{
    private Func<bool>? magicOpen, improveOpen;
    private Func<TerminalStorage,long>? magicImport, improveImport;

    private Func<int>? magicSource, improveSource;
    private Func<TerminalStorage,int,long>? magicServer, improveServer;
    private Action? refreshMagic,refreshImprove;

    internal void ReceiveResult(byte kind,long moved){
        if(kind==0 && magicOpen?.Invoke()==true)refreshMagic?.Invoke();
        if(kind==1 && improveOpen?.Invoke()==true)refreshImprove?.Invoke();
        if(moved>0)Recipe.FindRecipes();
        Main.NewText(string.Format(TerminalText.Get("已转入终端：{0} 个物品"),moved));
    }
    internal void Server(Player player,int team,byte kind,int source){
        long moved=0;
        var import=kind==0?magicServer:kind==1?improveServer:null;
        if(import!=null && TeamNetwork.TryBank(player,team,out var key,out var bank)){
            var storage=player.GetModPlayer<TerminalStorage>();
            storage.WithBank(bank,()=>moved=import(storage,source));
            if(moved>0)TeamNetwork.SupplyChanged(key,bank);
        }
        var reply=TeamNetwork.Packet(TeamOp.ImportResult,team);
        reply.Write(kind);reply.Write(moved);reply.Send(player.whoAmI);
    }
    public override void PostSetupContent()
    {
        if (ModLoader.HasMod("MagicStorage")) BindMagic();
        if (ModLoader.HasMod("ImproveGame")) BindImprove();
    }

    [JITWhenModsEnabled("MagicStorage")]
    private void BindMagic()
    {
        magicOpen=IsMagicOpen;
        magicImport=ImportMagic;
        magicSource=MagicSource;
        magicServer=ImportMagicServer;
        refreshMagic=RefreshMagic;
    }

    [JITWhenModsEnabled("MagicStorage")]
    private static bool IsMagicOpen() => MagicUI.IsStorageUIOpen() || MagicUI.IsCraftingUIOpen();

    [JITWhenModsEnabled("MagicStorage")]
    private static int MagicSource()=>!MagicUI.CurrentlyRefreshing && MagicStorage.StoragePlayer.LocalPlayer.CanAccessCurrentNetwork()
        ? MagicStorage.StoragePlayer.LocalPlayer.GetStorageHeart()?.ID??-1 : -1;
    [JITWhenModsEnabled("MagicStorage")]
    private static void RefreshMagic()=>MagicUI.SetRefresh(true);
    [JITWhenModsEnabled("MagicStorage")]
    private static long ImportMagicServer(TerminalStorage storage,int source){
        if(!Terraria.DataStructures.TileEntity.ByID.TryGetValue(source,out var entity) || entity is not TEStorageHeart heart)return 0;
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var who=storage.Player.whoAmI;
        if(typeof(TEStorageHeart).GetField("clientUsingHeart",flags)?.GetValue(heart) is not bool[] users ||
           (uint)who>=users.Length || !users[who] ||
           typeof(TEStorageComponent).GetField("assignedNetwork",flags)?.GetValue(heart) is not int network ||
           !SecuritySystem.CanPlayerAccessImmediately(storage.Player,network))return 0;
        using var context=SecuritySystem.CreateAccessContext(who);
        long total=MoveMagicStations(storage,heart.ComponentManager.GetCraftingAccessEntities());
        total+=MoveMagicUnits(storage,heart.GetStorageUnits());
        if(total>0)MagicStorage.NetHelper.SendRefreshNetworkItems(heart.Position,true,null);
        return total;
    }
    [JITWhenModsEnabled("MagicStorage")]
    private static long ImportMagic(TerminalStorage storage)
    {
        var player=MagicStorage.StoragePlayer.LocalPlayer;
        if (!IsMagicOpen() || MagicUI.CurrentlyRefreshing || !player.CanAccessCurrentNetwork()) return 0;
        var heart=player.GetStorageHeart();
        if (heart==null) return 0;
        using var access=SecuritySystem.CreateAccessContext(storage.Player.whoAmI);
        long total=MoveMagicStations(storage,heart.ComponentManager.GetCraftingAccessEntities());
        total+=MoveMagicUnits(storage,heart.GetStorageUnits());
        if (total>0) MagicUI.SetRefresh(true);
        return total;
    }

    [JITWhenModsEnabled("MagicStorage")]
    private static long MoveMagicStations(TerminalStorage storage,IEnumerable<TECraftingAccess> accesses)
    {
        long total=0;
        foreach (var access in accesses.Distinct())
        {
            long previous=total;
            for (int i=access.stations.Count-1; i>=0; i--)
            {
                var station=access.stations[i];
                if (station == null || station.IsAir) continue;
                int count=station.stack;
                storage.StoreTransfer(station);
                access.stations.RemoveAt(i);
                total+=count;
            }
            if(total!=previous && Main.netMode==NetmodeID.Server)MagicStorage.NetHelper.SendTEUpdate(access.ID,access.Position);
        }
        return total;
    }

    [JITWhenModsEnabled("MagicStorage")]
    private static long MoveMagicUnits(TerminalStorage storage,IEnumerable<TEAbstractStorageUnit> units)
    {
        long total=0;
        foreach (var unit in units.OfType<TEStorageUnit>().Distinct())
        {
            long previous=total;
            foreach (var request in unit.GetItems().Where(i=>!i.IsAir).Select(i=>i.Clone()).ToArray())
            {
                int left=request.stack;
                while (left>0)
                {
                    request.stack=Math.Min(left,request.maxStack);
                    var withdrawn=unit.TryWithdraw(request,true,false);
                    if (withdrawn.IsAir) break;
                    left-=withdrawn.stack;
                    total+=withdrawn.stack;
                    storage.StoreTransfer(withdrawn);
                }
            }
            if(total!=previous && Main.netMode==NetmodeID.Server){unit.FullySync();MagicStorage.NetHelper.SendTEUpdate(unit.ID,unit.Position);}
        }
        return total;
    }

    [JITWhenModsEnabled("ImproveGame")]
    private void BindImprove()
    {
        improveOpen=IsImproveOpen;
        improveImport=ImportImprove;
        improveSource=ImproveSource;
        improveServer=ImportImproveServer;
        refreshImprove=ExtremeStorageGUI.RefreshCachedAllItems;
    }

    [JITWhenModsEnabled("ImproveGame")]
    private static bool IsImproveOpen() => ExtremeStorageGUI.VisibleAndExpanded && ExtremeStorageGUI.Storage!=null;

    [JITWhenModsEnabled("ImproveGame")]
    private static int ImproveSource()=>ExtremeStorageGUI.Storage?.ID??-1;
    [JITWhenModsEnabled("ImproveGame")]
    private static long ImportImproveServer(TerminalStorage storage,int id){
        var p=storage.Player;
        if(!Terraria.DataStructures.TileEntity.ByID.TryGetValue(id,out var entity) || entity is not ImproveGame.Content.Tiles.TEExtremeStorage source ||
           p.GetModPlayer<ImproveGame.Common.ModPlayers.ExtremeStoragePlayer>().UsingStorage!=id)return 0;
        // The server does not run the local inventory UI that maintains lastTileRange.
        int dx=(int)(p.Center.X/16)-source.Position.X,dy=(int)(p.Center.Y/16)-source.Position.Y;
        int reachX=Math.Max(Player.tileRangeX,p.lastTileRangeX),reachY=Math.Max(Player.tileRangeY,p.lastTileRangeY);
        if((dx < -reachX || dx > reachX+1 || dy < -reachY || dy > reachY+1) &&
           !HasLinkedCommunicator(p,source.Position))return 0;
        return MoveImproveChests(storage,source.FindAllNearbyChests());
    }
    [JITWhenModsEnabled("ImproveGame")]
    private static bool HasLinkedCommunicator(Player player,Terraria.DataStructures.Point16 position){
        foreach(var item in player.inventory)
            if(item.ModItem is ImproveGame.Content.Items.StorageCommunicator remote && remote.Location==position)return true;
        return false;
    }
    [JITWhenModsEnabled("ImproveGame")]
    private static long ImportImprove(TerminalStorage storage)
    {
        if (!IsImproveOpen()) return 0;
        long total=MoveImproveChests(storage,ExtremeStorageGUI.Storage.FindAllNearbyChests());
        ExtremeStorageGUI.RefreshCachedAllItems();
        return total;
    }

    [JITWhenModsEnabled("ImproveGame")]
    private static long MoveImproveChests(TerminalStorage storage,IEnumerable<int> nearby)
    {
        long total=0;
        foreach (int index in nearby)
        {
            var chest=Main.chest[index];
            if (Main.netMode==NetmodeID.Server && Chest.UsingChest(index)>=0)continue;
            if (chest==null || string.IsNullOrEmpty(chest.name) || StorageHelper.RedirectChestToGroup(chest)==ItemGroup.Setting ||
                Chest.IsLocked(chest.x,chest.y)) continue;
            for (int slot=0;slot<chest.item.Length;slot++)
            {
                var item=chest.item[slot];
                if (item==null || item.IsAir) continue;
                total+=item.stack;
                storage.StoreTransfer(item);
                chest.item[slot]=new Item();
                if(Main.netMode==NetmodeID.Server)NetMessage.SendData(MessageID.SyncChestItem,-1,-1,null,index,slot);
            }
        }
        return total;
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if (!Main.playerInventory || Main.LocalPlayer.dead ||
            !(TeamSupply.Active || Main.netMode==NetmodeID.SinglePlayer && Main.LocalPlayer.GetModPlayer<TerminalStorage>().Unlocked)) return;
        var import=magicOpen?.Invoke()==true ? magicImport : improveOpen?.Invoke()==true ? improveImport : null;
        if (import==null) return;
        int index=layers.FindIndex(l=>l.Name=="Vanilla: Mouse Text");
        layers.Insert(index<0?layers.Count:index,new LegacyGameInterfaceLayer("AEBeyondDimensions: Import Storage",()=>{
            string label=TerminalText.Get("全部转入 AE 终端");
            int width=(int)Math.Ceiling(TerminalFont.Measure(label).X)+24;
            var bounds=new Rectangle((int)(Main.screenWidth/Main.UIScale)-width-16,(int)(Main.screenHeight/Main.UIScale)-82,width,36);
            bool over=!PlayerInput.IgnoreMouseInterface && bounds.Contains(Main.MouseScreen.ToPoint());
            Utils.DrawInvBG(Main.spriteBatch,bounds,over?new Color(79,90,154):new Color(59,69,120));
            TerminalFont.Draw(Main.spriteBatch,label,new Vector2(bounds.Center.X,bounds.Center.Y),Color.White,1,.5f,.5f);
            if (over)
            {
                Main.LocalPlayer.mouseInterface=true;
                if (Main.mouseLeft && Main.mouseLeftRelease)
                {
                    Main.mouseLeftRelease=false;
                    if(TeamSupply.Active){
                        bool magic=import==magicImport;
                        int source=(magic?magicSource:improveSource)?.Invoke()??-1;
                        if(source>=0){
                            var request=TeamNetwork.Packet(TeamOp.ExternalImport,Main.LocalPlayer.team);
                            request.Write((byte)(magic?0:1));request.Write(source);request.Send();
                        }
                        return true;
                    }
                    long moved=import(Main.LocalPlayer.GetModPlayer<TerminalStorage>());
                    if (moved>0) Recipe.FindRecipes();
                    Main.NewText(string.Format(TerminalText.Get("已转入终端：{0} 个物品"),moved));
                }
            }
            return true;
        },InterfaceScaleType.UI));
    }
}
