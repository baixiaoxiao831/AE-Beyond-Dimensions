using System;
using System.Linq;
using System.Collections.Generic;
using AccessoryProbe;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using EquipmentSlots = AccessoryProbe.AccessoryProbe;

namespace AEBeyondDimensions;

internal sealed class EquipmentPane : UIElement
{
    internal readonly ReforgeChoices Choices = new();
    private Item? inspectedAccessory;
    private int inspectedPrefix;
    private ulong nextEffectUpdate;
    private string inspectedEffect = "";
    private const int SlotSize = 44;
    private const int Step = 48;
    private const int PageSize = 9;
    private bool vanity;
    internal void SelectBench() => selectedPage=WorkPage.Bench;
    private enum WorkPage { Stations, Shimmer, Bench, Reforge }
    private static readonly string[] TabNames = { TerminalText.Get("工作方块栏"), TerminalText.Get("微光栏"), TerminalText.Get("工作台"), TerminalText.Get("重铸") };
    private static readonly string[] TabAssets = { "Stations", "Shimmer", "Bench", "Reforge" };
    private WorkPage selectedPage;
    private int stationRow, productPage;
    private bool ordersOpen;
    private readonly TerminalOrderPane orderPane = new();
    private int page;
    private readonly Dictionary<ModAccessorySlot,bool> restrictedSlots = new();

    private bool IsRestricted(Slot slot)
    {
        var source = slot.Source;
        if (source == null) return false;
        if (restrictedSlots.TryGetValue(source, out bool restricted)) return restricted;
        bool onlyVanity = !source.DrawFunctionalSlot;
        var mode = (AccessorySlotType)(onlyVanity ? ItemSlot.Context.EquipAccessoryVanity : ItemSlot.Context.EquipAccessory);
        restricted = source.GetType().GetMethod(nameof(ModAccessorySlot.CanAcceptItem),
            new[] {typeof(Item),typeof(AccessorySlotType)})?.DeclaringType != typeof(ModAccessorySlot) &&
            ContentSamples.ItemsByType.Values.Any(item => item.accessory && (!onlyVanity || item.FitsAccessoryVanitySlot) &&
                !source.CanAcceptItem(item, mode));
        restrictedSlots[source] = restricted;
        return restricted;
    }


    public EquipmentPane()
    {
        Width.Set(0, 1);
        Height.Set(216, 0);
    }

    public void ResetView()
    {
        Choices.Close();
        restrictedSlots.Clear();
        vanity = false;
        selectedPage = WorkPage.Stations;
        stationRow = productPage = 0;
        ordersOpen = false;
        orderPane.Reset();
        page = 0;
    }

    public bool EditingOrderQuantity => ordersOpen && orderPane.Editing;
    public void EndOrderTextInput() => orderPane.EndTextInput();
    public void OpenOrders()
    {
        ordersOpen=true;
        orderPane.Reset();
    }

    public bool OrdersOpen => ordersOpen;
    public void UpdateOrderDialog() => orderPane.Update();
    public void DrawOrderDialog(SpriteBatch batch, Rectangle viewport) => ordersOpen = orderPane.Draw(batch, viewport);

    public bool TryShiftStation(Item item, int backpackIndex = -1)
    {
        if (ordersOpen || !ItemSlot.ShiftInUse || !Main.mouseLeft || !Main.mouseLeftRelease)
            return false;
        var work = Main.LocalPlayer.GetModPlayer<TerminalWorkArea>();
        if(TeamClient.Open)return backpackIndex>=0 && TerminalWorkArea.IsStationOutput(item) &&
            !TeamClient.Stations.Exists(i=>i.type==item.type) && TeamClient.Action(TeamOp.Deposit,TeamTab.Stations,backpackIndex);
        if (!work.StoreStation(item, out bool duplicate))
        {
            if (duplicate && backpackIndex >= 0)
                Main.LocalPlayer.GetModPlayer<TerminalStorage>().Deposit(backpackIndex);
            return duplicate;
        }
        if (backpackIndex >= 0 && !item.IsAir)
            Main.LocalPlayer.GetModPlayer<TerminalStorage>().Deposit(backpackIndex);
        stationRow = Math.Max(0, work.Stations.Count / 5 - 3);
        SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
        return true;
    }

    public bool WantsTransfer => selectedPage == WorkPage.Reforge && !ordersOpen && ItemSlot.ShiftInUse && Main.mouseLeft && Main.mouseLeftRelease;

