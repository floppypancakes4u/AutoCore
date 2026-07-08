namespace AutoCore.Game.Inventory;

/// <summary>
/// Per-operation persistence for character cargo and vehicle equipment FKs.
/// </summary>
public interface IInventoryPersistence
{
    IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid);

    void UpsertCargo(long characterCoid, CharacterInventoryItem item);

    void MoveCargo(long characterCoid, CharacterInventoryItem item);

    void DeleteCargo(long characterCoid, long itemCoid);

    void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0);

    void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot);

    void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount);
}

public readonly record struct VehicleEquipmentSnapshot(
    long Ornament,
    long RaceItem,
    long PowerPlant,
    long Wheelset,
    long Armor,
    long MeleeWeapon,
    long Front,
    long Turret,
    long Rear);
