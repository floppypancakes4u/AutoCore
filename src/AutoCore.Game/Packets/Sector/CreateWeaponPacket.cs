using System;
using System.IO;

namespace AutoCore.Game.Packets.Sector
{
    using Constants;
    using Structures;
    using Utils.Extensions;

    public class CreateWeaponPacket : CreateSimpleObjectPacket
    {
        public override GameOpcode Opcode => GameOpcode.CreateWeapon;

        public float VarianceRange { get; set; }
        public float VarianceRefireRate { get; set; }
        public float VarianceDamageMinimum { get; set; }
        public float VarianceDamageMaximum { get; set; }
        public short VarianceOffensiveBonus { get; set; }
        public float PrefixAccuracyBonus { get; set; }
        public short PrefixPenetrationBonus { get; set; }
        public int RechargeTime { get; set; }
        public float Mass { get; set; }
        public float RangeMinimum { get; set; }
        public float RangeMaximum { get; set; }
        public float ValidArc { get; set; }
        public DamageSpecific MinimumDamage { get; set; }
        public DamageSpecific MaximumDamage { get; set; }
        public string Name { get; set; }

        public override void Read(BinaryReader reader)
        {
            throw new NotImplementedException();
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(VarianceRange);
            writer.Write(VarianceRefireRate);
            writer.Write(VarianceDamageMinimum);
            writer.Write(VarianceDamageMaximum);
            writer.Write(VarianceOffensiveBonus);

            writer.BaseStream.Position += 2;

            writer.Write(PrefixAccuracyBonus);
            writer.Write(PrefixPenetrationBonus);

            writer.BaseStream.Position += 2;

            writer.Write(RechargeTime);
            writer.Write(Mass);
            writer.Write(RangeMinimum);
            writer.Write(RangeMaximum);
            writer.Write(ValidArc);

            MinimumDamage.Write(writer);
            MaximumDamage.Write(writer);

            writer.WriteUtf8StringOn(Name, 100);

            writer.BaseStream.Position += 4;
        }

        public new static void WriteEmptyPacket(BinaryWriter writer)
        {
            CreateSimpleObjectPacket.WriteEmptyPacket(writer);

            writer.BaseStream.Position += 176;
        }
    }
}