    internal void ShowTransferCursor(Item item)
    {
        if (selectedPage == WorkPage.Reforge && !ordersOpen && ItemSlot.ShiftInUse && !Main.mouseLeft &&
            Main.LocalPlayer.GetModPlayer<TerminalReforgePlayer>().CanMove(item))
            Main.cursorOverride = CursorOverrideID.InventoryToChest;
    }

    internal static bool IsHovered(Vector2 topLeft, float width) =>
        !PlayerInput.IgnoreMouseInterface && Main.MouseScreen.Between(topLeft, topLeft + new Vector2(width));

    protected override void DrawSelf(SpriteBatch batch)
    {
        var topLeft = GetDimensions().Position();
        var player = Main.LocalPlayer;
        float previousScale = Main.inventoryScale;
        Main.inventoryScale = SlotSize / 52f;
        try
        {
            var controller = ModContent.GetInstance<ProbeMod>().Assets.Request<Texture2D>("Assets/MEController").Value;
            batch.Draw(controller, new Rectangle((int)topLeft.X + (SlotSize - 32) / 2, (int)topLeft.Y + 5, 32, 32), Color.White);
            var switchArt = ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/EquipmentSwitch").Value;
            var button = new Rectangle((int)topLeft.X + 4*Step - 40, (int)topLeft.Y, 50, 42);
            batch.Draw(switchArt,button,new Rectangle(vanity ? 20 : 0,0,20,17),Color.White);
            if (!PlayerInput.IgnoreMouseInterface && button.Contains(Main.MouseScreen.ToPoint()))
            {
                player.mouseInterface = true;
                TerminalTooltip.Show(TerminalText.Get(vanity ? "切换到穿戴" : "切换到装饰"));
                if (Main.mouseLeft && Main.mouseLeftRelease)
                {
                    Main.mouseLeftRelease = false;
                    TerminalSounds.PlayClick();
                    vanity = !vanity;
                }
            }

            TerminalSkin.Panel(batch,new Rectangle((int)topLeft.X+Step-4,(int)topLeft.Y+42,3*Step+6,3*Step+30));
            int row = 0;
            var slotsTop = topLeft + new Vector2(0, 46);
            foreach (var slot in EquipmentSlots.ReadArmor(player))
                DrawSlot(batch, slot, slotsTop + new Vector2(0, row++ * Step), hasEye: false);

            var accessories = EquipmentSlots.Read(player).Where(slot => (vanity ? slot.Vanity : slot.Functional) != null).OrderBy(IsRestricted).ToList();
            int pageCount = (accessories.Count + PageSize - 1) / PageSize;
            page = Math.Clamp(page, 0, Math.Max(0, pageCount - 1));
            if (pageCount > 1)
            {
                var nav = topLeft + new Vector2(Step, 3 * Step + 46);
                if (IconButton(batch, nav, TextureAssets.ScrollLeftButton.Value, null, TerminalText.Get("上一页")))
                    page = (page + pageCount - 1) % pageCount;
                if (IconButton(batch, nav + new Vector2(Step * 2, 0), TextureAssets.ScrollRightButton.Value,
                    null, TerminalText.Get("下一页")))
                    page = (page + 1) % pageCount;
                TerminalFont.Draw(batch, $"{page+1}/{pageCount}", nav + new Vector2(62, 12),
                    Color.White, .7f, .5f, .5f);
            }
            for (int cell = 0, i = page * PageSize; cell < PageSize && i < accessories.Count; cell++, i++)
            {
                var offset = new Vector2((1 + cell % 3) * Step, (cell / 3) * Step);
                DrawSlot(batch, accessories[i], slotsTop + offset, hasEye: !vanity);
            }
            DrawWorkArea(batch, topLeft);
        }
        finally
        {
            Main.inventoryScale = previousScale;
        }
    }

