using System;
using Terraria;
using Terraria.ModLoader;

namespace AEBeyondDimensions;

public sealed class TerminalDimensionContext : ModSystem
{
    private bool worldLoaded;

    public override void OnWorldLoad() => worldLoaded = true;
    public override void OnWorldUnload() => worldLoaded = false;

    internal (int Version, Guid CurrentWorldId, string DimensionId, bool SupportsCrossDimensionAccess) Read()
    {
        Guid world = worldLoaded ? Main.ActiveWorldFileData?.UniqueId ?? Guid.Empty : Guid.Empty;
        // An unregistered dimension has no identifier. The save GUID alone cannot identify a subworld.
        return (1, world, string.Empty, false);
    }
}
