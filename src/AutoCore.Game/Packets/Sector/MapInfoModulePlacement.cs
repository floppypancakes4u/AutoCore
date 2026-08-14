namespace AutoCore.Game.Packets.Sector;

/// <summary>
/// One 24-byte MapInfo module overlay. Client FUN_00637990 reads 0xC0 bits;
/// SetMapInfo 0x004CE230 copies six uint32s to map+0x950.
/// </summary>
public struct MapInfoModulePlacement
{
    public int PlacementCoidLow { get; set; }
    public int PlacementCoidHigh { get; set; }
    public int RebaseCoidLow { get; set; }
    public int RebaseCoidHigh { get; set; }
    public int ModuleId { get; set; }
    public int Unknown14 { get; set; }
}
