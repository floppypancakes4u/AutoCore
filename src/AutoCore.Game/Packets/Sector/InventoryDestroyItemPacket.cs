namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// Inventory destroy item (0x2049).
/// C2S: client toss/destroy request (Read captures raw + identity).
/// S2C: <c>SMSG_Sector_InventoryDestroyItem</c> size 0x18 —
/// pad4 + coidItem@+0x08 + quantity@+0x10 + bDelete@+0x14 (Documentation/PACKET STRUCTURES.md).
/// <c>bDelete=true</c> removes the item object from client inventory/mission UI.
/// </summary>
public sealed class InventoryDestroyItemPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryDestroyItem;

    public byte[] RawBytes { get; private set; } = [];
    public long ItemCoid { get; set; } = -1;
    public bool ItemGlobal { get; set; }
    public int Quantity { get; set; } = 1;
    /// <summary>S2C: when true, client deletes the item object (not just cargo grid).</summary>
    public bool Delete { get; set; } = true;

    public InventoryDestroyItemPacket()
    {
    }

    public InventoryDestroyItemPacket(long itemCoid, int quantity = 1, bool delete = true, bool itemGlobal = true)
    {
        ItemCoid = itemCoid;
        Quantity = quantity;
        Delete = delete;
        ItemGlobal = itemGlobal;
    }

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

        if (RawBytes.Length >= 0x14)
            Quantity = BitConverter.ToInt32(RawBytes, 0x10);

        if (RawBytes.Length > 0x14)
            Delete = RawBytes[0x14] != 0;
    }

    public override void Write(BinaryWriter writer)
    {
        // Absolute layout: opcode already written; pad to coid @ +0x08.
        writer.BaseStream.Position += 4;
        writer.Write(ItemCoid);
        writer.Write(Quantity);
        writer.Write(Delete);
        writer.Write(new byte[3]); // pad to size 0x18
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
