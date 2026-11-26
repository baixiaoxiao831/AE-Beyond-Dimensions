using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace AEBeyondDimensions;

internal static class TerminalTooltip
{
    private static string? title, detail;

    internal static int HoveredType { get; private set; }
    private static bool itemStyle;
    private static Color titleColor;

    internal static Func<Item,string>? ItemDetails;

    public static void ShowItem(Item? item, string? name = null, string? context = null)
    {
        Main.HoverItem = new Item();
        Main.hoverItemName = "";
        if (!Main.mouseItem.IsAir || item == null || item.IsAir) return;
        Show(name ?? item.AffixName(), item.ModItem?.Mod.DisplayNameClean ?? "Terraria");
        string body = ItemDetails?.Invoke(item) ?? StandaloneDetails(item);
        if (!string.IsNullOrEmpty(body)) detail += "\n" + body;
        if (!string.IsNullOrEmpty(context)) detail += "\n" + context;
        HoveredType = item.type;
        TerminalGuide.Instance.Hover(item);
        if(item.ModItem is MagnetCard or ReforgeCard or ReforgeUpgradeCard or ShimmerCard && TerminalGuide.Instance.Hint.Length>0)
            detail+="\n"+TerminalGuide.Instance.Hint;
        titleColor = item.master || item.rare == ItemRarityID.Master ? new Color(255,(int)(Main.masterColor*200),0) :
            item.expert || item.rare == ItemRarityID.Expert ? Main.DiscoColor :
            item.rare >= ItemRarityID.Count ? RarityLoader.GetRarity(item.rare).RarityColor : ItemRarity.GetColor(item.rare);
        itemStyle = true;
    }

