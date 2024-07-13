namespace AutoCore.Game.Packets.Login;

using AutoCore.Game.Constants;
using AutoCore.Utils.Extensions;

public class LoginRequestPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.LoginRequest;
    public string Username { get; set; }
    public string Password { get; set; }
    public uint UserId { get; set; }
    public uint AuthKey { get; set; }

    public override void Read(BinaryReader reader)
    {
        Username = reader.ReadUTF8StringOn(33);
        Password = reader.ReadUTF8StringOn(33);

        reader.BaseStream.Position += 2;

        UserId = reader.ReadUInt32();
        AuthKey = reader.ReadUInt32();
    }
}
