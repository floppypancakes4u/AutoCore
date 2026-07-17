namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>
/// C2S Mass Move grab (<see cref="GameOpcode.InventoryGrabMM"/> = 0x2038).
/// Same field layout as <see cref="InventoryGrabPacket"/> for grid sources
/// (TFID @+0x8, global @+0x10, ucTypeFrom @+0x18, quantity @+0x1c).
/// Client shares GrabResponse handler for 0x2035; 0x2039 early-outs — respond with normal GrabResponse.
/// </summary>
public sealed class InventoryGrabMMPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryGrabMM;

    public byte[] RawBytes { get; private set; } = [];
    public long ItemCoid { get; private set; } = -1;
    public bool ItemGlobal { get; private set; }
    public byte InventoryType { get; private set; } = 1;
    public int Quantity { get; private set; } = 1;

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
            InventoryType = RawBytes[0x18] == 0 ? (byte)1 : RawBytes[0x18];

        if (RawBytes.Length >= 0x20)
            Quantity = Math.Max(1, BitConverter.ToInt32(RawBytes, 0x1c));
    }

    /// <summary>Build a regular grab packet so <see cref="Inventory.InventoryManager.Grab"/> can run unchanged.</summary>
    public InventoryGrabPacket ToGrabPacket()
    {
        var bytes = new byte[Math.Max(0x20, RawBytes.Length)];
        if (RawBytes.Length > 0)
            Buffer.BlockCopy(RawBytes, 0, bytes, 0, RawBytes.Length);

        // Force regular grab opcode so any logging/helpers see InventoryGrab.
        BitConverter.GetBytes((uint)GameOpcode.InventoryGrab).CopyTo(bytes, 0);
        if (bytes.Length >= 16)
            BitConverter.GetBytes(ItemCoid).CopyTo(bytes, 8);
        if (bytes.Length > 0x10)
            bytes[0x10] = (byte)(ItemGlobal ? 1 : 0);
        if (bytes.Length > 0x18)
            bytes[0x18] = InventoryType;
        if (bytes.Length >= 0x20)
            BitConverter.GetBytes(Quantity).CopyTo(bytes, 0x1c);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();
        var grab = new InventoryGrabPacket();
        grab.Read(reader);
        return grab;
    }
}
