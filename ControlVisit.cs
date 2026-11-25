using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using ImproveGame.UIFramework.BaseViews;
using ImproveGame.UIFramework.SUIElements;
using ImproveGame.UI.ModernConfig;
using ImproveGame.UIFramework.Graphics2D;
using ImproveGame.UI.PlayerStats;

namespace AEBeyondDimensions;

internal static class ControlVisit
{
    [JITWhenModsEnabled("ImproveGame")]
    internal static bool IsOpen(UIElement root) => root switch {
        ModernConfigUI config => config.Enabled,
        PlayerStatsGUI stats => stats.HasChild(stats.Window) && Main.playerInventory,
        BaseBody body => body.Enabled,
        _ => false
    };

    [JITWhenModsEnabled("ImproveGame")]
    internal static void Track(string name)
    {
        UIElement? root=name switch {
            "Spectating"=>ImproveGame.UI.DeathSpectating.SpectatingGUI.Instance,
            "SuperVault"=>ImproveGame.UI.BigBagGUI.Instance,
            "PlayerStats"=>PlayerStatsGUI.Instance,
            "BannerChest" or "PotionBag"=>ImproveGame.UI.ItemContainer.ItemContainerGUI.Instace,
            "WeatherControl"=>ImproveGame.UI.WeatherControl.WeatherGUI.Instance,
            "ItemSearcher"=>ImproveGame.UI.ItemSearcher.ItemSearcherGUI.Instance,
            "WorldFeature"=>ImproveGame.UI.WorldFeature.WorldFeatureGUI.Instance,
            "BagOpener"=>ImproveGame.UI.OpenBag.OpenBagGUI.Instance,
            "QuickShimmer"=>ImproveGame.UI.QuickShimmer.QuickShimmerGUI.Instance,
            "AmmoChain"=>ImproveGame.UI.AmmoChainPanel.AmmoChainUI.Instance,
            "ModConfig"=>ModernConfigUI.Instance,
            _=>null
        };
        if(root is ModernConfigUI config) config.OpenFromMasterControl=false;
        ModContent.GetInstance<TerminalSystem>().ControlRoot=root;
    }
}

[ExtendsFromMod("ImproveGame")]
[JITWhenModsEnabled("ImproveGame")]
internal sealed class ControlSkin : ModSystem
{
    private static readonly FieldInfo ButtonTexture=typeof(SUIButton).GetField("_texture",BindingFlags.Instance|BindingFlags.NonPublic)!;
    private static readonly Func<SUIScrollBar,float> ScrollRange=typeof(SUIScrollBar)
        .GetProperty("BarMaxTop",BindingFlags.Instance|BindingFlags.NonPublic)!.GetMethod!.CreateDelegate<Func<SUIScrollBar,float>>();

    public override void Load()
    {
        if(Main.dedServ)return;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        MonoModHooks.Add(typeof(BaseBody).GetMethod("Draw",flags)!,DrawBody);
        MonoModHooks.Add(typeof(SDFRectangle).GetMethod(nameof(SDFRectangle.HasBorder))!,DrawBorder);
        MonoModHooks.Add(typeof(SDFRectangle).GetMethod(nameof(SDFRectangle.NoBorder))!,DrawFill);
        MonoModHooks.Add(typeof(View).GetMethod("Draw",flags)!,DrawSurface);
        MonoModHooks.Add(typeof(SUICross).GetMethod("DrawSelf",flags)!,DrawBack);
        MonoModHooks.Add(typeof(SUITriangleIcon).GetMethod("DrawSelf",flags)!,DrawExpand);
        MonoModHooks.Add(typeof(SUIScrollBar).GetMethod("Draw",flags)!,DrawScroll);
        MonoModHooks.Add(typeof(SUIScrollbar2).GetMethod("DrawScrollbar",flags)!,DrawScrollThumb);
        MonoModHooks.Add(typeof(Terraria.GameContent.UI.Elements.UIText).GetMethod("DrawSelf",flags)!,DrawLootMessage);
        MonoModHooks.Add(typeof(SUIButton).GetMethod("DrawSelf",flags)!,DrawLootButton);
        MonoModHooks.Add(typeof(SUIText).GetMethod("DrawSelf",flags)!,DrawText);
    }

    private static bool drawingControl;

    private static void DrawBody(Action<BaseBody,SpriteBatch> original,BaseBody body,SpriteBatch batch)
    {
        bool previous=drawingControl;
        drawingControl=previous || body==ModContent.GetInstance<TerminalSystem>().ControlRoot;
        try { original(body,batch); }
        finally { drawingControl=previous; }
    }

    private static bool PanelSized(Vector2 size) => drawingControl && size.X>=16 && size.Y>=12;
    private static void DrawFill(Action<Vector2,Vector2,Vector4,Color,Matrix> draw,Vector2 at,Vector2 size,Vector4 radius,Color face,Matrix matrix)
    {
        draw(at,size,PanelSized(size)?Vector4.Zero:radius,face,matrix);
    }

