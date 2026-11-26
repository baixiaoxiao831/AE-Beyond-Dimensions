using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;

namespace AEBeyondDimensions;

public enum ControllerShape { Large, Compact, Tower, Bridge, Halo }

public sealed class ControllerShapeElement : ConfigElement
{
    private static readonly ControllerShape[] shapes = Enum.GetValues<ControllerShape>();
    private readonly Texture2D[] previews = new Texture2D[shapes.Length];

    public override void OnBind()
    {
        base.OnBind();
        Height.Set(213, 0);
        foreach (var shape in shapes)
            previews[(int)shape] = ModContent.Request<Texture2D>(
                $"AEBeyondDimensions/Assets/Controller/Preview/{shape}",
                ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
        TextDisplayFunction = () => Label + " · " + NameOf((ControllerShape)GetObject());
    }

    private static string NameOf(ControllerShape shape) =>
        Language.GetTextValue("Mods.AEBeyondDimensions.ShapeNames." + shape);

    private Rectangle Card(int index)
    {
        var bounds = GetDimensions().ToRectangle();
        int width = (bounds.Width - 28) / 3;
        return new Rectangle(bounds.X + 8 + index % 3 * (width + 6),
            bounds.Y + 35 + index / 3 * 87, width, 81);
    }

    public override void LeftClick(UIMouseEvent evt)
    {
        base.LeftClick(evt);
        foreach (var shape in shapes)
            if (Card((int)shape).Contains(evt.MousePosition.ToPoint()) && !Equals(GetObject(), shape))
            {
                SetObject(shape);
                return;
            }
    }

    protected override void DrawSelf(SpriteBatch batch)
    {
        base.DrawSelf(batch);
        var selected = (ControllerShape)GetObject();
        foreach (var shape in shapes)
        {
            var box = Card((int)shape);
            if (box.Width < 12) continue;
            var pixel = TextureAssets.MagicPixel.Value;
            var border = shape == selected ? Color.Gold :
                box.Contains(Main.MouseScreen.ToPoint()) ? Color.White : new Color(90, 106, 151);
            batch.Draw(pixel, box, border);
            var inner = box;
            inner.Inflate(-2, -2);
            batch.Draw(pixel, inner, new Color(31, 41, 66));
            var image = previews[(int)shape];
            float scale = Math.Min(1f, Math.Min((box.Width - 12f) / image.Width, 50f / image.Height));
            batch.Draw(image, new Vector2(box.Center.X, box.Top + 30), null, Color.White, 0,
                new Vector2(image.Width, image.Height) / 2, scale, SpriteEffects.None, 0);
            string name = NameOf(shape);
            float labelScale = Math.Min(.72f, (box.Width - 8f) / FontAssets.MouseText.Value.MeasureString(name).X);
            Utils.DrawBorderString(batch, name, new Vector2(box.Center.X, box.Top + 58), Color.White, labelScale, .5f);
        }
    }
}
