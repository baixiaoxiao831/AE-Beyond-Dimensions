using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace AEBeyondDimensions;

public sealed class GuideItemTooltip : GlobalItem
{
    public override bool AppliesToEntity(Item item,bool lateInstantiation) => item.ModItem is MagnetCard or ReforgeCard or ReforgeUpgradeCard or ShimmerCard || item.ModItem is MasterControlCard && ModLoader.HasMod("ImproveGame");
    public override void ModifyTooltips(Item item,List<TooltipLine> lines)
    {
        var hint=TerminalGuide.Instance.Hint;
        if (hint.Length>0) lines.Add(new TooltipLine(Mod,"Guide",hint){OverrideColor=Color.LightGray});
    }
    public override bool PreDrawTooltip(Item item,ReadOnlyCollection<TooltipLine> lines,ref int x,ref int y)
    {
        TerminalGuide.Instance.Hover(item);
        return !TerminalGuide.Instance.Open;
    }
}

public sealed class TerminalGuide : ModSystem
{
    internal static TerminalGuide Instance => ModContent.GetInstance<TerminalGuide>();
    internal bool Open { get; private set; }
    private Dictionary<string,Dictionary<string,string>> copy=null!;
    private ModKeybind key=null!;
    private static readonly string[] BaseCardPages = { "magnet", "shimmer", "reforge", "reforgeUpgrade" };
    private static readonly string[] ControlCardPages = { "magnet", "shimmer", "reforge", "reforgeUpgrade", "control" };
    private static string[] CardPages => ModLoader.HasMod("ImproveGame") ? ControlCardPages : BaseCardPages;
    private string page="install", hovered="", holding="";
    private ulong lastHover;
    private long started;
    private bool slots=true,cards, crafting, inventoryAtOpen;
    private int scroll,visit;
    private string? jumpChapter;
    private string activeChapter="";
    private readonly Dictionary<string,int> chapterRows=new();
    private bool hideNav;
    private string search="";
    private readonly List<string> visits=new();
    private void Go(string id,bool record=true)
    {
        if(id=="grouped")id="overview";
        if(record && (visits.Count==0 || visits[visit]!=id)){
            visits.RemoveRange(visit+1,visits.Count-visit-1);
            visits.Add(id);visit=visits.Count-1;
        }
        jumpChapter=CardPages.Contains(id)?id:null;
        activeChapter=jumpChapter??"";
        page=jumpChapter!=null?"cards":id;
        scroll=0;
        if(id is "crafting" or "order" or "plan")crafting=true;
        if(id=="install")slots=true;
        if(CardPages.Contains(id))cards=true;
        if(id=="search"){search="";Main.clrInput();}
    }
    private Dictionary<string,string> Text => copy[Language.ActiveCulture.Name.StartsWith("zh")?"zh":"en"];
    internal string Title => Text["title"];
    internal string Hint => key?.GetAssignedKeys().FirstOrDefault() is string binding ? string.Format(Text["hold"],binding) + (holding!="" && holding==hovered ? $"  {Math.Min(100,(int)(Stopwatch.GetElapsedTime(started).TotalMilliseconds/6))}%" : "") : "";
    public override void Load()
    {
        if(Main.dedServ)return;
        copy=JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(Mod.GetFileBytes("Assets/Guide.json"))!;
        key=KeybindLoader.RegisterKeybind(Mod,"OpenItemGuide",Keys.G);
    }
    internal bool ItemShortcutDown => key?.Current==true && hovered!="" && Main.GameUpdateCount-lastHover<=1;
    internal void Hover(Item item)
    {
        hovered=item.ModItem switch {MagnetCard=>"magnet",ShimmerCard=>"shimmer",ReforgeCard=>"reforge",ReforgeUpgradeCard=>"reforgeUpgrade",MasterControlCard when ModLoader.HasMod("ImproveGame")=>"control",_=>""};
        lastHover=Main.GameUpdateCount;
    }
    internal void Show(string entry)
    {
        inventoryAtOpen=Main.playerInventory;
        Open=true;holding="";slots=cards=crafting=hideNav=false;visits.Clear();visits.Add(entry);visit=0;Go(entry,false);
    }
    private ulong? closedTick;
    internal bool ClosedThisTick => closedTick == Main.GameUpdateCount;
    private void Close(){closedTick=Main.GameUpdateCount;Open=false;holding=hovered="";Main.playerInventory=inventoryAtOpen;}
    public override void OnWorldUnload(){Open=false;holding=hovered="";}
    public override void UpdateUI(GameTime time)
    {
        if(Main.LocalPlayer.dead){Open=false;holding="";return;}
        if(Open){
            Main.LocalPlayer.mouseInterface=true;
            PlayerInput.LockVanillaMouseScroll("TerminalGuide");
            if(page=="search"){PlayerInput.WritingText=true;Main.instance.HandleIME();search=Main.GetInputText(search);}
            if(Main.keyState.IsKeyDown(Keys.Escape)&&Main.oldKeyState.IsKeyUp(Keys.Escape))Close();
            return;
        }
        if(!key.Current || !Main.hasFocus || Main.drawingPlayerChat ||
            Main.GameUpdateCount-lastHover>1 || hovered=="" || !Main.mouseItem.IsAir){holding="";return;}
        if(key.JustPressed){holding=hovered;started=Stopwatch.GetTimestamp();}
        if(holding!=hovered){holding="";return;}
        if(Stopwatch.GetElapsedTime(started).TotalMilliseconds>=600)Show(holding);
    }
    internal GameInterfaceLayer? GuideLayer { get; private set; }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        if(!Open)return;
        int pos=layers.FindIndex(l=>l.Name=="Vanilla: Cursor");
        GuideLayer = new LegacyGameInterfaceLayer("AEBeyondDimensions: Guide",Draw,InterfaceScaleType.UI);
        layers.Insert(pos<0?layers.Count:pos,GuideLayer);
    }
    private (string Page,string Culture,int Width,float Scale,float Zoom) wrapKey;
    private readonly List<(string Value,bool Heading)> wrappedLines=new();

    private bool Draw()
    {
        var batch=Main.spriteBatch;
        TerminalSkin.BeginPixelBatch(batch);
        var viewport=batch.GraphicsDevice.Viewport;
        var extent=Vector2.Transform(new Vector2(viewport.Width,viewport.Height),Matrix.Invert(Main.UIScaleMatrix));
        var screen=new Rectangle(0,0,(int)Math.Ceiling(extent.X),(int)Math.Ceiling(extent.Y));

        var mouse=Main.MouseScreen.ToPoint();
        var accent=new Color(65,218,235);
        float scale=screen.Width<600?.68f:.84f;
        void Fill(Rectangle rect,Color color)=>batch.Draw(TextureAssets.MagicPixel.Value,rect,color);
        void Label(string s,int x,int y,Color c)=>TerminalFont.Draw(batch,s,new Vector2(x,y),c,scale);
        bool Button(Rectangle rect,string s,bool active=false){
            bool over=rect.Contains(mouse);
            if(over||active)Fill(rect,new Color(49,53,53));
            Label(s,rect.X+4,rect.Y+2,over||active?accent:Color.White);
            if(!over||!Main.mouseLeft||!Main.mouseLeftRelease)return false;
            Main.mouseLeftRelease=false;TerminalSounds.PlayClick();return true;
        }
        int bar=Math.Clamp(screen.Height/18,36,60),pad=Math.Clamp(screen.Width/128,8,18);
        int edge=hideNav?0:(int)(screen.Width*.2345f);
        Fill(screen,new Color(12,13,10)*.91f);
        Fill(new Rectangle(0,bar-2,screen.Width,2),Color.Gray);
        Fill(new Rectangle(edge,bar,screen.Width-edge,screen.Height-2*bar),Color.White*.045f);
        Label(Text[page],pad,(bar-26)/2,Color.White);
        var tools=Mod.Assets.Request<Texture2D>("Assets/GuideTools").Value;
        for(int i=0;i<5;i++){
            int hit=Math.Min(bar,screen.Width/10),size=hit/2;
            var box=new Rectangle(screen.Width-pad-(5-i)*hit,0,hit,bar);
            bool enabled=i==1?visit>0:i==2?visit+1<visits.Count:true;
            batch.Draw(tools,new Rectangle(box.Center.X-size/2,box.Center.Y-size/2,size,size),new Rectangle(i*10,0,10,10),
                enabled?(box.Contains(mouse)?accent:Color.White):Color.White*.3f);
            if(enabled && box.Contains(mouse) && Main.mouseLeft && Main.mouseLeftRelease){
                Main.mouseLeftRelease=false;TerminalSounds.PlayClick();
                if(i==0)Go("search");
                if(i==1)Go(visits[--visit],false);
                if(i==2)Go(visits[++visit],false);
                if(i==3)hideNav=!hideNav;
                if(i==4){Close();return true;}
            }
        }
        int y=bar+2, step=(int)Math.Ceiling(TerminalFont.Measure("Ag",scale).Y)+2;
        void Group(string id,ref bool expanded,params string[] entries){
            if(Button(new Rectangle(pad,y,edge-pad,step),(expanded?"▼ ":"▶ ")+Text[id],page==id)){expanded=!expanded;Go(id);}
            y+=step;if(!expanded)return;
            foreach(var id2 in entries){
                string caption=Text[id2];
                if(id2=="install" && TerminalFont.Measure(caption,scale).X>edge-30)caption=Language.ActiveCulture.Name.StartsWith("zh")?"安装":"Installation";
                if(Button(new Rectangle(pad+12,y,edge-pad-12,step),caption,page==id2 || page=="cards" && activeChapter==id2))Go(id2);
                y+=step;
            }
        }
        if(!hideNav){
            if(Button(new Rectangle(pad,y,edge-pad,step),Text["index"],page=="index"))Go("index");
            y+=step;
            if(Button(new Rectangle(pad,y,edge-pad,step),Text["overview"],page=="overview"))Go("overview");
            y+=step;
            Group("slots",ref slots,"install");Group("cards",ref cards,CardPages);Group("crafting",ref crafting,"order","plan");
        }
        int x=edge+pad,width=Math.Max(40,screen.Width-x-pad),top=bar+pad;
        string attribution=Text["from"]+" "+Mod.DisplayNameClean;
        Label(attribution,screen.Width-pad-(int)(TerminalFont.Measure(attribution,scale).X),screen.Height-bar,Color.LightGray);
        if(page is "index" or "slots" or "crafting" or "search"){
            int yy=top;
            if(page=="search"){Label(Text["search"]+"   "+search+"|",x,yy,Color.White);yy+=step+6;}
            string[] entries=page switch{"index"=>new[]{"overview","slots","cards","crafting"},"slots"=>new[]{"install"},"cards"=>CardPages,"crafting"=>new[]{"order","plan"},_=>new[]{"overview","install"}.Concat(CardPages).Concat(new[]{"order","plan"}).ToArray()};
            foreach(var id in entries){
                if(page=="search" && !new[]{"","Lead","H1","P1","H2","P2","H3","P3"}.Any(k=>(Text.TryGetValue(id+k,out var value) && value.Contains(search,StringComparison.OrdinalIgnoreCase) || id=="overview" && Text.TryGetValue("grouped"+k,out var grouped) && grouped.Contains(search,StringComparison.OrdinalIgnoreCase))))continue;
                Fill(new Rectangle(x+3,yy+step/2,3,3),Color.LightGray);
                var box=new Rectangle(x+12,yy,width-12,step);
                Label(Text[id],box.X,yy,accent);
                if(box.Contains(mouse)&&Main.mouseLeft&&Main.mouseLeftRelease){
                    Main.mouseLeftRelease=false;if(id=="slots")slots=true;if(id=="cards")cards=true;Go(id);break;
                }
                yy+=step;
            }
            return true;
        }
        var lines=wrappedLines;
        void Wrap(string s,bool heading=false){
            foreach(var paragraph in s.Split('\n')){
                string line="";
                foreach(Match m in Regex.Matches(paragraph,@"[A-Za-z0-9'’-]+|.")){
                    if(line.Length>0 && TerminalFont.Measure(line+m.Value,scale).X>width){lines.Add((line.TrimEnd(),heading));line="";}
                    if(line.Length!=0||m.Value!=" ")line+=m.Value;
                }
                lines.Add((line,heading));
            }
        }
        var key=(page,Language.ActiveCulture.Name,width,scale,Main.UIScaleMatrix.M11);
        if (key!=wrapKey)
        {
            lines.Clear();
            chapterRows.Clear();
            void Article(string id,bool icon=false){
                Wrap(Text[id],true);
                if(icon)lines.Add(("@icon/"+id,false));
                Wrap(Text[id+"Lead"]);
                for(int n=1;Text.ContainsKey(id+"H"+n);n++){lines.Add(("",false));Wrap(Text[id+"H"+n],true);Wrap(Text[id+"P"+n]);}
            }
            if(page=="cards"){
                foreach(string card in CardPages){
                    if(lines.Count>0)lines.Add(("",false));
                    chapterRows[card]=lines.Count;
                    Article(card,true);
                }
            }else{
                Article(page);
                if(page=="overview"){lines.Add(("",false));Article("grouped");}
            }
            wrapKey=key;
        }
        int lineHeight=(int)Math.Ceiling(TerminalFont.Measure("Ag",scale).Y)+4;
        int EndHeight((string Value,bool Heading) row)=>row.Value.StartsWith("@icon/")?54:lineHeight;
        int max=Math.Max(0,lines.Sum(EndHeight)-(screen.Height-top-bar));
        if(page=="cards"){
            int offset=0;
            foreach(var chapter in chapterRows){
                offset=chapter.Value*lineHeight+Array.IndexOf(CardPages,chapter.Key)*(54-lineHeight);
                if(jumpChapter==chapter.Key)scroll=offset;
            }
            max=Math.Max(max,offset);
            jumpChapter=null;
        }
        scroll=Math.Clamp(scroll-PlayerInput.ScrollWheelDeltaForUI/3,0,max);
        if(page=="cards")
            activeChapter=CardPages.Last(id=>chapterRows[id]*lineHeight+Array.IndexOf(CardPages,id)*(54-lineHeight)<=scroll);
        if(max>0)TerminalScroll.Draw(batch,new Rectangle(screen.Right-12,top,9,screen.Height-top-bar),1.2f,(float)scroll/max,true);
        int rowY=top-scroll;
        foreach(var row in lines){
            int height=EndHeight(row);
            if(rowY>=top && rowY+height<=screen.Height-bar){
                if(row.Value.StartsWith("@icon/")){
                    var icon=row.Value[6..] switch { "magnet"=>"Card", "shimmer"=>"ShimmerCard", "reforgeUpgrade"=>"ReforgeUpgradeCard", "control"=>"MasterControlCard", _=>"ReforgeCard" };
                    var art=Mod.Assets.Request<Texture2D>("Assets/Magnet/"+icon).Value;
                    batch.Draw(art,new Rectangle(x,rowY,48,48),Color.White);
                }else{
                    Label(row.Value,x,rowY,row.Heading?Color.White:Color.LightGray);
                    if(row.Heading)Fill(new Rectangle(x,rowY+lineHeight-3,(int)(TerminalFont.Measure(row.Value,scale).X),1),Color.Gray);
                }
            }
            rowY+=height;
        }
        return true;
    }
}
