using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// The per-tick sweeps (<see cref="MapManager.RebucketAllGrids"/>, <see cref="MapManager.TickNpcs"/>,
/// <see cref="MapManager.ForcePathVehiclePoseDirty"/>) must cover per-player instance maps, not
/// just the shared <c>SectorMaps</c> registry — otherwise NPCs inside instances freeze and
/// interest queries read stale positions.
/// </summary>
[TestClass]
public class MapInstancingTickLoopTests
{
    private const int InstancedContId = 9941;

    private static SectorMap CreateTestMap(int continentId) =>
        SectorMap.CreateForTests(
            new ContinentObject
            {
                Id = continentId,
                MapFileName = $"tm_instancing_tick_{continentId}",
                DisplayName = "test",
                IsTown = false,
                IsPersistent = true,
            },
            new Vector4(0, 0, 0, 0));

    [TestInitialize]
    public void SetUp()
    {
        MapManager.Instance.ClearMapsForTests();
        InstancedContinents.SetForTests(new HashSet<int> { InstancedContId });
        MapManager.Instance.CreateInstanceForTests = CreateTestMap;
    }

    [TestCleanup]
    public void Cleanup()
    {
        MapManager.Instance.CreateInstanceForTests = null;
        InstancedContinents.SetForTests(null);
        MapManager.Instance.ClearMapsForTests();
    }

    private static Character CreateCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.AttachTestDataForTests();
        return character;
    }

    [TestMethod]
    public void RebucketAllGrids_SweepsInstanceMapGrids()
    {
        var character = CreateCharacter(2101);
        var instance = MapManager.Instance.GetMapForCharacter(InstancedContId, character);
        character.SetMap(instance);

        // Drift the character without going through EnterMap/LeaveMap; only a rebucket sweep
        // re-homes it into the new grid cell.
        character.Position = new Vector3(1000f, 0f, 1000f);
        MapManager.Instance.RebucketAllGrids();

        var found = new List<ClonedObjectBase>();
        instance.Grid.QueryRadius(new Vector3(1000f, 0f, 1000f), 50f, found);
        CollectionAssert.Contains(found, character,
            "Rebucket must sweep instance-map grids, not only the shared registry.");
    }

    [TestMethod]
    public void ForcePathVehiclePoseDirty_CoversInstanceMaps()
    {
        var character = CreateCharacter(2102);
        var instance = MapManager.Instance.GetMapForCharacter(InstancedContId, character);
        character.SetMap(instance);

        var pathVeh = new Vehicle();
        pathVeh.SetCoid(2103, true);
        pathVeh.CoidCurrentPath = 55;
        pathVeh.CreateGhost();
        pathVeh.NpcAi = new NpcAiState();
        pathVeh.SetMap(instance);

        var connection = new ScopeProbeConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.BeginGhostingForTests();
        connection.ObjectInScope(pathVeh.Ghost!);

        Assert.AreEqual(1, MapManager.Instance.ForcePathVehiclePoseDirty(),
            "Path vehicles on instance maps must re-enter the dirty queue each tick.");
    }

    [TestMethod]
    public void TickNpcs_IncludesPopulatedInstances_DoesNotThrow()
    {
        var character = CreateCharacter(2104);
        var instance = MapManager.Instance.GetMapForCharacter(InstancedContId, character);
        character.SetMap(instance);
        Assert.IsTrue(instance.PlayerCount > 0);

        // Parity with MapManagerCoverageTests: populated maps tick without throwing.
        MapManager.Instance.TickNpcs(Environment.TickCount64, 0.05f);
    }

    private sealed class ScopeProbeConnection : TNLConnection
    {
    }
}
