using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AEBeyondDimensions;

internal sealed class TerminalOrderPane
{
    private const int Columns = 3;
    private const int VisibleRows = 7;
    private static readonly int[] StepX = { 20, 48, 82, 120 };
    private static readonly int[] StepWidths = { 22, 28, 32, 38 };
    private static readonly int[] CommonSteps = { 1, 10, 100, 1000 };
    private static readonly int[] StackSteps = { 1, 16, 32, 64 };
    private static readonly Color Ink = new(64, 62, 83);
    private string text = "1";
    private bool reviewing;
    private bool tree,missingOnly,panning;
    private bool treeSettings;
    private int routeScroll;
    private System.Collections.Generic.List<(string Label,string Key,string? Value,int Icon)>? routeChoices;
    private void EditRoute(InstantCraft.Node node)
    {
        if(node.Recipe is not Recipe recipe)return;
        var recipes=InstantCraft.RecipesFor(node.Type);
        var choices=new System.Collections.Generic.List<(string,string,string?,int)>();
        string product=TerminalOrders.ResultKey(node.Type);
        if(recipes.Length>1){
            choices.Add((TerminalText.Get("自动选择配方"),product,null,node.Type));
            foreach(var option in recipes){
                string ingredients=string.Join(" + ",option.requiredItem.Select(i=>i.Name+" ×"+i.stack));
                string station=string.Join(", ",option.requiredTile.Where(t=>t>=0).Select(t=>Lang.GetMapObjectName(Terraria.Map.MapHelper.TileToLookup(t,0))));
                var conditions=string.Join(", ",option.Conditions.Select(c=>c.Description.Value));
                choices.Add((string.Join(" · ",new[]{ingredients,station,conditions}.Where(str=>str.Length>0)),product,InstantCraft.RecipeKey(option),option.createItem.type));
            }
        }
        for(int slot=0;slot<recipe.requiredItem.Count;slot++){
            var ingredient=recipe.requiredItem[slot];
            var types=InstantCraft.Alternatives(recipe,ingredient.type);
            if(types.Length<=1)continue;
            string key=InstantCraft.RecipeKey(recipe)+"/"+slot;
            choices.Add((ingredient.Name+" · "+TerminalText.Get("自动选择材料"),key,null,ingredient.type));
            foreach(int type in types)choices.Add((Lang.GetItemNameValue(type)+" ×"+ingredient.stack,key,TerminalOrders.ResultKey(type),type));
        }
        if(choices.Count==0)return;
        routeChoices=choices;routeScroll=0;panning=false;
    }
    private void DrawRoute(SpriteBatch batch)
    {
        var choices=routeChoices!;
        batch.Draw(Art("TreePanel"),Rect(20,10,310,210),Color.White);
        Caption(batch,TerminalText.Get("选择配方或材料"),Rect(30,16,250,14),9,Ink);
        if(DrawButton(batch,304,12,20,"",TerminalText.Get("返回合成树"),true,"Back")){routeChoices=null;return;}
        int last=Math.Max(0,choices.Count-9);
        if(Hover(Rect(22,35,304,178)))routeScroll=Math.Clamp(routeScroll-Math.Sign(PlayerInput.ScrollWheelDeltaForUI),0,last);
        if(last>0)TerminalScroll.Draw(batch,Rect(318,36,7,177),scale,routeScroll/(float)last,true);
        for(int i=0;i<9 && routeScroll+i<choices.Count;i++){
            var row=choices[routeScroll+i];
            int y=36+i*20;
            var box=Rect(28,y,286,18);
            bool selected=(Orders.Route.TryGetValue(row.Key,out var pin)?pin:null)==row.Value;
            TerminalSkin.Inset(batch,box,selected?new Color(171,197,220):Hover(box)?new Color(211,215,228):new Color(192,195,207));
            DrawItem(batch,row.Icon,Rect(31,y+1,16,16));
            Caption(batch,row.Label,Rect(51,y,260,18),8,Ink);
            if(!Hover(box))continue;
            TerminalTooltip.Show(row.Label,"");
            if(!Pressed())continue;
            if(row.Value==null)Orders.Route.Remove(row.Key);else Orders.Route[row.Key]=row.Value;
            Orders.RefreshQuote();routeChoices=null;return;
        }
    }
    private static TerminalSearchSettings Prefs=>Main.LocalPlayer.GetModPlayer<TerminalSearchSettings>();
    private float treeZoom=1;
    private Vector2 treePan,previousMouse;
    private System.Collections.Generic.List<InstantCraft.Node>? cachedTree;
    private bool cachedMissing;
    private Vector2[] pos=Array.Empty<Vector2>();
    private int[][] treeRows=Array.Empty<int[]>();
    private System.Collections.Generic.List<int>[] children=Array.Empty<System.Collections.Generic.List<int>>();
    private bool[] visible=Array.Empty<bool>();
    private bool dragging;
    private bool replaceQuantity;
    private int dragOffset;
    private int scrollRow;
    private Point origin;
    private float scale;
    public bool Editing { get; private set; }
    private static TerminalOrders Orders => Main.LocalPlayer.GetModPlayer<TerminalOrders>();
    private static Texture2D Art(string name) => ModContent.GetInstance<ProbeMod>().Assets
        .Request<Texture2D>("Assets/Orders/" + name, AssetRequestMode.ImmediateLoad).Value;

