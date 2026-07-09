namespace AutoCore.Game.Managers.Asset;

/// <summary>
/// Minimal TGA reader that extracts explored-area ids from the G channel (byte &gt;&gt; 3).
/// Supports uncompressed true-color (type 2) 24/32-bit, matching CVOGTerrain_LoadMapImage.
/// </summary>
public static class TgaAreaMaskReader
{
    /// <summary>
    /// Reads a TGA stream and returns pre-shifted area ids (G &gt;&gt; 3) for each pixel.
    /// Index order matches the client: <c>height * cellX + cellZ</c> where X is width axis and Z is height axis.
    /// </summary>
    public static bool TryReadAreaIds(Stream stream, out int width, out int height, out byte[] areaIds, out string error)
    {
        width = 0;
        height = 0;
        areaIds = null;
        error = null;

        if (stream == null || !stream.CanRead)
        {
            error = "Stream is null or not readable";
            return false;
        }

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        if (stream.Length - stream.Position < 18)
        {
            error = "TGA header too short";
            return false;
        }

        var idLength = reader.ReadByte();
        var colorMapType = reader.ReadByte();
        var imageType = reader.ReadByte();
        reader.ReadBytes(5); // color map specification
        reader.ReadUInt16(); // x origin
        reader.ReadUInt16(); // y origin
        width = reader.ReadUInt16();
        height = reader.ReadUInt16();
        var bitsPerPixel = reader.ReadByte();
        var imageDescriptor = reader.ReadByte();

        if (width <= 0 || height <= 0)
        {
            error = $"Invalid TGA dimensions {width}x{height}";
            return false;
        }

        if (imageType != 2)
        {
            error = $"Unsupported TGA image type {imageType} (need uncompressed true-color type 2)";
            return false;
        }

        if (bitsPerPixel != 24 && bitsPerPixel != 32)
        {
            error = $"Unsupported TGA bpp {bitsPerPixel} (need 24 or 32)";
            return false;
        }

        if (colorMapType != 0)
        {
            error = $"Unsupported TGA color map type {colorMapType}";
            return false;
        }

        if (idLength > 0)
            reader.ReadBytes(idLength);

        var bytesPerPixel = bitsPerPixel / 8;
        var pixelCount = width * height;
        long remaining;
        try
        {
            remaining = stream.Length - stream.Position;
        }
        catch
        {
            remaining = long.MaxValue;
        }

        if (remaining < (long)pixelCount * bytesPerPixel)
        {
            error = "TGA image data truncated";
            return false;
        }

        var raw = reader.ReadBytes(pixelCount * bytesPerPixel);
        if (raw.Length < pixelCount * bytesPerPixel)
        {
            error = "TGA image data truncated";
            return false;
        }

        // Image descriptor bit 5: 1 = top-left origin, 0 = bottom-left (TGA default).
        // Client NDAssetImage TGA load (FUN_004347d0): bottom-origin keeps file order;
        // top-origin 32bpp is vertically flipped (FUN_004332e0) so GetPixel y=0 is always
        // the image *bottom*. Terrain then stores height*x+y and samples the same way
        // (FUN_004a8b90). Match that: cellZ=0 must be the bottom of the map image.
        var topOrigin = (imageDescriptor & 0x20) != 0;

        // Client samples index = height * cellX + cellZ.
        // TGA columns → world X (cellX); rows → world Z (cellZ).
        areaIds = new byte[pixelCount];
        for (var row = 0; row < height; ++row)
        {
            // Bottom-origin: file row 0 is bottom → cellZ 0.
            // Top-origin: file row 0 is top → after client flip, bottom is last file row.
            var srcRow = topOrigin ? (height - 1 - row) : row;
            for (var col = 0; col < width; ++col)
            {
                var srcIndex = (srcRow * width + col) * bytesPerPixel;
                // BGRA/BGR: G is at +1
                var g = raw[srcIndex + 1];
                var areaId = (byte)(g >> 3);
                // cellX = col (width axis), cellZ = row (height axis)
                areaIds[height * col + row] = areaId;
            }
        }

        return true;
    }
}
