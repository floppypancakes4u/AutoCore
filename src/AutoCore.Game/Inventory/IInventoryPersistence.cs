namespace AutoCore.Game.Inventory;

/// <summary>
/// Per-operation persistence for character cargo and vehicle equipment FKs.
/// </summary>
public interface IInventoryPersistence
{
    IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid);

    IReadOnlyList<CharacterInventoryItem> LoadLocker(long characterCoid);

    void UpsertCargo(long characterCoid, CharacterInventoryItem item);

    void UpsertLocker(long characterCoid, CharacterInventoryItem item);

    void MoveCargo(long characterCoid, CharacterInventoryItem item);

    void MoveLocker(long characterCoid, CharacterInventoryItem item);

    void DeleteCargo(long characterCoid, long itemCoid);

    void DeleteLocker(long characterCoid, long itemCoid);

    void ClearCargo(long characterCoid);

    void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0);

    /// <summary>
    /// SS-31: release a caller-preallocated simple_object placeholder row (Type == 0, CBID == 0)
    /// that ended up unused — e.g. a merge-only accept absorbed the whole add and the placeholder
    /// coid was never placed. No-op if the row was already filled in (real identity) or does not
    /// exist. Must never delete a row that is not a placeholder.
    /// </summary>
    void ReleaseUnusedPlaceholder(long coid);

    void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot);

    void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount);

    /// <summary>Load absolute credits balance for a character (0 if missing).</summary>
    long LoadCredits(long characterCoid);

    /// <summary>Persist absolute credits balance for a character.</summary>
    void SaveCredits(long characterCoid, long credits);
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
