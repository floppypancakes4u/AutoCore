using System;
using System.IO;

namespace AutoCore.Game.Packets.Sector
{
    using Constants;
    using Extensions;
    using Utils.Extensions;

    public class CreateCharacterExtendedPacket : CreateCharacterPacket
    {
        public override GameOpcode Opcode => GameOpcode.CreateCharacterExtended;

        public int NumCompletedQuests { get; set; }
        public int NumCurrentQuests { get; set; }
        public short NumAchievements { get; set; }
        public short NumDisciplines { get; set; }
        public byte NumSkills { get; set; }

        public override void Read(BinaryReader reader)
        {
            throw new NotImplementedException();
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(Opcode);

            base.Write(writer);

            // TODO
        }
    }
}
