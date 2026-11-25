using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace AEBeyondDimensions;

internal static class TerminalSkin
{
    internal static void BeginPixelBatch(SpriteBatch batch)
    {
        batch.End();
        batch.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp,
            DepthStencilState.None,RasterizerState.CullNone,null,Main.UIScaleMatrix);
    }

    internal static readonly Color Ink = new(65,63,84);
    static readonly Color Light = new(242,242,242);
    static void Fill(SpriteBatch batch,int x,int y,int w,int h,Color color)
    {
        if (w>0 && h>0) batch.Draw(TextureAssets.MagicPixel.Value,new Rectangle(x,y,w,h),color);
    }

    internal static void Button(SpriteBatch batch,Texture2D skin,Rectangle box)
    {
        int edge=Math.Max(1,(int)Math.Round(2f*box.Height/skin.Height));
        batch.Draw(skin,new Rectangle(box.X,box.Y,edge,box.Height),new Rectangle(0,0,2,skin.Height),Color.White);
        batch.Draw(skin,new Rectangle(box.X+edge,box.Y,box.Width-2*edge,box.Height),new Rectangle(2,0,1,skin.Height),Color.White);
        batch.Draw(skin,new Rectangle(box.Right-edge,box.Y,edge,box.Height),new Rectangle(skin.Width-2,0,2,skin.Height),Color.White);
    }

    internal static void Panel(SpriteBatch batch, Rectangle r)
    {
        Fill(batch,r.X,r.Y,r.Width,r.Height,Ink);
        Fill(batch,r.X+2,r.Y+2,r.Width-4,r.Height-4,Light);
        Fill(batch,r.X+4,r.Y+4,r.Width-6,r.Height-6,new Color(135,143,165));
        Fill(batch,r.X+4,r.Y+4,r.Width-10,r.Height-10,new Color(203,204,212));
    }

    internal static void Inset(SpriteBatch batch,Rectangle r,Color face)
    {
        Fill(batch,r.X,r.Y,r.Width,r.Height,Light);
        Fill(batch,r.X,r.Y,r.Width-2,r.Height-2,new Color(102,107,129));
        Fill(batch,r.X+2,r.Y+2,r.Width-4,r.Height-4,face);
    }

    internal static void Slot(SpriteBatch batch,Vector2 p,int size,bool favorite=false) =>
        Inset(batch,new Rectangle((int)p.X,(int)p.Y,size,size),favorite ? new Color(226,98,98) : new Color(173,176,196));

    internal static void FavoriteLock(SpriteBatch batch,Vector2 p,int size)
    {
        var art=Terraria.ModLoader.ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/FavoriteLock").Value;
        int pixel=Math.Max(1,(int)Math.Round(size/18f));
        batch.Draw(art,new Rectangle((int)p.X+size-6*pixel-1,(int)p.Y,6*pixel,7*pixel),Color.White);
    }

    internal static void Label(SpriteBatch batch,string text,Vector2 pos) =>
        TerminalFont.Draw(batch,text,pos,Ink,.85f);

    internal static void Item(SpriteBatch batch,Item item,int context,Vector2 p,Color tint,Color? background=null)
    {
        int size=(int)Math.Round(Main.inventoryScale*52);
        if (!item.favorited && background is Color face) Inset(batch,new Rectangle((int)p.X,(int)p.Y,size,size),face);
        else Slot(batch,p,size,item.favorited);
        if (item.IsAir) return;
        ItemSlot.DrawItemIcon(item,context,batch,p+new Vector2(size/2f),Main.inventoryScale,32,tint);
        if (item.favorited) FavoriteLock(batch,p,size);
        if (item.stack<=1) return;
        var text=StoragePane.FormatStockCount(item.stack);
        var font=FontAssets.ItemStack.Value;
        float scale=Math.Min(.75f,(size-4)/Math.Max(1,TerminalFont.Measure(text).X));
        TerminalFont.Draw(batch,text,p+new Vector2(size-3,size-2),Color.White,scale,1,1);
    }
}