    private static string StandaloneDetails(Item source)
    {
        var keys = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        if (!keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift) && !keys.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift))
            return TerminalText.Get("按住 Shift 显示全部信息");
        var item = source.Clone();
        int count = 1, yoyo = -1, research = -1;
        var names = new string[30 + item.ToolTip.Lines];
        var text = new string[names.Length];
        var modifiers = new bool[names.Length];
        var bad = new bool[names.Length];
        Main.MouseText_DrawItemTooltip_GetLinesInfo(item, ref yoyo, ref research, item.knockBack, ref count, text, modifiers, bad, names, out int prefix);
        var lines = ItemLoader.ModifyTooltips(item, ref count, names, ref text, ref modifiers, ref bad, ref yoyo, out _, prefix);
        return string.Join("\n", lines.Where(line => line.Visible && line.Name is not ("ItemName" or "ShimmerResult"))
            .Select(line => string.Concat(ChatManager.ParseMessage(line.Text, Color.White).Select(s => s.Text))));
    }

    public static void BeginFrame()
    {
        title = detail = null;
        HoveredType = 0;
    }

    public static void Show(string? caption, string? description = null)
    {
        if (string.IsNullOrEmpty(caption))
            return;
        itemStyle = false;
        titleColor = new Color(252,252,252);
        title = caption;
        detail = string.IsNullOrEmpty(description) ? null : description;
        Main.hoverItemName = "";
        Main.mouseText = true;
    }

    public static void Draw(SpriteBatch batch, Rectangle viewport)
    {
        if (!Main.mouseItem.IsAir)
        {
            // Slot and mod draw hooks can also set vanilla's mouse text after ShowItem.
            Main.HoverItem = new Item();
            Main.hoverItemName = "";
            if (itemStyle) return;
        }
        if (title == null || viewport.Width < 32 || viewport.Height < 32)
            return;
        const int margin = 4, outer = 1, edge = 2, paddingX = 7, paddingY = 5, shadow = 2, gap = 12;
        var font = FontAssets.MouseText.Value;
        var heading = TerminalFont.Measure(title);
        var note = detail == null ? Vector2.Zero : TerminalFont.Measure(detail);
        float textWidth = Math.Max(1, Math.Max(heading.X, note.X));
        float textHeight = Math.Max(1, heading.Y + note.Y);
        int dividerSpace = detail == null ? 0 : 6;
        int availableWidth = viewport.Width - margin * 2 - shadow;
        int availableHeight = viewport.Height - margin * 2 - shadow;
        float scale = Math.Min(.8f, Math.Min((availableWidth - paddingX * 2) / textWidth,
            (availableHeight - paddingY * 2 - dividerSpace) / textHeight));
        heading=TerminalFont.Measure(title,scale);
        note=detail==null ? Vector2.Zero : TerminalFont.Measure(detail,scale);
        int width = Math.Min(availableWidth, (int)Math.Ceiling(Math.Max(heading.X,note.X)) + paddingX * 2);
        int height = Math.Min(availableHeight, (int)Math.Ceiling(heading.Y+note.Y) + paddingY * 2 + dividerSpace);
        var mouse = Main.MouseScreen.ToPoint();
        int x = mouse.X + gap, y = mouse.Y + gap;
        if (x + width + margin + shadow > viewport.Right) x = mouse.X - gap - width;
        if (y + height + margin + shadow > viewport.Bottom) y = mouse.Y - gap - height;
        x = Math.Clamp(x, viewport.Left + margin, viewport.Right - margin - shadow - width);
        y = Math.Clamp(y, viewport.Top + margin, viewport.Bottom - margin - shadow - height);

        var pixel = TextureAssets.MagicPixel.Value;
        void Fill(int left, int top, int w, int h, Color color) =>
            batch.Draw(pixel, new Rectangle(left, top, w, h), color);
        void Stroke(int inset, int thickness, Color top, Color bottom)
        {
            int w = width - 2 * inset, h = height - 2 * inset;
            Fill(x + inset, y + inset, w, thickness, top);
            Fill(x + inset, y + height - inset - thickness, w, thickness, bottom);
            var side = Color.Lerp(top, bottom, .5f);
            Fill(x + inset, y + inset + thickness, thickness, h - 2 * thickness, side);
            Fill(x + width - inset - thickness, y + inset + thickness, thickness, h - 2 * thickness, side);
        }
        var shade = Color.Black * .30f;
        Fill(x + width, y + shadow, shadow, height - shadow, shade);
        Fill(x + shadow, y + height, width, shadow, shade);
        var rim = new Color(16,16,18) * .93f;
        Stroke(0, outer, rim, rim);
        Stroke(outer, edge, itemStyle ? titleColor : Color.White, itemStyle ? titleColor : new Color(242,242,242));
        int inset = outer + edge;
        Fill(x + inset, y + inset, width - inset * 2, height - inset * 2, new Color(12,12,14) * .90f);
        void Text(string text, float measuredWidth, int top, Color color) =>
            TerminalFont.Draw(batch,text,
                new Vector2(x + (itemStyle ? paddingX : (int)Math.Round((width - measuredWidth) / 2f)), top),
                color,scale);
        Text(title, heading.X, y + paddingY, titleColor);
        if (detail == null) return;
        int lineY = y + paddingY + (int)Math.Ceiling(heading.Y) + 1;
        int lineWidth = width - 2 * (inset + 1);
        for (int i = 0; i < lineWidth; i++)
        {
            float position = i / (float)(lineWidth - 1);
            float opacity = position < .47f ? position / .47f : (1 - position) / .53f;
            Fill(x + inset + 1 + i, lineY, 1, 2, (itemStyle ? titleColor : new Color(249,249,249)) * opacity);
        }
        int top = lineY + dividerSpace - 1;
        int split = itemStyle ? detail.IndexOf('\n') : -1;
        if (split >= 0)
        {
            string owner = detail[..split];
            Text(owner, note.X, top, new Color(119,119,255));
            Text(detail[(split+1)..], note.X, top + (int)TerminalFont.Measure(owner,scale).Y, Color.LightGray);
        }
        else Text(detail, note.X, top, itemStyle ? new Color(119,119,255) : new Color(168,168,168));
    }
}
