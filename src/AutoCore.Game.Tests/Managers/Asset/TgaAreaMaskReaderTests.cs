using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

using AutoCore.Game.Managers.Asset;

[TestClass]
public class TgaAreaMaskReaderTests
{
    [TestMethod]
    public void TryReadAreaIds_BottomOrigin_MapsFileRow0ToCellZ0()
    {
        // Retail map TGAs use bottom-left origin (descriptor bit5 clear).
        // File row-major (bottom→top): bottom-left=1, bottom-right=2, top-left=3, top-right=4
        using var stream = BuildUncompressedTga(
            width: 2,
            height: 2,
            topOrigin: false,
            bpp: 32,
            areasRowMajor: new byte[] { 1, 2, 3, 4 });

        Assert.IsTrue(TgaAreaMaskReader.TryReadAreaIds(stream, out var w, out var h, out var ids, out var error), error);
        Assert.AreEqual(2, w);
        Assert.AreEqual(2, h);
        // Storage index = height * cellX + cellZ; cellZ=0 is image bottom.
        Assert.AreEqual(1, ids[2 * 0 + 0]); // bottom-left
        Assert.AreEqual(3, ids[2 * 0 + 1]); // top-left
        Assert.AreEqual(2, ids[2 * 1 + 0]); // bottom-right
        Assert.AreEqual(4, ids[2 * 1 + 1]); // top-right
    }

    [TestMethod]
    public void TryReadAreaIds_TopOrigin_FlipsSoCellZ0IsBottom()
    {
        // Top-origin file: first row is top of image. Client flips so y=0 is bottom.
        // File order top→bottom: top-left=3, top-right=4, bottom-left=1, bottom-right=2
        using var stream = BuildUncompressedTga(
            width: 2,
            height: 2,
            topOrigin: true,
            bpp: 32,
            areasRowMajor: new byte[] { 3, 4, 1, 2 });

        Assert.IsTrue(TgaAreaMaskReader.TryReadAreaIds(stream, out _, out _, out var ids, out var error), error);
        Assert.AreEqual(1, ids[2 * 0 + 0]); // bottom-left
        Assert.AreEqual(3, ids[2 * 0 + 1]); // top-left
        Assert.AreEqual(2, ids[2 * 1 + 0]); // bottom-right
        Assert.AreEqual(4, ids[2 * 1 + 1]); // top-right
    }

    [TestMethod]
    public void TryReadAreaIds_RejectsUnsupportedType()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)0); // id length
            w.Write((byte)0); // color map
            w.Write((byte)10); // RLE — unsupported
            w.Write(new byte[5]);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)1);
            w.Write((byte)32);
            w.Write((byte)0x20);
        }

        stream.Position = 0;
        Assert.IsFalse(TgaAreaMaskReader.TryReadAreaIds(stream, out _, out _, out _, out var error));
        Assert.IsTrue(error.Contains("type", StringComparison.OrdinalIgnoreCase));
    }

    private static MemoryStream BuildUncompressedTga(int width, int height, bool topOrigin, int bpp, byte[] areasRowMajor)
    {
        var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((byte)2);
        w.Write(new byte[5]);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)width);
        w.Write((ushort)height);
        w.Write((byte)bpp);
        w.Write((byte)(topOrigin ? 0x20 : 0x00));

        var bytesPerPixel = bpp / 8;
        foreach (var area in areasRowMajor)
        {
            // BGRA: B, G, R, (A)
            w.Write((byte)0);
            w.Write((byte)(area << 3));
            w.Write((byte)0);
            if (bytesPerPixel == 4)
                w.Write((byte)255);
        }

        stream.Position = 0;
        return stream;
    }
}
