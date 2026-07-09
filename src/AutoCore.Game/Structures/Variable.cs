namespace AutoCore.Game.Structures;

using AutoCore.Utils.Extensions;

public class Variable
{
    public string Name { get; private set; }
    public byte Type { get; private set; }
    public int Id { get; private set; }
    public float Value { get; private set; }
    public float InitialValue { get; private set; }
    public bool UniqueForImport { get; private set; }
    public List<long> Triggers { get; private set; }
    

    public static Variable Read(BinaryReader reader, int mapVersion)
    {
        return new Variable
        {
            Id = reader.ReadInt32(),
            Type = reader.ReadByte(),
            Value = reader.ReadSingle(),
            InitialValue = reader.ReadSingle(),
            UniqueForImport = mapVersion >= 46 && reader.ReadBoolean(),
            Name = reader.ReadUTF8StringOn(64)
        };
    }

    /// <summary>Unit-test factory for map logic variables.</summary>
    internal static Variable CreateForTests(int id, byte type, float value, float initialValue = 0f, string name = null)
    {
        return new Variable
        {
            Id = id,
            Type = type,
            Value = value,
            InitialValue = initialValue,
            Name = name ?? $"var_{id}",
        };
    }
}
