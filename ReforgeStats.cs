using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

// Only compare matching descriptions. Additional effects, different units and
// unrecognised text stay in the signature and cannot silently disappear.
internal sealed record ReforgeStats(string Signature, decimal[] Values, string Description)
{
    private static readonly Regex Numbers = new(@"[+＋−\-]?(?:\d+(?:[.,]\d+)?)",RegexOptions.CultureInvariant);
    internal static ReforgeStats Read(List<TooltipLine> lines,HashSet<(string,string,string)> baseline)
    {
        var parts=new List<(string Key,decimal? Value)>();
        var description=new List<string>();
        foreach (var line in lines) {
            if (!line.Visible || line.Name=="ItemName" || string.IsNullOrWhiteSpace(line.Text)) continue;
            bool modifier=line.IsModifier || line.Name.StartsWith("Prefix",StringComparison.Ordinal);
            if (!modifier && (line.Mod=="Terraria" || baseline.Contains((line.Mod,line.Name,line.Text)))) continue;
            description.Add(line.Text);
            string key=line.Mod+"\u001f"+line.Name+"\u001f";
            var matches=Numbers.Matches(line.Text);
            if (matches.Count==1) {
                var number=matches[0];
                string normalized=number.Value.Replace('−','-').Replace('＋','+');
                bool signed=normalized[0] is '+' or '-';
                if (signed && decimal.TryParse(normalized.Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,out var value)) {
                    // A bad modifier can be displayed as a positive increase in a cost or delay.
                    parts.Add((key+line.Text.Remove(number.Index,number.Length).Insert(number.Index,"#"),
                        line.IsModifierBad ? -Math.Abs(value) : value));
                    continue;
                }
            }
            parts.Add((key+line.Text,null));
        }
        var sorted=parts.OrderBy(p=>p.Key,StringComparer.Ordinal).ThenBy(p=>p.Value).ToArray();
        return new ReforgeStats(string.Join("\u001e",sorted.Select(p=>p.Key)),
            sorted.Where(p=>p.Value.HasValue).Select(p=>p.Value!.Value).ToArray(),string.Join(" · ",description.Distinct()));
    }
    internal bool Beats(ReforgeStats other)
    {
        if (Values.Length==0 || Signature!=other.Signature || Values.Length!=other.Values.Length) return false;
        bool higher=false;
        for(int i=0;i<Values.Length;i++) {
            if(Values[i]<other.Values[i]) return false;
            higher |= Values[i]>other.Values[i];
        }
        return higher;
    }
}
