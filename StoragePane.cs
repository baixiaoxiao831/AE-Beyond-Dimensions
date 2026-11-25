using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace AEBeyondDimensions;

internal sealed class StoragePane(EquipmentPane equipment, Action openSettings, Action openMagnetSettings, Action openControl, Func<Rectangle> getFrame) : UIElement
{
    private const int Columns = 9;
    internal const int Step = 46;
    private int scrollRow;
    private bool draggingScroll;
    private int? detailType;
    private bool? detailBatch;
    private bool consumedRight;
    private long lastReceipt;
    private IReadOnlyList<(int Type, long Count)>? lastPending;
    private string search = "";

    public void SetSearch(string text)
    {
        search=text;
        ResetView();
    }
    public void ResetView()
    {
        scrollRow=0;
        detailType=null;
        detailBatch=null;
    }

    protected override void DrawSelf(SpriteBatch batch)
    {
        var storage=Main.LocalPlayer.GetModPlayer<TerminalStorage>();
        var bounds=GetDimensions();
        var origin=bounds.Position();
        var pending = Main.LocalPlayer.GetModPlayer<TerminalOrders>().PendingStockPreview;
        if (storage.ReceiptStamp != lastReceipt || !ReferenceEquals(lastPending, pending)) {
            ReceiptIcon.Clear();
            lastReceipt=storage.ReceiptStamp;
            lastPending=pending;
            ResetView();
        }
        int rows=Math.Max(1,(int)bounds.Height / Step);
        if (!Main.mouseRight) consumedRight=false;
        var frame=getFrame();
        float unit=frame.Width/195f;
        int Edge(int coordinate) => (int)MathF.Round(coordinate*unit);
        Rectangle WebBox(int x,int y,int w,int h) => new(frame.Left+Edge(x),frame.Top+Edge(y),Edge(x+w)-Edge(x),Edge(y+h)-Edge(y));
        void SidebarButton(string asset,string title,string detail,int row,Action click,bool resetScroll=true) {
            var box=WebBox(-19,2+row*22,18,20);
            var icon=ModContent.GetInstance<ProbeMod>().Assets.Request<Texture2D>("Assets/"+asset).Value;
            batch.Draw(icon,box,Color.White);
            if(!box.Contains(Main.MouseScreen.ToPoint()) || PlayerInput.IgnoreMouseInterface) return;
            Main.LocalPlayer.mouseInterface=true;
            TerminalTooltip.Show(title,detail);
            if(Main.mouseLeft && Main.mouseLeftRelease) {
                Main.mouseLeftRelease=false;
                TerminalSounds.PlayClick();
                click();
                if(resetScroll)scrollRow=0;
            }
        }
        var magnet = Main.LocalPlayer.GetModPlayer<TerminalMagnet>();
        bool unlocked = magnet.Available;
        bool control=magnet.ControlAvailable;
        var sidebar = ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/Magnet/BarFive").Value;
        int barHeight=sidebar.Height+22*((unlocked?1:0)+(control?1:0));
        const int cap=4;
        batch.Draw(sidebar,WebBox(-21,0,21,cap),new Rectangle(0,0,21,cap),Color.White);
        batch.Draw(sidebar,WebBox(-21,cap,21,barHeight-2*cap),new Rectangle(0,cap,21,1),Color.White);
        batch.Draw(sidebar,WebBox(-21,barHeight-cap,21,cap),new Rectangle(0,sidebar.Height-cap,21,cap),Color.White);
        // Carry the open edge of the toolbar across the terminal bevel.
        batch.Draw(sidebar,WebBox(0,0,3,1),new Rectangle(20,0,1,1),Color.White);
        batch.Draw(sidebar,WebBox(0,1,3,1),new Rectangle(20,1,1,1),Color.White);
        batch.Draw(sidebar,WebBox(0,2,3,barHeight-6),new Rectangle(20,2,1,1),Color.White);
        string label=storage.Order switch {
            TerminalStorage.SortOrder.Quantity => TerminalText.Get("数量"), TerminalStorage.SortOrder.Name => "A–Z", _ => TerminalText.Get("最后放入")
        };
        SidebarButton("GuideButton",TerminalGuide.Instance.Title,"",0,()=>TerminalGuide.Instance.Show("index"),false);
        SidebarButton("Sort/"+storage.Order,TerminalText.Get("排序方式"),label,1,()=>{
            storage.Order=storage.Order switch {
                TerminalStorage.SortOrder.Recent => TerminalStorage.SortOrder.Quantity,
                TerminalStorage.SortOrder.Quantity => TerminalStorage.SortOrder.Name,
                _ => TerminalStorage.SortOrder.Recent
            };
        });
        SidebarButton(storage.Descending ? "Sort/Descending" : "Sort/Ascending",TerminalText.Get("排序方向"),storage.Descending ? TerminalText.Get("降序") : TerminalText.Get("升序"),
            2,storage.ToggleDirection);
        SidebarButton("SearchSettings/Settings",TerminalText.Get("终端设置"),"",3,openSettings);
        string lengthLabel=storage.Length switch {
            TerminalStorage.TerminalLength.Short => TerminalText.Get("短"), TerminalStorage.TerminalLength.Long => TerminalText.Get("长"), _ => TerminalText.Get("最长")
        };
        SidebarButton("Length/"+storage.Length,TerminalText.Get("终端长度"),lengthLabel,4,storage.CycleLength);
        if (unlocked)
            SidebarButton("Magnet/Button",TerminalText.Get("磁卡设置"),"",5,openMagnetSettings);
        if (control)
            SidebarButton("Magnet/MasterControlButton",TerminalText.Get("总控面板"),"",unlocked?6:5,openControl,false);

        bool jei=Main.LocalPlayer.GetModPlayer<TerminalSearchSettings>().JeiActive;
        var groups=storage.GetCells(null,jei?"":search);
        if (jei && ModContent.GetInstance<TerminalSystem>().JeiMatches is { } matches)
            groups.RemoveAll(cell=>!matches(cell.Type));
        var layout=new List<TerminalStorage.Cell?>();
        if (!groups.Any(c=>c.Type==detailType && c.Delivered==detailBatch && c.Variants))
            detailType=null;
        void AddGroup(TerminalStorage.Cell cell) {
            layout.Add(cell);
            if (cell.Type==detailType && cell.Delivered==detailBatch)
                layout.AddRange(storage.GetCells(cell.Type,"",cell.Delivered));
        }
        for (int i=0;i<pending.Count;i++) layout.Add(null);
        if (Main.LocalPlayer.GetModPlayer<TerminalSearchSettings>().PinCraft)
        foreach (int type in storage.LatestTypes) {
            var received=groups.Find(cell=>cell.Type==type && cell.Delivered);
            if (received!=null) { AddGroup(received); groups.Remove(received); }
        }
        while (layout.Count % Columns != 0) layout.Add(null);
        foreach (var cell in groups) AddGroup(cell);
        int maxRow=Math.Max(0,(layout.Count + Columns-1) / Columns - rows);
        if (ContainsPoint(Main.MouseScreen) && PlayerInput.ScrollWheelDeltaForUI!=0) {
            scrollRow-=Math.Sign(PlayerInput.ScrollWheelDeltaForUI);
        }
        scrollRow=Math.Clamp(scrollRow,0,maxRow);
        var track = new Rectangle((int)origin.X+Columns*Step+12,(int)origin.Y,22,rows*Step);
        int thumbHeight = TerminalScroll.Height(track,unit);
        int travel = track.Height-thumbHeight;
        if (!Main.mouseLeft) draggingScroll = false;
        if (maxRow>0 && travel>0 && !PlayerInput.IgnoreMouseInterface &&
            (draggingScroll || (Main.mouseLeft && Main.mouseLeftRelease && track.Contains(Main.MouseScreen.ToPoint()))))
        {
            Main.LocalPlayer.mouseInterface=true;
            Main.mouseLeftRelease=false;
            draggingScroll=true;
            scrollRow=Math.Clamp((int)Math.Round((Main.MouseScreen.Y-track.Top-thumbHeight/2f)*maxRow/travel),0,maxRow);
        }
        TerminalScroll.Draw(batch,track,unit,maxRow>0?(float)scrollRow/maxRow:0,maxRow>0);
        var gridBox=new Rectangle((int)origin.X,(int)origin.Y,Columns*Step,rows*Step);
        if (!PlayerInput.IgnoreMouseInterface && gridBox.Contains(Main.MouseScreen.ToPoint()) &&
            Main.mouseLeft && Main.mouseLeftRelease && !Main.mouseItem.IsAir)
        {
            Main.LocalPlayer.mouseInterface=true;
            Main.mouseLeftRelease=false;
            if(TeamClient.Open)TerminalDelivery.Drop(Main.LocalPlayer,Main.mouseItem,false);
            else storage.StoreLooseItem(Main.mouseItem);
            Main.mouseItem=new Item();
            TerminalCursor.Clear();
            Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab);
            Recipe.FindRecipes();
        }
        float prior=Main.inventoryScale;
        Main.inventoryScale=.85f;
        try {
            for(int r=0;r<rows;r++) for(int c=0;c<Columns;c++) {
                int i=(r+scrollRow)*Columns+c;
                var pos=origin+new Vector2(c*Step,r*Step);
                if (i < pending.Count) {
                    DrawPending(batch, pending[i], pos);
                    continue;
                }
                var cell=i<layout.Count ? layout[i] : null;
                if (cell==null && i<layout.Count) { TerminalSkin.Slot(batch,pos,Step); continue; }
                DrawCell(batch,storage,cell,pos);
            }
        } finally { Main.inventoryScale=prior; }
    }

    internal static string FormatStockCount(long count)
    {
        if (count <= 1000) return count.ToString();
        const string units = "KMGTPE";
        long divisor = 1000;
        int unit = 0;
        while (count / divisor >= 1000 && unit < units.Length - 1)
        {
            divisor *= 1000;
            unit++;
        }
        long whole = count / divisor;
        long tenth = count % divisor / (divisor / 10);
        return whole < 10 ? $"{whole}.{tenth}{units[unit]}" : $"{whole}{units[unit]}";
    }

    private static void DrawFitted(SpriteBatch batch,string text,Vector2 position,float width)
    {
        float scale=Math.Min(.7f,width / Math.Max(1,TerminalFont.Measure(text).X));
        TerminalFont.Draw(batch,text,position,Color.White,scale);
    }

    private void DrawPending(SpriteBatch batch, (int Type, long Count) order, Vector2 pos)
    {
        TerminalSkin.Slot(batch,pos,Step);
        float size = 52f * Main.inventoryScale;
        var preview = ContentSamples.ItemsByType[order.Type];
        ReceiptIcon.DrawPending(batch, preview, pos + new Vector2(size / 2));
        ReceiptIcon.Plus(batch, pos, size, topLeft: true);
        DrawFitted(batch, FormatStockCount(order.Count), pos + new Vector2(2, 23), size - 3);
        if (!EquipmentPane.IsHovered(pos, size)) return;
        Main.LocalPlayer.mouseInterface = true;
        TerminalTooltip.ShowItem(preview);
        TerminalCursor.HandleOccupiedCursor(preview);
        if (Main.mouseLeft && Main.mouseLeftRelease) {
            Main.mouseLeftRelease = false;
        }
    }

    private void DrawCell(SpriteBatch batch,TerminalStorage storage,TerminalStorage.Cell? cell,Vector2 pos)
    {
        float size=52f*Main.inventoryScale;
        bool hovered=EquipmentPane.IsHovered(pos,size);
        int index=cell?.Index ?? -1;
        // Empty visual cells all represent a legal new physical stack.
        if (hovered && index<0 && !TeamClient.Open) index=storage.EmptyCell();
        var item=index<0 ? null : storage.DisplaySlots[index];
        bool expanded=cell?.Variants==true && cell.Type==detailType && cell.Delivered==detailBatch;
        if(TeamClient.Open && hovered && item!=null){
            Main.LocalPlayer.mouseInterface=true;TerminalTooltip.ShowItem(item);
            if(Main.mouseLeft && Main.mouseLeftRelease && Main.mouseItem.IsAir){
                if(equipment.WantsTransfer && Main.LocalPlayer.GetModPlayer<TerminalReforgePlayer>().CanMove(item))
                    TeamClient.Shared(SharedAction.Reforge,index);
                else TeamClient.Action(TeamOp.Withdraw,TeamTab.Stock,index);
                Main.mouseLeftRelease=false;
            }
        }else if (hovered && item!=null) {
            Main.LocalPlayer.mouseInterface=true;
            bool click=Main.mouseLeft && Main.mouseLeftRelease;
            var inventory=storage.Slots;
            int before=item.IsAir ? 0 : item.stack;
            ItemSlot.OverrideHover(inventory,ItemSlot.Context.ChestItem,index);
            bool corner=Main.MouseScreen.X>=pos.X+size*2/3 && Main.MouseScreen.Y>=pos.Y+size*2/3;
            if (click && TerminalCursor.HandleOccupiedCursor(item,allowStack:true)) {
                storage.ReconcileDirectClick(index,before);
            } else if (ItemSlot.ControlInUse && Main.cursorOverride == CursorOverrideID.TrashCan) {
                ItemSlot.LeftClick(inventory,ItemSlot.Context.ChestItem,index);
                storage.ReconcileDirectClick(index,before);
            } else if ((cell?.Variants==true) && ((click && corner) || (Main.mouseRight && Main.mouseRightRelease && !consumedRight))) {
                detailType=expanded ? null : cell!.Type;
                detailBatch=cell!.Delivered;
                if (click) Main.mouseLeftRelease=false;
                if (Main.mouseRight) { consumedRight=true; Main.mouseRightRelease=false; }
            } else if (equipment.TryShiftStation(item)) {
                    Main.mouseLeftRelease=false;
            } else if (equipment.WantsTransfer && Main.LocalPlayer.GetModPlayer<TerminalReforgePlayer>().CanMove(item)) {
                    Main.LocalPlayer.GetModPlayer<TerminalReforgePlayer>().TryTransferStorage(storage,index);
                Main.mouseLeftRelease=false;
            } else if (click && ItemSlot.ShiftInUse) {
                    if (!PlayerLoader.ShiftClickSlot(Main.LocalPlayer,inventory,ItemSlot.Context.ChestItem,index))
                    storage.Withdraw(index);
                Main.mouseLeftRelease=false;
            } else {
                int heldBefore=TerminalCursor.HeldCount;
                if (!TerminalCursor.HandleOccupiedCursor(inventory[index], allowStack:true)) {
                    ItemSlot.LeftClick(inventory,ItemSlot.Context.ChestItem,index);
                    if (!consumedRight) ItemSlot.RightClick(inventory,ItemSlot.Context.ChestItem,index);
                }
                if (TerminalCursor.HeldCount > heldBefore)
                    TerminalCursor.RememberStorage(storage,index,storage.IsBatch(index),heldBefore);
                storage.ReconcileDirectClick(index,before);
            }
            TerminalTooltip.ShowItem(inventory[index], cell?.Variants == true ? Lang.GetItemNameValue(cell.Type) : null);
            if (Main.cursorOverride != CursorOverrideID.TrashCan) equipment.ShowTransferCursor(inventory[index]);
        }
        bool favorite=item is { IsAir:false, favorited:true };
        TerminalSkin.Slot(batch,pos,Step,favorite);
        if (cell==null || item==null || item.IsAir) return;
        var center = pos + new Vector2(size / 2);
        var tint=expanded ? new Color(194,194,194) : Color.White;
        ItemSlot.DrawItemIcon(item, ItemSlot.Context.ChestItem, batch, center, Main.inventoryScale, 32, tint);
        if (favorite) TerminalSkin.FavoriteLock(batch,pos,Step);
        if (storage.IsBatch(index)) storage.MarkDeliveryDisplayed();
        if (cell.Count>1)
            DrawFitted(batch,FormatStockCount(cell.Count),pos+new Vector2(2,23),cell.Variants ? size-14 : size-3);
        if (cell.Variants)
            ReceiptIcon.Plus(batch, pos, size, topLeft: false);
    }
}

