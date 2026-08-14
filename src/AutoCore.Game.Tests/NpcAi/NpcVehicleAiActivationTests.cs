using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.NpcAi;

/// <summary>
/// Pass 23 — NPC vehicle AI activation. Retail <c>CVOGSpawnPoint::CreateAIVehicle</c>
/// (<c>0x00563AB0</c>) always registers the chassis+driver into the server heartbeat list.
/// AutoCore previously assigned <see cref="Vehicle.NpcAi"/> only when the driver clonebase
/// had a resolvable <c>tCreatureAI</c> row, so a materialized hostile car sat in the world
/// with no tick owner.
/// </summary>
[TestClass]
public class NpcVehicleAiActivationTests
{
    private const int ContId = 23_100;
    private const int VehicleCbid = 23_201;
    private const int DriverCbid = 23_202;
    private const int WheelsetCbid = 23_203;
    private const int WeaponCbid = 23_204;
    private const int TemplateId = 23_205;
    private const int AiBehaviorId = 23_206;
    private const int WalkingCbid = 23_207;
    private const long SpawnCoid = 23_301;
    private const long PathCoid = 23_401;
    private const long CreateRx = 23_501;
    private const long ActivateRx = 23_502;

    [TestInitialize]
    public void SetUp()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TriggerManager.Instance.ClearAllForTests();
        IncompleteHandlerLog.ResetOnceKeysForTests();
        TNLConnection.TestPacketSink = null;
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TriggerManager.Instance.ClearAllForTests();
        TNLConnection.TestPacketSink = null;
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
    }

    [TestMethod]
    public void NpcVehicle_SpawnedVehicleHasAiOwner()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        var spawn = PlaceTemplateSpawn(map, SpawnCoid);

        Assert.IsTrue(spawn.Spawn());
        var vehicle = SingleOwnedVehicle(map, SpawnCoid);
        Assert.IsNotNull(vehicle.NpcAi,
            "retail CreateAIVehicle always installs a heartbeat owner; AutoCore must assign NpcAi even when the driver has no tCreatureAI row");
    }

    [TestMethod]
    public void NpcVehicle_HostileVehicleEntersAiTick()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();

        var vehicle = SingleOwnedVehicle(map, SpawnCoid);
        Assert.IsTrue(map.NpcAiEntities.Contains(vehicle),
            "SetMap must register the chassis in NpcAiEntities so NpcTicker sees it");
        Assert.IsFalse(map.NpcAiEntities.Contains(vehicle.Owner),
            "the unmapped driver must not be the tick owner");
    }

    [TestMethod]
    public void NpcVehicle_DriverRemainsUnmappedButAiFunctions()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();

        var vehicle = SingleOwnedVehicle(map, SpawnCoid);
        Assert.IsNotNull(vehicle.Owner);
        Assert.IsNull(vehicle.Owner.Map, "Pass 9: driver stays unmapped");
        Assert.IsNull(vehicle.Owner.Ghost, "Pass 9: driver stays ghostless");
        Assert.IsNotNull(vehicle.NpcAi);
        Assert.IsTrue(map.NpcAiEntities.Contains(vehicle));
    }

    [TestMethod]
    public void NpcVehicle_FactionMatchesRetailTemplate()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2);
        var spawn = PlaceTemplateSpawn(map, SpawnCoid, factionDirty: true, originalFaction: 7);
        spawn.Spawn();

        var vehicle = SingleOwnedVehicle(map, SpawnCoid);
        Assert.AreEqual(7, vehicle.GetIDFaction(),
            "FactionDirty spawn override must land on the AI owner chain (chassis GetIDFaction)");
        Assert.AreEqual(7, vehicle.Owner.GetIDFaction(),
            "the unmapped driver must carry the same authored faction");
    }

    [TestMethod]
    public void NpcVehicle_NearbyHostilePlayerAcquiresTarget()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        PlaceConnectedPlayer(map, new Vector3(20f, 0f, 0f), faction: 0);

        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);

        Assert.IsNotNull(npc.Target, "a hostile player in vision must be acquired once the chassis is in the AI tick");
        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState);
    }

    [TestMethod]
    public void NpcVehicle_FriendlyPlayerNotTargeted()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 0, visionRange: 80f);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        PlaceConnectedPlayer(map, new Vector3(10f, 0f, 0f), faction: 0);

        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);

        Assert.IsNull(npc.Target, "same-faction (Human 0) players must not be acquired");
        Assert.AreEqual(HBAICombatState.IdlePatrol, npc.NpcAi.CombatState);
    }

    [TestMethod]
    public void NpcVehicle_PathSpawnInitializesMapPath()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        SeedPath(map, PathCoid, new Vector3(0f, 0f, 0f), new Vector3(40f, 0f, 0f));
        var spawn = PlaceTemplateSpawn(map, SpawnCoid, pathCoid: PathCoid, patrolDistance: 18f);
        spawn.Spawn();

        var vehicle = SingleOwnedVehicle(map, SpawnCoid);
        Assert.AreEqual(PathCoid, vehicle.CoidCurrentPath,
            "ApplySpawnPath must copy authored MapPathCoid onto the chassis that NpcTicker reads");
        Assert.AreEqual(18f, vehicle.PatrolDistance, 0.001f,
            "InitialPatrolDistance must land on the chassis PatrolDistance (retail InitializePathIDs)");
    }

    [TestMethod]
    public void NpcVehicle_PathVehicleMovesOverTicks()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        SeedPath(map, PathCoid, new Vector3(0f, 0f, 0f), new Vector3(80f, 0f, 0f));
        var spawn = PlaceTemplateSpawn(map, SpawnCoid, pathCoid: PathCoid);
        spawn.Position = new Vector3(0f, 0f, 0f);
        spawn.Spawn();

        var vehicle = SingleOwnedVehicle(map, SpawnCoid);
        var start = vehicle.Position;
        for (var i = 0; i < 20; i++)
            NpcTicker.Tick(map, nowMs: 1_000 + (i * 50), dt: 0.05f);

        Assert.IsTrue(vehicle.Position.Dist(start) > 0.5f,
            $"a path-linked NPC vehicle must leave spawn; start={start} now={vehicle.Position}");
    }

    [TestMethod]
    public void NpcVehicle_MovementMarksGhostPositionDirty()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        SeedPath(map, PathCoid, new Vector3(0f, 0f, 0f), new Vector3(80f, 0f, 0f));
        PlaceTemplateSpawn(map, SpawnCoid, pathCoid: PathCoid).Spawn();
        var vehicle = SingleOwnedVehicle(map, SpawnCoid);
        ScopeGhost(vehicle);

        var ghostInfo = vehicle.Ghost!.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo);
        ghostInfo!.UpdateMask = 0;

        NpcTicker.Tick(map, nowMs: 2_000, dt: 0.05f);
        NetObject.CollapseDirtyList();

        Assert.AreNotEqual(0UL, ghostInfo.UpdateMask & GhostObject.PositionMask,
            "server-side path movement must dirty GhostVehicle Position so clients see the car move");
    }

    [TestMethod]
    public void NpcVehicle_TargetChangeMarksGhostTargetDirty()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        ScopeGhost(npc);
        var ghostInfo = npc.Ghost!.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo);
        PlaceConnectedPlayer(map, new Vector3(15f, 0f, 0f), faction: 0);

        ghostInfo!.UpdateMask = 0;
        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);
        NetObject.CollapseDirtyList();

        Assert.IsNotNull(npc.Target);
        Assert.AreEqual(GhostObject.TargetMask, ghostInfo.UpdateMask & GhostObject.TargetMask,
            "acquiring a player must dirty GhostVehicle TargetMask");
    }

    [TestMethod]
    public void NpcVehicle_AiStateChangeMarksGhostAiDirty()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        ScopeGhost(npc);
        var ghostInfo = npc.Ghost!.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo);
        PlaceConnectedPlayer(map, new Vector3(15f, 0f, 0f), faction: 0);

        ghostInfo!.UpdateMask = 0;
        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState);
        Assert.AreEqual(GhostVehicle.StateMask, ghostInfo.UpdateMask & GhostVehicle.StateMask,
            "IdlePatrol→Engage must dirty GhostVehicle StateMask (AI-state wire)");
    }

    [TestMethod]
    public void NpcVehicle_MissionCreateReceivesAiInitialization()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        var spawn = PlaceInactiveTemplateSpawn(map, SpawnCoid);
        PlaceCreate(map, CreateRx, SpawnCoid);
        var (character, playerVehicle) = PlaceConnectedPlayer(map, new Vector3(0f, 0f, 0f), faction: 0);
        SeedKillQuest(character, TemplateId);
        map.ApplyMissionPhaseWorldState(playerVehicle);

        Assert.IsTrue(spawn.HasLiveSpawn(), "Create-only mission spawn must materialize");
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        Assert.IsNotNull(npc.NpcAi, "mission-created vehicles must receive the same AI owner as map-load vehicles");
        Assert.IsTrue(map.NpcAiEntities.Contains(npc));
    }

    [TestMethod]
    public void NpcVehicle_MissionActivateReceivesAiInitialization()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        var spawn = PlaceInactiveTemplateSpawn(map, SpawnCoid);
        var actTpl = new ReactionTemplate { COID = (int)ActivateRx, ReactionType = ReactionType.Activate };
        actTpl.Objects.Add(SpawnCoid);
        var activate = new Reaction(actTpl);
        activate.SetCoid(ActivateRx, false);
        activate.SetMap(map);
        var (character, _) = PlaceConnectedPlayer(map, new Vector3(0f, 0f, 0f), faction: 0);

        Assert.IsFalse(spawn.HasLiveSpawn());
        Assert.IsTrue(activate.TriggerIfPossible(character));
        Assert.IsTrue(spawn.HasLiveSpawn());

        var npc = SingleOwnedVehicle(map, SpawnCoid);
        Assert.IsNotNull(npc.NpcAi, "Activate-created vehicles must receive full AI initialization");
        Assert.IsTrue(map.NpcAiEntities.Contains(npc));
        Assert.IsNull(npc.Owner.Map);
        Assert.IsNull(npc.Owner.Ghost);
    }

    [TestMethod]
    public void NpcVehicle_RespawnReceivesAiInitialization()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        var spawn = PlaceTemplateSpawn(map, SpawnCoid, respawnTime: 1_000f);
        spawn.Spawn();
        var first = SingleOwnedVehicle(map, SpawnCoid);
        var firstCoid = first.ObjectId.Coid;
        first.SetMap(null);
        spawn.NotifySpawnedChildDied(first, null);
        map.TickSpawnRespawns(spawn.RespawnDueAtMs ?? 0);

        var replacement = SingleOwnedVehicle(map, SpawnCoid);
        Assert.AreNotEqual(firstCoid, replacement.ObjectId.Coid, "respawn must allocate a new child");
        Assert.IsNotNull(replacement.NpcAi, "the replacement must receive AI initialization; first-spawn-only is a bug");
        Assert.IsTrue(map.NpcAiEntities.Contains(replacement));
    }

    [TestMethod]
    public void NpcVehicle_ReentryStillTicks()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        Assert.IsNotNull(SingleOwnedVehicle(map, SpawnCoid).NpcAi);

        var (character, playerVehicle) = PlaceConnectedPlayer(map, new Vector3(10f, 0f, 0f), faction: 0);
        character.SetMap(null);
        playerVehicle.SetMap(null);

        // Last-player-leave rebuilds fam-active children. The replacement must still have an AI owner.
        var rebuilt = SingleOwnedVehicle(map, SpawnCoid);
        Assert.IsNotNull(rebuilt.NpcAi, "ResetLocalWorldToAuthored must re-run ApplyDriverAi / EnsureNpcVehicleAi");
        Assert.IsTrue(map.NpcAiEntities.Contains(rebuilt));

        character.SetMap(map);
        playerVehicle.Position = new Vector3(rebuilt.Position.X + 10f, rebuilt.Position.Y, rebuilt.Position.Z);
        playerVehicle.SetMap(map);
        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);
        Assert.IsNotNull(rebuilt.Target, "after re-entry the rebuilt vehicle must still acquire a nearby hostile player");
    }

    [TestMethod]
    public void NpcVehicle_TargetLossClearsTarget()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        var (_, playerVehicle) = PlaceConnectedPlayer(map, new Vector3(12f, 0f, 0f), faction: 0);

        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);
        Assert.IsNotNull(npc.Target);

        playerVehicle.SetMap(null);
        NpcTicker.Tick(map, nowMs: 100_500, dt: 0.05f);

        Assert.IsNull(npc.Target, "a target that left the map must be cleared; no stale cross-map lock");
    }

    [TestMethod]
    public void NpcVehicle_TargetLossReturnsToPatrolOrIdle()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        SeedPath(map, PathCoid, new Vector3(0f, 0f, 0f), new Vector3(40f, 0f, 0f));
        PlaceTemplateSpawn(map, SpawnCoid, pathCoid: PathCoid).Spawn();
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        var (_, playerVehicle) = PlaceConnectedPlayer(map, new Vector3(12f, 0f, 0f), faction: 0);

        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);
        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState);

        playerVehicle.SetMap(null);
        NpcTicker.Tick(map, nowMs: 100_500, dt: 0.05f);

        Assert.AreEqual(HBAICombatState.IdlePatrol, npc.NpcAi.CombatState);
        Assert.IsTrue(npc.NpcAi.ReturningHome || npc.NpcAi.PathIndex < 0,
            "target loss on a path vehicle must return to idle/patrol, not stay in Engage");
    }

    [TestMethod]
    public void WalkingCreature_AiControlStillWorks()
    {
        var map = CreateFieldMap();
        AssetManagerTestHelper.RegisterCreatureCloneBase(WalkingCbid, aiBehaviorId: AiBehaviorId, faction: 3);
        AssetManager.Instance.SetTestCreatureAiProfiles(new[] { new CreatureAiProfile { AiId = AiBehaviorId } });
        var spec = AssetManager.Instance.GetCloneBase<CloneBaseCreature>(WalkingCbid).CreatureSpecific;
        spec.VisionRange = 80f;

        var template = new SpawnPointTemplate { COID = (int)SpawnCoid, OriginalIsActive = true, IsActive = true };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = WalkingCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        var spawn = new SpawnPoint(template);
        spawn.SetCoid(SpawnCoid, false);
        spawn.SetMap(map);
        Assert.IsTrue(spawn.Spawn());

        var walker = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.IsNotNull(walker.NpcAi, "combat walking creatures with a profile must still receive NpcAi");
        Assert.IsTrue(map.NpcAiEntities.Contains(walker));
        Assert.IsNotNull(walker.Map);
        Assert.IsNotNull(walker.Ghost);

        PlaceConnectedPlayer(map, new Vector3(walker.Position.X + 10f, walker.Position.Y, walker.Position.Z), faction: 0);
        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);
        Assert.IsNotNull(walker.Target, "walking-creature AI must still acquire a nearby hostile player");
    }

    [TestMethod]
    public void ScrapValley_RealHostileVehicleDoesNotRemainInert()
    {
        var map = CreateFieldMap(ContId + 8);
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 1, visionRange: 80f);
        SeedPath(map, PathCoid, new Vector3(0f, 0f, 0f), new Vector3(60f, 0f, 0f));
        var spawn = PlaceTemplateSpawn(map, SpawnCoid, pathCoid: PathCoid);
        spawn.Position = new Vector3(0f, 0f, 0f);
        spawn.Spawn();

        var vehicle = SingleOwnedVehicle(map, SpawnCoid);
        Assert.IsNotNull(vehicle.NpcAi, "ordinary highway-class template vehicles must not sit without an AI owner");
        var start = vehicle.Position;
        for (var i = 0; i < 16; i++)
            NpcTicker.Tick(map, nowMs: 3_000 + (i * 50), dt: 0.05f);
        Assert.IsTrue(vehicle.Position.Dist(start) > 0.5f,
            "a Scrap-Valley-class path vehicle must leave its spawn instead of remaining inert");
    }

    [TestMethod]
    public void TierraRoja_DuelVehicleMatchesRetailActivation()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        var spawn = PlaceInactiveTemplateSpawn(map, 3882);
        PlaceCreate(map, 3883, 3882);
        var (character, playerVehicle) = PlaceConnectedPlayer(map, new Vector3(4000f, 0f, 4000f), faction: 0);
        SeedKillQuest(character, TemplateId);
        map.ApplyMissionPhaseWorldState(playerVehicle);

        var duel = SingleOwnedVehicle(map, 3882);
        Assert.IsNotNull(duel.NpcAi, "Champion car 3882 must receive AI once Create-only materializes it");
        Assert.AreEqual(-1, duel.CoidCurrentPath, "this synthetic twin authors no MapPathCoid; live FAM 3882 carries path 3888");
        Assert.IsNull(duel.Target, "a player 4000 units away must not be acquired — do not make the duel premature");

        playerVehicle.Position = new Vector3(duel.Position.X + 8f, duel.Position.Y, duel.Position.Z);
        map.Grid.RebucketSweep();
        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.05f);
        Assert.IsNotNull(duel.Target, "once the player is next to the duel car it must acquire");
    }

    [TestMethod]
    public void Wastes_TestOfMetalVehicleMatchesRetailActivation()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        var spawn = PlaceInactiveTemplateSpawn(map, 18609);
        PlaceCreate(map, 18608, 18609);
        var (character, playerVehicle) = PlaceConnectedPlayer(map, new Vector3(0f, 0f, 0f), faction: 0);
        SeedKillQuest(character, TemplateId);
        map.ApplyMissionPhaseWorldState(playerVehicle);

        var helena = SingleOwnedVehicle(map, 18609);
        Assert.IsTrue(spawn.HasLiveSpawn());
        Assert.IsNotNull(helena.NpcAi, "Wastes 18609 / template 593 class must initialize AI on Create");
        Assert.IsTrue(map.NpcAiEntities.Contains(helena));
        Assert.IsNull(helena.Owner.Map);
    }

    [TestMethod]
    public void CanyonRun_CreviceVehicleMatchesRetailActivation()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false, driverFaction: 2, visionRange: 80f);
        var spawn = PlaceInactiveTemplateSpawn(map, 23413);
        PlaceCreate(map, 23415, 23413);
        var (character, playerVehicle) = PlaceConnectedPlayer(map, new Vector3(0f, 0f, 0f), faction: 0);
        SeedKillQuest(character, TemplateId);
        map.ApplyMissionPhaseWorldState(playerVehicle);

        var crevice = SingleOwnedVehicle(map, 23413);
        Assert.IsTrue(spawn.HasLiveSpawn());
        Assert.IsNotNull(crevice.NpcAi, "Canyon 23413 / template 636 class must initialize AI on Create");
        Assert.IsTrue(map.NpcAiEntities.Contains(crevice));
    }

    [TestMethod]
    public void VehicleAiFix_DoesNotMapOrGhostDriver()
    {
        var map = CreateFieldMap();
        RegisterTemplateVehicle(aiBehaviorId: AiBehaviorId, registerProfile: true);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        var vehicle = SingleOwnedVehicle(map, SpawnCoid);

        Assert.IsNotNull(vehicle.Owner);
        Assert.IsNull(vehicle.Owner.Map);
        Assert.IsNull(vehicle.Owner.Ghost);
        Assert.IsFalse(map.Objects.ContainsKey(vehicle.Owner.ObjectId),
            "driver must stay out of the map object table");
        Assert.IsNotNull(vehicle.Ghost);
        Assert.IsInstanceOfType(vehicle.Ghost, typeof(GhostVehicle));
    }

    [TestMethod]
    public void VehicleAiFix_Preserves500msCreateHold()
    {
        Assert.AreEqual(500, TNLConnection.ForeignGhostScopeHoldMilliseconds,
            "Pass 5 / Pass 9: 500 ms Create→GhostVehicle hold must not change");
        Assert.AreEqual(1, TNLConnection.ForeignGhostScopeHoldQueries,
            "Pass 5 / Pass 9: 1-query hold must not change");
    }

    [TestMethod]
    public void NpcVehicle_MissingAiProfileStillTicksAndLogsOnce()
    {
        var logs = new List<string>();
        IncompleteHandlerLog.TestSink = msg => logs.Add(msg);
        try
        {
            var map = CreateFieldMap();
            RegisterTemplateVehicle(aiBehaviorId: 99, registerProfile: false);
            PlaceTemplateSpawn(map, SpawnCoid).Spawn();
            var vehicle = SingleOwnedVehicle(map, SpawnCoid);
            Assert.IsNotNull(vehicle.NpcAi);
            Assert.IsNull(vehicle.NpcAi.Profile);
            Assert.IsTrue(logs.Any(l => l.Contains("no AI profile", StringComparison.OrdinalIgnoreCase)
                                        || l.Contains("AI owner", StringComparison.OrdinalIgnoreCase)
                                        || l.Contains("tCreatureAI", StringComparison.OrdinalIgnoreCase)),
                "a missing tCreatureAI row must log once, not silently drop the vehicle from the tick. got: "
                + string.Join(" | ", logs));
        }
        finally
        {
            IncompleteHandlerLog.TestSink = null;
        }
    }

    [TestMethod]
    public void NpcVehicle_EmptyMapDoesNotTickAi_PlayerEntryStartsImmediately()
    {
        var map = CreateFieldMap(ContId + 9);
        RegisterTemplateVehicle(aiBehaviorId: 0, registerProfile: false);
        SeedPath(map, PathCoid, new Vector3(0f, 0f, 0f), new Vector3(80f, 0f, 0f));
        PlaceTemplateSpawn(map, SpawnCoid, pathCoid: PathCoid).Spawn();
        var vehicle = SingleOwnedVehicle(map, SpawnCoid);

        try
        {
            MapManager.Instance.RegisterMapForTests(map);
            MapManager.Instance.TickNpcs(4_000, 0.05f);
            Assert.AreEqual(0L, vehicle.NpcAi.LastAggroScanMs,
                "empty maps may skip AI ticks (PlayerCount==0) — spawn refill still runs separately");

            PlaceConnectedPlayer(map, new Vector3(0f, 0f, 0f), faction: 0);
            Assert.IsTrue(map.PlayerCount > 0);
            MapManager.Instance.TickNpcs(4_050, 0.05f);
            Assert.IsTrue(vehicle.NpcAi.LastAggroScanMs > 0,
                "the first TickNpcs after a player enters must run NpcCombatAi immediately");
        }
        finally
        {
            MapManager.Instance.ClearMapsForTests();
        }
    }

    private static void RegisterTemplateVehicle(
        int aiBehaviorId,
        bool registerProfile,
        int driverFaction = 2,
        float visionRange = 0f)
    {
        AssetManagerTestHelper.RegisterCloneBase(WheelsetCbid, CloneBaseObjectType.WheelSet);
        AssetManagerTestHelper.RegisterVehicleCloneBase(VehicleCbid, defaultDriverCbid: DriverCbid, defaultWheelsetCbid: WheelsetCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(DriverCbid, aiBehaviorId: aiBehaviorId, faction: driverFaction, isNpc: 0);
        if (visionRange > 0f)
        {
            var spec = AssetManager.Instance.GetCloneBase<CloneBaseCreature>(DriverCbid).CreatureSpecific;
            spec.VisionRange = visionRange;
        }

        if (registerProfile && aiBehaviorId > 0)
        {
            AssetManager.Instance.SetTestCreatureAiProfiles(new[]
            {
                new CreatureAiProfile { AiId = aiBehaviorId }
            });
        }

        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = TemplateId,
                VehicleCbid = VehicleCbid,
                DriverCbid = DriverCbid,
            }
        });
    }

    private static SpawnPoint PlaceTemplateSpawn(
        SectorMap map,
        long spawnCoid,
        long pathCoid = 0,
        float patrolDistance = 0f,
        float respawnTime = -1f,
        bool factionDirty = false,
        int originalFaction = 0)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)spawnCoid,
            OriginalIsActive = true,
            IsActive = true,
            MapPathCoid = pathCoid,
            InitialPatrolDistance = patrolDistance,
            RespawnTime = respawnTime,
            FactionDirty = factionDirty,
            OriginalFaction = originalFaction,
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = TemplateId,
            IsTemplate = true,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[spawnCoid] = tpl;
        var spawn = new SpawnPoint(tpl);
        spawn.SetCoid(spawnCoid, false);
        spawn.Position = new Vector3(0f, 0f, 0f);
        spawn.SetMap(map);
        return spawn;
    }

    private static SpawnPoint PlaceInactiveTemplateSpawn(SectorMap map, long spawnCoid)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)spawnCoid,
            OriginalIsActive = false,
            IsActive = false,
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = TemplateId,
            IsTemplate = true,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[spawnCoid] = tpl;
        var spawn = (SpawnPoint)tpl.Create();
        spawn.SetCoid(spawnCoid, false);
        spawn.Position = new Vector3(100f, 4f, -50f);
        spawn.SetMap(map);
        return spawn;
    }

    private static void PlaceCreate(SectorMap map, long reactionCoid, long targetCoid)
    {
        var tpl = new ReactionTemplate { COID = (int)reactionCoid, ReactionType = ReactionType.Create };
        tpl.Objects.Add(targetCoid);
        map.MapData.Templates[reactionCoid] = tpl;
        var rx = new Reaction(tpl);
        rx.SetCoid(reactionCoid, false);
        rx.SetMap(map);
    }

    private static void SeedKillQuest(Character character, int templateId)
    {
        var obj = AutoCore.Game.Mission.MissionObjective.CreateForTests(95_001, 0, 92_977, 1);
        obj.Requirements.Add(new AutoCore.Game.Mission.Requirements.ObjectiveRequirementKill(obj)
        {
            TargetCBID = templateId,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
        });
        var mission = AutoCore.Game.Mission.Mission.CreateForTests(92_977, obj);
        AssetManager.Instance.SetTestMission(mission);
        var quest = new CharacterQuest(92_977, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void SeedPath(SectorMap map, long pathCoid, params Vector3[] points)
    {
        var path = new MapPathTemplate { COID = (int)pathCoid, ReverseDirection = false };
        foreach (var p in points)
            path.Points.Add(new MapPathTemplate.MapPathPoint { Position = p, AcceptDistance = 2f });
        map.MapData.Templates[pathCoid] = path;
    }

    private static SectorMap CreateFieldMap(int continentId = ContId)
    {
        return SectorMap.CreateForTests(new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_npc_vehicle_ai_{continentId}",
            DisplayName = "npc-vehicle-ai",
            IsTown = false,
            IsPersistent = true,
        }, new Vector4());
    }

    private static (Character Character, Vehicle Vehicle) PlaceConnectedPlayer(
        SectorMap map,
        Vector3 position,
        int faction)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(500 + map.ContinentId, true);
        character.Faction = faction;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle { Position = position };
        vehicle.SetCoid(600 + map.ContinentId, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle);
    }

    private static void ScopeGhost(Vehicle vehicle)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.BeginGhostingForTests();
        connection.ObjectInScope(vehicle.Ghost!);
        connection.ObjectLocalScopeAlways(vehicle.Ghost!);
    }

    private static Vehicle SingleOwnedVehicle(SectorMap map, long spawnCoid)
        => map.Objects.Values.OfType<Vehicle>().Single(v => v.SpawnOwnerCoid == spawnCoid);
}