    public void Reset()
    {
        routeChoices=null;
        reviewing = dragging = false;
        tree=panning=treeSettings=false;
        Editing = replaceQuantity = Orders.SelectedProvider != null;
        if (Editing) Main.clrInput();
        scrollRow = 0;
        text = Orders.Quantity.ToString(CultureInfo.InvariantCulture);
        Orders.InvalidateQuote();
    }

    public void EndTextInput() => Editing = false;

    public void Update()
    {
        Main.LocalPlayer.mouseInterface = true;
        PlayerInput.LockVanillaMouseScroll("TerminalOrder");
        if (!Editing) return;
        PlayerInput.WritingText = true;
        Main.instance.HandleIME();
        string updated = Main.GetInputText(replaceQuantity ? "" : text);
        if (updated.Length <= 10 && updated.All(char.IsAsciiDigit) &&
            (!replaceQuantity || updated.Length > 0 || Main.keyState.IsKeyDown(Keys.Back) || Main.keyState.IsKeyDown(Keys.Delete)))
        {
            text = updated;
            replaceQuantity = false;
        }
        if (Main.inputTextEnter)
        {
            if (Orders.SelectedProvider != null && Quantity(out int quantity))
            {
                TerminalSounds.PlayClick();
                Next(quantity);
            }
            Editing = false;
        }
        if (Main.inputTextEscape) Editing = false;
    }

    public bool Draw(SpriteBatch batch, Rectangle viewport)
    {
        int width = tree ? 350 : reviewing ? 238 : 176, height = tree ? 230 : reviewing ? 218 : 107;
        float fit = Math.Min((viewport.Width - 10f) / (width + 38), (viewport.Height - 12f) / (height + 10));
        scale = Math.Min(3, fit < 1 ? Math.Max(.1f, fit) : (float)Math.Floor(fit));
        origin = new Point(viewport.Center.X - (int)(width * scale / 2), viewport.Center.Y - (int)(height * scale / 2));
        batch.Draw(TextureAssets.MagicPixel.Value, viewport, Color.Black * .53f);
        if(tree){DrawTree(batch);return true;}
        batch.Draw(Art(reviewing ? "Report" : "Amount"), Rect(0, 0, width, height), Color.White);
        if (!reviewing) batch.Draw(Art("VerticalButtonsBg"), Rect(-19, 0, 21, 26), Color.White);
        if (DrawButton(batch, reviewing ? 194 : -16, 3, 18, "", TerminalGuide.Instance.Title, true, "Help"))
        {
            TerminalGuide.Instance.Show("order");
            return true;
        }
        if(reviewing && Orders.Tree.Count>0 && DrawButton(batch,214,3,18,"",TerminalText.Get("显示合成树"),true,"CraftingTree"))
        {
            tree=true;treeZoom=1;treePan=Vector2.Zero;missingOnly=Prefs.TreeMissingDefault;
            return true;
        }
        return reviewing ? DrawReview(batch) : DrawQuantity(batch);
    }

