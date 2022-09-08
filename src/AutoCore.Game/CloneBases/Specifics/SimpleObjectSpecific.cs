namespace AutoCore.Game.CloneBases.Specifics;

using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public struct SimpleObjectSpecific
{
    public float Alpha { get; set; }
    public int Armor { get; set; }
    public int CustomColor { get; set; }
    public DamageSpecific DamageArmor { get; set; }
    public int DisciplineRanks { get; set; }
    public int DisciplineRequirement { get; set; }
    public bool DropBrokenOnly { get; set; }
    public int Faction { get; set; }
    public short Flags { get; set; }
    public int[] Ingredients { get; set; }
    public byte InvSizeX { get; set; }
    public byte InvSizeY { get; set; }
    public bool IsNotTradeable { get; set; }
    public short ItemRarity { get; set; }
    public float Mass { get; set; }
    public short MaxHitPoint { get; set; }
    public ushort MaxUses { get; set; }
    public byte MaximumEnhancements { get; set; }
    public short MaximumGadgets { get; set; }
    public short MinHitPoints { get; set; }
    public string PhysicsName { get; set; }
    public int Prefix { get; set; }
    public short RaceRegenRate { get; set; }
    public short RaceShieldFactor { get; set; }
    public short RaceShieldRegenerate { get; set; }
    public byte RenderType { get; set; }
    public int RequiredClass { get; set; }
    public short RequiredCombat { get; set; }
    public short RequiredLevel { get; set; }
    public short RequiredPerception { get; set; }
    public short RequiredTech { get; set; }
    public short RequiredTheory { get; set; }
    public float Scale { get; set; }
    public int Skill1 { get; set; }
    public int Skill2 { get; set; }
    public int Skill3 { get; set; }
    public int SkillGroup1 { get; set; }
    public int SkillGroup2 { get; set; }
    public int SkillGroup3 { get; set; }
    public ushort StackSize { get; set; }
    public short SubType { get; set; }

    public static SimpleObjectSpecific ReadNew(BinaryReader reader)
    {
        return new SimpleObjectSpecific
        {
            Armor = reader.ReadInt32(),
            Skill1 = reader.ReadInt32(),
            Skill2 = reader.ReadInt32(),
            Skill3 = reader.ReadInt32(),
            SkillGroup1 = reader.ReadInt32(),
            SkillGroup2 = reader.ReadInt32(),
            SkillGroup3 = reader.ReadInt32(),
            CustomColor = reader.ReadInt32(),
            Faction = reader.ReadInt32(),
            Prefix = reader.ReadInt32(),
            RequiredClass = reader.ReadInt32(),
            Mass = reader.ReadSingle(),
            Alpha = reader.ReadSingle(),
            Scale = reader.ReadSingle(),
            RequiredLevel = reader.ReadInt16(),
            Flags = reader.ReadInt16(),
            SubType = reader.ReadInt16(),
            MinHitPoints = reader.ReadInt16(),
            MaxHitPoint = reader.ReadInt16(),
            RaceRegenRate = reader.ReadInt16(),
            RaceShieldFactor = reader.ReadInt16(),
            RequiredCombat = reader.ReadInt16(),
            RequiredPerception = reader.ReadInt16(),
            RequiredTech = reader.ReadInt16(),
            RequiredTheory = reader.ReadInt16(),
            InvSizeX = reader.ReadByte(),
            InvSizeY = reader.ReadByte(),
            RenderType = reader.ReadByte(),
            MaximumEnhancements = reader.ReadByte(),
            PhysicsName = reader.ReadUTF16StringOn(65),
            DamageArmor = DamageSpecific.ReadNew(reader),
            Ingredients = reader.ReadConstArray(5, reader.ReadInt32),
            DisciplineRequirement = reader.ReadInt32(),
            DisciplineRanks = reader.ReadInt32(),
            MaximumGadgets = reader.ReadInt16(),
            RaceShieldRegenerate = reader.ReadInt16(),
            ItemRarity = reader.ReadInt16(),
            StackSize = reader.ReadUInt16(),
            MaxUses = reader.ReadUInt16(),
            IsNotTradeable = reader.ReadBoolean(),
            DropBrokenOnly = reader.ReadBoolean(),
        };
    }
}
