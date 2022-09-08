namespace AutoCore.Game.CloneBases.Prefixes;

public class PrefixOrnament : PrefixBase
{
    public short CombatAdjust { get; set; }
    public float CombatAdjustf { get; set; }
    public short PerceptionAdjust { get; set; }
    public float PerceptionAdjustf { get; set; }
    public short TechAdjust { get; set; }
    public float TechAdjustf { get; set; }
    public short TheoryAdjust { get; set; }
    public float TheoryAdjustf { get; set; }

    public PrefixOrnament(BinaryReader reader)
        : base(reader)
    {
        CombatAdjust = reader.ReadInt16();
        PerceptionAdjust = reader.ReadInt16();
        TheoryAdjust = reader.ReadInt16();
        TechAdjust = reader.ReadInt16();
        CombatAdjustf = reader.ReadSingle();
        PerceptionAdjustf = reader.ReadSingle();
        TheoryAdjustf = reader.ReadSingle();
        TechAdjustf = reader.ReadSingle();
    }
}
