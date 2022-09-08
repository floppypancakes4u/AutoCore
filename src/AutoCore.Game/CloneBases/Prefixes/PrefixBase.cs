namespace AutoCore.Game.CloneBases.Prefixes;

using AutoCore.Utils.Extensions;

public class PrefixBase
{
    public float AttributeRequirementIncrease { get; set; }
    public int BaseValue { get; set; }
    public int Class { get; set; }
    public int Complexity { get; set; }
    public int Id { get; set; }
    public int[] Ingredients { get; set; }
    public int IsComponent { get; set; }
    public int IsGadgetOnly { get; set; }
    public int IsPrefix { get; set; }
    public short ItemRarity { get; set; }
    public short LevelOffset { get; set; }
    public float MassPercent { get; set; }
    public string Name { get; set; }
    public int ObjectType { get; set; }
    public string PrefixName { get; set; }
    public int Race { get; set; }
    public float Rarity { get; set; }
    public short RequiredCombat { get; set; }
    public short RequiredPerception { get; set; }
    public short RequiredTech { get; set; }
    public short RequiredTheory { get; set; }
    public int Skill { get; set; }
    public float ValuePercent { get; set; }

    public PrefixBase(BinaryReader reader)
    {
        Id = reader.ReadInt32();
        ObjectType = reader.ReadInt32();
        ValuePercent = reader.ReadSingle();
        IsComponent = reader.ReadInt32();
        Rarity = reader.ReadSingle();
        Race = reader.ReadInt32();
        Class = reader.ReadInt32();
        Name = reader.ReadUTF16StringOn(51);

        reader.BaseStream.Position += 2;

        MassPercent = reader.ReadSingle();
        Skill = reader.ReadInt32();
        Ingredients = reader.ReadConstArray(5, reader.ReadInt32);
        BaseValue = reader.ReadInt32();
        IsGadgetOnly = reader.ReadInt32();
        LevelOffset = reader.ReadInt16();

        reader.BaseStream.Position += 2;

        AttributeRequirementIncrease = reader.ReadSingle();
        RequiredCombat = reader.ReadInt16();
        RequiredPerception = reader.ReadInt16();
        RequiredTech = reader.ReadInt16();
        RequiredTheory = reader.ReadInt16();
        ItemRarity = reader.ReadInt16();

        reader.BaseStream.Position += 2;

        Complexity = reader.ReadInt32();
        IsPrefix = reader.ReadInt32();
        PrefixName = reader.ReadUTF16StringOn(33);

        reader.BaseStream.Position += 2;
    }
}
