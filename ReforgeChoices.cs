using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

internal sealed class ReforgeChoices
{
    internal static bool Enabled => Main.LocalPlayer.GetModPlayer<TerminalMagnet>().SelectPrefix;
    private Item? original;
    internal sealed record Option(int Id,string Name,int Quote,ReforgeStats Stats);
    private List<Option> entries = new();
    private int first, lastFirst;
    private float? drag;
    private static int Height(string text) => Math.Clamp(8+20*(1+text.Count(c=>c=='\n')),28,244);
    private static string Label(Option option,bool accessory) =>
        option.Name+(accessory && option.Stats.Description.Length>0 ? "  "+option.Stats.Description : "");
    internal bool Open => original != null;
    internal void Close() => original = null;

    internal static Item? Candidate(Item item,int prefix)
    {
        if (prefix <= 0 || prefix >= PrefixLoader.PrefixCount || PrefixLoader.GetPrefix(prefix) is Terraria.ModLoader.Default.UnloadedPrefix) return null;
        var copy = item.Clone();
        copy.ResetPrefix();
        return copy.CanApplyPrefix(prefix) && copy.Prefix(prefix) && copy.prefix == prefix ? copy : null;
    }

    private static List<TooltipLine> Tooltip(Item item)
    {
        int length = 30 + (item.ToolTip?.Lines ?? 0), count = 1, logo = -1, research = -1, index = 0;
        var names = new string[length];
        var text = new string[length];
        var modifiers = new bool[length];
        var negative = new bool[length];
        Color?[] colors = Array.Empty<Color?>();
        var previous = Main.HoverItem;
        try {
            Main.HoverItem = item;
            Main.MouseText_DrawItemTooltip_GetLinesInfo(item,ref logo,ref research,item.knockBack,ref count,
                text,modifiers,negative,names,out index);
            return ItemLoader.ModifyTooltips(item,ref count,names,ref text,ref modifiers,ref negative,ref logo,out colors,index);
        } finally { Main.HoverItem = previous; }
    }

    internal static string Effect(Item item)
    {
        if (!item.accessory || item.prefix == 0) return "";
        var plain = item.Clone();
        plain.ResetPrefix();
        var baseline = Tooltip(plain).Select(l=>(l.Mod,l.Name,l.Text)).ToHashSet();
        var stats=ReforgeStats.Read(Tooltip(item),baseline);
        return stats.Description;
    }

    internal static List<Option> Options(Item item,IEnumerable<int> ids)
    {
        var plain=item.Clone();
        plain.ResetPrefix();
        var baseline=Tooltip(plain).Select(l=>(l.Mod,l.Name,l.Text)).ToHashSet();
        var options=new List<Option>();
        foreach (int id in ids) {
            var candidate=Candidate(item,id);
            if(candidate==null) continue;
            var stats=ReforgeStats.Read(Tooltip(candidate),baseline);
            options.Add(new(id,Lang.prefix[id].Value,TerminalReforgePlayer.Quote(Main.LocalPlayer,candidate),stats));
        }
        return options.GroupBy(o=>o.Stats.Signature)
            .SelectMany(group=>group.Where(o=>!group.Any(peer=>peer.Stats.Beats(o.Stats))))
            .OrderByDescending(o=>o.Quote).ThenBy(o=>o.Name,StringComparer.CurrentCulture).ThenBy(o=>o.Id).ToList();
    }

    internal void Show(Item item)
    {
        entries=Options(item,Enumerable.Range(1,PrefixLoader.PrefixCount-1));
        original=item;
        first=0;
        drag=null;
        lastFirst=entries.Count;
        int used=0;
        while(lastFirst>0) {
            int height=Height(Label(entries[lastFirst-1],item.accessory))+2;
            if(used+height>280)break;
            used+=height;
            lastFirst--;
        }
    }

