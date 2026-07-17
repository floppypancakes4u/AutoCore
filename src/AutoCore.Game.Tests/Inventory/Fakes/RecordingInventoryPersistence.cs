using AutoCore.Game.Inventory;

namespace AutoCore.Game.Tests.Inventory.Fakes;

public sealed class RecordingInventoryPersistence : IInventoryPersistence
{
    public List<(long CharacterCoid, CharacterInventoryItem Item)> Upserted { get; } = new();
    public List<(long CharacterCoid, CharacterInventoryItem Item)> LockerUpserted { get; } = new();
    public List<(long CharacterCoid, CharacterInventoryItem Item)> Moved { get; } = new();
    public List<(long CharacterCoid, CharacterInventoryItem Item)> LockerMoved { get; } = new();
    public List<long> DeletedItemCoids { get; } = new();
    public List<long> LockerDeletedItemCoids { get; } = new();
    public List<(long VehicleCoid, VehicleEquipmentSnapshot Snapshot)> EquipmentSaves { get; } = new();
    public List<(long CharacterCoid, int Width, int PageCount)> CapacitySaves { get; } = new();
    public List<(long CharacterCoid, long Credits)> CreditsSaves { get; } = new();
    public List<(long ItemCoid, byte Type, int Cbid)> EnsuredSimpleObjects { get; } = new();

    public List<CharacterInventoryItem> CargoToLoad { get; } = new();
    public List<CharacterInventoryItem> LockerToLoad { get; } = new();
    public long CreditsToLoad { get; set; }

    public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) => CargoToLoad;

    public IReadOnlyList<CharacterInventoryItem> LoadLocker(long characterCoid) => LockerToLoad;

    public void UpsertCargo(long characterCoid, CharacterInventoryItem item) =>
        Upserted.Add((characterCoid, item));

    public void UpsertLocker(long characterCoid, CharacterInventoryItem item) =>
        LockerUpserted.Add((characterCoid, item));

    public void MoveCargo(long characterCoid, CharacterInventoryItem item) =>
        Moved.Add((characterCoid, item));

    public void MoveLocker(long characterCoid, CharacterInventoryItem item) =>
        LockerMoved.Add((characterCoid, item));

    public void DeleteCargo(long characterCoid, long itemCoid) =>
        DeletedItemCoids.Add(itemCoid);

    public void DeleteLocker(long characterCoid, long itemCoid) =>
        LockerDeletedItemCoids.Add(itemCoid);

    public List<long> ClearedCharacterCoids { get; } = new();

    public void ClearCargo(long characterCoid) =>
        ClearedCharacterCoids.Add(characterCoid);

    public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0) =>
        EnsuredSimpleObjects.Add((itemCoid, type, cbid));

    public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot) =>
        EquipmentSaves.Add((vehicleCoid, snapshot));

    public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount) =>
        CapacitySaves.Add((characterCoid, width, pageCount));

    public long LoadCredits(long characterCoid) => CreditsToLoad;

    public void SaveCredits(long characterCoid, long credits)
    {
        CreditsToLoad = credits;
        CreditsSaves.Add((characterCoid, credits));
    }
}
