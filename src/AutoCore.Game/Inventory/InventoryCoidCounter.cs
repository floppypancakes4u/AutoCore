namespace AutoCore.Game.Inventory;

using AutoCore.Game.Entities;

public static class InventoryCoidCounter
{
    /// <summary>
    /// Bump map local COID counter past any cargo or locker item COID so new
    /// allocations never collide with persisted inventory.
    /// </summary>
    public static void SyncFromCargo(Character character)
    {
        if (character?.Map == null)
            return;

        long maxInventoryCoid = 0;
        var hasAny = false;
        foreach (var item in character.Inventory.Items)
        {
            hasAny = true;
            if (item.Coid > maxInventoryCoid)
                maxInventoryCoid = item.Coid;
        }

        foreach (var item in character.Inventory.LockerItems)
        {
            hasAny = true;
            if (item.Coid > maxInventoryCoid)
                maxInventoryCoid = item.Coid;
        }

        if (!hasAny)
            return;

        if (maxInventoryCoid >= character.Map.LocalCoidCounter)
            character.Map.LocalCoidCounter = maxInventoryCoid + 1;
    }
}
