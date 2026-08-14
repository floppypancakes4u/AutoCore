namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;

/// <summary>
/// S2C InventoryGrabResponse (0x2035).
/// Client struct <c>SMSG_Sector_InventoryGrab_Response</c> (size 0x40):
/// +0x08 fidItem, +0x18 ucTypeFrom, +0x1c lQuantity, +0x20 bNewItem,
/// +0x28 fidNewItem (TFID), +0x38 bWasSuccessful.
/// <see cref="AddToExistingItem"/> maps to bNewItem (stack-split creating a new COID).
/// When bNewItem is false, the +0x28 region is unused by the client; AutoCore may echo
/// cargo slot ints there for diagnostics.
/// </summary>
public sealed class InventoryGrabResponsePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryGrabResponse;

    public long ItemCoid { get; set; }
    public bool ItemGlobal { get; set; } = true;
    public byte InventoryType { get; set; } = 1;
    public int Quantity { get; set; } = 1;
    /// <summary>Client bNewItem — true only when splitting a stack onto a new COID.</summary>
    public bool AddToExistingItem { get; set; }
    /// <summary>Legacy slot echo into fidNewItem region when <see cref="AddToExistingItem"/> is false.</summary>
    public int InventoryPositionX { get; set; }
    /// <summary>Legacy slot echo into fidNewItem region when <see cref="AddToExistingItem"/> is false.</summary>
    public int InventoryPositionY { get; set; }
    /// <summary>New stack COID written as fidNewItem when <see cref="AddToExistingItem"/> is true.</summary>
    public long NewItemCoid { get; set; } = -1;
    public bool NewItemGlobal { get; set; } = true;
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

        if (AddToExistingItem)
        {
            writer.WriteTFID(NewItemCoid, NewItemGlobal);
        }
        else
        {
            writer.Write(InventoryPositionX);
            writer.Write(InventoryPositionY);
            writer.BaseStream.Position += 8;
        }

        writer.Write(WasSuccessful);
        writer.BaseStream.Position += 7; // pad to struct size 0x40
    }
}
