namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

public sealed class ItemDropPacket : BasePacket
{
    public const int MinimumLength = 0x30;

    public override GameOpcode Opcode => GameOpcode.ItemDrop;

    public byte[] RawBytes { get; private set; } = [];
    /// <summary>
    /// Echoed context id from the dragged inventory item (client item field +0x58), not vehicle COID.
    /// </summary>
    public int SourceObjectId { get; private set; }
    public long ItemCoid { get; private set; }
    public Vector3 DropPosition { get; private set; }
    public long TailValue { get; private set; }

    public override void Read(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var start = stream.Position - sizeof(uint);
        var remaining = Math.Max(0, stream.Length - start);

        stream.Position = start;
        RawBytes = reader.ReadBytes((int)remaining);

        if (RawBytes.Length >= 8)
            SourceObjectId = BitConverter.ToInt32(RawBytes, 4);

        if (RawBytes.Length >= 16)
            ItemCoid = BitConverter.ToInt64(RawBytes, 8);

        if (RawBytes.Length >= 0x1C)
        {
            DropPosition = new Vector3(
                BitConverter.ToSingle(RawBytes, 0x10),
                BitConverter.ToSingle(RawBytes, 0x14),
                BitConverter.ToSingle(RawBytes, 0x18));
        }

        if (RawBytes.Length >= 0x30)
            TailValue = BitConverter.ToInt64(RawBytes, 0x28);
    }
}
