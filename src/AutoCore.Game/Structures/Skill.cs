namespace AutoCore.Game.Structures;

using AutoCore.Utils.Extensions;

public class Skill
{
    public int AffectedObjectType { get; set; }
    public int AffectedSubType { get; set; }
    public int AffectedTarget { get; set; }
    public int CategoryId { get; set; }
    public int Class { get; set; }
    public string Description { get; set; }
    public List<SkillElement> Elements { get; set; }
    public int GroupId { get; set; }
    public int Id { get; set; }
    public int IsChain { get; set; }
    public int IsSpray { get; set; }
    public byte LocationLine { get; set; }
    public byte LocationTree { get; set; }
    public byte MaxSkillLevel { get; set; }
    public byte MinimumLevel { get; set; }
    public string Name { get; set; }
    public short NumOfElements { get; set; }
    public byte OptionalAction { get; set; }
    public int Race { get; set; }
    public int SkillOptional1 { get; set; }
    public int SkillOptional2 { get; set; }
    public int SkillOptional3 { get; set; }
    public int SkillOptional4 { get; set; }
    public int SkillPrerequisite1 { get; set; }
    public int SkillPrerequisite2 { get; set; }
    public int SkillPrerequisite3 { get; set; }
    public byte SkillType { get; set; }
    public int StatusEffect { get; set; }
    public int SummonedCreatureId { get; set; }
    public int TargetObjectType { get; set; }
    public int TargetSubType { get; set; }
    public int TargetType { get; set; }
    public int UseBodyForArc { get; set; }
    public string XMLName { get; set; }

    public static Skill Read(BinaryReader reader)
    {
        var s = new Skill
        {
            Id = reader.ReadInt32(),
            Class = reader.ReadInt32(),
            Race = reader.ReadInt32(),
            TargetType = reader.ReadInt32(),
            TargetSubType = reader.ReadInt32(),
            TargetObjectType = reader.ReadInt32(),
            AffectedTarget = reader.ReadInt32(),
            AffectedSubType = reader.ReadInt32(),
            AffectedObjectType = reader.ReadInt32(),
            StatusEffect = reader.ReadInt32(),
            SkillPrerequisite1 = reader.ReadInt32(),
            SkillPrerequisite2 = reader.ReadInt32(),
            SkillPrerequisite3 = reader.ReadInt32(),
            LocationTree = reader.ReadByte(),
            LocationLine = reader.ReadByte(),
            MinimumLevel = reader.ReadByte(),
            SkillType = reader.ReadByte(),
            Name = reader.ReadUTF16StringOn(33),
            Description = reader.ReadUTF16StringOn(1025),
            XMLName = reader.ReadUTF16StringOn(65)
        };

        reader.ReadBytes(2);

        s.IsChain = reader.ReadInt32();
        s.IsSpray = reader.ReadInt32();
        s.OptionalAction = reader.ReadByte();
        s.MaxSkillLevel = reader.ReadByte();

        reader.ReadBytes(2);

        s.UseBodyForArc = reader.ReadInt32();
        s.GroupId = reader.ReadInt32();
        s.CategoryId = reader.ReadInt32();
        s.SummonedCreatureId = reader.ReadInt32();
        s.SkillOptional1 = reader.ReadInt32();
        s.SkillOptional2 = reader.ReadInt32();
        s.SkillOptional3 = reader.ReadInt32();
        s.SkillOptional4 = reader.ReadInt32();
        s.NumOfElements = reader.ReadInt16();

        reader.ReadBytes(2);

        s.Elements = s.NumOfElements > 0 ? new List<SkillElement>(s.NumOfElements) : new List<SkillElement>(0);

        for (var i = 0; i < s.NumOfElements; ++i)
            s.Elements.Add(SkillElement.ReadNew(reader));

        return s;
    }
}
