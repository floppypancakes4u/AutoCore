using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Packets.Login
{
    using Constants;
    using Extensions;
    

    public class NewCharacterResponsePacket : BasePacket
    {
        public override GameOpcode Opcode => GameOpcode.LoginNewCharacterResponse;

        public long NewCharCoid { get; set; }

        public override void Read(BinaryReader reader)
        {
            throw new NotImplementedException();
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(Opcode);
            writer.Write(NewCharCoid);
        }
    }
}
