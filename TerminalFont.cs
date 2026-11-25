using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria.GameContent;

namespace AEBeyondDimensions;

internal static class TerminalFont
{
    // Keep one terminal text line within the original 20-unit row at base scale.
    private static float TextScale(float scale) => scale * 20f / FontAssets.MouseText.Value.LineSpacing;

    internal static Vector2 Measure(string text, float scale = 1) =>
        FontAssets.MouseText.Value.MeasureString(text) * TextScale(scale);

    internal static void Draw(SpriteBatch batch, string text, Vector2 at, Color color,
        float scale = 1, float anchorX = 0, float anchorY = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        var font = FontAssets.MouseText.Value;
        var origin = anchorX == 0 && anchorY == 0
            ? Vector2.Zero : font.MeasureString(text) * new Vector2(anchorX, anchorY);
        batch.DrawString(font, text, at, color, 0, origin, TextScale(scale), SpriteEffects.None, 0);
    }
}
