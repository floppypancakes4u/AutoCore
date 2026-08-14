using System.Runtime.CompilerServices;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Prefixes;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GameMission = AutoCore.Game.Mission.Mission;
using GameMissionObjective = AutoCore.Game.Mission.MissionObjective;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>
/// Tripwire for the live-asset leak behind 24 of the 25 failures in the 2026-08-14
/// full-suite report.
///
/// The <c>LiveFam*</c> / live-map suites load the retail <c>clonebase.wad</c> into the
/// process-wide <see cref="AssetManager"/> singleton. They used to leave it loaded, so
/// every test that ran afterwards in the same assembly saw the retail catalog instead of
/// its own seeded fixtures: LootManager indexed 1434 generatable items on an "empty"
/// catalog, soft-path lookups for absent IDs hit real data, the HAD-E4Rs offer pin gained
/// a fourth mission, and cargo-grid tests placed items using retail footprints instead of
/// the 1x1 fakes they registered.
///
/// Each test here fails if the unload path is removed again.
/// </summary>
[TestClass]
public class LiveAssetIsolationTests
{
    private const int SeedCbid = 424_242;
    private const int SeedMissionId = 424_243;
    private const int SeedObjectiveId = 424_244;
    private const int SeedSkillId = 424_245;

    [TestMethod]
    public void Clear_EmptiesEveryWadCatalogTable()
    {
        var wad = new WADLoader();
        SeedEveryTable(wad);

        wad.Clear();

        Assert.AreEqual(0, wad.CloneBases.Count, nameof(wad.CloneBases));
        Assert.AreEqual(0, wad.Missions.Count, nameof(wad.Missions));
        Assert.AreEqual(0, wad.Skills.Count, nameof(wad.Skills));
        Assert.AreEqual(0, wad.ArmorPrefixes.Count, nameof(wad.ArmorPrefixes));
        Assert.AreEqual(0, wad.PowerPlantPrefixes.Count, nameof(wad.PowerPlantPrefixes));
        Assert.AreEqual(0, wad.WeaponPrefixes.Count, nameof(wad.WeaponPrefixes));
        Assert.AreEqual(0, wad.VehiclePrefixes.Count, nameof(wad.VehiclePrefixes));
        Assert.AreEqual(0, wad.OrnamentPrefixes.Count, nameof(wad.OrnamentPrefixes));
        Assert.AreEqual(0, wad.RaceItemPrefixes.Count, nameof(wad.RaceItemPrefixes));
    }

    [TestMethod]
    public void ClearLiveAssetsForTests_LeavesSingletonLookupsMissing()
    {
        var wad = AssetManagerTestHelper.GetWadLoader();
        SeedEveryTable(wad);

        try
        {
            // Guard: the seed really is visible through the singleton before the unload,
            // so the asserts below cannot pass vacuously.
            Assert.IsNotNull(AssetManager.Instance.GetCloneBase(SeedCbid), "seed not visible pre-clear");
            Assert.IsNotNull(AssetManager.Instance.GetMission(SeedMissionId), "seed mission not visible pre-clear");
            Assert.IsNotNull(AssetManager.Instance.GetSkill(SeedSkillId), "seed skill not visible pre-clear");

            AssetManager.Instance.ClearLiveAssetsForTests();

            Assert.IsNull(AssetManager.Instance.GetCloneBase(SeedCbid), "clone base survived clear");
            Assert.IsNull(AssetManager.Instance.GetMission(SeedMissionId), "mission survived clear");
            Assert.IsNull(AssetManager.Instance.GetMissionByObjectiveId(SeedObjectiveId), "objective survived clear");
            Assert.IsNull(AssetManager.Instance.GetSkill(SeedSkillId), "skill survived clear");
        }
        finally
        {
            AssetManager.Instance.ClearLiveAssetsForTests();
        }
    }

    [TestMethod]
    public void ClearLiveAssetsForTests_AllowsLoadAllDataAgain()
    {
        // A live suite that loads and unloads must leave DataLoaded false, otherwise the
        // next LoadAllData() short-circuits and the server sees an empty catalog.
        AssetManagerTestHelper.SetDataLoaded(true);
        try
        {
            AssetManager.Instance.ClearLiveAssetsForTests();
            Assert.IsFalse(AssetManagerTestHelper.GetDataLoaded(), "DataLoaded survived clear");
        }
        finally
        {
            AssetManagerTestHelper.SetDataLoaded(false);
        }
    }

    private static void SeedEveryTable(WADLoader wad)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific
        {
            Type = (int)CloneBaseObjectType.Item,
            CloneBaseId = SeedCbid,
        };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { InvSizeX = 1, InvSizeY = 1 };

        wad.CloneBases[SeedCbid] = clone;
        wad.Missions[SeedMissionId] = GameMission.CreateForTests(
            SeedMissionId,
            GameMissionObjective.CreateForTests(SeedObjectiveId, sequence: 0, questId: SeedMissionId));
        wad.Skills[SeedSkillId] = new Skill { Id = SeedSkillId };
        wad.ArmorPrefixes[1] = Prefix<PrefixArmor>();
        wad.PowerPlantPrefixes[1] = Prefix<PrefixPowerPlant>();
        wad.WeaponPrefixes[1] = Prefix<PrefixWeapon>();
        wad.VehiclePrefixes[1] = Prefix<PrefixVehicle>();
        wad.OrnamentPrefixes[1] = Prefix<PrefixOrnament>();
        wad.RaceItemPrefixes[1] = Prefix<PrefixRaceItem>();
    }

    private static PrefixBase Prefix<T>() where T : PrefixBase =>
        (PrefixBase)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
