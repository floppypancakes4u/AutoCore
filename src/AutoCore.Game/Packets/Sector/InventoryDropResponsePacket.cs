namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;

/// <summary>
/// S2C InventoryDropResponse (0x2037).
/// Client struct <c>SMSG_Sector_InventoryDrop_Response</c> (size 0x40):
/// +0x08 fidItem, +0x18 x/y/typeTo, +0x1c lQuantity, +0x20 uiUsesLeft,
/// +0x22 bWasSuccessful, +0x23 bSwapped, +0x28 fidSwapItem, +0x38 bConcatinate.
/// </summary>
public sealed class InventoryDropResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryDropResponse;

    public long ItemCoid { get; set; }
    public bool ItemGlobal { get; set; } = true;
    public byte InventoryPositionX { get; set; }
    public byte InventoryPositionY { get; set; }
    public byte InventoryType { get; set; } = 1;
    public int Quantity { get; set; } = 1;
    public ushort UsesLeft { get; set; }
    public bool WasSuccessful { get; set; }
    public bool HasSwappedOrConcatenatedItem { get; set; }
    public long SwapItemCoid { get; set; } = -1;
    public bool SwapItemGlobal { get; set; }
    public bool Concatenate { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position += 4;
        writer.WriteTFID(ItemCoid, ItemGlobal);
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.Write(InventoryType);
        writer.Write((byte)0); // align to +0x1c
        writer.Write(Quantity);
        writer.Write(UsesLeft);
        writer.Write(WasSuccessful);
        writer.Write(HasSwappedOrConcatenatedItem);
        writer.BaseStream.Position += 4; // pad to +0x28
        writer.WriteTFID(SwapItemCoid, SwapItemGlobal);
        writer.Write(Concatenate);
        writer.BaseStream.Position += 7; // pad to struct size 0x40
    }
}
