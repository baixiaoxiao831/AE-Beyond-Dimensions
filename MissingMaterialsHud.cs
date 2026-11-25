using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace AEBeyondDimensions;

// Use the actual buff positions so the checklist follows wrapped rows and UI scale.
internal sealed class ChecklistBuffAnchor : GlobalBuff
{
    public override void PostDraw(SpriteBatch batch,int type,int buffIndex,BuffDrawParams drawParams)
    {
        var hud=ModContent.GetInstance<MissingMaterialsHud>();
        var bounds=drawParams.MouseRectangle;
        if (hud.TrackBuffs && bounds.Left<MissingMaterialsHud.Left+MissingMaterialsHud.Width &&
            bounds.Right>MissingMaterialsHud.Left)
            hud.BuffBottom=Math.Max(hud.BuffBottom,bounds.Bottom+14);
    }
}

internal sealed class MissingMaterialsHud : ModSystem
{
    internal bool TrackBuffs;
    internal int BuffBottom;
    internal const int Left=20, Width=260;
    private int inventoryBottom;

    public override void Load()
    {
        if (!Main.dedServ) On_ItemSlot.Draw_SpriteBatch_ItemArray_int_int_Vector2_Color+=TrackSlot;
    }

    private void TrackSlot(On_ItemSlot.orig_Draw_SpriteBatch_ItemArray_int_int_Vector2_Color original,
        SpriteBatch batch,Item[] items,int context,int slot,Vector2 position,Color color)
    {
        if (TrackBuffs && Main.playerInventory &&
            context is ItemSlot.Context.InventoryItem or ItemSlot.Context.TrashItem or ItemSlot.Context.ChestItem
                or ItemSlot.Context.BankItem or ItemSlot.Context.ShopItem or ItemSlot.Context.VoidItem &&
            (context==ItemSlot.Context.TrashItem || (position.X<Left+Width && position.X+52*Main.inventoryScale>Left)))
            inventoryBottom=Math.Max(inventoryBottom,(int)Math.Ceiling(position.Y+
                TextureAssets.InventoryBack.Height()*Main.inventoryScale));
        original(batch,items,context,slot,position,color);
    }
    private int offset;
    private readonly record struct Row(int Type,string Text,Color Tint,bool Heading);

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        TrackBuffs=false;
        if (Main.gameMenu || Main.ingameOptionsWindow || Main.LocalPlayer.dead ||
            ModContent.GetInstance<TerminalSystem>().CapturesMovement ||
            !Main.LocalPlayer.GetModPlayer<TerminalSearchSettings>().ShowMissingList) return;
        var orders=Main.LocalPlayer.GetModPlayer<TerminalOrders>();
        if (!orders.HasWaiting) { offset=0; return; }
        BuffBottom=76;
        // Reserve the action row even when a mod replaces the vanilla inventory layer.
        inventoryBottom=Main.playerInventory ? Main.instance.invBottom+46 : 0;
        TrackBuffs=true;
        int pos=layers.FindIndex(layer=>layer.Name=="Vanilla: Cursor");
        layers.Insert(pos<0?layers.Count:pos,new LegacyGameInterfaceLayer("AEBeyondDimensions: Missing materials",
            ()=>Draw(orders),InterfaceScaleType.UI));
    }

    private bool Draw(TerminalOrders orders)
    {
        var rows=new List<Row>();
        foreach (var group in orders.MissingGroups)
        {
            if (group.Type<=0) continue;
            Color tint=Main.hslToRgb(Convert.ToInt32(group.Ticket.Substring(0,4),16)/65536f,.56f,.25f);
            rows.Add(new(group.Type,$"{Lang.GetItemNameValue(group.Type)} ×{group.Quantity}",tint,true));
            foreach (var (type,count) in group.Missing)
                rows.Add(new(type,$"{Lang.GetItemNameValue(type)} ×{count}",tint,false));
            if (group.Missing.Length==0) rows.Add(new(0,TerminalText.Get("等待合成条件"),tint,false));
        }
        if(rows.Count(row=>row.Heading)>1){
            var total=orders.MissingGroups.SelectMany(group=>group.Missing).GroupBy(m=>m.Type)
                .Select(g=>(Type:g.Key,Count:g.Sum(m=>(long)m.Count))).OrderByDescending(m=>m.Count).ThenBy(m=>m.Type).ToArray();
            if(total.Length>0){
                var tint=new Color(57,60,77);
                rows.Add(new(0,TerminalText.Get("合计缺少"),tint,true));
                foreach(var m in total)rows.Add(new(m.Type,$"{Lang.GetItemNameValue(m.Type)} ×{m.Count}",tint,false));
            }
        }
        if (rows.Count==0) return true;
        const int x=Left, line=24;
        var screen=Main.instance.GraphicsDevice.Viewport;
        int screenH=(int)(screen.Height/Main.UIScale), screenW=(int)(screen.Width/Main.UIScale);
        int y=Math.Max(BuffBottom+4,inventoryBottom+8), width=Math.Min(Width,screenW-x-6);
        int capacity=Math.Min(14,(screenH-y-8)/line);
        if (capacity<1 || width<64) return true;
        bool overflow=rows.Count>capacity;
        int visible=overflow ? Math.Max(1,capacity-1) : rows.Count;
        offset=Math.Clamp(offset,0,rows.Count-visible);
        var batch=Main.spriteBatch;
        if (overflow && new Rectangle(x,y,width,capacity*line).Contains(Main.MouseScreen.ToPoint()))
        {
            PlayerInput.LockVanillaMouseScroll("AE: Missing materials");
            offset=Math.Clamp(offset-Math.Sign(PlayerInput.ScrollWheelDeltaForUI),0,rows.Count-visible);
        }
        for (int i=0;i<visible;i++)
        {
            var row=rows[offset+i];
            int top=y+i*line;
            batch.Draw(TextureAssets.MagicPixel.Value,new Rectangle(x,top,width,line-(row.Heading?1:0)),
                row.Tint*(row.Heading?.90f:.72f));
            int indent=row.Heading?5:15;
            if (row.Type>0)
                ItemSlot.DrawItemIcon(ContentSamples.ItemsByType[row.Type],ItemSlot.Context.ChestItem,batch,
                    new Vector2(x+indent+9,top+12),1,18,Color.White);
            int textX=x+indent+23;
            float scale=Math.Min(.78f,(width-indent-28)/Math.Max(1f,TerminalFont.Measure(row.Text).X));
            Utils.DrawBorderString(batch,row.Text,new Vector2(textX,top+2),Color.White,scale);
        }
        if (overflow && capacity>1)
        {
            TerminalScroll.Draw(batch,new Rectangle(x+width-5,y,7,visible*line),1,(float)offset/(rows.Count-visible),true);
            Utils.DrawBorderString(batch,$"{offset+1}–{offset+visible}/{rows.Count}  {TerminalText.Get("滚轮翻页")}",
                new Vector2(x+4,y+visible*line),Color.White,.72f);
        }
        return true;
    }
}
