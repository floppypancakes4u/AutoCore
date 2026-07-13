using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Game.Map;

/// <summary>
/// Map heightfield from retail map TGA (CVOGTerrain::LoadMapImage encoding used by the level viewer):
/// 32bpp BGRA, height16 = (A&lt;&lt;8)|B, world Y = height16 * HeightScale / 256.
/// </summary>
[TestClass]
public class MapTerrainHeightfieldTests
{
    private const float Tol = 0.001f;

    [TestMethod]
    public void TryLoad_Parses16BitHeightFromBgraAndSamplesCorners()
    {
        // 2x2 grid, gridSize 10:
        // (0,0) h16=256 → y=4; (1,0) h16=512 → y=8; (0,1) h16=0 → y=0; (1,1) h16=768 → y=12
        using var stream = BuildHeightTga(2, 2, new ushort[] { 256, 512, 0, 768 });

        Assert.IsTrue(MapTerrainHeightfield.TryLoad(stream, expectedWidth: 2, expectedHeight: 2, gridSize: 10f, out var field, out var error), error);
        Assert.IsNotNull(field);

        Assert.IsTrue(field.TrySample(0f, 0f, out var y00));
        Assert.AreEqual(4f, y00, Tol);

        Assert.IsTrue(field.TrySample(10f, 0f, out var y10));
        Assert.AreEqual(8f, y10, Tol);

        Assert.IsTrue(field.TrySample(0f, 10f, out var y01));
        Assert.AreEqual(0f, y01, Tol);

        Assert.IsTrue(field.TrySample(10f, 10f, out var y11));
        Assert.AreEqual(12f, y11, Tol);
    }

    [TestMethod]
    public void TrySample_BilinearInterpolatesBetweenCells()
    {
        // Same 2x2 as above; midpoint between (0,0)=4 and (1,0)=8 at z=0 is y=6.
        using var stream = BuildHeightTga(2, 2, new ushort[] { 256, 512, 0, 768 });
        Assert.IsTrue(MapTerrainHeightfield.TryLoad(stream, 2, 2, 10f, out var field, out _));

        Assert.IsTrue(field.TrySample(5f, 0f, out var y));
        Assert.AreEqual(6f, y, Tol);
    }

    [TestMethod]
    public void TryLoad_RejectsDimensionMismatch()
    {
        using var stream = BuildHeightTga(2, 2, new ushort[] { 0, 0, 0, 0 });
        Assert.IsFalse(MapTerrainHeightfield.TryLoad(stream, expectedWidth: 4, expectedHeight: 4, gridSize: 5f, out var field, out var error));
        Assert.IsNull(field);
        Assert.IsTrue(error.Contains("dimension", StringComparison.OrdinalIgnoreCase) || error.Contains("match", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TrySample_ClampsToMapEdge()
    {
        using var stream = BuildHeightTga(2, 2, new ushort[] { 256, 512, 0, 768 });
        Assert.IsTrue(MapTerrainHeightfield.TryLoad(stream, 2, 2, 10f, out var field, out _));

        // Outside world extent — clamp to last cell (1,1) → y=12.
        Assert.IsTrue(field.TrySample(1000f, 1000f, out var y));
        Assert.AreEqual(12f, y, Tol);
    }

    /// <summary>
    /// Build uncompressed 32bpp TGA in RAW (top-down / file) order: row0 = Z=0, col0 = X=0.
    /// Pixels: h16 encoded as B=low, G=0, R=0, A=high (retail LoadMapImage channel layout).
    /// </summary>
    internal static MemoryStream BuildHeightTga(int width, int height, ushort[] heightsRowMajor)
    {
        Assert.AreEqual(width * height, heightsRowMajor.Length);
        var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write((byte)0); // id length
        w.Write((byte)0); // no colormap
        w.Write((byte)2); // uncompressed truecolor
        w.Write(new byte[5]);
        w.Write((ushort)0);
        w.Write((ushort)0);
        w.Write((ushort)width);
        w.Write((ushort)height);
        w.Write((byte)32);
        w.Write((byte)0); // descriptor; engine treats map TGA as raw order regardless

        foreach (var h16 in heightsRowMajor)
        {
            w.Write((byte)(h16 & 0xFF));        // B = low byte
            w.Write((byte)0);                     // G
            w.Write((byte)0);                     // R
            w.Write((byte)((h16 >> 8) & 0xFF)); // A = high byte
        }

        stream.Position = 0;
        return stream;
    }
}