    private void DrawSlot(SpriteBatch batch, Slot slot, Vector2 position, bool hasEye)
    {
        TerminalSkin.Slot(batch,position,SlotSize);
        var eye = slot.Visible ? TextureAssets.InventoryTickOn.Value : TextureAssets.InventoryTickOff.Value;
        var eyePosition = position + new Vector2(SlotSize - eye.Width, 0);
        bool eyeHovered = hasEye && !PlayerInput.IgnoreMouseInterface &&
            Main.MouseScreen.Between(eyePosition, eyePosition + eye.Size());
        bool hovered = !eyeHovered && IsHovered(position, SlotSize);
        if (eyeHovered)
        {
            slot.HandleVisibility(TerminalSounds.ButtonClick);
            Main.hoverItemName = "";
        }
        else if (hovered)
        {
            var item = vanity ? slot.Vanity : slot.Functional;
            bool wasEmpty = Main.mouseItem.IsAir;
            Main.LocalPlayer.mouseInterface = true;
            if (!TerminalCursor.HandleOccupiedCursor(item))
            {
                if (WantsTransfer)
                {
                    Main.LocalPlayer.GetModPlayer<TerminalReforgePlayer>().TryTransferEquipment(slot, vanity);
                    Main.mouseLeftRelease = false;
                }
                else
                {
                    slot.HandleInput(vanity);
                    if (wasEmpty && !Main.mouseItem.IsAir) TerminalCursor.RememberEquipment(slot, vanity);
                }
            }
            if (item != null) ShowTransferCursor(item);
            TerminalTooltip.ShowItem(item);
        }
        slot.DrawContents(batch, vanity, position + new Vector2(SlotSize / 2f), Main.inventoryScale, hovered);
        if (hasEye)
            batch.Draw(eye, eyePosition, Color.White * (eyeHovered ? 1f : .75f));
    }

    private static Texture2D TabIcon(WorkPage tab) => ModContent.GetInstance<ProbeMod>().Assets
        .Request<Texture2D>("Assets/Tabs/" + TabAssets[(int)tab], AssetRequestMode.ImmediateLoad).Value;

    private void DrawWorkArea(SpriteBatch batch, Vector2 topLeft)
    {
        var upgrades = Main.LocalPlayer.GetModPlayer<TerminalMagnet>();
        bool Locked(WorkPage tab) => tab == WorkPage.Shimmer && !upgrades.ShimmerAvailable || tab == WorkPage.Reforge && !upgrades.ReforgeAvailable;
        if (Locked(selectedPage))
        {
            selectedPage = WorkPage.Stations;
            Choices.Close();
        }
        var origin = topLeft + new Vector2(4 * Step + 22, 0);
        const int tabWidth=40, tabHeight=44;
        int tabX = GetDimensions().ToRectangle().Right - tabWidth;
        int left = (int)origin.X, top = (int)origin.Y;
        var pixel = TextureAssets.MagicPixel.Value;
        var baseColor = new Color(203,204,212);
        var outline = new Color(65,63,84);
        var light = new Color(242,242,242);
        void Fill(int x,int y,int width,int height,Color color) =>
            batch.Draw(pixel,new Rectangle(x,y,width,height),color);
        TerminalSkin.Inset(batch,new Rectangle(left,top,tabX-left,tabHeight*TabAssets.Length),new Color(175,178,195));
        for (int i=0; i<TabAssets.Length; i++)
        {
            var tab = (WorkPage)i;
            bool locked = Locked(tab);
            bool active = selectedPage == tab;
            int y=top+i*tabHeight;
            var bounds = new Rectangle(tabX,y,tabWidth,tabHeight);
            if (active)
            {
                Fill(tabX-4,y,tabWidth+4,tabHeight,outline);
                Fill(tabX-4,y,tabWidth+2,tabHeight-2,light);
                Fill(tabX-4,y+2,tabWidth,tabHeight-6,baseColor);
                Fill(tabX-4,y,6,tabHeight,baseColor);
            }
            else
            {
                Fill(tabX,y,tabWidth,tabHeight,outline);
                Fill(tabX+2,y+2,tabWidth-4,tabHeight-4,new Color(154,159,180));
                Fill(tabX+2,y+2,tabWidth-4,tabHeight-8,new Color(173,176,196));
                Fill(tabX+2,y+4,tabWidth-6,tabHeight-12,new Color(135,143,165));
                Fill(tabX-2,y,2,tabHeight,light);
            }
            batch.Draw(TabIcon(tab),new Rectangle(bounds.Center.X-16,bounds.Center.Y-16,32,32),locked ? Color.Gray : Color.White);
            if (!PlayerInput.IgnoreMouseInterface && bounds.Contains(Main.MouseScreen.ToPoint()))
            {
                Main.LocalPlayer.mouseInterface = true;
                Main.HoverItem = new Item();
                TerminalTooltip.Show(TabNames[i],locked ? TerminalText.Get(tab == WorkPage.Shimmer ? "需要安装微光卡" : "需要安装重铸卡或重铸升级卡") : tab == WorkPage.Reforge ? TerminalText.Get("选择物品") : null);
                if (Main.mouseLeft && Main.mouseLeftRelease)
                {
                    Main.mouseLeftRelease=false;
                    if (locked) continue;
                    if (!active) TerminalSounds.PlayClick();
                    selectedPage=tab;
                }
            }
        }
        var work = Main.LocalPlayer.GetModPlayer<TerminalWorkArea>();
        var content = origin + new Vector2(7,8);
        switch (selectedPage)
        {
            case WorkPage.Stations: DrawStations(batch,content,work); break;
            case WorkPage.Shimmer: DrawShimmer(batch,new Rectangle(left+5,top+5,tabX-left-10,tabHeight*TabAssets.Length-10),work); break;
            case WorkPage.Bench:
                DrawBench(batch,new Rectangle(left+8,top+6,tabX-left-16,tabHeight*TabAssets.Length-12));
                break;
            case WorkPage.Reforge: DrawReforge(batch,new Rectangle(left,top,tabX-left,tabHeight*TabAssets.Length)); break;
        }
    }

