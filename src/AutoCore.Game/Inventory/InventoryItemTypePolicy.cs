namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;

public static class InventoryItemTypePolicy
{
    public static bool IsInventoryCapable(CloneBaseObjectType type)
    {
        return type switch
        {
            CloneBaseObjectType.Item => true,
            CloneBaseObjectType.Commodity => true,
            CloneBaseObjectType.Gadget => true,
            CloneBaseObjectType.PowerPlant => true,
            CloneBaseObjectType.Weapon => true,
            CloneBaseObjectType.Vehicle => true,
            CloneBaseObjectType.WheelSet => true,
            CloneBaseObjectType.Armor => true,
            CloneBaseObjectType.TinkeringKit => true,
            CloneBaseObjectType.Accessory => true,
            CloneBaseObjectType.RaceItem => true,
            CloneBaseObjectType.Ornament => true,
            // Mission Inventory gear (deliver GiveItemOnStart / use-item objectives).
            CloneBaseObjectType.QuestObject => true,
            CloneBaseObjectType.MissionObject => true,
            _ => false
        };
    }
}
