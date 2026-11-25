using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AEBeyondDimensions;

public sealed class TerminalSearchSettings : ModPlayer
{
    public bool Remember { get; private set; }
    public bool AutoFocus { get; private set; } = true;
    internal bool UseJei,ClearJei,Sync;
    internal bool HideCoins,HideAmmo,SwapSideColumns;
    internal bool ShowsColumn(bool coins) => coins ? !HideCoins : !HideAmmo;
    internal bool JeiAvailable => ModContent.GetInstance<TerminalSystem>().ReadJei != null;
    internal bool JeiActive => UseJei && JeiAvailable;
    internal bool PinCraft { get; private set; } = true;
    public bool DeliverToBag { get; internal set; }
    internal bool ShowMissingList = true;
    internal bool LootChestToTerminal { get; set; }
    public bool UseEquipment { get; internal set; }
    internal bool CompactTree,TreeMissingDefault,TreeScreenshotCounts=true;
    internal string CurrentQuery { get; set; } = "";

    public void Set(bool remember, bool autoFocus)
    {
        Remember = remember;
        AutoFocus = autoFocus;
    }

    public override void SaveData(TagCompound tag)
    {
        tag["useJei"]=UseJei;tag["clearJei"]=ClearJei;tag["syncJei"]=Sync;
        tag["hideCoins"]=HideCoins;
        tag["hideAmmo"]=HideAmmo;
        tag["swapSideColumns"]=SwapSideColumns;
        tag["pinCraft"]=PinCraft;
        tag["rememberSearch"] = Remember;
        tag["deliverToBag"] = DeliverToBag;
        tag["showMissingList"] = ShowMissingList;
        tag["lootChestToTerminal"]=LootChestToTerminal;
        tag["useEquipment"] = UseEquipment;
        tag["compactTree"]=CompactTree;tag["treeMissingDefault"]=TreeMissingDefault;tag["treeScreenshotCounts"]=TreeScreenshotCounts;
        tag["autoFocusSearch"] = AutoFocus;
        if (Remember) tag["searchText"] = CurrentQuery;
    }

    public override void LoadData(TagCompound tag)
    {
        Set(tag.GetBool("rememberSearch"),!tag.ContainsKey("autoFocusSearch") || tag.GetBool("autoFocusSearch"));
        UseJei=tag.GetBool("useJei");ClearJei=tag.GetBool("clearJei");Sync=tag.GetBool("syncJei");
        HideCoins=tag.GetBool("hideCoins");
        HideAmmo=tag.GetBool("hideAmmo");
        SwapSideColumns=tag.GetBool("swapSideColumns");
        PinCraft=!tag.ContainsKey("pinCraft") || tag.GetBool("pinCraft");
        DeliverToBag = tag.GetBool("deliverToBag");
        ShowMissingList = !tag.ContainsKey("showMissingList") || tag.GetBool("showMissingList");
        LootChestToTerminal=tag.GetBool("lootChestToTerminal");
        UseEquipment = tag.GetBool("useEquipment");
        CompactTree=tag.GetBool("compactTree");TreeMissingDefault=tag.GetBool("treeMissingDefault");
        TreeScreenshotCounts=!tag.ContainsKey("treeScreenshotCounts") || tag.GetBool("treeScreenshotCounts");
        CurrentQuery = Remember ? tag.GetString("searchText") : "";
    }