    private void DrawTree(SpriteBatch batch)
    {
        if(routeChoices!=null){DrawRoute(batch);return;}
        if(treeSettings){
            batch.Draw(Art("TreePanel"),Rect(75,7,200,216),Color.White);
            Caption(batch,TerminalText.Get("合成树设置"),Rect(83,12,120,12),9,Ink);
            bool Option(int y,string label,bool value){
                var icon=ModContent.GetInstance<ProbeMod>().Assets.Request<Texture2D>("Assets/SearchSettings/"+(value?"On":"Off")).Value;
                batch.Draw(icon,Rect(85,y,22,12),Color.White);Caption(batch,label,Rect(111,y,156,12),8,Ink);
                return Hover(Rect(85,y,179,18))&&Pressed();
            }
            if(Option(32,TerminalText.Get("使用紧凑视图"),Prefs.CompactTree)){Prefs.CompactTree=!Prefs.CompactTree;cachedTree=null;}
            if(Option(57,TerminalText.Get("截图中带上物品数量"),Prefs.TreeScreenshotCounts))Prefs.TreeScreenshotCounts=!Prefs.TreeScreenshotCounts;
            if(Option(82,TerminalText.Get("默认仅显示缺失"),Prefs.TreeMissingDefault))Prefs.TreeMissingDefault=!Prefs.TreeMissingDefault;
            if(DrawButton(batch,250,6,20,"",TerminalText.Get("返回合成树"),true,"Back"))treeSettings=false;
            return;
        }
        batch.Draw(Art("TreePanel"),Rect(0,0,350,230),Color.White);
        batch.Draw(Art("TreeToolsFrame"),Rect(-19,0,21,66),Color.White);
        Caption(batch,TerminalText.Get("合成树"),Rect(8,5,89,12),9,Ink);
        if(DrawButton(batch,326,-5,20,"",TerminalText.Get("返回合成计划"),true,"Back")){tree=false;return;}
        if(DrawButton(batch,-16,4,18,"",TerminalText.Get("合成树设置"),true,"TreeCog")){treeSettings=true;return;}
        bool capture=DrawButton(batch,-16,24,18,"",TerminalText.Get("保存合成树截图"),true,"TreeScreenshot");
        if(DrawButton(batch,-16,44,18,"",TerminalText.Get("仅显示缺失分支"),true,"TreeMissing"))missingOnly=!missingOnly;
        var nodes=Orders.Tree;
        if(nodes.Count==0)return;
        if(cachedTree!=nodes || cachedMissing!=missingOnly){
        cachedTree=nodes;cachedMissing=missingOnly;
        visible=nodes.Select(n=>!missingOnly||n.Missing).ToArray();visible[0]=true;
        children=Enumerable.Range(0,nodes.Count).Select(i=>new System.Collections.Generic.List<int>()).ToArray();
        for(int i=nodes.Count-1;i>0;i--)if(visible[i]){visible[nodes[i].Parent]=true;children[nodes[i].Parent].Add(i);}
        pos=new Vector2[nodes.Count];int leaves=0;
        void Place(int i,int depth){
            var kids=children[i];kids.Reverse();foreach(int child in kids)Place(child,depth+1);
            pos[i]=new(kids.Count==0?(Prefs.CompactTree?23:30)*leaves++:(pos[kids[0]].X+pos[kids[^1]].X)/2,30*depth);
        }
        Place(0,0);
        treeRows=Enumerable.Range(0,nodes.Count).Where(i=>visible[i]).GroupBy(i=>(int)pos[i].Y)
            .OrderBy(g=>g.Key).Select(g=>g.OrderBy(i=>pos[i].X).ToArray()).ToArray();
        }
        if(capture)SaveTreeImage();
        float center=pos[0].X;
        Caption(batch,TerminalText.Get("点击节点选择配方或材料"),Rect(10,212,330,12),7,Ink,.5f);
        var board=Rect(10,20,330,190);
        Vector2 mouse=Main.MouseScreen;
        if(Hover(board)){
            int wheel=PlayerInput.ScrollWheelDeltaForUI;
            if(wheel!=0)treeZoom=Math.Clamp(treeZoom*(wheel>0?1.15f:.87f),.35f,3f);

        }
        if(!Main.mouseLeft)panning=false;
        if(panning){treePan+=(mouse-previousMouse)/scale;previousMouse=mouse;}
        Point PointAt(int i)=>Rect((int)(175+(pos[i].X-center)*treeZoom+treePan.X),(int)(36+pos[i].Y*treeZoom+treePan.Y),0,0).Location;
        void Line(Point a,Point b){
            var clip=Rectangle.Intersect(board,new Rectangle(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(1,Math.Abs(a.X-b.X)+1),Math.Max(1,Math.Abs(a.Y-b.Y)+1)));
            if(clip.Width>0&&clip.Height>0)Fill(batch,clip,Ink);
        }
        var skin=Art("TreeNodes");
        for(int depth=0;depth<treeRows.Length;depth++){
          var ids=treeRows[depth];int y=PointAt(ids[0]).Y,half=(int)(15*scale*treeZoom);
          if(y-half>board.Bottom)break;
          if(y+half<board.Top)continue;
          foreach(int i in ids){
            var at=PointAt(i);var kids=children[i];
            if(kids.Count>0){
                int x1=PointAt(kids[0]).X,x2=PointAt(kids[^1]).X;
                if(x2>=board.Left && x1<=board.Right)Line(new(x1,y+half),new(x2,y+half));
                if(board.Left<=at.X && at.X<=board.Right)Line(at,new(at.X,y+half));
            }
            if(i!=0 && board.Left<=at.X && at.X<=board.Right)Line(new(at.X,y-half),at);
          }
          int lo=0,hi=ids.Length;
          while(lo<hi){int mid=(lo+hi)/2;if(PointAt(ids[mid]).X<board.Left)lo=mid+1;else hi=mid;}
          for(int k=lo;k<ids.Length;k++){
            int i=ids[k];if(PointAt(i).X>board.Right)break;
            var n=nodes[i];var at=PointAt(i);int side=Math.Max(6,(int)(22*scale*treeZoom));
            var rect=new Rectangle(at.X-side/2,at.Y-side/2,side,side);
            if(!board.Contains(rect))continue;
            batch.Draw(skin,rect,new Rectangle(0,n.Missing?22:0,22,22),Color.White);
            var icon=rect;icon.Inflate(-side/7,-side/7);DrawItem(batch,n.Type,icon);
            Caption(batch,n.Count.ToString(),new Rectangle(rect.X,rect.Bottom-side/3,side,side/3),7*treeZoom,Color.White,1);
            if(Hover(rect)){
                TerminalTooltip.ShowItem(ContentSamples.ItemsByType[n.Type]);
                var editable=n;
                if(n.Recipe==null && n.Parent>=0 && nodes[n.Parent].Recipe is Recipe parent &&
                    parent.acceptedGroups.Any(g=>RecipeGroup.recipeGroups[g].ValidItems.Contains(n.Type)))editable=nodes[n.Parent];
                if(editable.Recipe is Recipe candidate && (InstantCraft.RecipesFor(editable.Type).Length>1 || candidate.acceptedGroups.Count>0) && Pressed()){
                    EditRoute(editable);return;
                }
            }
          }
        }
        if(Hover(board) && Pressed()){panning=true;previousMouse=mouse;}
    }

