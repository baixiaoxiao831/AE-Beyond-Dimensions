using Terraria;

namespace AEBeyondDimensions;

internal static class TerminalSearchKeyboard
{
    internal static string ReadText(string current) =>
        Main.GetInputText(current).Replace('＠', '@').Replace('＃', '#');
}
