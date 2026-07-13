namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;

public class InventoryCargoSendAllPacket : BasePacket
{
    /// <summary>Fixed wire array length (6×13×4 pages). See <see cref="VehicleCargoCapacity"/>.</summary>
    public const int ItemCount = VehicleCargoCapacity.MaxWireSlotCount;
    /// <summary>Default UI page count for a 1-page starter chassis (Callisto X).</summary>
    public const byte DefaultCargoPageCount = 1;

    public override GameOpcode Opcode => GameOpcode.InventoryCargoSendAll;

    public byte InventorySize { get; set; } = DefaultCargoPageCount;
    public InventoryPacketItem[] Items { get; } = Enumerable.Range(0, ItemCount)
        .Select(_ => new InventoryPacketItem())
        .ToArray();

    public override void Write(BinaryWriter writer)
    {
        writer.Write(InventorySize);
        writer.BaseStream.Position += 3;

        for (var i = 0; i < ItemCount; i++)
            Items[i].Write(writer);
    }
}
