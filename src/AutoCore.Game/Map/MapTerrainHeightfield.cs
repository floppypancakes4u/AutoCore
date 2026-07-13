namespace AutoCore.Game.Map;

/// <summary>
/// Continuous terrain heightfield extracted from a map's 32bpp TGA at load time.
/// Encoding matches retail <c>CVOGTerrain::LoadMapImage</c> (and the level-viewer sampler):
/// <list type="bullet">
///   <item>Uncompressed BGRA; height16 = (A &lt;&lt; 8) | B</item>
///   <item>world Y = height16 * <see cref="DefaultHeightScale"/> / 256</item>
///   <item>world X = col * gridSize, world Z = row * gridSize (row 0 = Z 0, no vertical flip)</item>
/// </list>
/// Source file: <c>{MapFileName}.tga</c> from the game GLM pack, dimensions must match fam
/// <c>m_lWidth</c>/<c>m_lHeight</c>.
/// </summary>
public sealed class MapTerrainHeightfield
{
    /// <summary>Retail / level-viewer default: world Y units per 256 of the 16-bit height.</summary>
    public const float DefaultHeightScale = 4.0f;

    private readonly ushort[] _heights;
    private readonly int _width;
    private readonly int _height;
    private readonly float _gridSize;
    private readonly float _heightScaleOver256;

    private MapTerrainHeightfield(ushort[] heights, int width, int height, float gridSize, float heightScale)
    {
        _heights = heights;
        _width = width;
        _height = height;
        _gridSize = gridSize;
        _heightScaleOver256 = heightScale / 256f;
    }

    public int Width => _width;
    public int Height => _height;
    public float GridSize => _gridSize;

    /// <summary>
    /// Parse an uncompressed 32bpp map TGA into a heightfield. Returns false when the image
    /// type/bpp is wrong or dimensions do not match the fam terrain size.
    /// </summary>
    public static bool TryLoad(
        Stream tgaStream,
        int expectedWidth,
        int expectedHeight,
        float gridSize,
        out MapTerrainHeightfield field,
        out string error,
        float heightScale = DefaultHeightScale)
    {
        field = null;
        error = null;

        if (tgaStream == null)
        {
            error = "TGA stream is null";
            return false;
        }

        if (expectedWidth <= 1 || expectedHeight <= 1 || gridSize <= 0f)
        {
            error = $"Invalid terrain size {expectedWidth}x{expectedHeight} grid={gridSize}";
            return false;
        }

        using var reader = new BinaryReader(tgaStream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (tgaStream.Length < 18)
        {
            error = "TGA too small";
            return false;
        }

        var idLen = reader.ReadByte();
        reader.ReadByte(); // color map type
        var imageType = reader.ReadByte();
        reader.BaseStream.Position += 5; // color map spec
        reader.ReadUInt16(); // x origin
        reader.ReadUInt16(); // y origin
        var width = reader.ReadUInt16();
        var height = reader.ReadUInt16();
        var bpp = reader.ReadByte();
        reader.ReadByte(); // descriptor — map TGAs are sampled in raw file order (no flip)

        if (imageType != 2 || bpp != 32)
        {
            error = $"Unsupported TGA type={imageType} bpp={bpp} (need uncompressed 32bpp)";
            return false;
        }

        if (width != expectedWidth || height != expectedHeight)
        {
            error = $"TGA dimensions {width}x{height} do not match fam terrain {expectedWidth}x{expectedHeight}";
            return false;
        }

        if (idLen > 0)
            reader.BaseStream.Position += idLen;

        var pixelCount = width * height;
        var need = (long)pixelCount * 4;
        if (reader.BaseStream.Length - reader.BaseStream.Position < need)
        {
            error = "TGA pixel data truncated";
            return false;
        }

        var heights = new ushort[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var b = reader.ReadByte();
            reader.ReadByte(); // G (tile layer in retail — ignored for height)
            reader.ReadByte(); // R
            var a = reader.ReadByte();
            heights[i] = (ushort)((a << 8) | b);
        }

        field = new MapTerrainHeightfield(heights, width, height, gridSize, heightScale);
        return true;
    }

    /// <summary>
    /// Bilinear sample of terrain Y at world (x, z). Coordinates outside the map clamp to the edge.
    /// Always returns true once loaded (edge clamp); false only if grid is degenerate.
    /// </summary>
    public bool TrySample(float worldX, float worldZ, out float worldY)
    {
        worldY = 0f;
        if (_gridSize <= 0f || _width < 2 || _height < 2)
            return false;

        var fx = worldX / _gridSize;
        var fz = worldZ / _gridSize;
        // Clamp to cell-index range [0, dim-1]; last column/row uses a zero-span bilinear (c1==c0).
        if (fx < 0f) fx = 0f;
        else if (fx > _width - 1f) fx = _width - 1f;
        if (fz < 0f) fz = 0f;
        else if (fz > _height - 1f) fz = _height - 1f;

        var c0 = (int)fx;
        var r0 = (int)fz;
        var c1 = c0 < _width - 1 ? c0 + 1 : c0;
        var r1 = r0 < _height - 1 ? r0 + 1 : r0;
        var tx = fx - c0;
        var tz = fz - r0;

        var h00 = HeightAt(r0, c0);
        var h10 = HeightAt(r0, c1);
        var h01 = HeightAt(r1, c0);
        var h11 = HeightAt(r1, c1);

        worldY = (h00 * (1f - tx) + h10 * tx) * (1f - tz)
               + (h01 * (1f - tx) + h11 * tx) * tz;
        return true;
    }

    private float HeightAt(int row, int col)
    {
        if (row < 0) row = 0;
        else if (row >= _height) row = _height - 1;
        if (col < 0) col = 0;
        else if (col >= _width) col = _width - 1;
        return _heights[row * _width + col] * _heightScaleOver256;
    }
}
