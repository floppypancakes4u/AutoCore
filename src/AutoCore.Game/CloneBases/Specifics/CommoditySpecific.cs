namespace AutoCore.Game.CloneBases.Specifics;

public struct CommoditySpecific
{
    public int CommodityGroupType;
    public float DropChance;
    public int Group;
    public byte MaterialDifficulty;
    public int MaxLevel;
    public int MinLevel;
    public byte Purity;
    public byte PurityFrom;
    public int RefineTarget;
    public int RefinesFrom;
    public int Value;

    public static CommoditySpecific ReadNew(BinaryReader reader)
    {
        var cs = new CommoditySpecific
        {
            RefineTarget = reader.ReadInt32(),
            Value = reader.ReadInt32(),
            MaterialDifficulty = reader.ReadByte(),
            Purity = reader.ReadByte(),
        };

        reader.ReadInt16();

        cs.Group = reader.ReadInt32();
        cs.RefinesFrom = reader.ReadInt32();
        cs.PurityFrom = reader.ReadByte();

        reader.ReadBytes(3);

        cs.CommodityGroupType = reader.ReadInt32();
        cs.MinLevel = reader.ReadInt32();
        cs.MaxLevel = reader.ReadInt32();
        cs.DropChance = reader.ReadSingle();

        return cs;
    }
}
