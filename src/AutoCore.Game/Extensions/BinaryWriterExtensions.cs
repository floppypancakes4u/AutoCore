namespace AutoCore.Game.Extensions;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

public static class BinaryWriterExtensions
{
    private static readonly byte[] TFIDPadding = new byte[7];

    public static void WriteTFID(this BinaryWriter writer, long coid, bool global)
    {
        writer.Write(coid);
        writer.Write(global);
        writer.Write(TFIDPadding);
    }

    public static void WriteTFID(this BinaryWriter writer, TFID tfid)
    {
        writer.WriteTFID(tfid.Coid, tfid.Global);
    }

    public static void Write(this BinaryWriter writer, GameOpcode opcode)
    {
        writer.Write((uint)opcode);
    }
}
