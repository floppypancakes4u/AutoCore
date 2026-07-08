namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;

public sealed class InventoryGrabResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryGrabResponse;

    public long ItemCoid { get; set; }
    public bool ItemGlobal { get; set; } = true;
    public byte InventoryType { get; set; } = 1;
    public int Quantity { get; set; } = 1;
    public bool AddToExistingItem { get; set; }
    public int InventoryPositionX { get; set; }
    public int InventoryPositionY { get; set; }
    public bool WasSuccessful { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;
        writer.WriteTFID(ItemCoid, ItemGlobal);
        writer.Write(InventoryType);
        writer.BaseStream.Position += 3;
        writer.Write(Quantity);
        writer.Write(AddToExistingItem);
        writer.BaseStream.Position += 7;
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.BaseStream.Position += 8;
        writer.Write(WasSuccessful);
        writer.BaseStream.Position += 3;
    }
}