    private static void DrawBench(SpriteBatch batch,Rectangle area)
    {
        var bench=Main.LocalPlayer.GetModPlayer<TerminalBench>();
        var recipe=bench.ViewRecipe;
        if(recipe!=null && !bench.ViewFilled && !bench.HasMaterials && Main.GameUpdateCount%30==0 && (!TeamClient.Open || bench.Prepare(recipe.RecipeIndex,false)))bench.Prepare(recipe.RecipeIndex,true);
        int gap=Math.Max(4,area.Width/20);
        int step=Math.Min(area.Height/3,(area.Width-gap)/4);
        var origin=new Vector2(area.Center.X-(4*step+gap)/2,area.Center.Y-3*step/2);
        var side=origin+new Vector2(3*step+gap,0);
        int buttonSize=(step-2)/2;
        Main.inventoryScale=(step-1)/52f;
        int pages=Math.Max(1,((recipe?.requiredItem.Count??0)+8)/9);
        var status = recipe != null && !bench.ViewFilled ? bench.MaterialStatus(recipe.RecipeIndex) : null;
        for(int i=0;i<9;i++){
            int n=bench.Page*9+i;
            var pos=origin+new Vector2(i%3*step,i/3*step);
            var part=n<bench.ViewHeld.Length?bench.ViewHeld[n].FirstOrDefault():null;
            var item=part?.Clone()??new Item();
            if(part!=null)item.stack=bench.ViewHeld[n].Sum(x=>x.stack);
            var shortage = status != null && n < status.Length ? status[n] : default;
            Color? background = null;
            if (item.IsAir && recipe != null && n < recipe.requiredItem.Count) item = recipe.requiredItem[n].Clone();
            if (shortage.Missing > 0)
            {
                background = shortage.Craftable ? new Color(38,100,222) : new Color(197,52,54);
                if (item.IsAir) item = recipe.requiredItem[n].Clone();
            }
            TerminalSkin.Item(batch,item,ItemSlot.Context.ChestItem,pos,Color.White,background);
            if(IsHovered(pos,step))
            {
                TerminalTooltip.ShowItem(item, context: shortage.Missing > 0 ? string.Format(TerminalText.Get("缺少数量：{0}"), shortage.Missing) : null);
            }
        }
        bool TransferButton(int x,string asset,string title,string detail) =>
            IconButton(batch,side+new Vector2(x,0),ModContent.GetInstance<ProbeMod>().Assets.Request<Texture2D>("Assets/InventoryActions/"+asset).Value,
                null,TerminalText.Get(title),bench.HasMaterials,TerminalText.Get(detail),includesFrame:true,size:buttonSize);
        if(TransferButton(0,"BenchStore","放回物品","将合成槽的物品存入网络")) { bench.Refund(); Recipe.FindRecipes(); }
        if(TransferButton(step-buttonSize,"BenchInventory","取走物品","将合成槽的物品放入物品栏")) bench.TransferToInventory();
        if(recipe==null || bench.ViewRecipe==null)return;
        var output=recipe.createItem.Clone();
        var outputPos=side+new Vector2(0,buttonSize+gap);
        TerminalSkin.Item(batch,output,ItemSlot.Context.ChestItem,outputPos,bench.ViewFilled?Color.White:Color.Gray);
        if(IsHovered(outputPos,step)){
            TerminalTooltip.ShowItem(output, context: bench.ViewFilled?TerminalText.Get("点击取出并自动补料"):status?.Any(s => s.Missing > 0) == true ?
                TerminalText.Get("蓝底：物品可制作\n红底：物品缺失") : TerminalText.Get("等待材料或工作站"));
            if(Main.mouseLeft && Main.mouseLeftRelease){Main.mouseLeftRelease=false;bench.Take();}
        }
        if(pages>1 && IconButton(batch,outputPos+new Vector2(0,step+4),TextureAssets.ScrollRightButton.Value,null,TerminalText.Get("下一页材料")))bench.Page=(bench.Page+1)%pages;

    }