    private void SaveTreeImage()
    {
        var nodes=Orders.Tree;var device=Main.instance.GraphicsDevice;
        int width=(int)pos.Max(p=>p.X)+34,height=(int)pos.Max(p=>p.Y)+34;
        float factor=Math.Min(2f,4096f/Math.Max(width,height));
        using var target=new RenderTarget2D(device,Math.Max(1,(int)Math.Ceiling(width*factor)),Math.Max(1,(int)Math.Ceiling(height*factor)));
        using var export=new SpriteBatch(device);
        var previous=device.GetRenderTargets();
        try{
            device.SetRenderTarget(target);device.Clear(new Color(203,204,212));
            export.Begin(SpriteSortMode.Deferred,BlendState.AlphaBlend,SamplerState.PointClamp,null,null,null,Matrix.CreateScale(factor));
            Rectangle At(int i)=>new((int)pos[i].X+6,(int)pos[i].Y+6,22,22);
            for(int i=1;i<nodes.Count;i++)if(visible[i]){
                var a=At(nodes[i].Parent).Center;var b=At(i).Center;int mid=(a.Y+b.Y)/2;
                Fill(export,new(a.X,a.Y,1,mid-a.Y+1),Ink);
                Fill(export,new(Math.Min(a.X,b.X),mid,Math.Abs(a.X-b.X)+1,1),Ink);
                Fill(export,new(b.X,mid,1,b.Y-mid+1),Ink);
            }
            for(int i=0;i<nodes.Count;i++)if(visible[i]){
                var n=nodes[i];var rect=At(i);export.Draw(Art("TreeNodes"),rect,new Rectangle(0,n.Missing?22:0,22,22),Color.White);
                var icon=rect;icon.Inflate(-3,-3);DrawItem(export,n.Type,icon);
                if(Prefs.TreeScreenshotCounts)export.DrawString(FontAssets.MouseText.Value,n.Count.ToString(),new Vector2(rect.Left+9,rect.Bottom-8),Color.White,0,Vector2.Zero,.36f,SpriteEffects.None,0);
            }
            export.End();
        }finally{device.SetRenderTargets(previous);}
        var dir=System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Main.SavePath,"Captures"));
        var path=System.IO.Path.Combine(dir.FullName,"CraftingTree-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".png");
        using var file=System.IO.File.Create(path);target.SaveAsPng(file,target.Width,target.Height);
        Main.NewText(TerminalText.Get("合成树已保存：")+path);
    }

    private Rectangle Rect(int x, int y, int width, int height)
    {
        int left = origin.X + (int)Math.Round(x * scale), top = origin.Y + (int)Math.Round(y * scale);
        return new Rectangle(left, top, origin.X + (int)Math.Round((x + width) * scale) - left,
            origin.Y + (int)Math.Round((y + height) * scale) - top);
    }

    private static bool Hover(Rectangle bounds) => !PlayerInput.IgnoreMouseInterface && bounds.Contains(Main.MouseScreen.ToPoint());

    private static bool Pressed()
    {
        if (!Main.mouseLeft || !Main.mouseLeftRelease) return false;
        Main.mouseLeftRelease = false;
        return true;
    }

    private bool Quantity(out int quantity) => int.TryParse(text, NumberStyles.None,
        CultureInfo.InvariantCulture, out quantity) && quantity > 0;

    private void Next(int quantity)
    {
        Orders.Quantity = quantity;
        Editing = false;
        if (!Orders.RefreshQuote()) return;
        reviewing = true;
        scrollRow = 0;
    }

    private bool DrawQuantity(SpriteBatch batch)
    {
        var orders = Orders;
        bool selected = orders.SelectedProvider != null;
        bool valid = Quantity(out int quantity);
        Caption(batch, TerminalText.Get("选择数量"), Rect(8, 4, 139, 13), 9, Ink);
        if (DrawButton(batch, 152, -5, 20, "", TerminalText.Get("返回终端"), true, "Back")) return false;
        bool shifted = ItemSlot.ShiftInUse || Main.keyState.IsKeyDown(Keys.LeftControl) || Main.keyState.IsKeyDown(Keys.RightControl);
        var steps = shifted ? StackSteps : CommonSteps;
        for (int i = 0; i < steps.Length; i++)
        {
            int amount = steps[i];
            if (DrawButton(batch, StepX[i], 30, StepWidths[i], "+" + amount, TerminalText.Get("增加 ") + amount, selected))
                SetQuantity((long)quantity + amount);
            if (DrawButton(batch, StepX[i], 72, StepWidths[i], "-" + amount, TerminalText.Get("减少 ") + amount, selected))
                SetQuantity((long)quantity - amount);
        }
        if (selected) DrawItem(batch, orders.ResultType, Rect(23, 53, 16, 16));
        if (selected && Hover(Rect(22, 52, 18, 18))) HoverItem(orders.ResultType);
        var field = Rect(48, 55, 66, 12);
        Fill(batch, field, new Color(242, 242, 242));
        Fill(batch, Rect(49, 56, 64, 10), new Color(105, 109, 136));
        Fill(batch, Rect(49, 58, 64, 8), new Color(154, 159, 180));
        Caption(batch, text + (Editing && Main.GameUpdateCount % 54 < 31 ? "_" : ""), Rect(50, 55, 62, 12), 8, Color.White);
        if (selected && Hover(field))
        {
            TerminalTooltip.Show(TerminalText.Get("下单数量"), TerminalText.Get("可直接输入正整数"));
            if (Pressed()) { Editing = true; replaceQuantity = false; Main.clrInput(); }
        }
        if (DrawButton(batch, 120, 51, 38, TerminalText.Get("下一页"), TerminalText.Get("查看材料清单"), selected && valid)) Next(quantity);
        string status = selected ? orders.Status : TerminalText.Get("请先从配方浏览器选择物品");
        if (status.Length > 0) Caption(batch, status, Rect(8, 93, 160, 10), 7, Ink, .5f);
        return true;
    }

    private void SetQuantity(long value)
    {
        text = Math.Clamp(value, 1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        Editing = false;
    }

    private bool DrawReview(SpriteBatch batch)
    {
        var orders = Orders;
        string bytes = orders.QuoteBytes is long value ? $"{value:N0} B" : "—";
        Caption(batch, TerminalText.Get("字节：") + bytes, Rect(8, 6, 183, 15), 9, Ink);
        var entries = orders.Materials;
        // The preview is independent of ingredient stock and only describes the selected output.
        int count = entries.Length + 1;
        var stations = orders.MissingStations;
        int reservedRows = stations.Length > 0 ? 1 : 0;
        int visibleRows = VisibleRows - reservedRows;
        if (stations.Length > 0)
        {
            var card = Rect(9, 27, 203, 22);
            Fill(batch, card, new Color(70, 64, 75));
            Fill(batch, Rect(9, 27, 2, 22), new Color(233, 181, 88));
            var first = stations[0];
            if (first.Item > 0) DrawItem(batch, first.Item, Rect(13, 30, 16, 16));
            string title = TerminalText.Get("缺少工作方块：") + first.Name + (stations.Length > 1 ? TerminalText.Get(" 等 ") + stations.Length + TerminalText.Get(" 种") : "");
            string hint = first.Item > 0 ? TerminalText.Get("放入终端工作方块栏，或靠近对应工作站。") : TerminalText.Get("请靠近对应工作站。");
            Caption(batch, title, Rect(32, 28, 177, 10), 8, Color.Wheat);
            Caption(batch, hint, Rect(32, 39, 177, 9), 6, Color.White);
            if (Hover(card)) TerminalTooltip.Show(TerminalText.Get("缺少工作方块"), string.Join("、", stations.Select(s=>s.Name))+"\n"+hint);
        }
        int maxRow = Math.Max(0, (count + Columns - 1) / Columns - visibleRows);
        var grid = Rect(8, 26, 219, 162);
        if (Hover(grid) && PlayerInput.ScrollWheelDeltaForUI != 0)
            scrollRow -= Math.Sign(PlayerInput.ScrollWheelDeltaForUI);
        scrollRow = Math.Clamp(scrollRow, 0, maxRow);
        DrawScroll(batch, maxRow);
        for (int slot = 0; slot < Columns * visibleRows; slot++)
        {
            int i = scrollRow * Columns + slot;
            if (i >= count) break;
            int x = 9 + slot % Columns * 68, y = 27 + (reservedRows + slot / Columns) * 23;
            var cell = Rect(x, y, 67, 22);
            bool output = i == 0;
            var entry = output ? default : entries[i - 1];
            int type = output ? orders.ResultType : entry.Type;
            int planned = output ? orders.Quantity : orders.ToCraft[i - 1];
            int fromStorage = output ? 0 : Math.Min(entry.Available, Math.Max(0, entry.Required - planned));
            int missing = output ? 0 : Math.Max(0, entry.Required - planned - fromStorage);
            if (missing > 0) Fill(batch, cell, Color.Red * .10f);
            if (Hover(cell))
            {
                Fill(batch, cell, Color.White * .16f);
                HoverItem(type);
            }
            DrawItem(batch, type, Rect(x + 48, y + 3, 16, 16));
            int lines = (fromStorage > 0 ? 1 : 0) + (missing > 0 ? 1 : 0) + (planned > 0 ? 1 : 0);
            int lineY = y + (22 - lines * 6) / 2;
            void CountLine(string label, int number)
            {
                if (number <= 0) return;
                Caption(batch, label + number.ToString("N0"), Rect(x + 1, lineY, 45, 6), 4.8f, Ink, 1f);
                lineY += 6;
            }
            CountLine(TerminalText.Get("可用数量："), fromStorage);
            CountLine(TerminalText.Get("缺失数量："), missing);
            CountLine(TerminalText.Get("合成数量："), planned);
        }
        if (DrawButton(batch, 8, 193, 50, TerminalText.Get("取消"), TerminalText.Get("返回选择数量")))
        {
            Reset();
            return true;
        }
        var bookmark = ModContent.GetInstance<TerminalSystem>().BookmarkMissing;
        bool IsMissing(int i) => entries[i].Required - orders.ToCraft[i] > entries[i].Available;
        if (DrawButton(batch, 62, 193, 98, TerminalText.Get("收藏缺失"), TerminalText.Get("将缺失的物品加入 JEI 收藏，不退出当前界面。"),
            Enumerable.Range(0, entries.Length).Any(IsMissing)) && orders.QueueMissing())
        {
            var needed = Enumerable.Range(0, entries.Length).Where(IsMissing)
                .GroupBy(i => entries[i].Type)
                .Select(g => (g.Key, (int)Math.Min(int.MaxValue, g.Sum(i => (long)entries[i].Required - orders.ToCraft[i])))).ToArray();
            bookmark?.Invoke(needed);
            return true;
        }
        if (DrawButton(batch, 164, 193, 50, TerminalText.Get("开始"), TerminalText.Get("提交订单"), orders.Ready) && orders.Submit()) return false;
        if (orders.Status.Length > 0) Caption(batch, orders.Status, Rect(8, 17, 206, 8), 6f, Ink);
        return true;
    }

    private void DrawScroll(SpriteBatch batch, int maxRow)
    {
        const int top = 26, travel = 147;
        int y = top + (maxRow == 0 ? 0 : (int)Math.Round((double)scrollRow / maxRow * travel));
        var thumb = Rect(218, y, 12, 15);
        if (!Main.mouseLeft) dragging = false;
        if (maxRow > 0 && Hover(Rect(218, top, 12, travel + 15)) && Pressed())
        {
            dragging = true;
            dragOffset = thumb.Contains(Main.MouseScreen.ToPoint()) ? Main.MouseScreen.ToPoint().Y - thumb.Top : thumb.Height / 2;
        }
        if (dragging && maxRow > 0)
        {
            double fraction = (Main.MouseScreen.Y - dragOffset - Rect(218, top, 12, 15).Top) / (travel * scale);
            scrollRow = (int)Math.Round(Math.Clamp(fraction, 0, 1) * maxRow);
            thumb = Rect(218, top + (int)Math.Round((double)scrollRow / maxRow * travel), 12, 15);
        }
        TerminalScroll.Draw(batch,Rect(218,top,12,travel+15),scale,maxRow>0?(float)scrollRow/maxRow:0,maxRow>0);
    }

    private static void DrawItem(SpriteBatch batch, int type, Rectangle bounds)
    {
        Main.instance.LoadItem(type);
        var icon = TextureAssets.Item[type].Value;
        var frame = Main.itemAnimations[type]?.GetFrame(icon) ?? icon.Bounds;
        batch.Draw(icon, bounds.Center.ToVector2(), frame, Color.White, 0, frame.Size() / 2f,
            Math.Min((float)bounds.Width / frame.Width, (float)bounds.Height / frame.Height), SpriteEffects.None, 0);
    }

    private static void HoverItem(int type)
    {
        var preview = ContentSamples.ItemsByType[type];
        TerminalTooltip.ShowItem(preview);
        TerminalCursor.HandleOccupiedCursor(preview);
    }

    private bool DrawButton(SpriteBatch batch, int x, int y, int width, string label, string tip,
        bool enabled = true, string? glyph = null, string? detail = null)
    {
        var bounds = Rect(x, y, width, 20);
        bool hover = Hover(bounds);
        var skin = Art(!enabled ? "ButtonDisabled" : hover ? "ButtonHighlighted" : "Button");
        // AE2 declares 3 px nine-slice borders. Height is exactly the source's 20 px.
        batch.Draw(skin, Rect(x, y, 3, 20), new Rectangle(0, 0, 3, 20), Color.White);
        batch.Draw(skin, Rect(x + 3, y, width - 6, 20), new Rectangle(3, 0, 194, 20), Color.White);
        batch.Draw(skin, Rect(x + width - 3, y, 3, 20), new Rectangle(197, 0, 3, 20), Color.White);
        if (glyph != null) batch.Draw(Art(glyph), Rect(x + (width - 16) / 2, y + 1, 16, 16), Color.White);
        else Caption(batch, label, Rect(x + 2, y, width - 4, 18), 8.6f, enabled ? Color.White : Ink, .5f);
        if (!hover) return false;
        TerminalTooltip.Show(tip, detail);
        if (!enabled || !Pressed()) return false;
        TerminalSounds.PlayClick();
        return true;
    }

    private static void Fill(SpriteBatch batch, Rectangle bounds, Color color) =>
        batch.Draw(TextureAssets.MagicPixel.Value, bounds, color);

    private void Caption(SpriteBatch batch, string label, Rectangle bounds, float fontHeight, Color color, float align = 0f)
    {
        var font = FontAssets.MouseText.Value;
        var measured = TerminalFont.Measure(label);
        float factor = Math.Min(fontHeight * scale / Math.Max(1, measured.Y * .80f),
            bounds.Width / Math.Max(1, measured.X));
        var size = measured * factor;
        var at = new Vector2((float)Math.Round(bounds.Left + (bounds.Width - size.X) * align),
            (float)Math.Round(bounds.Center.Y - size.Y * .5f));
        TerminalFont.Draw(batch,label,at,color,factor);
    }
}
