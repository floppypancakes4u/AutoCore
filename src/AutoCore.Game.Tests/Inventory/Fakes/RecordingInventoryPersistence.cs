using AutoCore.Game.Inventory;

namespace AutoCore.Game.Tests.Inventory.Fakes;

public sealed class RecordingInventoryPersistence : IInventoryPersistence
{
    public List<(long CharacterCoid, CharacterInventoryItem Item)> Upserted { get; } = new();
    public List<(long CharacterCoid, CharacterInventoryItem Item)> Moved { get; } = new();
    public List<long> DeletedItemCoids { get; } = new();
    public List<(long VehicleCoid, VehicleEquipmentSnapshot Snapshot)> EquipmentSaves { get; } = new();
    public List<(long CharacterCoid, int Width, int PageCount)> CapacitySaves { get; } = new();
    public List<(long ItemCoid, byte Type, int Cbid)> EnsuredSimpleObjects { get; } = new();

    public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) => Array.Empty<CharacterInventoryItem>();

    public void UpsertCargo(long characterCoid, CharacterInventoryItem item) =>
        Upserted.Add((characterCoid, item));

    public void MoveCargo(long characterCoid, CharacterInventoryItem item) =>
        Moved.Add((characterCoid, item));

    public void DeleteCargo(long characterCoid, long itemCoid) =>
        DeletedItemCoids.Add(itemCoid);

    public List<long> ClearedCharacterCoids { get; } = new();

    public void ClearCargo(long characterCoid) =>
        ClearedCharacterCoids.Add(characterCoid);

    public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0) =>
        EnsuredSimpleObjects.Add((itemCoid, type, cbid));

    public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot) =>
        EquipmentSaves.Add((vehicleCoid, snapshot));

    public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount) =>
        CapacitySaves.Add((characterCoid, width, pageCount));
}
