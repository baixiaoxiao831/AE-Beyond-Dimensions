namespace AEBeyondDimensions;

public sealed class TowerControllerTile : ControllerTileBase
{
    protected override int Columns => 6;
    protected override int Rows => 12;
    protected override string Art => "Tower";
    protected override bool HasTopModule(int moduleX) => moduleX == 1;
}

public sealed class BridgeControllerTile : ControllerTileBase
{
    protected override int Columns => 14;
    protected override int Rows => 6;
    protected override string Art => "Bridge";
    protected override bool HasTopModule(int moduleX) => moduleX is 0 or 6;
}

public sealed class HaloControllerTile : ControllerTileBase
{
    protected override int Columns => 10;
    protected override int Rows => 10;
    protected override string Art => "Halo";
    protected override bool HasTopModule(int moduleX) => moduleX is >= 1 and <= 3;
}
