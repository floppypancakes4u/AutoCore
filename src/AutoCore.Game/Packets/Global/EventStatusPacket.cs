namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

public class EventStatusPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.EventStatus;

    public int Id { get; set; }
    public int Status { get; set; }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(Id);
        writer.Write(Status);
    }
}
