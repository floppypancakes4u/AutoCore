using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class VehicleEquipmentSnapshotTests
{
    [TestMethod]
    public void VehicleEquipmentSnapshot_RecordStoresSlotCoids()
    {
        var snapshot = new VehicleEquipmentSnapshot(
            Ornament: 1,
            RaceItem: 2,
            PowerPlant: 3,
            Wheelset: 4,
            Armor: 5,
            MeleeWeapon: 6,
            Front: 7,
            Turret: 8,
            Rear: 9);

        Assert.AreEqual(1, snapshot.Ornament);
        Assert.AreEqual(8, snapshot.Turret);
        Assert.AreEqual(9, snapshot.Rear);
    }
}
