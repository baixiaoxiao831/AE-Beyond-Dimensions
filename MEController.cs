using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using Terraria.DataStructures;
using Terraria.Enums;

namespace AEBeyondDimensions;

public sealed class MEControllerItem : ModItem
{
    public override string Texture => "AEBeyondDimensions/Assets/Controller/ControllerItem";
    private static int PlacementTile => ModContent.GetInstance<TerminalCardConfig>().ControllerShape switch
    {
        ControllerShape.Compact => ModContent.TileType<CompactControllerTile>(),
        ControllerShape.Tower => ModContent.TileType<TowerControllerTile>(),
        ControllerShape.Bridge => ModContent.TileType<BridgeControllerTile>(),
        ControllerShape.Halo => ModContent.TileType<HaloControllerTile>(),
        _ => ModContent.TileType<LargeControllerTile>()
    };
    internal byte PlacementColor;
    private bool choosing;
    public override void HoldItem(Player player)
    {
        Item.createTile=PlacementTile;
        if(Main.netMode!=NetmodeID.SinglePlayer || player!=Main.LocalPlayer)return;
        bool pressed=Terraria.UI.ItemSlot.ShiftInUse && Main.mouseRight && !player.mouseInterface;
        if(pressed && !choosing){
            PlacementColor=(byte)((PlacementColor+1)%6);
            Main.NewText(TerminalText.Get("放置颜色")+" "+TerminalText.Get(new[]{"无色","红队","绿队","蓝队","黄队","粉队"}[PlacementColor]));
        }
        choosing=pressed;
    }
    public override void ModifyTooltips(System.Collections.Generic.List<TooltipLine> tooltips){
        if(Main.netMode==NetmodeID.SinglePlayer)tooltips.Add(new TooltipLine(Mod,"PlacementTeam",
            TerminalText.Get("手持时 Shift＋右键切换放置颜色")+" · "+TerminalText.Get(new[]{"无色","红队","绿队","蓝队","黄队","粉队"}[PlacementColor])));
    }
    public override bool CanUseItem(Player player) { Item.createTile=PlacementTile; return true; }
    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(PlacementTile);
        Item.width = Item.height = 16;
    }
    public override void AddRecipes() => CreateRecipe()
        .AddIngredient(ItemID.Chest).AddIngredient(ItemID.Glass,10)
        .AddIngredient(ItemID.JungleSpores,6).AddIngredient(ItemID.FallenStar,3)
        .AddTile(TileID.Anvils).Register();
}

// Keep the original registered tile name and 1x1 framing for already placed controllers.
public sealed class MEControllerTile : ControllerTileBase { }
public sealed class CompactControllerTile : ControllerTileBase
{
    protected override int Side => 2;
    protected override string Art => "Compact";
}
public sealed class LargeControllerTile : ControllerTileBase
{
    protected override int Side => 14;
    protected override string Art => "Large";
}

public abstract class ControllerTileBase : ModTile
{
    protected virtual int Side => 1;
    protected virtual int Columns => Side;
    protected virtual int Rows => Side;
    protected virtual bool HasTopModule(int moduleX) => true;
    protected virtual string Art => "controller_powered";
    public override string Texture => "AEBeyondDimensions/Assets/Controller/"+Art;
    private Asset<Texture2D>? lights;
    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        var data=TileObjectData.newTile;
        data.CopyFrom(TileObjectData.Style1x1);
        data.Width=Columns;
        data.Height=Rows;
        data.Origin=new Point16((Columns-1)/2,Rows-1);
        data.CoordinateHeights=new int[Rows];
        System.Array.Fill(data.CoordinateHeights,16);
        if (Columns>1) data.AnchorBottom=new AnchorData(AnchorType.SolidTile|AnchorType.SolidWithTop|AnchorType.SolidSide,2,Columns/2-1);
        TileObjectData.addTile(Type);
        RegisterItemDrop(ModContent.ItemType<MEControllerItem>());
        AddMapEntry(new Color(99,112,134),CreateMapEntryName());
        DustType = DustID.Iron;
        if (!Main.dedServ) lights=Mod.Assets.Request<Texture2D>("Assets/Controller/"+(Columns==1?"controller_lights":Art+"Lights"));
    }

    public override void PlaceInWorld(int i,int j,Item item){
        if(Main.netMode==NetmodeID.SinglePlayer && item.ModItem is MEControllerItem controller &&
           controller.PlacementColor!=0 && ControllerBinding.TryTile(i,j,out var anchor))
            ControllerBinding.OpenFor(anchor,controller.PlacementColor);
    }
    public override bool RightClick(int i,int j)
    {
        if(Main.netMode==NetmodeID.MultiplayerClient && !Main.LocalPlayer.dead){TeamClient.Request(i,j);return true;}
        if (Main.netMode!=NetmodeID.SinglePlayer || Main.LocalPlayer.dead) return false;
        if(Main.LocalPlayer.HeldItem.ModItem is MEControllerItem && Terraria.UI.ItemSlot.ShiftInUse)return true;
        var storage=Main.LocalPlayer.GetModPlayer<TerminalStorage>();
        if (!storage.Unlocked)
        {
            storage.Unlock();
            Main.NewText(TerminalText.Get("ME 终端已解锁。"));
        }
        if(ControllerBinding.TryTile(i,j,out var anchor))SingleTerminal.Select(Main.LocalPlayer,anchor);
        ModContent.GetInstance<TerminalSystem>().OpenController();
        return true;
    }

    public override void KillMultiTile(int i,int j,int frameX,int frameY)
    {
        ModContent.GetInstance<ControllerBinding>().Kill(i,j);
    }

    public override void MouseOver(int i,int j)
    {
        var player=Main.LocalPlayer;
        player.noThrow=2;
        player.cursorItemIconEnabled=true;
        player.cursorItemIconID=ModContent.ItemType<MEControllerItem>();
    }

    public override void PostDraw(int i,int j,SpriteBatch spriteBatch)
    {
        if (lights==null || !TileDrawing.IsVisible(Main.tile[i,j])) return;
        // AE2 advances one frame every 25 Minecraft ticks (75 Terraria updates).
        const int period=75;
        var sheet=lights.Value;
        const int count=12;
        ulong time=Main.GameUpdateCount;
        int frame=(int)(time/period%(ulong)count);
        float blend=(time%period)/(float)period;
        var position=new Vector2(i*16,j*16)-Main.screenPosition;
        if (!Main.drawToScreen) position+=new Vector2(Main.offScreenRange);
        var tile=Main.tile[i,j];
        int frameWidth=Columns*18;
        int frameHeight=Rows*18;
        void Draw(int n,Color tint)
        {
            var source=Columns==1 ? new Rectangle(0,n*16,16,16)
                : new Rectangle(n%4*frameWidth+tile.TileFrameX,n/4*frameHeight+tile.TileFrameY,16,16);
            spriteBatch.Draw(sheet,position,source,tint);
        }
        Color tint=Color.White;
        if(ControllerBinding.TryTile(i,j,out var anchor) && ControllerBinding.ColorAt(anchor) is byte color && color>0){
            tint=Main.teamColor[color];
            // A solid edge remains legible on dark portions of the animated light mask.
            var mark=new Rectangle((int)position.X,(int)position.Y,16,2);
            if(tile.TileFrameY==0 && HasTopModule(tile.TileFrameX/36))spriteBatch.Draw(Terraria.GameContent.TextureAssets.MagicPixel.Value,mark,tint);
        }
        Draw(frame,tint);
        Draw((frame+1)%count,tint*blend);
    }
}
