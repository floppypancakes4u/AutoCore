namespace AutoCore.Game.Packets.Global;

using AutoCore.Game.Constants;

public class DisconnectPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.Disconnect;

    public bool Intentional { get; set; }

    public override void Read(BinaryReader reader)
    {
        Intentional = reader.ReadBoolean();

        reader.BaseStream.Position += 3;
    }
}
