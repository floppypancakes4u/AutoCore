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
}
