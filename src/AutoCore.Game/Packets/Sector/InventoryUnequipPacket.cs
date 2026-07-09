namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

/// <summary>
/// Server → client InventoryUnequip (0x203E), 48-byte layout.
/// Matches Documentation/PACKET STRUCTURES.md and client FUN_00813bf0 /
/// VehicleNet_PostCorrectionEvent's synthesized unequip payload.
/// </summary>
public sealed class InventoryUnequipPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryUnequip;

    public TFID ItemId { get; set; } = new();

    /// <summary>
    /// Ghidra: fidVehicleSentFrom. Ghost-synthesized unequips copy the vehicle object's TFID here.
    /// </summary>
    public TFID VehicleId { get; set; } = new();

    public byte InventoryPositionX { get; set; }
    public byte InventoryPositionY { get; set; }

    /// <summary>
    /// eVOG_INVENTORY_TYPE_HARDPOINT = 2. Ghost-synthesized unequips use this.
    /// </summary>
    public byte InventoryType { get; set; } = 2;

    public override void Write(BinaryWriter writer)
    {
        // Opcode already written by SendGamePacket. Layout is relative to message start:
        // +0x04 padding, +0x08 item TFID, +0x18 vehicle TFID, +0x28 x/y/type.
        writer.BaseStream.Position += 4;
        writer.WriteTFID(ItemId);
        writer.WriteTFID(VehicleId);
        writer.Write(InventoryPositionX);
        writer.Write(InventoryPositionY);
        writer.Write(InventoryType);
        writer.BaseStream.Position += 5;
    }
}
