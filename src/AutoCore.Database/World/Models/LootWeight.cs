namespace AutoCore.Database.World.Models;

/// <summary>
/// Fixed junk drop from <c>wad.xml</c> <c>tLootWeights</c>:
/// destroying <see cref="DestroyedCbid"/> can yield <see cref="LootCbid"/> with relative weight.
/// </summary>
public class LootWeight
{
    public int DestroyedCbid { get; set; }
    public int LootCbid { get; set; }
    public short Weight { get; set; }
}
