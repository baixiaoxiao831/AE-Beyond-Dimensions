using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

internal sealed class TerminalSearchData : ModSystem
{
    public override void Load() => BrowserSearch.Load(Mod);
    public override void PostSetupRecipes() => BrowserSearch.IndexCurrentNames();
    public override void OnLocalizationsLoaded() => BrowserSearch.IndexCurrentNames();
    public override void Unload() => BrowserSearch.Unload();
}

internal sealed class TerminalItemSearch
{
    private CatalogFilters? catalog;
    private string? culture, previous;
    private HashSet<int> matches = new();

    internal bool Matches(Item item,string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (query.IndexOfAny(['@','#','＠','＃','"','＂','*','＊','|','｜','-','－']) < 0)
            return BrowserSearch.Matches(item.Name,query);
        if (catalog == null || culture != Language.ActiveCulture.Name)
        {
            culture = Language.ActiveCulture.Name;
            catalog = new CatalogFilters(ContentSamples.ItemsByType.Keys.Where(id => id > 0).ToArray());
            previous = null;
        }
        if (query != previous)
        {
            matches = catalog.Filter(catalog.OrderedItems,query).ToHashSet();
            previous = query;
        }
        return matches.Contains(item.type);
    }
}
