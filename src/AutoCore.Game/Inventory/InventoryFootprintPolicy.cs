namespace AutoCore.Game.Inventory;

using AutoCore.Game.CloneBases;

/// <summary>
/// Resolves inventory grid footprint from clonebase <c>tinInventorySizeX/Y</c>
/// (<see cref="CloneBases.Specifics.SimpleObjectSpecific.InvSizeX"/> /
/// <see cref="CloneBases.Specifics.SimpleObjectSpecific.InvSizeY"/>).
/// Client places using the same fields (object blob +0x406 / +0x407).
/// Positive sizes only — zero or missing size is a hard reject (no silent 1×1 fallback).
/// </summary>
public static class InventoryFootprintPolicy
{
    /// <summary>
    /// Resolve footprint from a loaded clonebase. Requires a <see cref="CloneBaseObject"/>
    /// with both dimensions ≥ 1.
    /// </summary>
    public static bool TryResolve(CloneBase cloneBase, out byte sizeX, out byte sizeY)
    {
        sizeX = 0;
        sizeY = 0;

        if (cloneBase is not CloneBaseObject obj)
            return false;

        var x = obj.SimpleObjectSpecific.InvSizeX;
        var y = obj.SimpleObjectSpecific.InvSizeY;
        if (x < 1 || y < 1)
            return false;

        sizeX = x;
        sizeY = y;
        return true;
    }

    /// <summary>
    /// Resolve footprint via CBID lookup. Fails when the CBID is unknown or size is invalid.
    /// </summary>
    public static bool TryResolve(ICloneBaseLookup lookup, int cbid, out byte sizeX, out byte sizeY)
    {
        sizeX = 0;
        sizeY = 0;

        if (lookup == null || cbid <= 0)
            return false;

        return TryResolve(lookup.GetCloneBase(cbid), out sizeX, out sizeY);
    }
}
