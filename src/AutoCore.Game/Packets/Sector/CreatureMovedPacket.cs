namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

public class CreatureMovedPacket : ObjectMovedPacket
{
    public override GameOpcode Opcode => GameOpcode.CreatureMoved;

    public TFID Target { get; set; }

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);

        Target = reader.ReadTFID();
    }

    public override void Write(BinaryWriter writer)
    {
        throw new NotSupportedException();
    }
}