    private static bool WorkCell(SpriteBatch batch, Vector2 position, Item item, bool allowStack = false)
    {
        TerminalSkin.Item(batch,item,ItemSlot.Context.ChestItem,position,Color.White);
        if (!IsHovered(position,52 * Main.inventoryScale)) return false;
        Main.LocalPlayer.mouseInterface = true;
        TerminalTooltip.ShowItem(item);
        if (TerminalCursor.HandleOccupiedCursor(item, allowStack)) return false;
        if (!Main.mouseLeft || !Main.mouseLeftRelease) return false;
        Main.mouseLeftRelease = false;
        return true;
    }

    private void DrawStations(SpriteBatch batch, Vector2 origin, TerminalWorkArea work)
    {
        var displayed=TeamClient.Open?TeamClient.Stations:work.Stations;
        const int cell=36, columns=5, rows=4;
        int lastRow=Math.Max(0, displayed.Count / columns + 1 - rows);
        if (Main.MouseScreen.Between(origin,origin+new Vector2(cell*columns,cell*rows)) &&
            PlayerInput.ScrollWheelDeltaForUI!=0) stationRow-=Math.Sign(PlayerInput.ScrollWheelDeltaForUI);
        stationRow=Math.Clamp(stationRow,0,lastRow);
        if (lastRow>0)
        {
            var nav=origin+new Vector2(cell*columns+2,0);
            if (IconButton(batch,nav,TextureAssets.CraftUpButton.Value,null,TerminalText.Get("向上滚动"),stationRow>0)) stationRow--;
            if (IconButton(batch,nav+new Vector2(0,30),TextureAssets.CraftDownButton.Value,null,TerminalText.Get("向下滚动"),stationRow<lastRow)) stationRow++;
        }
        Main.inventoryScale=(cell-2)/52f;
        int start=stationRow*columns;
        for (int index=start; index<=displayed.Count && index<start+columns*rows; index++)
        {
            bool empty=index==displayed.Count;
            var item=empty ? new Item() : displayed[index];
            var pos=origin+new Vector2((index-start)%columns*cell,(index-start)/columns*cell);
            if (!WorkCell(batch,pos,item)) continue;
            if(TeamClient.Open){if(!empty && Main.mouseItem.IsAir)TeamClient.Action(TeamOp.Withdraw,TeamTab.Stations,index);break;}
            if (empty)
            {
                if (work.StoreStation(Main.mouseItem))
                    stationRow=Math.Max(stationRow,work.Stations.Count/columns+1-rows);
            }
            else if (Main.mouseItem.IsAir)
            {
                if (ItemSlot.ShiftInUse)
                {
                    var player=Main.LocalPlayer;
                    var remainder=player.GetItem(player.whoAmI,item,GetItemSettings.LootAllSettingsRegularChest);
                    if (!remainder.IsAir) { work.Stations[index]=remainder; break; }
                    work.Stations.RemoveAt(index);
                }
                else
                {
                    Main.mouseItem=item;
                    work.Stations.RemoveAt(index);
                    TerminalCursor.RememberList(work.Stations,index);
                }
                Recipe.FindRecipes();
            }
            break;
        }
    }

