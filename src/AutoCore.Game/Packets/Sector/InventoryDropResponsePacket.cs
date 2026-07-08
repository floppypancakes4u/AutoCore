namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;

public sealed class InventoryDropResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryDropResponse;

    public long ItemCoid { get; set; }
    public bool ItemGlobal { get; set; } = true;
    public byte InventoryPositionX { get; set; }
    public byte InventoryPositionY { get; set; }
    public byte InventoryType { get; set; } = 1;
    public bool WasSuccessful { get; set; }
    public bool HasSwappedOrConcatenatedItem { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;
        writer.WriteTFID(ItemCoid, ItemGlobal);
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.Write(InventoryType);
        writer.BaseStream.Position += 7;
        writer.Write(WasSuccessful);
        writer.Write(HasSwappedOrConcatenatedItem);
    }
}
