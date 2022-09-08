using System.Buffers;
using System.IO;

namespace AutoCore.Utils.Memory;

public class ArrayPoolMemoryStream : MemoryStream
{
    public byte[] Data { get; private set; }
    public int Offset { get; private set; }
    public int Size { get; private set; }

    public ArrayPoolMemoryStream(int size)
        : this(ArrayPool<byte>.Shared.Rent(size))
    {
        Size = size;
    }

    public ArrayPoolMemoryStream(byte[] buffer)
        : this(buffer, true)
    {
    }

    public ArrayPoolMemoryStream(byte[] buffer, bool writable)
        : this(buffer, 0, buffer.Length, writable)
    {
    }

    public ArrayPoolMemoryStream(byte[] buffer, int offset, int size)
        : this(buffer, offset, size, true)
    {
    }

    public ArrayPoolMemoryStream(byte[] buffer, int offset, int size, bool writable)
        : base(buffer, offset, size, writable)
    {
        Data = buffer;
        Offset = offset;
        Size = size;
    }

    protected override void Dispose(bool disposing)
    {
        if (Data != null)
        {
            ArrayPool<byte>.Shared.Return(Data);

            Data = null;
            Offset = 0;
            Size = 0;
        }
    }
}