    private void DrawShimmer(SpriteBatch batch, Rectangle area, TerminalWorkArea work)
    {
        int gap=4;
        int step=Math.Min((area.Width-2*gap)/5,(area.Height-26)/3);
        var origin=new Vector2(area.Center.X-(5*step+2*gap)/2,area.Center.Y-(3*step+(work.Products.Count>9 ? 26 : 0))/2);
        int resultsX=step+gap;
        int actionsX=4*step+2*gap;
        Main.inventoryScale=(step-2)/52f;
        if (WorkCell(batch,origin,work.ShimmerInput[0],allowStack:true))
        {
            var input = work.ShimmerInput[0];
            (Main.mouseItem,work.ShimmerInput[0])=(input,Main.mouseItem);
            if (!input.IsAir) TerminalCursor.RememberSlot(work.ShimmerInput,0);
        }
        if (IconButton(batch,origin+new Vector2(0,step+2),TabIcon(WorkPage.Shimmer),null,TerminalText.Get("微光转化"),!work.ShimmerInput[0].IsAir,size:step-2))
            work.Transform();
        int pages=Math.Max(1,(work.Products.Count+8)/9);
        productPage=Math.Clamp(productPage,0,pages-1);
        for (int i=0;i<9;i++)
        {
            int index=productPage*9+i;
            var item=index<work.Products.Count ? work.Products[index] : new Item();
            var pos=origin+new Vector2(resultsX+(i%3)*step,(i/3)*step);
            if (WorkCell(batch,pos,item) && index<work.Products.Count && Main.mouseItem.IsAir)
            {
                Main.mouseItem=item;
                work.Products.RemoveAt(index);
                TerminalCursor.RememberList(work.Products,index);
            }
        }
        if (pages>1)
        {
            if (IconButton(batch,origin+new Vector2(resultsX,step*3+1),TextureAssets.ScrollLeftButton.Value,null,TerminalText.Get("上一页产物"),productPage>0)) productPage--;
            if (IconButton(batch,origin+new Vector2(resultsX+3*step-26,step*3+1),TextureAssets.ScrollRightButton.Value,null,TerminalText.Get("下一页产物"),productPage+1<pages)) productPage++;
        }
        if (IconButton(batch,origin+new Vector2(actionsX,0),TextureAssets.ChestStack[0].Value,null,TerminalText.Get("转移至存储"),work.Products.Count>0,size:step-2)) work.TransferProducts(true);
        var backpack = ModContent.GetInstance<ProbeMod>().Assets.Request<Texture2D>("Assets/Backpack").Value;
        if (IconButton(batch,origin+new Vector2(actionsX,step),backpack,null,TerminalText.Get("转移至背包"),work.Products.Count>0,size:step-2)) work.TransferProducts(false);
    }

