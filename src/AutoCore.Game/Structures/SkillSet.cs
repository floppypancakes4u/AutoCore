namespace AutoCore.Game.Structures;

public struct SkillSet
{
    public byte AnimationId;
    public int MaxHealth;
    public ushort MinCastTime;
    public int MinHealth;
    public ushort PauseTime;
    public int SkillId;
    public ushort SkillLevel;
    public bool StopsToAttack;
    public float Weight;

    public static SkillSet Read(BinaryReader reader)
    {
        return new SkillSet
        {
            SkillId = reader.ReadInt32(),
            PauseTime = reader.ReadUInt16(),
            MinCastTime = reader.ReadUInt16(),
            SkillLevel = reader.ReadUInt16(),
            StopsToAttack = reader.ReadBoolean(),
            AnimationId = reader.ReadByte(),
            MinHealth = reader.ReadInt32(),
            MaxHealth = reader.ReadInt32(),
            Weight = reader.ReadSingle()
        };
    }

    public override string ToString()
    {
        return $"Id: {SkillId}";
    }
}