    private static void DrawBorder(Action<Vector2,Vector2,Vector4,Color,float,Color,Matrix> draw,
        Vector2 at,Vector2 size,Vector4 radius,Color face,float border,Color edge,Matrix matrix)
    {
        if(!PanelSized(size) || border<=0 || edge.A==0){
            draw(at,size,radius,face,border,edge,matrix);
            return;
        }
        float pixel=Math.Min(2,Math.Max(1,border));
        float alpha=edge.A/255f;
        draw(at,size,Vector4.Zero,face,pixel,TerminalSkin.Ink*alpha,matrix);
        Vector2 start=at+new Vector2(pixel);
        Vector2 inside=size-new Vector2(2*pixel);
        Color light=new Color(242,242,242)*alpha, shade=new Color(135,143,165)*alpha;
        SDFRectangle.NoBorder(start,new Vector2(inside.X,pixel),Vector4.Zero,light,matrix);
        SDFRectangle.NoBorder(start,new Vector2(pixel,inside.Y),Vector4.Zero,light,matrix);
        SDFRectangle.NoBorder(start+new Vector2(0,inside.Y-pixel),new Vector2(inside.X,pixel),Vector4.Zero,shade,matrix);
        SDFRectangle.NoBorder(start+new Vector2(inside.X-pixel,0),new Vector2(pixel,inside.Y),Vector4.Zero,shade,matrix);
    }

    private static bool Active(UIElement view)
    {
        var root=ModContent.GetInstance<TerminalSystem>().ControlRoot;
        if(root==null)return false;
        for(UIElement? node=view;node!=null;node=node.Parent)
            if(node==root)return true;
        return false;
    }

    private static void DrawSurface(Action<View,SpriteBatch> original, View view,SpriteBatch batch)
    {
        if(!Active(view) || (view.BgColor.A==0 && view.BorderColor.A==0)) { original(view,batch); return; }
        var rect=view.GetDimensions().ToRectangle();
        if(rect.Width<5 || rect.Height<5) {original(view,batch); return;}
        var (rounded,face,edge)=(view.Rounded,view.BgColor,view.BorderColor);
        try {
            // Keep backgrounds in ImproveGame's own draw pass, before its shader-drawn controls.
            view.Rounded=Vector4.Zero;
            if(face.A!=0)view.BgColor=view is SUIPanel?new Color(203,204,212):view.IsMouseHovering?new Color(188,199,218):new Color(147,153,174);
            if(edge.A!=0)view.BorderColor=TerminalSkin.Ink;
            original(view,batch);
        } finally { (view.Rounded,view.BgColor,view.BorderColor)=(rounded,face,edge); }
    }

    private static Rectangle IconBounds(UIElement element)
    {
        var r=element.GetDimensions().ToRectangle();
        int side=Math.Min(28,Math.Min(r.Width,r.Height));
        return new Rectangle(r.Center.X-side/2,r.Center.Y-side/2,side,side);
    }

