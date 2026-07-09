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

    /// <summary>
    /// Writes <paramref name="count"/> zero bytes. Prefer this over advancing Position
    /// so MemoryStream gaps are always defined zeros for packet layout tests.
    /// </summary>
    public static void WriteZeros(this BinaryWriter writer, int count)
    {
        if (count <= 0)
            return;

        if (count <= 64)
        {
            Span<byte> zeros = stackalloc byte[count];
            zeros.Clear();
            writer.Write(zeros);
            return;
        }

        var buffer = new byte[Math.Min(count, 256)];
        var remaining = count;
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, buffer.Length);
            writer.Write(buffer, 0, chunk);
            remaining -= chunk;
        }
    }
}
