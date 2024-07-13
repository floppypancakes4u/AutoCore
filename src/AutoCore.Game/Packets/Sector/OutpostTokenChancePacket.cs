namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class OutpostTokenChancePacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.OutpostTokenChance;

    public float Chance { get; set; }

    public override void Write(BinaryWriter writer) => writer.Write(Chance);
}
