using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

/// <summary>
/// Ground-loot COID identity.
///
/// Client ground truth (autoassault.exe, <c>Process_EMSG_Sector_CreateSimpleObject</c> @0x00812360):
/// a CreateSimpleObject whose COID is already in the client's object list is NOT a create — the
/// handler calls <c>ProcessSectorUpdate</c> and the existing object keeps its original CBID, so the
/// player sees the *previous* item's name on the new drop. If the COID instead matches a local map
/// object, the client repositions/respawns that authored prop (<c>MoveRBToLocation</c> /
/// <c>DoRespawnOfObject</c>) rather than spawning loot at all.
///
/// Loot used to mint from <c>Map.LocalCoidCounter</c>, which map teardown rewinds to
/// <c>MapData.HighestCoid + 1</c> — so loot COIDs repeat for any client that outlives a teardown of
/// a map it re-enters. Loot now uses its own never-rewound identity range instead, which is the
/// same defence <see cref="MapNpcIdentity"/> applies to spawned NPCs.
/// </summary>
[TestClass]
public class LootCoidIdentityTests
{
    private const int LootCbid = 7900;

    [TestInitialize]
    public void SetUp()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCloneBase(LootCbid, CloneBaseObjectType.Item);
        LootManager.Instance.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
    }

    [TestMethod]
    public void LootCoids_AreUnique_AcrossRepeatedSpawns()
    {
        var map = CreateMap(12000);

        var coids = new List<long>();
        for (var i = 0; i < 25; i++)
        {
            Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
                LootCbid, new Vector3(i, 0, i), Quaternion.Default, map, out var coid));
            coids.Add(coid);
        }

        CollectionAssert.AllItemsAreUnique(coids, "two live drops must never share a COID");
    }

    [TestMethod]
    public void LootCoids_AreNotReused_AfterMapTeardown()
    {
        var map = CreateMap(12100);

        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            LootCbid, new Vector3(1, 0, 1), Quaternion.Default, map, out var before));

        // Teardown rewinds the map-local counter to its fresh-load value.
        map.ResetLocalWorldToAuthored();

        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            LootCbid, new Vector3(2, 0, 2), Quaternion.Default, map, out var after));

        Assert.AreNotEqual(
            before,
            after,
            "a rewound map counter must not hand a fresh drop a COID the client still has cached — "
            + "the client would treat the create as an update and show the old item's name");
    }

    [TestMethod]
    public void LootCoids_DoNotConsumeTheMapLocalCounter()
    {
        var map = CreateMap(12200);
        var before = map.LocalCoidCounter;

        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            LootCbid, new Vector3(1, 0, 1), Quaternion.Default, map, out _));

        Assert.AreEqual(
            before,
            map.LocalCoidCounter,
            "loot has its own identity space; burning the map counter would shift NPC spawn COIDs");
    }

    [TestMethod]
    public void LootCoids_SitAboveAuthoredMapObjectRange()
    {
        var map = CreateMap(12300);

        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            LootCbid, new Vector3(1, 0, 1), Quaternion.Default, map, out var coid));

        Assert.IsTrue(
            coid >= MapLootIdentity.CoidBase,
            $"loot COID {coid} must sit in the dedicated loot range so it can never alias an "
            + "authored map object the client already instantiated from the .fam file");
    }

    [TestMethod]
    public void SpawnedLoot_IsResolvableByCoid_ForPickup()
    {
        var map = CreateMap(12400);

        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            LootCbid, new Vector3(1, 0, 1), Quaternion.Default, map, out var coid));

        // The pickup handler resolves the world item by bare COID; the new range must not break it.
        Assert.IsNotNull(map.GetObjectByCoid(coid), "ground loot must stay resolvable for ItemPickup");
    }

    private static SectorMap CreateMap(long localCoid)
    {
        var continent = new ContinentObject
        {
            Id = (int)(localCoid % 10000),
            MapFileName = $"tm_loot_coid_{localCoid}",
            DisplayName = "lootcoid",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        map.LocalCoidCounter = localCoid;
        return map;
    }
}
