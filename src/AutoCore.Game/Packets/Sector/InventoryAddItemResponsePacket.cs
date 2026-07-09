namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class InventoryAddItemResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryAddItemResponse;

    public long ItemCoid { get; set; }
    public byte InventoryPositionX { get; set; }
    public byte InventoryPositionY { get; set; }
    public bool AddToExistingItem { get; set; }
    public int Quantity { get; set; } = 1;
    public bool WasSuccessful { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;
        writer.Write(ItemCoid);
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.Write(AddToExistingItem);
        writer.BaseStream.Position += 1;
        writer.Write(Quantity);
        writer.Write(WasSuccessful);
        writer.BaseStream.Position += 7;
    }
}
