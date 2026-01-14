namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class UpdateFirstTimeFlagsRequestPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.UpdateFirstTimeFlagsRequest;

    public uint FirstFlags1 { get; set; }
    public uint FirstFlags2 { get; set; }
    public uint FirstFlags3 { get; set; }
    public uint FirstFlags4 { get; set; }

    public override void Read(BinaryReader reader)
    {
        // Based on CreateCharacterExtendedPacket pattern: 4 uint values
        FirstFlags1 = reader.ReadUInt32();
        FirstFlags2 = reader.ReadUInt32();
        FirstFlags3 = reader.ReadUInt32();
        FirstFlags4 = reader.ReadUInt32();
    }
}






