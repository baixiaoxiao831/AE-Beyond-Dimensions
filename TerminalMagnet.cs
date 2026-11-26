using System;
using System.ComponentModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

public sealed class TerminalCardConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ServerSide;

    [CustomModConfigItem(typeof(ControllerShapeElement))]
    [DefaultValue(ControllerShape.Large)]
    public ControllerShape ControllerShape { get; set; } = ControllerShape.Large;

    // Read the previous setting on upgrade. New files only write the selected shape.
    [Newtonsoft.Json.JsonProperty("LargeController")]
    private bool? LegacyLargeController { set { if (value.HasValue) legacyLarge = value; } }
    private bool? legacyLarge;

    [System.Runtime.Serialization.OnDeserialized]
    private void MigrateShape(System.Runtime.Serialization.StreamingContext context)
    {
        if (legacyLarge.HasValue)
            ControllerShape = legacyLarge.Value ? ControllerShape.Large : ControllerShape.Compact;
        legacyLarge = null;
    }

    [Range(0, 256)]
    [DefaultValue(16)]
    public int MagnetRangeTiles { get; set; } = 16;

    [DefaultValue(true)]
    public bool RequireUpgradeCard { get; set; } = true;
}

public sealed class MagnetCard : ModItem
{
    public override string Texture => "AEBeyondDimensions/Assets/Magnet/Card";
    public override void SetDefaults()
    {
        Item.width = Item.height = 16;
        Item.maxStack = 1;
    }
}

public sealed class ReforgeCard : ModItem
{
    public override string Texture => "AEBeyondDimensions/Assets/Magnet/ReforgeCardItem";
    public override void SetDefaults() { Item.width = Item.height = 16; Item.maxStack = 1; }
}

public sealed class ReforgeUpgradeCard : ModItem
{
    public override string Texture => "AEBeyondDimensions/Assets/Magnet/ReforgeUpgradeCard";
    public override void SetDefaults() { Item.width = Item.height = 16; Item.maxStack = 1; }
}

public sealed class ShimmerCard : ModItem
{
    public override string Texture => "AEBeyondDimensions/Assets/Magnet/ShimmerCard";
    public override void SetDefaults() { Item.width = Item.height = 16; Item.maxStack = 1; }
}

public sealed class TerminalMagnet : ModPlayer
{
    private Item[] cards = Array.Empty<Item>();
    private int dockPage;
    internal bool ControlAvailable => ModLoader.HasMod("ImproveGame") &&
        (!ModContent.GetInstance<TerminalCardConfig>().RequireUpgradeCard || Array.Exists(cards,c=>c.ModItem is MasterControlCard));
    internal bool MagnetEnabled { get; set; }
    internal enum PickupMode { Overflow, Direct, Matching }
    internal PickupMode Route { get; set; }
    private bool Installed => Array.Exists(cards,c=>c.ModItem is MagnetCard);
    internal bool Available => !ModContent.GetInstance<TerminalCardConfig>().RequireUpgradeCard || Installed;
    internal bool ShimmerAvailable => !ModContent.GetInstance<TerminalCardConfig>().RequireUpgradeCard || Array.Exists(cards,c=>c.ModItem is ShimmerCard);
    internal bool SelectPrefix => !ModContent.GetInstance<TerminalCardConfig>().RequireUpgradeCard || Array.Exists(cards,c=>c.ModItem is ReforgeUpgradeCard);
    internal bool ReforgeAvailable => SelectPrefix || Array.Exists(cards,c=>c.ModItem is ReforgeCard);
    internal bool Attract => MagnetEnabled && Available;

    public override void Initialize()
    {
        cards = new Item[] { new(), new(), new(), new() };
        dockPage=0;
        MagnetEnabled = false;
        Route = PickupMode.Overflow;
    }

    public override void SaveData(TagCompound tag)
    {
        tag["magnetEnabled"] = MagnetEnabled;
        tag["pickupRoute"] = (int)Route;
        for (int i=0; i<cards.Length; i++)
            if (!cards[i].IsAir) tag["upgrade" + i] = ItemIO.Save(cards[i]);
    }

    public override void LoadData(TagCompound tag)
    {
        int previous = tag.GetInt("magnetMode");
        MagnetEnabled = tag.ContainsKey("magnetEnabled") ? tag.GetBool("magnetEnabled") : previous is 1 or 2;
        bool wasDirect = tag.ContainsKey("directPickup") ? tag.GetBool("directPickup") : previous is 2 or 3;
        Route = tag.TryGet("pickupRoute",out int route) && Enum.IsDefined(typeof(PickupMode),route)
            ? (PickupMode)route : wasDirect ? PickupMode.Direct : PickupMode.Overflow;
        for (int i=0; i<cards.Length; i++)
            if (tag.TryGet("upgrade" + i,out TagCompound item)) cards[i] = ItemIO.Load(item);
    }

