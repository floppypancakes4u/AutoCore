using System.IO;

namespace AutoCore.Game.CloneBases.Prefixes
{
    using Structures;
    using Utils.Extensions;

    public class PrefixWeapon : PrefixBase
    {
        public float AccucaryBonusPercent { get; set; }
        public DamageSpecific DamageAdjustMaximum { get; set; }
        public DamageSpecific DamageAdjustMinimum { get; set; }
        public float DamagePercentAll { get; set; }
        public float[] DamagePercentMaximum { get; set; }
        public float[] DamagePercentMinimum { get; set; }
        public float FiringArcPercent { get; set; }
        public short HeatAdjust { get; set; }
        public float HeatPercent { get; set; }
        public short OffenseBonus { get; set; }
        public float OffenseBonusPercent { get; set; }
        public short PenetrationBonus { get; set; }
        public short PowerPerShot { get; set; }
        public float RangePercent { get; set; }
        public float RechargeTimePercent { get; set; }

        public PrefixWeapon(BinaryReader reader)
            : base(reader)
        {
            FiringArcPercent = reader.ReadSingle();
            RangePercent = reader.ReadSingle();
            RechargeTimePercent = reader.ReadSingle();
            HeatPercent = reader.ReadSingle();
            HeatAdjust = reader.ReadInt16();
            PowerPerShot = reader.ReadInt16();
            DamagePercentAll = reader.ReadSingle();
            DamagePercentMinimum = reader.ReadConstArray(6, reader.ReadSingle);
            DamagePercentMaximum = reader.ReadConstArray(6, reader.ReadSingle);
            DamageAdjustMinimum = DamageSpecific.ReadNew(reader);
            DamageAdjustMaximum = DamageSpecific.ReadNew(reader);
            OffenseBonus = reader.ReadInt16();

            reader.BaseStream.Position += 2;

            OffenseBonusPercent = reader.ReadSingle();
            AccucaryBonusPercent = reader.ReadSingle();
            PenetrationBonus = reader.ReadInt16();

            reader.BaseStream.Position += 2;
        }
    }
}
