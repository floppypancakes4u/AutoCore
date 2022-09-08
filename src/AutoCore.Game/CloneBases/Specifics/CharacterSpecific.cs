namespace AutoCore.Game.CloneBases.Specifics;

public struct CharacterSpecific
{
    public byte Race;
    public byte Class;
    public bool IsMale;
    public byte Flags;
    public short HPFactor;
    public short HPStart;

    public static CharacterSpecific ReadNew(BinaryReader reader)
    {
        var cs = new CharacterSpecific
        {
            IsMale = reader.ReadInt32() != 0,
            HPStart = reader.ReadInt16(),
            HPFactor = reader.ReadInt16(),
            Flags = reader.ReadByte(),
            Class = reader.ReadByte(),
            Race = reader.ReadByte()
        };

        reader.ReadByte();

        return cs;
    }
}
