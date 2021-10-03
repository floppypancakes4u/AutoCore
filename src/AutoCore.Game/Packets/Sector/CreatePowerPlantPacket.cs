using System;
using System.IO;

namespace AutoCore.Game.Packets.Sector
{
    using CloneBases.Specifics;
    using Constants;
    using Utils.Extensions;

    public class CreatePowerPlantPacket : CreateSimpleObjectPacket
    {
        public override GameOpcode Opcode => GameOpcode.CreatePowerPlant;

        public PowerPlantSpecific PowerPlantSpecific { get; set; }
        public float Mass { get; set; }
        public float SkillCooldown { get; set; }
        public string Name { get; set; }

        public override void Read(BinaryReader reader)
        {
            throw new NotImplementedException();
        }

        public override void Write(BinaryWriter writer)
        {
            PowerPlantSpecific.Write(writer);

            writer.Write(Mass);
            writer.WriteUtf8StringOn(Name, 100);
            writer.Write(SkillCooldown);
        }
    }
}