    internal void DrawDock(SpriteBatch batch, Vector2 position, float scale)
    {
        var assets = Mod.Assets;
        var dock = assets.Request<Texture2D>("Assets/Magnet/Dock").Value;
        // Keep both end caps at their original size and repeat the 18 px slot row.
        void Strip(int sourceY,int height,int destY) => batch.Draw(dock,position+new Vector2(0,destY*scale),
            new Rectangle(0,sourceY,dock.Width,height),Color.White,0,Vector2.Zero,scale,SpriteEffects.None,0);
        const int pageSize=4;
        int visible=Math.Min(pageSize,cards.Length);
        if (cards.Length>pageSize) {
            int last=(cards.Length-1)/pageSize;
            var area=new Rectangle((int)position.X,(int)position.Y,(int)((dock.Width+7)*scale),(int)((12+18*visible)*scale));
            if (area.Contains(Main.MouseScreen.ToPoint()) && !Terraria.GameInput.PlayerInput.IgnoreMouseInterface) {
                Terraria.GameInput.PlayerInput.LockVanillaMouseScroll("AE upgrade slots");
                dockPage=Math.Clamp(dockPage-Math.Sign(Terraria.GameInput.PlayerInput.ScrollWheelDeltaForUI),0,last);
                Player.mouseInterface=true;
            }
            var track=new Rectangle((int)(position.X+dock.Width*scale),area.Top,Math.Max(7,(int)(7*scale)),area.Height);
            TerminalScroll.Draw(batch,track,scale,(float)dockPage/last,true);
        }
        Strip(0,24,0);
        for (int i=1;i<visible-1;i++) Strip(6,18,6+18*i);
        Strip(24,dock.Height-24,6+18*(visible-1));
        for (int row=0;row<visible;row++)
        {
            int i=dockPage*pageSize+row;
            if(i>=cards.Length) break;
            int size = (int)Math.Round(16*scale);
            var slot = new Rectangle((int)Math.Round(position.X+5*scale),(int)Math.Round(position.Y+(6+row*18)*scale),size,size);
            var card = cards[i];
            if (card.IsAir)
                batch.Draw(assets.Request<Texture2D>("Assets/Magnet/Ghost").Value,slot,Color.White);
            else
                Terraria.UI.ItemSlot.DrawItemIcon(card, Terraria.UI.ItemSlot.Context.ChestItem,batch,slot.Center.ToVector2(),size / 32f,32,Color.White);
            if (Terraria.GameInput.PlayerInput.IgnoreMouseInterface || !slot.Contains(Main.MouseScreen.ToPoint())) continue;
            Player.mouseInterface = true;
            if (card.IsAir && Main.mouseItem.IsAir) TerminalTooltip.Show(TerminalText.Get("升级卡槽"),TerminalText.Get("放入升级卡"));
            else TerminalTooltip.ShowItem(card);
            if (!Main.mouseLeft || !Main.mouseLeftRelease) continue;
            Main.mouseLeftRelease = false;
            if (Main.mouseItem.IsAir && !card.IsAir)
            {
                Main.mouseItem = card;
                cards[i] = new Item();
                TerminalCursor.RememberSlot(cards,i);
            }
            else if (card.IsAir && Main.mouseItem.ModItem is (MagnetCard or ReforgeCard or ReforgeUpgradeCard or ShimmerCard or MasterControlCard) && !Array.Exists(cards,c=>c.type==Main.mouseItem.type))
            {
                cards[i] = Main.mouseItem;
                Main.mouseItem = new Item();
                TerminalCursor.Clear();
            }
            else continue;
            TerminalSounds.PlayClick();
        }
    }
}

public sealed class MagnetPickup : GlobalItem
{
    private static bool Local(Player player) => player == Main.LocalPlayer && (Main.netMode == NetmodeID.SinglePlayer && SingleTerminal.Selected && player.GetModPlayer<TerminalStorage>().Unlocked || TeamSupply.Active);
    private static bool Route(Item item, Player player)
    {
        if (!Local(player) || item.IsAir || ItemID.Sets.IsAPickup[item.type]) return false;
        var magnet = player.GetModPlayer<TerminalMagnet>();
        return magnet.Available && (magnet.Route != TerminalMagnet.PickupMode.Matching ||
            player.GetModPlayer<TerminalStorage>().MatchesQuickStack(item));
    }

    public override void GrabRange(Item item, Player player, ref int grabRange)
    {
        if (Local(player) && player.GetModPlayer<TerminalMagnet>().Attract)
            grabRange = Math.Max(grabRange, Math.Clamp(ModContent.GetInstance<TerminalCardConfig>().MagnetRangeTiles, 0, 256) * 16);
    }

    public override bool ItemSpace(Item item, Player player) => Main.netMode==NetmodeID.SinglePlayer ? Route(item,player) : TeamSupply.CanAttract(item,player);
    public override bool CanPickup(Item item,Player player) => !TeamSupply.InterceptPickup(item,player);
    public override void OnConsumeAmmo(Item weapon,Item ammo,Player player)=>TeamSupply.Consumed(player,ammo);

    public override bool OnPickup(Item item, Player player) => !TryCollect(item,player);

    internal static bool TryCollect(Item item, Player player)
    {
        if (Main.netMode!=NetmodeID.SinglePlayer || !Route(item,player)) return false;
        var remainder = item.Clone();
        if (player.GetModPlayer<TerminalMagnet>().Route == TerminalMagnet.PickupMode.Overflow)
            remainder = player.GetItem(player.whoAmI,remainder,GetItemSettings.InventoryEntityToPlayerInventorySettings);
        if (!remainder.IsAir)
            player.GetModPlayer<TerminalStorage>().StoreLooseItem(remainder);
        return true;
    }
}
