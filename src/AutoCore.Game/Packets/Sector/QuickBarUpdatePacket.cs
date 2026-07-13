namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// Client → server quick-bar slot update (opcode <see cref="GameOpcode.QuickBarUpdate"/> = 0x2062).
/// Retail body is 12 bytes after the opcode (total wire size 0x10 including opcode).
/// Layout (Ghidra FUN_00826720 / FUN_00897170): slot byte, isItem byte, 2-byte pad, int64 value.
/// </summary>
public sealed class QuickBarUpdatePacket : BasePacket
{
    public const int BodyLength = 12;

    public override GameOpcode Opcode => GameOpcode.QuickBarUpdate;

    public byte Slot { get; private set; }
    public bool IsItem { get; private set; }
    public long Value { get; private set; }
    public bool IsValid { get; private set; }

    /// <summary>Skill id to apply when <see cref="IsItem"/> is false; negative values normalize to 0 (clear).</summary>
    public int SkillId => IsItem ? 0 : (Value < 0 ? 0 : (int)Value);

    /// <summary>Item COID to apply when <see cref="IsItem"/> is true; otherwise -1 (empty).</summary>
    public long ItemCoid => IsItem ? Value : -1L;

    public byte[] RawBody { get; private set; } = Array.Empty<byte>();

    public override void Read(BinaryReader reader)
    {
        var remaining = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
        if (remaining < BodyLength)
        {
            RawBody = remaining > 0 ? reader.ReadBytes(remaining) : Array.Empty<byte>();
            IsValid = false;
            return;
        }

        var start = reader.BaseStream.Position;
        Slot = reader.ReadByte();
        IsItem = reader.ReadByte() != 0;
        reader.ReadUInt16(); // client leaves these two bytes uninitialized
        Value = reader.ReadInt64();
        IsValid = true;

        reader.BaseStream.Position = start;
        RawBody = reader.ReadBytes(BodyLength);
    }
}
