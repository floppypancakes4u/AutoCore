using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

/// <summary>
/// Live repro: entering a shared map freezes the client at a full loading bar, but only after the
/// map has been reset once. Server logs for map 693 (Back Range) show a perfect 6/6 correlation —
/// every entry that happened before <see cref="SectorMap.ResetLocalWorldToAuthored"/> had run in
/// that process succeeded; every entry after it froze. The map-transfer handshake itself completes
/// normally in both cases (Stage2/Stage3/ack all logged), so the divergence has to be in what the
/// rebuilt map contains.
/// <para>
/// These pin the invariant the reset claims in its own summary: "Rebuild map-local objects from
/// fam-authored state" — a reset map must be indistinguishable from a freshly loaded one.
/// </para>
/// </summary>
[TestClass]
public class SharedMapReentryTests
{
    private const int ActiveSpawnCoid = 40_101;
    private const int InactiveSpawnCoid = 40_102;
    private const int CreatureCbid = 780_001;

    [TestInitialize]
    public void Init()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.TestPacketSink = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.TestPacketSink = null;
    }

    /// <summary>
    /// The object set a re-entering player is scoped against must match what the first player saw.
    /// A count drift here is exactly what a client would see as a create stream that never resolves.
    /// </summary>
    [TestMethod]
    public void Reset_RebuildsSameLocalObjectSet_AsFreshLoad()
    {
        var map = CreateMapWithAuthoredContent(9801);
        map.InitializeLocalObjectsForTests();
        var fresh = SnapshotObjects(map);

        map.ResetLocalWorldToAuthored();

        var afterReset = SnapshotObjects(map);
        CollectionAssert.AreEquivalent(
            fresh,
            afterReset,
            $"reset map must match a fresh load.\nfresh:  [{string.Join(", ", fresh)}]\nreset: [{string.Join(", ", afterReset)}]");
    }

    /// <summary>
    /// Two resets in a row happen whenever players cycle in and out; the map must not accumulate
    /// or shed objects each time.
    /// </summary>
    [TestMethod]
    public void RepeatedResets_AreIdempotent()
    {
        var map = CreateMapWithAuthoredContent(9802);
        map.InitializeLocalObjectsForTests();
        var fresh = SnapshotObjects(map);

        map.ResetLocalWorldToAuthored();
        map.ResetLocalWorldToAuthored();
        map.ResetLocalWorldToAuthored();

        CollectionAssert.AreEquivalent(fresh, SnapshotObjects(map),
            "each enter/leave cycle resets the map; drift compounds across visits");
    }

    /// <summary>
    /// Authored COIDs are the identity the client already instantiated from the .fam file. If a
    /// rebuild hands out different COIDs for the same authored content, the client is being told
    /// about objects it cannot reconcile with its own map load.
    /// </summary>
    [TestMethod]
    public void Reset_PreservesAuthoredCoids()
    {
        var map = CreateMapWithAuthoredContent(9803);
        map.InitializeLocalObjectsForTests();

        map.ResetLocalWorldToAuthored();

        Assert.IsNotNull(map.GetObjectByCoid(ActiveSpawnCoid), "active authored spawn point lost by reset");
        Assert.IsNotNull(map.GetObjectByCoid(InactiveSpawnCoid), "inactive authored spawn point lost by reset");
    }

    /// <summary>
    /// The local COID counter is what hands identities to spawn children. Teardown rewinds it to
    /// <c>HighestCoid + 1</c>; the rebuild must then allocate the same identities as a fresh load
    /// rather than continuing from wherever the previous visit ended.
    /// </summary>
    [TestMethod]
    public void Reset_RestoresLocalCoidCounter_ToFreshLoadValue()
    {
        var map = CreateMapWithAuthoredContent(9804);
        map.InitializeLocalObjectsForTests();
        var freshCounter = map.LocalCoidCounter;

        map.ResetLocalWorldToAuthored();

        Assert.AreEqual(freshCounter, map.LocalCoidCounter,
            "a re-entering player must get the same child COIDs the first visitor did");
    }

    /// <summary>
    /// Spawn children are the bulk of what a player is scoped against on arrival. An active
    /// authored spawn point must be live again after the reset.
    /// </summary>
    [TestMethod]
    public void Reset_RematerializesActiveSpawnChildren()
    {
        var map = CreateMapWithAuthoredContent(9805);
        map.InitializeLocalObjectsForTests();
        var freshCreatures = CountCreatures(map);
        Assert.IsTrue(freshCreatures > 0, "fixture must produce spawn children on a fresh load");

        map.ResetLocalWorldToAuthored();

        Assert.AreEqual(freshCreatures, CountCreatures(map),
            "active spawn point must refill after reset, as it does on a fresh map load");
    }

    private static List<string> SnapshotObjects(SectorMap map)
    {
        var items = new List<string>();
        foreach (var kvp in map.Objects)
        {
            if (kvp.Value is Character)
                continue;

            items.Add($"{kvp.Value.GetType().Name}#{kvp.Key.Coid}(global={kvp.Key.Global})");
        }

        items.Sort(StringComparer.Ordinal);
        return items;
    }

    private static int CountCreatures(SectorMap map)
        => map.Objects.Values.OfType<Creature>().Count(c => c is not Character);

    private static SectorMap CreateMapWithAuthoredContent(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_reentry_{continentId}",
            DisplayName = "reentry",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);

        var active = new SpawnPointTemplate
        {
            COID = ActiveSpawnCoid,
            OriginalIsActive = true,
            IsActive = true,
        };
        active.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 2,
            UpperNumberOfSpawns = 2,
        });
        map.MapData.Templates[active.COID] = active;

        var inactive = new SpawnPointTemplate
        {
            COID = InactiveSpawnCoid,
            OriginalIsActive = false,
            IsActive = false,
        };
        inactive.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[inactive.COID] = inactive;

        return map;
    }
}
