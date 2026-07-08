using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryCatalogTests
{
    [TestMethod]
    public void GetInventoryItems_IncludesInventoryTypesAndExcludesWorldTypes()
    {
        var catalog = new InventoryCatalog(() => new[]
        {
            Entry(100, CloneBaseObjectType.Item, "Plain Item"),
            Entry(101, CloneBaseObjectType.Commodity, "Scrap"),
            Entry(102, CloneBaseObjectType.Gadget, "Gadget"),
            Entry(103, CloneBaseObjectType.PowerPlant, "Plant"),
            Entry(104, CloneBaseObjectType.Weapon, "Weapon"),
            Entry(105, CloneBaseObjectType.Vehicle, "Vehicle"),
            Entry(106, CloneBaseObjectType.WheelSet, "Wheels"),
            Entry(107, CloneBaseObjectType.Armor, "Armor"),
            Entry(108, CloneBaseObjectType.TinkeringKit, "Kit"),
            Entry(109, CloneBaseObjectType.Accessory, "Accessory"),
            Entry(110, CloneBaseObjectType.RaceItem, "Race Item"),
            Entry(111, CloneBaseObjectType.Ornament, "Ornament"),
            Entry(200, CloneBaseObjectType.Creature, "Creature"),
            Entry(201, CloneBaseObjectType.Character, "Character"),
            Entry(202, CloneBaseObjectType.Store, "Store"),
            Entry(203, CloneBaseObjectType.SpawnPoint, "Spawn"),
            Entry(204, CloneBaseObjectType.Trigger, "Trigger"),
            Entry(205, CloneBaseObjectType.Reaction, "Reaction"),
            Entry(206, CloneBaseObjectType.Bullet, "Bullet"),
            Entry(207, CloneBaseObjectType.Money, "Money"),
            Entry(208, CloneBaseObjectType.Object, "World Object"),
        });

        var items = catalog.GetInventoryItems();

        CollectionAssert.AreEquivalent(
            new[] { 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111 },
            items.Select(i => i.Cbid).ToArray());
    }

    [TestMethod]
    public void FormatPage_DefaultsToFirstPageAndFormatsRows()
    {
        var catalog = new InventoryCatalog(() => new[]
        {
            Entry(3, CloneBaseObjectType.Weapon, "Zeta"),
            Entry(2, CloneBaseObjectType.Item, "Beta"),
            Entry(1, CloneBaseObjectType.Item, "Alpha"),
        });

        var message = catalog.FormatPage(null);

        Assert.AreEqual(
            "Items page 1/1\n" +
            "1 | Item | Alpha\n" +
            "2 | Item | Beta\n" +
            "3 | Weapon | Zeta\n" +
            "Use /listItems 1 or /addItem 1",
            message);
    }

    [TestMethod]
    public void FormatPage_ReturnsValidRangeForInvalidPage()
    {
        var catalog = new InventoryCatalog(() => Enumerable.Range(1, 11)
            .Select(i => Entry(i, CloneBaseObjectType.Item, $"Item {i:00}")));

        var message = catalog.FormatPage("3");

        Assert.AreEqual("Invalid page 3. Valid pages: 1-2.", message);
    }

    private static InventoryCatalogEntry Entry(int cbid, CloneBaseObjectType type, string name)
    {
        return new InventoryCatalogEntry(cbid, type, name);
    }
}
