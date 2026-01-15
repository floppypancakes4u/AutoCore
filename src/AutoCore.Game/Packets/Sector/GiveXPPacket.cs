namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;

public class GiveXPPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.GiveXP;

    public int XP { get; set; }

    public override void Write(BinaryWriter writer)
    {
        // Many sector packets start with 4 reserved bytes.
        writer.BaseStream.Position += 4;

        writer.Write(XP);
    }
}


