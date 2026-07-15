using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerLoadRepackTests
{
    [TestMethod]
    public void LoadItems_OverlappingTwoByTwo_RepacksToNonOverlapping()
    {
        var lookup = new FakeCloneBaseLookup();
        lookup.Register(10, CreateClone(10, 2, 2));
        var inventory = new InventoryManager(cloneBases: lookup);

        var changed = inventory.LoadItems(new[]
        {
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 1, 0, 0, 1),
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "B", 2, 0, 0, 1), // same origin
        });

        Assert.IsTrue(changed);
        Assert.AreEqual(2, inventory.Items.Count);
        var a = inventory.FindByCoid(1);
        var b = inventory.FindByCoid(2);
        Assert.AreEqual((byte)0, a.InventoryPositionX);
        Assert.AreEqual((byte)0, a.InventoryPositionY);
        Assert.AreEqual((byte)2, b.InventoryPositionX);
        Assert.AreEqual((byte)0, b.InventoryPositionY);
        Assert.AreEqual(8, inventory.GetOccupiedSlotCount());
    }

    [TestMethod]
    public void LoadItems_ValidLayout_Unchanged()
    {
        var lookup = new FakeCloneBaseLookup();
        lookup.Register(10, CreateClone(10, 1, 1));
        var inventory = new InventoryManager(cloneBases: lookup);

        var changed = inventory.LoadItems(new[]
        {
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 1, 0, 0, 1),
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "B", 2, 1, 0, 1),
        });

        Assert.IsFalse(changed);
        Assert.AreEqual(2, inventory.Items.Count);
    }

    [TestMethod]
    public void ReloadCargo_WhenRepacked_PersistsClearAndUpserts()
    {
        var persistence = new RecordingInventoryPersistence();
        var lookup = new FakeCloneBaseLookup();
        lookup.Register(10, CreateClone(10, 2, 2));
        persistence.CargoToLoad.AddRange(new[]
        {
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 1, 0, 0, 1),
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "B", 2, 1, 0, 1), // overlaps 2×2
        });

        var inventory = new InventoryManager(persistence, lookup);
        inventory.ReloadCargo(5001);

        Assert.IsTrue(persistence.ClearedCharacterCoids.Contains(5001));
        Assert.IsTrue(persistence.Upserted.Count >= 2);
        Assert.AreEqual(2, inventory.Items.Count);
    }

    [TestMethod]
    public void LoadItems_DuplicateCoid_SkipsSecondAndReportsChanged()
    {
        var inventory = new InventoryManager();
        var changed = inventory.LoadItems(new[]
        {
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 100, 0, 0, 1),
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "DupCoid", 100, 1, 0, 1),
        });

        Assert.IsTrue(changed);
        Assert.AreEqual(1, inventory.Items.Count);
        Assert.AreEqual(0, inventory.FindByCoid(100).InventoryPositionX);
    }

    [TestMethod]
    public void LoadItems_WhenGridFull_DropsOverflowItem()
    {
        var lookup = new FakeCloneBaseLookup();
        lookup.Register(10, CreateClone(10, 1, 1));
        var inventory = new InventoryManager(cloneBases: lookup);
        inventory.SetCapacity(1, 1);

        var changed = inventory.LoadItems(new[]
        {
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 1, 0, 0, 1),
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "B", 2, 0, 0, 1),
        });

        Assert.IsTrue(changed);
        Assert.AreEqual(1, inventory.Items.Count);
        Assert.IsNotNull(inventory.FindByCoid(1));
        Assert.IsNull(inventory.FindByCoid(2));
    }

    [TestMethod]
    public void PersistRepackedCargo_NoOpsWhenPersistenceMissingOrCharacterZero()
    {
        var inventory = new InventoryManager();
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1));
        inventory.PersistRepackedCargo(0);

        var persistence = new RecordingInventoryPersistence();
        var withPersist = new InventoryManager(persistence);
        withPersist.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1));
        withPersist.PersistRepackedCargo(0);
        Assert.AreEqual(0, persistence.ClearedCharacterCoids.Count);

        withPersist.PersistRepackedCargo(5001);
        Assert.AreEqual(1, persistence.ClearedCharacterCoids.Count);
        Assert.AreEqual(1, persistence.Upserted.Count);
    }

    [TestMethod]
    public void ReloadCargo_NoOpWhenCharacterCoidZero()
    {
        var persistence = new RecordingInventoryPersistence();
        persistence.CargoToLoad.Add(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1));
        var inventory = new InventoryManager(persistence);
        inventory.ReloadCargo(0);
        Assert.AreEqual(0, inventory.Items.Count);
    }

    [TestMethod]
    public void TryAdd_NullItem_Fails()
    {
        var inventory = new InventoryManager();
        Assert.IsFalse(inventory.TryAdd(null));
    }

    [TestMethod]
    public void IsFull_WhenNoFreeOneByOne_IsTrue()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(1, 1);
        Assert.IsFalse(inventory.IsFull);
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1));
        Assert.IsTrue(inventory.IsFull);
    }

    [TestMethod]
    public void PersistRepackedCargo_WhenClearThrows_DoesNotPropagate()
    {
        var inventory = new InventoryManager(new ThrowingClearPersistence());
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1));
        inventory.PersistRepackedCargo(5001); // must not throw
    }

    private sealed class ThrowingClearPersistence : IInventoryPersistence
    {
        public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) => Array.Empty<CharacterInventoryItem>();
        public void UpsertCargo(long characterCoid, CharacterInventoryItem item) { }
        public void MoveCargo(long characterCoid, CharacterInventoryItem item) { }
        public void DeleteCargo(long characterCoid, long itemCoid) { }
        public void ClearCargo(long characterCoid) => throw new InvalidOperationException("db down");
        public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0) { }
        public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot) { }
        public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount) { }
        public long LoadCredits(long characterCoid) => 0;
        public void SaveCredits(long characterCoid, long credits) { }
    }

    private static CloneBaseObject CreateClone(int cbid, byte sizeX, byte sizeY)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { InvSizeX = sizeX, InvSizeY = sizeY };
        return clone;
    }
}
