namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public sealed class InventoryDropPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.InventoryDrop;

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
}
