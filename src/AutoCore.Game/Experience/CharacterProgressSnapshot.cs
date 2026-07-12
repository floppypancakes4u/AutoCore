namespace AutoCore.Game.Experience;

/// <summary>Absolute character progress fields persisted on the char DB row.</summary>
public readonly struct CharacterProgressSnapshot
{
    public CharacterProgressSnapshot(
        byte level,
        int experience,
        short skillPoints = 0,
        short attributePoints = 0,
        short researchPoints = 0)
    {
        Level = level < 1 ? (byte)1 : level;
        Experience = experience < 0 ? 0 : experience;
        SkillPoints = skillPoints < 0 ? (short)0 : skillPoints;
        AttributePoints = attributePoints < 0 ? (short)0 : attributePoints;
        ResearchPoints = researchPoints < 0 ? (short)0 : researchPoints;
    }

    public byte Level { get; }
    public int Experience { get; }
    public short SkillPoints { get; }
    public short AttributePoints { get; }
    public short ResearchPoints { get; }
}
