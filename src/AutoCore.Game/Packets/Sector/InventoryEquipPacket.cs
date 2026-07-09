namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Server → client InventoryEquip (0x203C), 64-byte layout.
/// Matches Documentation/PACKET STRUCTURES.md SMSG_Sector_InventoryEquip and
/// client FUN_00813f40 / VehicleNet_PostCorrectionEvent equip synthesis.
/// </summary>
public sealed class InventoryEquipPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryEquip;

    public TFID ItemId { get; set; } = new();
    public TFID VehicleId { get; set; } = new();
    public TFID OldItemId { get; set; } = new(-1, false);
    public bool PutInHand { get; set; }
    public byte InventoryPositionX { get; set; }
    public byte InventoryPositionY { get; set; }

    /// <summary>
    /// Source inventory type before equip. Cargo→hardpoint uses CARGO (1).
    /// Ghost-synthesized equips often use HARDPOINT (2).
    /// </summary>
    public byte InventoryTypeFrom { get; set; } = 1;

    public override void Write(BinaryWriter writer)
    {
        // Opcode already written by SendGamePacket.
        // +0x04 padding, +0x08 item, +0x18 vehicle, +0x28 old item,
        // +0x38 putInHand, +0x39 x, +0x3a y, +0x3b typeFrom.
        writer.BaseStream.Position += 4;
        writer.WriteTFID(ItemId);
        writer.WriteTFID(VehicleId);
        writer.WriteTFID(OldItemId);
        writer.Write(PutInHand);
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.Write(InventoryTypeFrom);
        writer.BaseStream.Position += 4;
    }
}
