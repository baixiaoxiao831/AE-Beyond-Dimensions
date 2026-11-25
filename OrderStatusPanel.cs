using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AEBeyondDimensions;

internal sealed class OrderStatusPanel
{
    private string selected="";
    private int orderScroll,materialScroll;
    private static TerminalOrders Orders=>Main.LocalPlayer.GetModPlayer<TerminalOrders>();
    private static Texture2D Art(string name)=>ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Orders/"+name).Value;
    private static bool Hover(Rectangle r)=>!PlayerInput.IgnoreMouseInterface && r.Contains(Main.MouseScreen.ToPoint());
    private static bool Click(Rectangle r)
    {
        if (!Hover(r) || !Main.mouseLeft || !Main.mouseLeftRelease) return false;
        Main.mouseLeftRelease=false;
        TerminalSounds.PlayClick();
        return true;
    }
    internal bool DrawButton(SpriteBatch batch,Rectangle box)
    {
        var skin=Art(Hover(box)?"ButtonHighlighted":"Button");
        TerminalSkin.Button(batch,skin,box);
        batch.Draw(Art("TreeHammer"),new Rectangle(box.X+3,box.Y+2,box.Width-7,box.Height-7),Color.White);
        string count=Orders.PendingCount.ToString();
        float fit=Math.Min(.65f,(box.Width-4)/Math.Max(1f,TerminalFont.Measure(count).X));
        TerminalFont.Draw(batch,count,new Vector2(box.Right-2,box.Bottom-2),Color.White,fit,1,1);
        if(Hover(box)) {Main.LocalPlayer.mouseInterface=true;TerminalTooltip.Show(TerminalText.Get("合成状态"),count);}
        return Click(box);
    }

