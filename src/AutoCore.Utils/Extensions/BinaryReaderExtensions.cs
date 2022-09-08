using System.Text;

namespace AutoCore.Utils.Extensions;

public static class BinaryReaderExtensions
{
    public static string ReadLengthedString(this BinaryReader reader)
    {
        var len = reader.ReadInt32();
        return len == 0 ? "" : Encoding.UTF8.GetString(reader.ReadBytes(len));
    }

    public static string ReadUTF8StringOn(this BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);

        var index = Array.IndexOf<byte>(bytes, 0);
        if (index == -1)
            index = bytes.Length;

        return index == 0 ? "" : Encoding.UTF8.GetString(bytes, 0, index);
    }

    public static string ReadUTF16StringOn(this BinaryReader br, int size)
    {
        if (size > 0)
        {
            var buff = br.ReadBytes(size * 2);

            for (var i = 0; i < buff.Length; i += 2)
                if (buff[i] == 0 && buff[i + 1] == 0)
                    return Encoding.Unicode.GetString(buff, 0, i);

            return Encoding.Unicode.GetString(buff);
        }

        return string.Empty;
    }

    public static T[] ReadConstArray<T>(this BinaryReader _, int count, Func<T> readerFunction) where T : new()
    {
        var result = new T[count];

        for (var i = 0; i < count; ++i)
            result[i] = readerFunction();

        return result;
    }

    public static T[] ReadConstArray<T>(this BinaryReader reader, int count, Func<BinaryReader, T> readerFunction) where T : new()
    {
        var result = new T[count];

        for (var i = 0; i < count; ++i)
            result[i] = readerFunction(reader);

        return result;
    }
}
