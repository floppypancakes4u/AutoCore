namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class TransferFromGlobalStage3Packet : TransferFromGlobalPacket
{
    public override GameOpcode Opcode => GameOpcode.TransferFromGlobalStage3;

    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);

        PositionX = reader.ReadSingle();
        PositionY = reader.ReadSingle();
        PositionZ = reader.ReadSingle();

        reader.BaseStream.Position += 4;
    }

    public override void Write(BinaryWriter writer)
    {
        base.Write(writer);

        writer.Write(PositionX);
        writer.Write(PositionY);
        writer.Write(PositionZ);

        writer.BaseStream.Position += 4;
    }
}
