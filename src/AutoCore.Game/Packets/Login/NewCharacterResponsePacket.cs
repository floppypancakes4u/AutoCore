using System;
using System.IO;

namespace AutoCore.Game.Packets.Login
{
    using Constants;
    using Extensions;

    public class NewCharacterResponsePacket : BasePacket
    {
        public override GameOpcode Opcode => GameOpcode.LoginNewCharacterResponse;

        public uint Result { get; set; }
        public long NewCharCoid { get; set; }

        public NewCharacterResponsePacket(uint result, long coid)
        {
            Result = result;
            NewCharCoid = coid;
        }

        public override void Read(BinaryReader reader)
        {
            throw new NotImplementedException();
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(Result);
            writer.Write(NewCharCoid);
        }
    }
}
