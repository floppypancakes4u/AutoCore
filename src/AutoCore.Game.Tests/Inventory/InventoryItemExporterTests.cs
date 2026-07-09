using AutoCore.Game.Inventory;
using AutoCore.Game.Managers.Asset;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryItemExporterTests
{
    private const string DefaultWadPath = @"C:\Program Files (x86)\NetDevil\Auto Assault\clonebase.wad";

    [TestMethod]
    public void NormalizeStackSize_ZeroMeansSingleNonStackable()
    {
        var (maxStack, stackable) = InventoryItemExporter.NormalizeStackSize(0);

        Assert.AreEqual(1, maxStack);
        Assert.IsFalse(stackable);
    }

    [TestMethod]
    public void NormalizeStackSize_OneMeansSingleNonStackable()
    {
        var (maxStack, stackable) = InventoryItemExporter.NormalizeStackSize(1);

        Assert.AreEqual(1, maxStack);
        Assert.IsFalse(stackable);
    }

    [TestMethod]
    public void NormalizeStackSize_GreaterThanOneIsStackable()
    {
        var (maxStack, stackable) = InventoryItemExporter.NormalizeStackSize(999);

        Assert.AreEqual(999, maxStack);
        Assert.IsTrue(stackable);
    }

    [TestMethod]
    public void Serialize_ProducesCamelCaseJson()
    {
        var document = new InventoryItemExportDocument(
            "2026-07-08T00:00:00.0000000Z",
            "clonebase.wad",
            1,
            new[]
            {
                new InventoryItemExportRecord(
                    938,
                    "Commodity",
                    26,
                    "item_res_n_aliengoo_1",
                    "Salvaged Blood",
                    "Long description",
                    999,
                    999,
                    true,
                    1,
                    1,
                    0,
                    0)
            });

        var json = InventoryItemExporter.Serialize(document);

        StringAssert.Contains(json, "\"displayName\": \"Salvaged Blood\"");
        StringAssert.Contains(json, "\"maxStackSize\": 999");
        StringAssert.Contains(json, "\"className\": \"Commodity\"");
    }

    [TestMethod]
    public void ExportFromCloneBases_WhenWadAvailable_ExportsInventoryItemsWithDisplayNames()
    {
        if (!File.Exists(DefaultWadPath))
        {
            Assert.Inconclusive($"Game data not installed at {DefaultWadPath}");
            return;
        }

        var loader = new WADLoader();
        Assert.IsTrue(loader.Load(DefaultWadPath));

        var document = InventoryItemExporter.ExportFromCloneBases(loader.CloneBases, DefaultWadPath);

        Assert.IsTrue(document.ItemCount > 1000);
        var salvagedBlood = document.Items.FirstOrDefault(item => item.Cbid == 938);
        Assert.IsNotNull(salvagedBlood);
        Assert.AreEqual("Commodity", salvagedBlood.ClassName);
        Assert.AreEqual("item_res_n_aliengoo_1", salvagedBlood.UniqueName);
        Assert.AreEqual("Salvaged Blood", salvagedBlood.DisplayName);
        Assert.AreEqual(999, salvagedBlood.MaxStackSize);
        Assert.IsTrue(salvagedBlood.Stackable);
    }

    [TestMethod]
    public void ExportFromCloneBases_WhenWadAvailable_ExcludesNonInventoryTypes()
    {
        if (!File.Exists(DefaultWadPath))
        {
            Assert.Inconclusive($"Game data not installed at {DefaultWadPath}");
            return;
        }

        var loader = new WADLoader();
        Assert.IsTrue(loader.Load(DefaultWadPath));

        var document = InventoryItemExporter.ExportFromCloneBases(loader.CloneBases, DefaultWadPath);

        Assert.IsFalse(document.Items.Any(item => item.ClassName is "Trigger" or "Reaction" or "SpawnPoint"));
        Assert.IsTrue(document.Items.Any(item => item.ClassName == "Weapon"));
        Assert.IsTrue(document.Items.Any(item => item.ClassName == "Commodity"));
    }

    [TestMethod]
    public void GenerateStandalone_EmbedsCatalogAndRendersAppShell()
    {
        var document = new InventoryItemExportDocument(
            "2026-07-08T00:00:00.0000000Z",
            "clonebase.wad",
            1,
            new[]
            {
                new InventoryItemExportRecord(
                    5788,
                    "Commodity",
                    26,
                    "item_res_n_pneumatics_1",
                    "Salvaged Pneumatics",
                    "Compressed air fittings.",
                    999,
                    999,
                    true,
                    1,
                    1,
                    0,
                    -1)
            });

        var html = InventoryCatalogHtmlGenerator.GenerateStandalone(document);

        StringAssert.Contains(html, "<script id=\"catalog-data\" type=\"application/json\">");
        StringAssert.Contains(html, "\"displayName\":\"Salvaged Pneumatics\"");
        StringAssert.Contains(html, "single-file catalog");
        StringAssert.Contains(html, "JSON.parse(document.getElementById(\"catalog-data\").textContent)");
    }
}
