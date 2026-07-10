namespace AutoCore.Game.Inventory;

using AutoCore.Game.CloneBases;

/// <summary>
/// Pure stack rules for cargo items (max size, split eligibility, merge amounts).
/// </summary>
public static class InventoryStackPolicy
{
    public static (int MaxStackSize, bool Stackable) GetLimits(CloneBase cloneBase)
    {
        var raw = 0;
        if (cloneBase is CloneBaseObject obj)
            raw = obj.SimpleObjectSpecific.StackSize;

        return InventoryItemExporter.NormalizeStackSize(raw);
    }

    /// <summary>
    /// Partial split when the client asks for a positive amount strictly less than the source stack.
    /// </summary>
    public static bool IsPartialSplitRequest(int sourceQuantity, int requestedCount) =>
        sourceQuantity > 1 && requestedCount > 0 && requestedCount < sourceQuantity;

    public static int ComputeMergeAmount(int currentQuantity, int incomingQuantity, int maxStack)
    {
        if (incomingQuantity <= 0 || maxStack <= 0)
            return 0;

        var space = maxStack - currentQuantity;
        if (space <= 0)
            return 0;

        return Math.Min(incomingQuantity, space);
    }
}
