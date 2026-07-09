namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public sealed class InventoryDestroyItemPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryDestroyItem;

    public byte[] RawBytes { get; private set; } = [];
    public long ItemCoid { get; private set; } = -1;
    public bool ItemGlobal { get; private set; }

    public override void Read(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var start = stream.Position - sizeof(uint);
        var remaining = Math.Max(0, stream.Length - start);

        stream.Position = start;
        RawBytes = reader.ReadBytes((int)remaining);

        if (RawBytes.Length >= 16)
            ItemCoid = BitConverter.ToInt64(RawBytes, 8);

        if (RawBytes.Length > 0x10)
            ItemGlobal = RawBytes[0x10] != 0;
    }

    public ReadOnlySpan<byte> TailBytes => RawBytes.Length > 0x11
        ? RawBytes.AsSpan(0x11)
        : ReadOnlySpan<byte>.Empty;

    public IEnumerable<long> EnumerateInt64Candidates()
    {
        for (var offset = sizeof(uint); offset <= RawBytes.Length - sizeof(long); offset++)
        {
            var value = BitConverter.ToInt64(RawBytes, offset);
            if (value > 0)
                yield return value;
        }
    }

    public IEnumerable<int> EnumerateInt32Candidates()
    {
        for (var offset = sizeof(uint); offset <= RawBytes.Length - sizeof(int); offset++)
        {
            var value = BitConverter.ToInt32(RawBytes, offset);
            if (value > 0)
                yield return value;
        }
    }
}
