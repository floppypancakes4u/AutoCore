namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class CreateVehicleExtendedPacket : CreateVehiclePacket
{
    public override GameOpcode Opcode => GameOpcode.CreateVehicleExtended;

    public short NumInventorySlots { get; set; }
    public ushort InventorySize { get; set; }
    public long[] InventoryCoids { get; } = new long[512];

    public override void Read(BinaryReader reader)
    {
        throw new NotImplementedException();
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);

        writer.Write(NumInventorySlots);
        writer.Write(InventorySize);
        
        for (var i = 0; i < 512; ++i)
        {
            writer.Write(InventoryCoids[i]);
        }
    }
}