    internal void Draw(SpriteBatch batch,Rectangle viewport)
    {
        var forge=Main.LocalPlayer.GetModPlayer<TerminalReforgePlayer>();
        if (!Enabled || original!=forge.ReforgeItem) { Close(); return; }
        float scale=Math.Min(1f,Math.Min(viewport.Width/520f,viewport.Height/350f));
        var origin=viewport.Center.ToVector2()-new Vector2(250,165)*scale;
        Rectangle Box(int x,int y,int w,int h)=>new((int)(origin.X+x*scale),(int)(origin.Y+y*scale),(int)(w*scale),(int)(h*scale));

        void Text(string s,Rectangle r,float maxScale=.8f,Color? ink=null) {
            float width=TerminalFont.Measure(s).X;
            float k=Math.Min(scale*maxScale,Math.Min((r.Width-8*scale)/Math.Max(1,width),(r.Height-4*scale)/Math.Max(1,TerminalFont.Measure(s).Y)));
            TerminalFont.Draw(batch,s,new Vector2(r.X+4*scale,r.Center.Y),ink ?? TerminalSkin.Ink,k,0,.5f);
        }
        TerminalSkin.Panel(batch,Box(0,0,500,330));
        var icon=Box(10,7,28,28);
        TerminalSkin.Slot(batch,icon.Location.ToVector2(),icon.Width);
        Terraria.UI.ItemSlot.DrawItemIcon(original!,Terraria.UI.ItemSlot.Context.ChestItem,batch,
            icon.Center.ToVector2(),icon.Width/36f,32,Color.White);
        Text(TerminalText.Get("自选重铸　　本次费用 ")+Main.ValueToCoins(forge.Price),Box(42,5,358,32));
        var back=Box(458,4,34,34);
        bool backHover=back.Contains(Main.MouseScreen.ToPoint()) && !PlayerInput.IgnoreMouseInterface;
        batch.Draw(ModContent.Request<Texture2D>("AEBeyondDimensions/Assets/SearchSettings/"+(backHover?"BackHover":"Back")).Value,back,Color.White);
        if(backHover && Main.mouseLeft && Main.mouseLeftRelease) {
            Main.mouseLeftRelease=false; TerminalSounds.PlayClick(); Close(); return;
        }
        if(lastFirst>0){
            var track=Box(477,43,13,278);
            float unit=track.Width/7f;
            int thumb=TerminalScroll.Height(track,unit), travel=Math.Max(1,track.Height-thumb);
            if(!PlayerInput.IgnoreMouseInterface && Box(0,0,500,330).Contains(Main.MouseScreen.ToPoint()))
                first=Math.Clamp(first-Math.Sign(PlayerInput.ScrollWheelDeltaForUI),0,lastFirst);
            if(!Main.mouseLeft) drag=null;
            if(!PlayerInput.IgnoreMouseInterface && track.Contains(Main.MouseScreen.ToPoint()) && Main.mouseLeft && Main.mouseLeftRelease){
                float offset=Main.MouseScreen.Y-track.Y-travel*first/(float)lastFirst;
                drag=offset>=0 && offset<thumb?offset:thumb/2f;
                Main.mouseLeftRelease=false;
            }
            if(drag.HasValue) first=(int)Math.Round(lastFirst*Math.Clamp((Main.MouseScreen.Y-track.Y-drag.Value)/travel,0,1));
            TerminalScroll.Draw(batch,track,unit,first/(float)lastFirst,true);
        }
        int y=43;
        for(int i=first;i<entries.Count;i++) {
            var option=entries[i];
            string label=Label(option,original!.accessory);
            int height=Height(label);
            if(y+height>323)break;
            var row=Box(10,y,lastFirst>0?464:480,height);
            bool hover=row.Contains(Main.MouseScreen.ToPoint()) && !PlayerInput.IgnoreMouseInterface;
            bool enabled=option.Id!=original.prefix && Main.LocalPlayer.CanAfford(forge.Price);
            var face=option.Id==original.prefix ? new Color(184,204,224) :
                enabled && hover ? new Color(222,232,247) : new Color(196,199,212);
            TerminalSkin.Inset(batch,row,face);
            Text(label,Box(10,y,320,height));
            Text(Main.ValueToCoins(option.Quote),Box(332,y,lastFirst>0?142:158,height),.74f,new Color(76,62,42));
            if (hover && enabled && Main.mouseLeft && Main.mouseLeftRelease) {
                Main.mouseLeftRelease=false;
                if(forge.TryChoose(original,option.Id)) { Close(); return; }
            }
            y+=height+2;
        }
    }
}
