namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// Server→client objective progress (opcode 0x2071).
/// Client Client_RecvObjectiveState @ 0x0080ff00 / FUN_00809460 style layout (opcode at +0):
///   +0x10 bitmask, +0x14 objective id (lookup key), +0x18..+0x24 four per-slot progress floats.
/// </summary>
public class ObjectiveStatePacket : BasePacket
{
    public const int BitmaskOffset = 16;
    public const int ObjectiveIdOffset = 20;
    public const int SlotFloatsOffset = 24;
    public const int SlotCount = 4;
    public const int PayloadLength = SlotFloatsOffset + SlotCount * 4;

    public override GameOpcode Opcode => GameOpcode.ObjectiveState;

    public uint ObjectiveBitmask { get; set; }
    /// <summary>Objective id used as client mission-hash lookup key (not always mission id).</summary>
    public int ObjectiveId { get; set; }
    public float[] SlotProgress { get; } = new float[SlotCount];

    public override void Write(BinaryWriter writer)
    {
        writer.BaseStream.Position = BitmaskOffset;
        writer.Write(ObjectiveBitmask);
        writer.Write(ObjectiveId);
        for (var i = 0; i < SlotCount; i++)
            writer.Write(SlotProgress[i]);

        writer.BaseStream.Position = Math.Max(writer.BaseStream.Position, PayloadLength);
    }
}
