using System;
using System.IO;

namespace AutoCore.Game.Packets.Login
{
    using Constants;
    using Extensions;

    public class LoginResponsePacket : BasePacket
    {
        public override GameOpcode Opcode => GameOpcode.LoginResponse;

        public uint Result { get; }

        public LoginResponsePacket(uint result)
        {
            Result = result;
        }

        public override void Read(BinaryReader reader)
        {
            throw new NotImplementedException();
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(Result);
        }
    }
}
