namespace AutoCore.Game.Structures;

public struct SkillElement
{
    public int ElementType { get; set; }
    public byte EquationType { get; set; }
    public int SkillId { get; set; }
    public float ValueBase { get; set; }
    public float ValuePerLevel { get; set; }

    public static SkillElement ReadNew(BinaryReader reader)
    {
        var se = new SkillElement
        {
            SkillId = reader.ReadInt32(),
            ElementType = reader.ReadInt32(),
            EquationType = reader.ReadByte()
        };

        reader.BaseStream.Position += 3;

        se.ValueBase = reader.ReadSingle();
        se.ValuePerLevel = reader.ReadSingle();

        return se;
    }
}
