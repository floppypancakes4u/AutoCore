using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

/// <summary>
/// Pass 19 — VehicleTemplate catalog / cache / diagnostic contracts that do not need live FAM I/O.
/// </summary>
[TestClass]
public class VehicleTemplateResolutionTests
{
    [TestInitialize]
    public void TestInitialize()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        GetWorldDbLoader().VehicleTemplates = null;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        GetWorldDbLoader().VehicleTemplates = null;
    }

    [TestMethod]
    public void DescribeVehicleTemplateLookupFailure_NamesCatalogOrMissingRow()
    {
        Assert.AreEqual("tVehicleTemplate catalog not loaded",
            AssetManager.Instance.DescribeVehicleTemplateLookupFailure(7));

        GetWorldDbLoader().VehicleTemplates = new Dictionary<int, VehicleTemplate>();
        Assert.AreEqual("tVehicleTemplate row missing id=7",
            AssetManager.Instance.DescribeVehicleTemplateLookupFailure(7));
    }

    [TestMethod]
    public void VehicleTemplateCache_MissDoesNotPermanentlyHideLateData()
    {
        Assert.IsNull(AssetManager.Instance.GetVehicleTemplate(42),
            "an empty catalog must miss");

        GetWorldDbLoader().VehicleTemplates = new Dictionary<int, VehicleTemplate>
        {
            [42] = new VehicleTemplate { Id = 42, VehicleCbid = 2069, DriverCbid = 2071 },
        };

        var late = AssetManager.Instance.GetVehicleTemplate(42);
        Assert.IsNotNull(late, "a later wad.xml / WorldDB load must become visible; do not cache NULL misses");
        Assert.AreEqual(2069, late!.VehicleCbid);
    }

    [TestMethod]
    public void VehicleTemplateLoader_LoadsBeforeMapInitialization()
    {
        const int templateId = 880_001;
        const int vehicleCbid = 880_002;
        const int driverCbid = 880_003;
        const int wheelCbid = 880_004;

        AssetManagerTestHelper.RegisterCloneBase(wheelCbid, AutoCore.Game.Constants.CloneBaseObjectType.WheelSet);
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultDriverCbid: driverCbid, defaultWheelsetCbid: wheelCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, isNpc: 0);

        GetWorldDbLoader().VehicleTemplates = new Dictionary<int, VehicleTemplate>
        {
            [templateId] = new VehicleTemplate
            {
                Id = templateId,
                VehicleCbid = vehicleCbid,
                DriverCbid = driverCbid,
            },
        };

        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = 9919,
            MapFileName = "tm_template_init_order",
            DisplayName = "init-order",
            IsTown = false,
            IsPersistent = true,
        }, new AutoCore.Game.Structures.Vector4(0, 0, 0, 0));
        var template = new SpawnPointTemplate
        {
            COID = 33_001,
            OriginalIsActive = true,
            IsActive = true,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = templateId,
            IsTemplate = true,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[template.COID] = template;

        map.InitializeLocalObjectsForTests();

        var vehicles = map.Objects.Values.OfType<Vehicle>().ToList();
        Assert.AreEqual(1, vehicles.Count,
            "SectorMap init must see VehicleTemplates that were loaded before map construction");
        Assert.AreEqual(templateId, vehicles[0].TemplateId);
    }

    [TestMethod]
    public void MissingVehicleTemplate_LogsMapSpawnAndTemplateId()
    {
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = 9920,
            MapFileName = "tm_template_miss_log",
            DisplayName = "miss-log",
            IsTown = false,
            IsPersistent = true,
        }, new AutoCore.Game.Structures.Vector4(0, 0, 0, 0));
        var template = new SpawnPointTemplate { COID = 33_010 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = 777_001,
            IsTemplate = true,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        var spawn = new SpawnPoint(template);
        spawn.SetCoid(template.COID, false);
        spawn.SetMap(map);

        Assert.IsFalse(spawn.Spawn());
        var diag = spawn.LastFailureDiagnostic ?? string.Empty;
        StringAssert.Contains(diag, "777001");
        StringAssert.Contains(diag, "33010");
        StringAssert.Contains(diag, "9920");
        StringAssert.Contains(diag, "VehicleTemplate");
        Assert.IsTrue(
            diag.Contains("tVehicleTemplate", StringComparison.Ordinal)
            || diag.Contains("catalog", StringComparison.OrdinalIgnoreCase),
            "the miss must name the missing table/catalog, not only 'GetVehicleTemplate returned null'. got: "
            + diag);
    }

    [TestMethod]
    public void VehicleTemplateImport_PreservesTemplateId()
    {
        var path = WriteTempWadXml(
            """
            <wad>
              <tVehicleTemplate>
                <row><IDVehicleTemplate>580</IDVehicleTemplate><CBIDVehicle>3100</CBIDVehicle><CBIDDriver>3101</CBIDDriver><CBIDWeaponTurret>-1</CBIDWeaponTurret><CBIDWeaponFront>-1</CBIDWeaponFront><CBIDArmor>-1</CBIDArmor><sinBaseLevel>4</sinBaseLevel><intBaseHP>150</intBaseHP><CBIDWeaponMelee>-1</CBIDWeaponMelee></row>
              </tVehicleTemplate>
            </wad>
            """);
        try
        {
            var loaded = WadXmlWorldDataLoader.LoadVehicleTemplates(path);
            Assert.IsTrue(loaded.ContainsKey(580));
            Assert.AreEqual(580, loaded[580].Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void VehicleTemplateImport_PreservesChassisCbid()
    {
        var path = WriteTempWadXml(
            """
            <wad>
              <tVehicleTemplate>
                <row><IDVehicleTemplate>7</IDVehicleTemplate><CBIDVehicle>3100</CBIDVehicle><CBIDDriver>3101</CBIDDriver></row>
              </tVehicleTemplate>
            </wad>
            """);
        try
        {
            Assert.AreEqual(3100, WadXmlWorldDataLoader.LoadVehicleTemplates(path)[7].VehicleCbid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void VehicleTemplateImport_PreservesWheelSet()
    {
        // wad.xml tVehicleTemplate has no wheel column — retail wheels come from chassis DefaultWheelset.
        var path = WriteTempWadXml(
            """
            <wad>
              <tVehicleTemplate>
                <row><IDVehicleTemplate>1</IDVehicleTemplate><CBIDVehicle>2069</CBIDVehicle><CBIDDriver>2071</CBIDDriver></row>
              </tVehicleTemplate>
            </wad>
            """);
        try
        {
            var row = WadXmlWorldDataLoader.LoadVehicleTemplates(path)[1];
            Assert.AreEqual(2069, row.VehicleCbid,
                "wheel set is not a tVehicleTemplate column; chassis CBID must survive so DefaultWheelset can resolve");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void VehicleTemplateImport_PreservesDriver()
    {
        var path = WriteTempWadXml(
            """
            <wad>
              <tVehicleTemplate>
                <row><IDVehicleTemplate>1</IDVehicleTemplate><CBIDVehicle>2069</CBIDVehicle><CBIDDriver>2071</CBIDDriver></row>
              </tVehicleTemplate>
            </wad>
            """);
        try
        {
            Assert.AreEqual(2071, WadXmlWorldDataLoader.LoadVehicleTemplates(path)[1].DriverCbid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void VehicleTemplateImport_LastRowWinsOnDuplicateId()
    {
        var path = WriteTempWadXml(
            """
            <wad>
              <tVehicleTemplate>
                <row><IDVehicleTemplate>9</IDVehicleTemplate><CBIDVehicle>100</CBIDVehicle><CBIDDriver>1</CBIDDriver><intBaseHP>10</intBaseHP></row>
                <row><IDVehicleTemplate>9</IDVehicleTemplate><CBIDVehicle>200</CBIDVehicle><CBIDDriver>2</CBIDDriver><intBaseHP>99</intBaseHP></row>
              </tVehicleTemplate>
            </wad>
            """);
        try
        {
            var loaded = WadXmlWorldDataLoader.LoadVehicleTemplates(path);
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(200, loaded[9].VehicleCbid, "retail catalog is keyed by IDVehicleTemplate; last row replaces");
            Assert.AreEqual(99, loaded[9].BaseHp);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempWadXml(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vt-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    private static WorldDBLoader GetWorldDbLoader()
    {
        return (WorldDBLoader)typeof(AssetManager)
            .GetProperty("WorldDBLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
    }
}
