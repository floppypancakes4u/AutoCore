namespace AutoCore.Game.Experience;

/// <summary>Absolute character progress fields persisted on the char DB row.</summary>
public readonly struct CharacterProgressSnapshot
{
    public CharacterProgressSnapshot(
        byte level,
        int experience,
        short skillPoints = 0,
        short attributePoints = 0,
        short researchPoints = 0,
        short attributeTech = 1,
        short attributeCombat = 1,
        short attributeTheory = 1,
        short attributePerception = 1)
    {
        Level = level < 1 ? (byte)1 : level;
        Experience = experience < 0 ? 0 : experience;
        SkillPoints = skillPoints < 0 ? (short)0 : skillPoints;
        AttributePoints = attributePoints < 0 ? (short)0 : attributePoints;
        ResearchPoints = researchPoints < 0 ? (short)0 : researchPoints;
        // Spent attributes floor at 1 (retail pool minimum).
        AttributeTech = attributeTech < 1 ? (short)1 : attributeTech;
        AttributeCombat = attributeCombat < 1 ? (short)1 : attributeCombat;
        AttributeTheory = attributeTheory < 1 ? (short)1 : attributeTheory;
        AttributePerception = attributePerception < 1 ? (short)1 : attributePerception;
    }

    public byte Level { get; }
    public int Experience { get; }
    public short SkillPoints { get; }
    public short AttributePoints { get; }
    public short ResearchPoints { get; }
    public short AttributeTech { get; }
    public short AttributeCombat { get; }
    public short AttributeTheory { get; }
    public short AttributePerception { get; }
}
