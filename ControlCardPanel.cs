using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;
using ImproveGame.UI.MasterControl;
using ImproveGame.UIFramework.BaseViews;

namespace AEBeyondDimensions;

internal sealed class ControlCardPanel
{
    private int firstRow;
    internal void Reset() => firstRow=0;

    [JITWhenModsEnabled("ImproveGame")]
    internal bool Draw(SpriteBatch batch,Rectangle viewport)
    {
        var functions=new List<(MasterControlFunction,bool)>();
        var disabled=new List<(MasterControlFunction,bool)>();
        foreach(var function in MasterControlManager.Instance.OriginalMCFuncions) {
            bool available=function.IsAvailable;
            (available?functions:disabled).Add((function,available));
        }
        functions.AddRange(disabled);
        const int columns=2;
        int width=Math.Min(440,viewport.Width-12);
        int rows=Math.Max(1,Math.Min((functions.Count+1)/2,(viewport.Height-66)/40));
        int height=50+rows*40;
        var panel=new Rectangle(viewport.Center.X-width/2,viewport.Center.Y-height/2,width,height);
        var mouse=Main.MouseScreen.ToPoint();
        bool Hover(Rectangle box)=>!PlayerInput.IgnoreMouseInterface && box.Contains(mouse);
        bool Click(Rectangle box) {
            if(!Hover(box) || !Main.mouseLeft || !Main.mouseLeftRelease) return false;
            Main.mouseLeftRelease=false;
            TerminalSounds.PlayClick();
            return true;
        }
        batch.Draw(TextureAssets.MagicPixel.Value,viewport,Color.Black*.53f);
        TerminalSkin.Panel(batch,panel);
        TerminalSkin.Label(batch,TerminalText.Get("总控面板"),new Vector2(panel.X+14,panel.Y+11));
        var back=new Rectangle(panel.Right-44,panel.Y+2,36,36);
        var backArt="Assets/SearchSettings/"+(Hover(back)?"BackHover":"Back");
        batch.Draw(ModContent.GetInstance<ProbeMod>().Assets.Request<Texture2D>(backArt).Value,back,Color.White);
        if(Click(back)) return false;
        int last=Math.Max(0,(functions.Count+1)/2-rows);
        if(Hover(panel)) firstRow-=Math.Sign(PlayerInput.ScrollWheelDeltaForUI);
        firstRow=Math.Clamp(firstRow,0,last);
        if(last>0)TerminalScroll.Draw(batch,new Rectangle(panel.Right-17,panel.Y+39,12,40*rows-3),1.4f,(float)firstRow/last,true);
        int cell=(width-20-(last>0?12:0))/columns;
        for(int slot=0;slot<rows*columns;slot++) {
            int index=firstRow*columns+slot;
            if(index>=functions.Count) break;
            var (function,available)=functions[index];
            var box=new Rectangle(panel.X+10+(slot%columns)*cell,panel.Y+39+(slot/columns)*40,cell-3,37);
            string name=function.GetDisplayName();
            var art=available?(Hover(box)?"ButtonHighlighted":"Button"):"ButtonDisabled";
            var skin=ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Orders/"+art).Value;
            TerminalSkin.Button(batch,skin,box);
            if(function.Icon is { } icon) {
                var source=function.IconRectangle ?? icon.Bounds;
                float scale=26f/Math.Max(1,Math.Max(source.Width,source.Height));
                batch.Draw(icon,new Vector2(box.X+19,box.Center.Y),source,available?Color.White:Color.Gray,
                    0,source.Size()/2,scale,SpriteEffects.None,0);
            }
            float textScale=Math.Min(.85f,(cell-45)/Math.Max(1f,TerminalFont.Measure(name).X));
            TerminalFont.Draw(batch,name,new Vector2(box.X+37,box.Center.Y),available?TerminalSkin.Ink:Color.DimGray,textScale,0,.5f);
            if(!Hover(box)) continue;
            TerminalTooltip.Show(name,available?function.GetDescription():TerminalText.Get("当前不可用")+" · "+function.GetDescription());
            if(available && Click(box)) {
                ModContent.GetInstance<TerminalSystem>().LeaveForControlFunction();
                function.MouseDown(new TimerView());
                ControlVisit.Track(function.Name);
                return false;
            }
        }
        return true;
    }
}