    internal bool Draw(SpriteBatch batch, Rectangle viewport, TerminalMagnet? magnet = null)
    {
        int height = magnet != null ? 256 : JeiActive ? 381 : 445;
        float scale = Math.Min(1f, Math.Min((viewport.Width-8)/396f,(viewport.Height-8)/height));
        int left = viewport.Center.X - (int)(198*scale), top = viewport.Center.Y - (int)(height*scale/2);
        Rectangle Box(int x,int y,int w,int h) => new(left+(int)(x*scale),top+(int)(y*scale),
            Math.Max(1,(int)(w*scale)),Math.Max(1,(int)(h*scale)));
        var pixel = TextureAssets.MagicPixel.Value;
        var ink = new Color(64,62,83);
        void Fill(int x,int y,int w,int h,Color color) => batch.Draw(pixel,Box(x,y,w,h),color);
        void Label(int x,int y,string label,bool enabled=true)
        {
            var font=FontAssets.MouseText.Value;
            float labelScale=Math.Min(.85f,(396-x-12)/Math.Max(1f,TerminalFont.Measure(label).X));
            TerminalFont.Draw(batch,label,new Vector2(left+x*scale,top+y*scale),
                enabled ? ink : new Color(100,99,117),scale*labelScale);
        }
        bool Pressed(Rectangle area)
        {
            if (PlayerInput.IgnoreMouseInterface || !area.Contains(Main.MouseScreen.ToPoint()) ||
                !Main.mouseLeft || !Main.mouseLeftRelease) return false;
            Main.mouseLeftRelease=false;
            TerminalSounds.PlayClick();
            return true;
        }
        void Option(int x,int y,string label,bool on,bool radio,bool enabled)
        {
            string asset = radio ? (on ? "RadioOn" : "RadioOff") : (on ? "On" : "Off");
            var icon=Mod.Assets.Request<Texture2D>("Assets/SearchSettings/"+asset).Value;
            batch.Draw(icon,Box(x,y,icon.Width*2,icon.Height*2),Color.White);
            Label(x+icon.Width*2+6,y+2,label,enabled);
        }
        batch.Draw(pixel,viewport,Color.Black*.53f);
        Fill(0,0,396,height,ink);
        Fill(2,2,392,height-4,new Color(242,242,242));
        Fill(4,4,390,height-6,new Color(135,143,165));
        Fill(4,4,387,height-9,new Color(203,204,212));
        Label(14,10,magnet == null ? TerminalText.Get("AE 终端设置") : TerminalText.Get("磁卡设置"));
        var back = Box(348,-10,40,40);
        bool backHovered = !PlayerInput.IgnoreMouseInterface && back.Contains(Main.MouseScreen.ToPoint());
        var backIcon = Mod.Assets.Request<Texture2D>("Assets/SearchSettings/" + (backHovered ? "BackHover" : "Back")).Value;
        batch.Draw(backIcon,back,Color.White);
        if (magnet != null)
        {
            Option(18,46,TerminalText.Get("开启磁力"),magnet.MagnetEnabled,false,true);
            Label(18,88,TerminalText.Get("拾取物品存储方式"));
            Option(18,116,TerminalText.Get("直接存入终端"),magnet.Route == TerminalMagnet.PickupMode.Direct,true,true);
            Option(18,148,TerminalText.Get("背包优先，剩余物品存入终端"),magnet.Route == TerminalMagnet.PickupMode.Overflow,true,true);
            Option(18,180,TerminalText.Get("拾取同类物品时直接存入终端"),magnet.Route == TerminalMagnet.PickupMode.Matching,true,true);
            if (Pressed(Box(14,42,365,34))) magnet.MagnetEnabled = !magnet.MagnetEnabled;
            if (Pressed(Box(14,114,365,32))) magnet.Route = TerminalMagnet.PickupMode.Direct;
            if (Pressed(Box(14,146,365,32))) magnet.Route = TerminalMagnet.PickupMode.Overflow;
            if (Pressed(Box(14,178,365,32))) magnet.Route = TerminalMagnet.PickupMode.Matching;
            Option(18,216,TerminalText.Get("宝箱强夺全部时存入终端"),LootChestToTerminal,false,true);
            if (Pressed(Box(14,212,370,32))) LootChestToTerminal=!LootChestToTerminal;
            return !Pressed(back);
        }
        Option(18,42,TerminalText.Get("置顶自动合成得到的物品"),PinCraft,false,true);
        Option(18,74,TerminalText.Get("下单产品直达背包"),DeliverToBag,false,true);
        Option(18,106,TerminalText.Get("允许调用已装备物品"),UseEquipment,false,true);
        if (Pressed(Box(14,38,370,32))) PinCraft=!PinCraft;
        if (Pressed(Box(14,70,370,32))) DeliverToBag=!DeliverToBag;
        if (Pressed(Box(14,102,370,32))) UseEquipment=!UseEquipment;
        Option(18,138,TerminalText.Get("不显示钱币栏"),HideCoins,false,true);
        Option(18,170,TerminalText.Get("不显示弹药栏"),HideAmmo,false,true);
        Option(18,202,TerminalText.Get("对调钱币栏与弹药栏位置"),SwapSideColumns,false,true);
        if (Pressed(Box(14,134,370,32))) HideCoins=!HideCoins;
        if (Pressed(Box(14,166,370,32))) HideAmmo=!HideAmmo;
        if (Pressed(Box(14,198,370,32))) SwapSideColumns=!SwapSideColumns;
        Label(14,236,TerminalText.Get("搜索框设置"));
        bool jei=JeiActive;
        Option(18,262,TerminalText.Get("使用 AE"),!jei,true,true);
        Option(190,262,TerminalText.Get("使用 JEI"),jei,true,JeiAvailable);
        if (Pressed(Box(14,260,165,32))) UseJei=false;
        if (JeiAvailable && Pressed(Box(186,260,192,32))) UseJei=true;
        if (jei)
        {
            Option(18,305,TerminalText.Get("打开时清除 JEI 搜索框内容"),ClearJei,false,true);
            if (Pressed(Box(14,301,370,32))) ClearJei=!ClearJei;
        }
        else
        {
            Option(18,305,TerminalText.Get("记住上次进行的搜索"),Remember,false,true);
            Option(18,337,TerminalText.Get("打开时自动聚焦搜索框"),AutoFocus,false,true);
            Option(18,369,TerminalText.Get("与 JEI 搜索框内容同步"),Sync,false,JeiAvailable);
            if (Pressed(Box(14,301,370,32))) Set(!Remember,AutoFocus);
            if (Pressed(Box(14,333,370,32))) Set(Remember,!AutoFocus);
            if (JeiAvailable && Pressed(Box(14,365,370,32))) {
                Sync=!Sync;
                if(Sync) ModContent.GetInstance<TerminalSystem>().WriteJei?.Invoke(CurrentQuery);
            }
        }
        int checklistY = jei ? 337 : 401;
        Option(18,checklistY,TerminalText.Get("显示缺失材料清单"),ShowMissingList,false,true);
        if (Pressed(Box(14,checklistY-4,370,32))) ShowMissingList=!ShowMissingList;
        return !Pressed(back);
    }
}
