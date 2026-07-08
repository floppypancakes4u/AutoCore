namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public sealed class InventoryGrabPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryGrab;

    public byte[] RawBytes { get; private set; } = [];
    public long ItemCoid { get; private set; } = -1;
    public bool ItemGlobal { get; private set; }
    public byte InventoryType { get; private set; } = 1;
    public int Quantity { get; private set; } = 1;
    public byte RequestedInventoryPositionX { get; private set; }
    public byte RequestedInventoryPositionY { get; private set; }
    public bool HasRequestedInventoryPosition { get; private set; }

    public override void Read(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var start = stream.Position - sizeof(uint);
        var remaining = Math.Max(0, stream.Length - start);

        stream.Position = start;
        RawBytes = reader.ReadBytes((int)remaining);

        if (RawBytes.Length > 0x10)
            ItemGlobal = RawBytes[0x10] != 0;

        if (RawBytes.Length > 0x18)
            InventoryType = RawBytes[0x18] == 0 ? (byte)1 : RawBytes[0x18];

        if (RawBytes.Length >= 16)
            ItemCoid = BitConverter.ToInt64(RawBytes, 8);

        if (RawBytes.Length >= 0x20)
            Quantity = Math.Max(1, BitConverter.ToInt32(RawBytes, 0x1c));

        // Provisional, based on Client_RecvInventoryGrab reading response offsets
        // +0x28/+0x2c. Live capture will verify whether requests mirror this.
        if (RawBytes.Length >= 0x30)
        {
            var x = BitConverter.ToInt32(RawBytes, 0x28);
            var y = BitConverter.ToInt32(RawBytes, 0x2c);
            if (x is >= 0 and < 24 && y is >= 0 and < 13)
            {
                RequestedInventoryPositionX = (byte)x;
                RequestedInventoryPositionY = (byte)y;
                HasRequestedInventoryPosition = true;
            }
        }
    }

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
