namespace AutoCore.Game.Inventory;

using AutoCore.Game.Entities;

public static class InventoryCoidCounter
{
    public static void SyncFromCargo(Character character)
    {
        if (character?.Map == null || character.Inventory.Items.Count == 0)
            return;

        var maxInventoryCoid = character.Inventory.Items.Max(i => i.Coid);
        if (maxInventoryCoid >= character.Map.LocalCoidCounter)
            character.Map.LocalCoidCounter = maxInventoryCoid + 1;
    }
}