    internal bool Draw(SpriteBatch batch,Rectangle viewport)
    {
        const int width=326,height=185,rows=6;
        float zoom=Main.UIScaleMatrix.M11;
        float scale=Math.Min(4f/zoom,Math.Min((viewport.Width-12f)/width,(viewport.Height-22f)/height));
        int left=viewport.Center.X-(int)(width*scale/2),top=viewport.Center.Y-(int)(height*scale/2);
        Rectangle Box(int x,int y,int w,int h)=>new(left+(int)Math.Round(x*scale),top+(int)Math.Round(y*scale),
            (int)Math.Round((x+w)*scale)-(int)Math.Round(x*scale),(int)Math.Round((y+h)*scale)-(int)Math.Round(y*scale));
        void Text(string text,int x,float y,int maxWidth,float font=.45f,float anchor=0) {
            float fit=font*scale;
            float measured=TerminalFont.Measure(text,fit).X;
            if(measured>maxWidth*scale) fit*=maxWidth*scale/measured;
            TerminalFont.Draw(batch,text,new Vector2(left+x*scale,top+y*scale),TerminalSkin.Ink,fit,anchor,.5f);
        }
        void Icon(int type,int x,int y,int size) {
            if(!ContentSamples.ItemsByType.TryGetValue(type,out var item))return;
            Main.instance.LoadItem(type);
            Main.GetItemDrawFrame(type,out _,out var frame);
            int longest=Math.Max(1,Math.Max(frame.Width,frame.Height));
            ItemSlot.DrawItemIcon(item,ItemSlot.Context.ChestItem,batch,new Vector2(left+x*scale,top+y*scale),
                size*scale/longest,longest,Color.White);
        }
        int Scroll(Rectangle area,int value,int total,int page,int trackX) {
            int max=Math.Max(0,total-page);
            var track=Box(trackX,19,12,137);
            if(Hover(area)||Hover(track))value-=Math.Sign(PlayerInput.ScrollWheelDeltaForUI);
            if(max>0 && Hover(track) && Main.mouseLeft)
                value=(int)Math.Round(max*Math.Clamp((Main.MouseScreen.Y-track.Top-7.5f*scale)/Math.Max(1,track.Height-15*scale),0,1));
            value=Math.Clamp(value,0,max);
            TerminalScroll.Draw(batch,track,scale,max>0?(float)value/max:0,max>0);
            return value;
        }
        batch.Draw(TextureAssets.MagicPixel.Value,viewport,Color.Black*.48f);
        batch.Draw(Art("StatusList"),Box(0,0,94,164),Color.White);
        batch.Draw(Art("StatusGrid"),Box(88,0,238,185),Color.White);
        var entries=Orders.StatusEntries();
        var current=entries.FirstOrDefault(o=>o.Ticket==selected) ?? entries.FirstOrDefault();
        if(current?.Ticket!=selected){selected=current?.Ticket ?? "";materialScroll=0;}
        Text(TerminalText.Get("合成订单"),7,11,72);
        var back=Box(302,-5,20,20);
        var backAsset=Hover(back)?"BackHover":"Back";
        batch.Draw(ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/SearchSettings/"+backAsset).Value,back,Color.White);
        if(Click(back))return false;
        orderScroll=Scroll(Box(6,19,68,137),orderScroll,entries.Length,rows,77);
        for(int row=0;row<rows && row+orderScroll<entries.Length;row++){
            var entry=entries[row+orderScroll];int y=19+23*row;
            var box=Box(7,y,67,22);batch.Draw(Art(entry.Ticket==selected?"StatusSelected":"StatusRow"),box,Color.White);
            Text(Lang.GetItemNameValue(entry.Type),10,y+6,44,.45f);
            Text(entry.Paused?TerminalText.Get("已暂停")+" ×"+entry.Quantity:"×"+entry.Quantity,10,y+16,43,.41f);
            Icon(entry.Type,64,y+11,15);
            if(Click(box)){current=entry;selected=entry.Ticket;materialScroll=0;}
        }
        string title=TerminalText.Get("合成状态");
        if(current?.Paused==true)title+=" · "+TerminalText.Get("已暂停");
        Text(title,96,11,197);
        var materials=current?.Materials ?? Array.Empty<TerminalOrders.MaterialStatus>();
        materialScroll=Scroll(Box(96,19,204,137),materialScroll,(materials.Length+2)/3,rows,306);
        for(int slot=0;slot<rows*3 && slot+materialScroll*3<materials.Length;slot++){
            var m=materials[slot+materialScroll*3];int x=97+slot%3*68,y=20+slot/3*23;
            var box=Box(x,y,67,22);
            if(m.Missing>0 || m.Planned>0)
                batch.Draw(TextureAssets.MagicPixel.Value,box,m.Missing>0?new Color(198,165,150):
                    m.Available>0?new Color(101,156,155):new Color(188,175,139));
            Icon(m.Type,x+56,y+11,14);
            int lines=(m.Available>0?1:0)+(m.Planned>0?1:0)+(m.Missing>0?1:0);
            float spacing=5.6f;
            float line=y+11-(lines-1)*spacing/2;
            void Count(string key,int n){if(n<=0)return;Text(TerminalText.Get(key)+StoragePane.FormatStockCount(n),x+46,line,44,
                Math.Max(.245f,11f/(18*scale*zoom)),1);line+=spacing;}
            Count("可用数量：",m.Available);Count("计划合成：",m.Planned);Count("缺失数量：",m.Missing);
            if(Hover(box))TerminalTooltip.ShowItem(ContentSamples.ItemsByType[m.Type]);
        }
        bool Button(int x,string key){
            var box=Box(x,160,50,20);bool enabled=current?.Waiting==true;
            var skin=Art(!enabled?"ButtonDisabled":Hover(box)?"ButtonHighlighted":"Button");
            // Preserve both bevel pixels on each side of the original 200-wide strip.
            void Strip(int offset,int sourceX,int sourceWidth,int width) {
                var start=new Vector2(MathF.Round((left+(x+offset)*scale)*zoom)/zoom,
                    MathF.Round((top+160*scale)*zoom)/zoom);
                batch.Draw(skin,start,new Rectangle(sourceX,0,sourceWidth,20),Color.White,0,Vector2.Zero,
                    new Vector2(scale*width/sourceWidth,scale),SpriteEffects.None,0);
            }
            Strip(0,0,2,2);Strip(2,2,1,46);Strip(48,skin.Width-2,2,2);
            TerminalFont.Draw(batch,TerminalText.Get(key),box.Center.ToVector2(),enabled?new Color(239,239,239):TerminalSkin.Ink,.45f*scale,.5f,.5f);
            return enabled && Click(box);
        }
        if(Button(191,current?.Paused==true?"继续":"暂停"))Orders.ChangeWaiting(current.Ticket,false);
        if(Button(251,"取消"))Orders.ChangeWaiting(current.Ticket,true);
        return true;
    }
}
