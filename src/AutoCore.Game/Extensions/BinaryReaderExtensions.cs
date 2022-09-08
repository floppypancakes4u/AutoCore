namespace AutoCore.Game.Extensions;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

public static class BinaryReaderExtensions
{
    public static TFID ReadTFID(this BinaryReader reader)
    {
        var id = new TFID
        {
            Coid = reader.ReadInt64(),
            Global = reader.ReadBoolean()
        };

        reader.BaseStream.Position += 7;

        return id;
    }

    public static GameOpcode ReadGameOpcode(this BinaryReader reader)
    {
        return (GameOpcode)reader.ReadUInt32();
    }
}
