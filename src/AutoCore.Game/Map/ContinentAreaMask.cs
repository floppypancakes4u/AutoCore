namespace AutoCore.Game.Map;

/// <summary>
/// Per-continent explored-area mask sampled from the terrain TGA G channel (high 5 bits).
/// Matches client FUN_004a8b90 / CVOGTerrain_LoadMapImage.
/// </summary>
public sealed class ContinentAreaMask
{
    public const byte MinAreaId = 1;
    public const byte MaxAreaId = 32;

    public int ContinentId { get; }
    public int Width { get; }
    public int Height { get; }
    public float GridSize { get; }

    /// <summary>Pre-shifted area ids (G &gt;&gt; 3), length Width * Height, index = Height * cellX + cellZ.</summary>
    public byte[] AreaIds { get; }

    public ContinentAreaMask(int continentId, int width, int height, float gridSize, byte[] areaIds)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (gridSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(gridSize));
        ArgumentNullException.ThrowIfNull(areaIds);
        if (areaIds.Length != width * height)
            throw new ArgumentException($"AreaIds length {areaIds.Length} != width*height {width * height}", nameof(areaIds));

        ContinentId = continentId;
        Width = width;
        Height = height;
        GridSize = gridSize;
        AreaIds = areaIds;
    }

    /// <summary>
    /// World position → explored area id (0 if out of bounds or empty cell).
    /// Client: cell = (pos - GridSize*0.5) / GridSize; sample tileBuffer[height*cellX + cellZ] &gt;&gt; 3.
    /// </summary>
    public byte GetAreaId(float x, float z)
    {
        return SampleAreaId(x, z, GridSize, Width, Height, AreaIds);
    }

    public static byte SampleAreaId(float x, float z, float gridSize, int width, int height, byte[] areaIds)
    {
        if (areaIds == null || width <= 0 || height <= 0 || gridSize <= 0f)
            return 0;

        var cellX = (int)((x - gridSize * 0.5f) / gridSize);
        var cellZ = (int)((z - gridSize * 0.5f) / gridSize);

        if (cellX < 0 || cellX >= width || cellZ < 0 || cellZ >= height)
            return 0;

        var index = height * cellX + cellZ;
        if ((uint)index >= (uint)areaIds.Length)
            return 0;

        return areaIds[index];
    }

    /// <summary>Bit for area id 1..32, else 0.</summary>
    public static uint AreaBit(byte areaId)
    {
        if (areaId < MinAreaId || areaId > MaxAreaId)
            return 0;

        return 1u << (areaId - 1);
    }

    /// <summary>
    /// OR <paramref name="areaId"/> into <paramref name="bits"/> if new.
    /// Returns true when the bit was not already set.
    /// </summary>
    public static bool TryAddArea(ref uint bits, byte areaId, out uint newBits)
    {
        var mask = AreaBit(areaId);
        if (mask == 0 || (bits & mask) != 0)
        {
            newBits = bits;
            return false;
        }

        newBits = bits | mask;
        bits = newBits;
        return true;
    }
}
