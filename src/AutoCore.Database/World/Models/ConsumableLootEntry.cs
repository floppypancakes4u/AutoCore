namespace AutoCore.Database.World.Models;

/// <summary>
/// Consumable eligible for death loot from <c>wad.xml</c> <c>tConsumables</c>.
/// </summary>
public class ConsumableLootEntry
{
    public int Cbid { get; set; }
    public short LevelMin { get; set; }
    public short LevelMax { get; set; }
    /// <summary>Relative weight (<c>intOffset</c> in wad).</summary>
    public int Offset { get; set; }
}
