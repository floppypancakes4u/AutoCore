namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class MapInfoPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.MapInfo;

    public uint SecurityKey { get; set; }
    public ulong CharacterCoid { get; set; }

    public override void Read(BinaryReader reader)
    {
        SecurityKey = reader.ReadUInt32();
        CharacterCoid = reader.ReadUInt64();
    }

    public override void Write(BinaryWriter writer)
    {
        writer.Write(SecurityKey);
        writer.Write(CharacterCoid);
    }
}
