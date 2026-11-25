using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;
using Terraria.UI;

namespace AEBeyondDimensions;

internal static class TerminalScroll
{
    internal static int Height(Rectangle track,float unit)=>Math.Clamp((int)Math.Round(15*unit),1,Math.Max(1,track.Height));
    internal static void Draw(SpriteBatch batch,Rectangle track,float unit,float fraction,bool enabled)
    {
        if(track.Height<=0 || track.Width<=0)return;
        int thumbHeight=Height(track,unit);
        int thumbWidth=Math.Max(1,(int)Math.Round(7*unit));
        int railWidth=Math.Max(1,(int)Math.Round(5*unit));
        int edge=Math.Max(1,(int)Math.Round(unit));
        var rail=new Rectangle(track.Center.X-railWidth/2,track.Top,railWidth,track.Height);
        batch.Draw(TextureAssets.MagicPixel.Value,rail,new Color(242,242,242));
        rail.Inflate(-edge,-edge);
        if(rail.Width>0 && rail.Height>0)batch.Draw(TextureAssets.MagicPixel.Value,rail,new Color(154,159,180));
        int y=track.Top+(int)Math.Round((track.Height-thumbHeight)*Math.Clamp(fraction,0,1));
        var thumb=new Rectangle(track.Center.X-thumbWidth/2,y,thumbWidth,thumbHeight);
        var texture=ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Orders/"+(enabled?"small_scroller":"small_scroller_disabled")).Value;
        batch.Draw(texture,thumb,Color.White);
    }
}

internal sealed class TerminalBagScroll : UIScrollbar
{
    private float? grab;
    private Rectangle Track=>GetDimensions().ToRectangle();
    private float Unit=>Math.Min(2f,Track.Width/7f);
    protected override void DrawSelf(SpriteBatch batch)
    {
        var track=Track;
        float max=Math.Max(0,MaxViewSize-ViewSize);
        if(!Main.mouseLeft)grab=null;
        if(grab.HasValue && max>0)
            ViewPosition=max*Math.Clamp((Main.MouseScreen.Y-track.Y-grab.Value)/Math.Max(1,track.Height-TerminalScroll.Height(track,Unit)),0,1);
        TerminalScroll.Draw(batch,track,Unit,max>0?ViewPosition/max:0,max>0);
    }
    public override void LeftMouseDown(UIMouseEvent evt)
    {
        if(!CanScroll)return;
        var track=Track;
        int height=TerminalScroll.Height(track,Unit);
        float top=track.Top+(track.Height-height)*ViewPosition/(MaxViewSize-ViewSize);
        float delta=evt.MousePosition.Y-top;
        grab=delta>=0 && delta<height?delta:height/2f;
    }
    public override void LeftMouseUp(UIMouseEvent evt){grab=null;base.LeftMouseUp(evt);}
}
