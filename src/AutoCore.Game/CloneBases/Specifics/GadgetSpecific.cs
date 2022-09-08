namespace AutoCore.Game.CloneBases.Specifics;

public struct GadgetSpecific
{
    public uint ObjectType;
    public int Prefix;

    public static GadgetSpecific ReadNew(BinaryReader reader)
    {
        return new GadgetSpecific
        {
            Prefix = reader.ReadInt32(),
            ObjectType = reader.ReadUInt32()
        };
    }
}
