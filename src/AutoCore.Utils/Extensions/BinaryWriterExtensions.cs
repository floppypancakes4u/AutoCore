using System.Text;

namespace AutoCore.Utils.Extensions;

public static class BinaryWriterExtensions
{
    public static void WriteUtf8NullString(this BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    public static void WriteUtf8StringOn(this BinaryWriter writer, string value, int len)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));

        for (var i = 0; i < len - value.Length; ++i)
            writer.Write((byte)0);
    }

    public static void WriteLengthedString(this BinaryWriter writer, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            writer.Write(0);
            return;
        }

        writer.Write(text.Length);
        writer.WriteUtf8StringOn(text, text.Length);
    }

    public static void WriteUtf8String(this BinaryWriter writer, string text)
    {
        writer.WriteUtf8StringOn(text, text.Length);
    }

    public static void WriteConstArray<T>(this BinaryWriter _, T[] data, int count, Action<T> writerFunction) where T : new()
    {
        for (var i = 0; i < count; ++i)
            writerFunction(data[i]);
    }

    public static void WriteAt(this BinaryWriter writer, byte value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, sbyte value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, short value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, ushort value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, int value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, uint value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, long value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, ulong value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, float value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }

    public static void WriteAt(this BinaryWriter writer, double value, long position)
    {
        var currentPosition = writer.BaseStream.Position;

        writer.BaseStream.Position = position;

        writer.Write(value);

        writer.BaseStream.Position = currentPosition;
    }
}
