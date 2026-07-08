using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryPersistenceTests
{
    [TestMethod]
    public void RecordingPersistence_CapturesCargoUpsertMoveDeleteAndEquipmentSave()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        inventory.SetCapacity(4, 1);

        var item = new CharacterInventoryItem(10, CloneBaseObjectType.Weapon, "Gun", 100, 0, 0, 1);
        Assert.IsTrue(inventory.TryAdd(item));
        persistence.UpsertCargo(5001, item);

        Assert.AreEqual(1, persistence.Upserted.Count);
        Assert.AreEqual(5001, persistence.Upserted[0].CharacterCoid);
        Assert.AreEqual(100, persistence.Upserted[0].Item.Coid);

        Assert.IsTrue(inventory.TryMove(100, 1, 0, out var moved));
        persistence.MoveCargo(5001, moved);
        Assert.AreEqual(1, persistence.Moved.Count);
        Assert.AreEqual((byte)1, persistence.Moved[0].Item.InventoryPositionX);

        persistence.DeleteCargo(5001, 100);
        Assert.AreEqual(1, persistence.DeletedItemCoids.Count);
        Assert.AreEqual(100L, persistence.DeletedItemCoids[0]);

        persistence.SaveVehicleEquipment(9001, new VehicleEquipmentSnapshot(0, 0, 0, 0, 0, 0, 0, 55, 0));
        Assert.AreEqual(1, persistence.EquipmentSaves.Count);
        Assert.AreEqual(55L, persistence.EquipmentSaves[0].Snapshot.Turret);
    }

    private sealed class RecordingInventoryPersistence : IInventoryPersistence
    {
        public List<(long CharacterCoid, CharacterInventoryItem Item)> Upserted { get; } = new();
        public List<(long CharacterCoid, CharacterInventoryItem Item)> Moved { get; } = new();
        public List<long> DeletedItemCoids { get; } = new();
        public List<(long VehicleCoid, VehicleEquipmentSnapshot Snapshot)> EquipmentSaves { get; } = new();

        public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) => Array.Empty<CharacterInventoryItem>();

        public void UpsertCargo(long characterCoid, CharacterInventoryItem item) =>
            Upserted.Add((characterCoid, item));

        public void MoveCargo(long characterCoid, CharacterInventoryItem item) =>
            Moved.Add((characterCoid, item));

        public void DeleteCargo(long characterCoid, long itemCoid) =>
            DeletedItemCoids.Add(itemCoid);

        public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0)
        {
        }

        public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot) =>
            EquipmentSaves.Add((vehicleCoid, snapshot));

        public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount)
        {
        }
    }
}