// Paint the glow against the actual alpha silhouette so transparent pixels and the slot stay clear.
internal static class ReceiptIcon
{
    private static readonly Dictionary<(Texture2D, Rectangle), Texture2D> masks = new();
    private static int cachedPixels;

    public static void Clear()
    {
        foreach (var mask in masks.Values) mask.Dispose();
        masks.Clear();
        cachedPixels = 0;
    }

    public static void Plus(SpriteBatch batch, Vector2 pos, float size, bool topLeft)
    {
        int pixel = Math.Max(1, (int)Math.Round(size / 36f));
        int inset = Math.Max(2, (int)Math.Round(size * .06));
        int x = (int)pos.X + (topLeft ? inset : (int)size - inset - 6 * pixel);
        int y = (int)pos.Y + (topLeft ? inset : (int)size - inset - 6 * pixel);
        void Cross(int left, int top, Color color)
        {
            batch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(left + 2 * pixel, top, pixel, 5 * pixel), color);
            batch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(left, top + 2 * pixel, 5 * pixel, pixel), color);
        }
        Cross(x + pixel, y + pixel, new Color(64, 62, 83));
        Cross(x, y, new Color(252, 252, 252));
    }

    public static void DrawPending(SpriteBatch batch, Item item, Vector2 center)
    {
        float scale = ItemSlot.DrawItemIcon(item, ItemSlot.Context.ChestItem, batch, center,
            Main.inventoryScale, 32, Color.White);
        Main.GetItemDrawFrame(item.type, out var texture, out var frame);
        double seconds = Main.GlobalTimeWrappedHourly;
        float hue = (float)(seconds / 5.6 % 1);
        var rainbow = Main.hslToRgb(hue, .85f, .67f);
        float intensity = .58f + .20f * (float)Math.Sin(seconds * Math.PI);
        var mask = GetMask(texture, frame);
        if (mask != null)
            batch.Draw(mask, center, null, rainbow * intensity, 0, mask.Size() / 2, scale, SpriteEffects.None, 0);
        else
            batch.Draw(texture, center, frame, rainbow * (intensity * .45f), 0, frame.Size() / 2, scale, SpriteEffects.None, 0);
    }

    private static Texture2D? GetMask(Texture2D texture, Rectangle frame)
    {
        if (masks.TryGetValue((texture, frame), out var mask)) return mask;
        int width = frame.Width + 2, height = frame.Height + 2;
        if (texture.Format != SurfaceFormat.Color || width > 514 || height > 514 ||
            cachedPixels + width * height > 2_000_000) return null;
        var rgba = new Color[frame.Width * frame.Height];
        texture.GetData(0, frame, rgba, 0, rgba.Length);
        byte Alpha(int x, int y) => (uint)x < frame.Width && (uint)y < frame.Height ? rgba[y * frame.Width + x].A : (byte)0;
        var pixels = new Color[width * height];
        for (int y = -1; y <= frame.Height; y++)
            for (int x = -1; x <= frame.Width; x++)
            {
                byte inner = Alpha(x, y);
                int neighbor = Math.Max(Math.Max(Alpha(x - 1, y), Alpha(x + 1, y)),
                    Math.Max(Alpha(x, y - 1), Alpha(x, y + 1)));
                int alpha = Math.Max(inner / 3, neighbor - inner);
                pixels[(y + 1) * width + x + 1] = new Color(alpha, alpha, alpha, alpha);
            }
        mask = new Texture2D(texture.GraphicsDevice, width, height);
        mask.SetData(pixels);
        masks.Add((texture, frame), mask);
        cachedPixels += pixels.Length;
        return mask;
    }
}
