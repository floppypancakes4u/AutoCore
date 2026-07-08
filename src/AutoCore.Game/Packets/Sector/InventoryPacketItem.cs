namespace AutoCore.Game.Packets.Sector;

public sealed class InventoryPacketItem
{
    public long ItemCoid { get; set; } = -1;
    public byte PositionX { get; set; }
    public byte PositionY { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(ItemCoid);
        writer.Write(PositionX);
        writer.Write(PositionY);
        writer.BaseStream.Position += 6;
    }
}
