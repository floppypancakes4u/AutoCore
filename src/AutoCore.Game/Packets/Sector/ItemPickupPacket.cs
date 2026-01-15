namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

public class ItemPickupPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.ItemPickup;

    public int UnknownField { get; set; }
    public TFID ItemId { get; set; }

    public override void Read(BinaryReader reader)
    {
        // Packet format: 4 bytes unknown + 16 bytes TFID
        UnknownField = reader.ReadInt32();
        ItemId = reader.ReadTFID();
    }
}

