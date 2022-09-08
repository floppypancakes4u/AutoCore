namespace AutoCore.Game.CloneBases.Prefixes;

public class PrefixRaceItem : PrefixBase
{
    public short HazardCountBonus { get; set; }
    public float HazardCountBonusf { get; set; }
    public short HazardSecondsBonus { get; set; }
    public float HazardSecondsBonusf { get; set; }

    public PrefixRaceItem(BinaryReader reader)
        : base(reader)
    {
        HazardCountBonus = reader.ReadInt16();

        reader.BaseStream.Position += 2;

        HazardCountBonusf = reader.ReadSingle();
        HazardSecondsBonus = reader.ReadInt16();

        reader.BaseStream.Position += 2;

        HazardSecondsBonusf = reader.ReadSingle();
    }
}
