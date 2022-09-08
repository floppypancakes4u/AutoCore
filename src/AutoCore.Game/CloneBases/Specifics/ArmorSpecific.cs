namespace AutoCore.Game.CloneBases.Specifics;

using AutoCore.Game.Structures;

public class ArmorSpecific
{
    public short ArmorFactor;
    public short DefenseBonus;
    public float DeflectionModifier;
    public DamageSpecific Resistances;

    public static ArmorSpecific ReadNew(BinaryReader reader)
    {
        return new ArmorSpecific
        {
            DeflectionModifier = reader.ReadSingle(), // TODO: in the structs it's INT, not FLOAT
            ArmorFactor = reader.ReadInt16(),
            Resistances = DamageSpecific.ReadNew(reader),
            DefenseBonus = reader.ReadInt16()
        };
    }

    public void Read(BinaryReader reader)
    {
        DeflectionModifier = reader.ReadSingle();
        ArmorFactor = reader.ReadInt16();

        Resistances.Read(reader);

        DefenseBonus = reader.ReadInt16();
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(DeflectionModifier);
        writer.Write(ArmorFactor);

        Resistances.Write(writer);

        writer.Write(DefenseBonus);
    }
}
