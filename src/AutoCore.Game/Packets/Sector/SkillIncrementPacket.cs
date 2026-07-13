namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

/// <summary>Client skill-rank request (0x2059). Raw body is retained until the retail layout is confirmed.</summary>
public sealed class SkillIncrementPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.SkillIncrement;
    public int? SkillId { get; private set; }
    public byte[] RawBody { get; private set; } = Array.Empty<byte>();
    public override void Read(BinaryReader reader)
    {
        RawBody = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
        if (RawBody.Length >= 4)
            SkillId = BitConverter.ToInt32(RawBody, RawBody.Length - 4);
    }
}
