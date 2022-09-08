namespace AutoCore.Game.CloneBases.Specifics;

public struct TinkeringKitSpecific
{
    public short MaxSlotLevel;
    public uint ObjectTypeRestriction;

    public static TinkeringKitSpecific ReadNew(BinaryReader reader)
    {
        var tks = new TinkeringKitSpecific
        {
            MaxSlotLevel = reader.ReadInt16()
        };

        reader.ReadInt16();

        tks.ObjectTypeRestriction = reader.ReadUInt32();

        return tks;
    }
}
