namespace AutoCore.Game.Inventory;

/// <summary>
/// Pure cargo grid placement helpers matching client:
/// <list type="bullet">
/// <item><c>FUN_00570840</c> — CanPlace (bounds, page non-span, cell free)</item>
/// <item><c>FUN_005713a0</c> — first free top-left (Y outer, X inner)</item>
/// </list>
/// Coordinates are origin top-left; linear index is row-major <c>y * width + x</c>
/// (AutoCore wire convention; client cell storage is column-major but logical (x,y) match).
/// </summary>
public static class InventoryGridPlacement
{
    public readonly struct Cell
    {
        public Cell(byte x, byte y)
        {
            X = x;
            Y = y;
        }

        public byte X { get; }
        public byte Y { get; }
    }

    public static int ToLinearIndex(int x, int y, int width) => y * width + x;

    /// <summary>
    /// All cells of a footprint rectangle (row-major order within the rect: x then y).
    /// </summary>
    public static IEnumerable<Cell> EnumerateCells(byte originX, byte originY, byte sizeX, byte sizeY)
    {
        if (sizeX < 1 || sizeY < 1)
            yield break;

        for (byte dy = 0; dy < sizeY; dy++)
        {
            for (byte dx = 0; dx < sizeX; dx++)
            {
                yield return new Cell((byte)(originX + dx), (byte)(originY + dy));
            }
        }
    }

    /// <summary>
    /// Whether a <paramref name="sizeX"/>×<paramref name="sizeY"/> item can sit at (x,y).
    /// Page rule: item must not cross cargo page bands of height <paramref name="pageHeight"/>
    /// (client: <c>(y % pageH) + sizeY &lt;= pageH</c>).
    /// </summary>
    public static bool CanPlace(
        int gridWidth,
        int gridHeight,
        int pageHeight,
        ISet<(byte X, byte Y)> occupied,
        byte x,
        byte y,
        byte sizeX,
        byte sizeY)
    {
        if (occupied == null)
            throw new ArgumentNullException(nameof(occupied));

        if (sizeX < 1 || sizeY < 1)
            return false;

        if (gridWidth < 1 || gridHeight < 1 || pageHeight < 1)
            return false;

        if (x + sizeX > gridWidth || y + sizeY > gridHeight)
            return false;

        // Client FUN_00570840: cannot span cargo pages.
        if ((y % pageHeight) + sizeY > pageHeight)
            return false;

        foreach (var cell in EnumerateCells(x, y, sizeX, sizeY))
        {
            if (occupied.Contains((cell.X, cell.Y)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// First-fit scan matching client <c>FUN_005713a0</c> (page filter = all pages):
    /// for y ascending, for x ascending, require origin free then full CanPlace.
    /// </summary>
    public static bool TryFindFirstFree(
        int gridWidth,
        int gridHeight,
        int pageHeight,
        ISet<(byte X, byte Y)> occupied,
        byte sizeX,
        byte sizeY,
        out byte x,
        out byte y)
    {
        x = 0;
        y = 0;

        if (occupied == null)
            throw new ArgumentNullException(nameof(occupied));

        if (sizeX < 1 || sizeY < 1 || gridWidth < sizeX || gridHeight < sizeY)
            return false;

        var maxY = gridHeight - sizeY;
        var maxX = gridWidth - sizeX;

        for (var ty = 0; ty <= maxY; ty++)
        {
            for (var tx = 0; tx <= maxX; tx++)
            {
                var bx = (byte)tx;
                var by = (byte)ty;

                // Client quick-reject: origin cell empty before full rect check.
                if (occupied.Contains((bx, by)))
                    continue;

                if (!CanPlace(gridWidth, gridHeight, pageHeight, occupied, bx, by, sizeX, sizeY))
                    continue;

                x = bx;
                y = by;
                return true;
            }
        }

        return false;
    }
}
