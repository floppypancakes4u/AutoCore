namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

public class DestroyObjectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.DestroyObject;

    // Client appears to use a 4-byte prefix on some object interaction packets (e.g. ItemPickup).
    // Keep this field explicit so we can adjust once the exact meaning is confirmed.
    public int UnknownField { get; set; } = 0;
    public TFID ObjectId { get; set; }

    public DestroyObjectPacket()
    {
    }

    public DestroyObjectPacket(TFID objectId)
    {
        ObjectId = objectId;
    }

    public override void Write(BinaryWriter writer)
    {
        // Observed pattern: 4 bytes unknown + TFID (16 bytes)
        writer.Write(UnknownField);
        writer.WriteTFID(ObjectId);
    }
}

