namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

public sealed class ItemDropResponsePacket : BasePacket
{
    public const int MinimumLength = 0x31;

    public override GameOpcode Opcode => GameOpcode.ItemDropResponse;

    /// <summary>
    /// Echoed from the request. Ghidra: client builds this from the dragged item field at +0x58,
    /// not the vehicle COID (FUN_00921890).
    /// </summary>
    public int SourceObjectId { get; set; }

    /// <summary>
    /// Cargo/dragged item COID echoed from the request (FUN_008136b0 resolves this at +0x8/+0xc
    /// and destroys the inventory object via FUN_009440e0; not the spawned world loot COID).
    /// </summary>
    public long ItemCoid { get; set; }
    public Vector3 DropPosition { get; set; }
    public long TailValue { get; set; }
    public bool WasSuccessful { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(SourceObjectId);
        writer.Write(ItemCoid);
        writer.Write(DropPosition.X);
        writer.Write(DropPosition.Y);
        writer.Write(DropPosition.Z);
        writer.BaseStream.Position = 0x1C;
        writer.BaseStream.Position += 12;
        writer.Write(TailValue);
        writer.BaseStream.Position = 0x30;
        writer.Write(WasSuccessful);
        writer.BaseStream.Position = MinimumLength;
    }
}
