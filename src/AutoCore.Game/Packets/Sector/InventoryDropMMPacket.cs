namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// C2S Mass Move drop (<see cref="GameOpcode.InventoryDropMM"/> = 0x203A).
/// Same placement fields as <see cref="InventoryDropPacket"/> (X/Y/typeTo @+0x18/+0x19/+0x1a).
/// Client DropResponse handler early-outs on 0x203B — respond with normal DropResponse (0x2037).
/// </summary>
public sealed class InventoryDropMMPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryDropMM;

    public byte[] RawBytes { get; private set; } = [];
    public long ItemCoid { get; private set; } = -1;
    public bool ItemGlobal { get; private set; }
    public byte InventoryPositionX { get; private set; } = byte.MaxValue;
    public byte InventoryPositionY { get; private set; } = byte.MaxValue;
    public byte InventoryType { get; private set; }

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

        if (RawBytes.Length > 0x18)
            InventoryPositionX = RawBytes[0x18];

        if (RawBytes.Length > 0x19)
            InventoryPositionY = RawBytes[0x19];

        if (RawBytes.Length > 0x1a)
            InventoryType = RawBytes[0x1a];
    }

    /// <summary>Build a regular drop packet so <see cref="Inventory.InventoryManager.Drop"/> can run unchanged.</summary>
    public InventoryDropPacket ToDropPacket()
    {
        var bytes = new byte[Math.Max(0x20, RawBytes.Length)];
        if (RawBytes.Length > 0)
            Buffer.BlockCopy(RawBytes, 0, bytes, 0, RawBytes.Length);

        BitConverter.GetBytes((uint)GameOpcode.InventoryDrop).CopyTo(bytes, 0);
        if (bytes.Length >= 16)
            BitConverter.GetBytes(ItemCoid).CopyTo(bytes, 8);
        if (bytes.Length > 0x10)
            bytes[0x10] = (byte)(ItemGlobal ? 1 : 0);
        if (bytes.Length > 0x18)
            bytes[0x18] = InventoryPositionX;
        if (bytes.Length > 0x19)
            bytes[0x19] = InventoryPositionY;
        if (bytes.Length > 0x1a)
            bytes[0x1a] = InventoryType;

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();
        var drop = new InventoryDropPacket();
        drop.Read(reader);
        return drop;
    }


    public ReadOnlySpan<byte> TailBytes => RawBytes.Length > 0x1b
        ? RawBytes.AsSpan(0x1b)
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
