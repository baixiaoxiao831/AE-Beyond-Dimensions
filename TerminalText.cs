using System.Collections.Generic;
using System.Text.Json;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

internal static class TerminalText
{
    private static Dictionary<string,string>? english;
    internal static string Get(string chinese)
    {
        if (Language.ActiveCulture.Name.StartsWith("zh")) return chinese;
        english ??= JsonSerializer.Deserialize<Dictionary<string,string>>(
            ModContent.GetInstance<ProbeMod>().GetFileBytes("Assets/TerminalText.json"))!;
        return english.GetValueOrDefault(chinese, chinese);
    }
}