    private static void DrawBack(Action<SUICross,SpriteBatch> original,SUICross button,SpriteBatch batch)
    {
        if(!Active(button)){original(button,batch);return;}
        var asset=button.IsMouseHovering?"BackHover":"Back";
        batch.Draw(ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/SearchSettings/"+asset).Value,IconBounds(button),Color.White);
    }

    private static void DrawExpand(Action<SUITriangleIcon,SpriteBatch> original,SUITriangleIcon button,SpriteBatch batch)
    {
        if(!Active(button)){original(button,batch);return;}
        var box=IconBounds(button);
        var art=ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Orders/"+(button.IsMouseHovering?"ButtonHighlighted":"Button")).Value;
        TerminalSkin.Button(batch,art,box);
        var points=button.trianglePercentCoord;
        int tip=0;
        for(int i=1;i<3;i++)
            if(Vector2.DistanceSquared(points[(i+1)%3],points[(i+2)%3])>
               Vector2.DistanceSquared(points[(tip+1)%3],points[(tip+2)%3]))tip=i;
        var direction=points[tip]-(points[(tip+1)%3]+points[(tip+2)%3])*.5f;
        bool horizontal=Math.Abs(direction.X)>Math.Abs(direction.Y);
        bool forward=(horizontal?direction.X:direction.Y)>0;
        int rows=Math.Max(1,box.Height/5);
        for(int i=0;i<rows;i++){
            int half=rows-1-i;
            int offset=-rows/2+(forward?i:rows-1-i);
            var strip=horizontal?new Rectangle(box.Center.X+offset,box.Center.Y-half,1,half*2+1):
                new Rectangle(box.Center.X-half,box.Center.Y+offset,half*2+1,1);
            batch.Draw(Terraria.GameContent.TextureAssets.MagicPixel.Value,strip,Color.White);
        }
    }

    private static bool LootPanel(UIElement element) =>
        !Main.gameMenu && ModContent.GetInstance<TerminalSystem>()?.ControlRoot is ImproveGame.UI.OpenBag.OpenBagGUI && Active(element);

    private static void LootLabel(SpriteBatch batch,string text,Rectangle area,float scale,float align=.5f)
    {
        if(area.Width<=0 || area.Height<=0 || string.IsNullOrEmpty(text))return;
        var size=TerminalFont.Measure(text,scale);
        scale*=Math.Min(1,Math.Min(area.Width/Math.Max(1,size.X),area.Height/Math.Max(1,size.Y)));
        TerminalFont.Draw(batch,text,new Vector2(area.X+align*area.Width,area.Center.Y),TerminalSkin.Ink,scale,align,.5f);
    }

    private static void DrawLootMessage(Action<Terraria.GameContent.UI.Elements.UIText,SpriteBatch> original,
        Terraria.GameContent.UI.Elements.UIText label,SpriteBatch batch)
    {
        if(!LootPanel(label) || label.Text.Contains('[')){original(label,batch);return;}
        LootLabel(batch,label.Text,label.GetInnerDimensions().ToRectangle(),1);
    }

    private static void DrawLootButton(Action<SUIButton,SpriteBatch> original,SUIButton button,SpriteBatch batch)
    {
        if(!LootPanel(button) || button.Text.Contains('[')){original(button,batch);return;}
        var box=button.GetDimensions().ToRectangle();
        var art=ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Orders/"+(button.IsMouseHovering?"ButtonHighlighted":"Button")).Value;
        TerminalSkin.Button(batch,art,box);
        var area=box;
        area.Inflate(-6,-3);
        if(button.IconMode && ButtonTexture.GetValue(button) is Texture2D icon){
            int side=Math.Min(area.Height,Math.Min(24,area.Width/3));
            float factor=(float)side/Math.Max(icon.Width,icon.Height);
            batch.Draw(icon,new Vector2(area.X+side*.5f,area.Center.Y),null,Color.White,0,icon.Bounds.Size()*.5f,factor,SpriteEffects.None,0);
            area.X+=side+4;area.Width-=side+4;
        }
        LootLabel(batch,button.Text,area,1);
    }

    private static void DrawText(Action<SUIText,SpriteBatch> original,SUIText label,SpriteBatch batch)
    {
        if(LootPanel(label) && label.IsLarge){
            LootLabel(batch,label.OriginalString,label.GetInnerDimensions().ToRectangle(),1.12f,label.TextAlign.X);
            return;
        }
        if(!Active(label) || label.TextColor!=Color.White){original(label,batch);return;}
        var border=label.TextBorder;
        try { label.TextColor=TerminalSkin.Ink; label.TextBorder=0; original(label,batch); }
        finally { label.TextColor=Color.White; label.TextBorder=border; }
    }

    private static void DrawScrollThumb(Action<SUIScrollbar2> original,SUIScrollbar2 bar)
    {
        if(!Active(bar)){original(bar);return;}
        var track=bar.GetInnerDimensions().ToRectangle();
        var size=bar.GetBarSize();
        if(track.Width<=0 || track.Height<=0 || size.X<=0 || size.Y<=0)return;
        var pos=track.Location.ToVector2()+bar.BarPosition;
        TerminalSkin.Inset(Main.spriteBatch,track,new Color(154,159,180));
        DrawThumb(Main.spriteBatch,new Rectangle((int)pos.X,(int)pos.Y,(int)size.X,(int)size.Y),track.Width>track.Height);
    }

    private static void DrawScroll(Action<SUIScrollBar,SpriteBatch> original,SUIScrollBar bar,SpriteBatch batch)
    {
        if(!Active(bar)) {original(bar,batch);return;}
        if(!bar.Visible || bar.InnerFilled && bar.HideIfFilled)return;
        var r=bar.GetInnerDimensions().ToRectangle();
        if(r.Width<=0 || r.Height<=0)return;
        // Keep thumb bounds equal to the original pointer hit area.
        TerminalSkin.Inset(batch,r,new Color(154,159,180));
        if(bar.InnerFilled && bar.HideInnerIfFilled)return;
        int height=Math.Clamp((int)Math.Round(r.Height*bar.BarHeightPercent),1,r.Height);
        float range=ScrollRange(bar);
        int offset=range>0?(int)Math.Round((r.Height-height)*bar.BarTop/range):0;
        DrawThumb(batch,new Rectangle(r.X,r.Y+offset,r.Width,height),false);
    }

    private static void DrawThumb(SpriteBatch batch,Rectangle box,bool horizontal)
    {
        var art=ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Orders/small_scroller").Value;
        int length=horizontal?box.Width:box.Height, thickness=horizontal?box.Height:box.Width;
        int cap=Math.Min(length/2,Math.Max(1,(int)Math.Round(2f*thickness/art.Width)));
        void Band(int offset,int span,int sourceY,int sourceHeight){
            if(span<=0 || thickness<=0)return;
            var source=new Rectangle(0,sourceY,art.Width,sourceHeight);
            if(horizontal)
                batch.Draw(art,new Rectangle(box.Right-offset,box.Y,thickness,span),source,Color.White,MathHelper.PiOver2,Vector2.Zero,SpriteEffects.None,0);
            else batch.Draw(art,new Rectangle(box.X,box.Y+offset,thickness,span),source,Color.White);
        }
        Band(0,cap,0,2);
        Band(cap,length-cap*2,art.Height/2,1);
        Band(length-cap,cap,art.Height-2,2);
    }
}
