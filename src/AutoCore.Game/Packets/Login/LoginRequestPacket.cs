using System;
using System.IO;

namespace AutoCore.Game.Packets.Login
{
    using Constants;
    using Utils.Extensions;

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

        public override void Write(BinaryWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
