namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class InventoryCargoSendAllPacket : BasePacket
{
    public const int ItemCount = 312;
    public const byte DefaultCargoPageCount = 13;

    public override GameOpcode Opcode => GameOpcode.InventoryCargoSendAll;

    public byte InventorySize { get; set; } = DefaultCargoPageCount;
    public InventoryPacketItem[] Items { get; } = Enumerable.Range(0, ItemCount)
        .Select(_ => new InventoryPacketItem())
        .ToArray();

    public override void Write(BinaryWriter writer)
    {
        writer.Write(InventorySize);
        writer.BaseStream.Position += 3;

        for (var i = 0; i < ItemCount; i++)
            Items[i].Write(writer);
    }
}