    private void DrawReforge(SpriteBatch batch, Rectangle area)
    {
        var player=Main.LocalPlayer;
        var forge=player.GetModPlayer<TerminalReforgePlayer>();
        float unit=Math.Min(area.Width/103f,area.Height/80f);
        Vector2 At(int x,int y) => new(area.X+MathF.Round(x*unit),area.Y+MathF.Round(y*unit));
        int Units(int n) => (int)MathF.Round(n*unit);
        var slot=At(7,17);
        int slotSize=Units(18);
        Main.inventoryScale=slotSize/52f;
        if (IsHovered(slot,slotSize)) forge.HandleSlot();
        var current=forge.ReforgeItem;
        TerminalSkin.Item(batch,current,ItemSlot.Context.PrefixItem,slot,Color.White);
        var hammer=At(81,38);
        int buttonSize=Units(17);
        var hammerArt=TextureAssets.Reforge[IsHovered(hammer,buttonSize)?1:0].Value;
        var purse=ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Backpack").Value;
        void RowIcon(Texture2D icon,int y)
        {
            var at=At(9,y);
            batch.Draw(icon,new Rectangle((int)at.X,(int)at.Y,Units(9),Units(9)),Color.White);
        }
        RowIcon(TextureAssets.Reforge[0].Value,41);
        RowIcon(purse,58);
        int price=forge.Price;
        bool affordable=!current.IsAir && player.CanAfford(price);
        string detail=current.IsAir ? TerminalText.Get("放入可重铸物品") :
            affordable ? TerminalText.Get("当前报价 ")+Main.ValueToCoins(price) : TerminalText.Get("余额不足");
        if (!current.IsAir)
        {
            string name=current.AffixName();
            float nameScale=Math.Min(.85f,Units(50)/Math.Max(1,TerminalFont.Measure(name).X));
            TerminalFont.Draw(batch,name,At(29,25),TerminalSkin.Ink,nameScale,0,.5f);
            long wallet=Utils.CoinsCount(out _,player.inventory,58)+Utils.CoinsCount(out _,player.bank.item)
                +Utils.CoinsCount(out _,player.bank2.item)+Utils.CoinsCount(out _,player.bank3.item)+Utils.CoinsCount(out _,player.bank4.item);
            DrawCoins(price,42,54);
            DrawCoins(wallet,60,74);
        }
        void DrawCoins(long value,int y,int available)
        {
            var at=At(25,y);
            var amounts=Utils.CoinsSplit(value);
            float width=0;
            for(int n=3;n>=0;n--) if(amounts[n]>0 || (value==0 && n==0))
                width+=Units(9)+TerminalFont.Measure(amounts[n].ToString(),.72f).X+2;
            float fit=Math.Min(1,Units(available)/Math.Max(1,width));
            for(int n=3;n>=0;n--)
            {
                if(amounts[n]==0 && (value!=0 || n!=0))continue;
                var coin=ContentSamples.ItemsByType[ItemID.CopperCoin+n];
                float side=Units(8)*fit;
                ItemSlot.DrawItemIcon(coin,ItemSlot.Context.InventoryItem,batch,at+new Vector2(side/2,Units(4)),side/16f,16,Color.White);
                at.X+=Units(9)*fit;
                string count=amounts[n].ToString();
                TerminalFont.Draw(batch,count,at+new Vector2(0,Units(4)),TerminalSkin.Ink,.72f*fit,0,.5f);
                at.X+=TerminalFont.Measure(count,.72f*fit).X+2*fit;
            }
        }
        if (current.accessory) {
            if (inspectedAccessory!=current || inspectedPrefix!=current.prefix || Main.GameUpdateCount>=nextEffectUpdate) {
                inspectedAccessory=current;
                inspectedPrefix=current.prefix;
                inspectedEffect=ReforgeChoices.Effect(current);
                nextEffectUpdate=Main.GameUpdateCount+30;
            }
            var text = inspectedEffect.Length > 0 ? inspectedEffect : current.prefix == 0 ? TerminalText.Get("无词缀加成") : TerminalText.Get("无效果说明");
            var width = Math.Max(36,area.Width-Units(13));
            float scale = Math.Min(.76f,width/Math.Max(1,TerminalFont.Measure(text).X));
            TerminalFont.Draw(batch,text,At(7,71),TerminalSkin.Ink,scale);
        }
        if (ReforgeChoices.Enabled && IconButton(batch,At(81,16),ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Tabs/PrefixPicker").Value,
            null,TerminalText.Get("自选重铸"),!current.IsAir,size:buttonSize)) Choices.Show(current);
        bool choose = ReforgeChoices.Enabled && !ItemSlot.ShiftInUse;
        if (choose) detail += "\n" + TerminalText.Get("Shift＋点击锤子随机重铸");
        if (IconButton(batch, hammer, hammerArt,
            null, choose ? TerminalText.Get("自选重铸") : TerminalText.Get("随机重铸"), choose ? !current.IsAir : affordable, detail, playClickSound: false,size:buttonSize)) {
            if (choose) Choices.Show(current);
            else forge.TryReforge();
        }
    }

    internal static bool IconButton(SpriteBatch batch, Vector2 position, Texture2D icon, Rectangle? frame,
        string tooltip, bool enabled = true, string? detail = null, bool playClickSound = true, bool includesFrame = false, int size = 26)
    {

        bool hovered = IsHovered(position, size);
        var source = frame ?? icon.Bounds;
        float scale = (size-4f) / Math.Max(source.Width, source.Height);
        if (includesFrame)
            batch.Draw(icon,new Rectangle((int)position.X,(int)position.Y,size,size),hovered ? Color.White : Color.White * .96f);
        else {
        TerminalSkin.Panel(batch,new Rectangle((int)position.X,(int)position.Y,size,size));
        batch.Draw(icon, position + new Vector2(size / 2f), source, Color.White * (enabled ? 1f : .45f),
            0f, source.Size() / 2f, scale, SpriteEffects.None, 0f);
        }
        if (!hovered)
            return false;
        Main.LocalPlayer.mouseInterface = true;
        Main.HoverItem = new Item();
        TerminalTooltip.Show(tooltip, detail);
        if (!enabled || !Main.mouseLeft || !Main.mouseLeftRelease)
            return false;
        Main.mouseLeftRelease = false;
        if (playClickSound) TerminalSounds.PlayClick();
        return true;
    }
}
