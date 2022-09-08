namespace AutoCore.Game.CloneBases.Prefixes;

using AutoCore.Game.Structures;

public class PrefixArmor : PrefixBase
{
    public short ArmorFactorAdjust { get; set; }
    public float ArmorFactorPercent { get; set; }
    public DamageSpecific ResistAdjust { get; set; }

    public PrefixArmor(BinaryReader reader)
        : base(reader)
    {
        ArmorFactorPercent = reader.ReadSingle();
        ArmorFactorAdjust = reader.ReadInt16();
        ResistAdjust = DamageSpecific.ReadNew(reader);

        reader.BaseStream.Position += 2;
    }
}
