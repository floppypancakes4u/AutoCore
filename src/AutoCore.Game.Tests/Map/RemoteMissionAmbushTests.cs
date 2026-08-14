using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using MissionDef = AutoCore.Game.Mission.Mission;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

/// <summary>
/// Pass 22 — mid-play <see cref="TriggerManager.OnMissionStateChanged"/> must not remotely
/// fire collision-authored ambushes. Persistent graphics still apply; volume encounters wait.
/// </summary>
[TestClass]
public class RemoteMissionAmbushTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    private const int CreatureCbid = 822_001;
    private const int SynthContId = 8622;
    private const int SynthMissionId = 92266;
    private const int SynthObjectiveId = 95521;
    private const int SynthDoneMissionId = 92270;
    private const int SynthDoneObjectiveId = 95570;
    private const int SynthActiveVar = 401;
    private const int SynthDoneVar = 402;
    private const int SynthConstOne = 403;
    private const int SynthLatchVar = 404;

    private const long AmbushTriggerCoid = 98201;
    private const long AmbushCreateRx = 98210;
    private const long AmbushSpawnCoid = 98220;
    private const long GateTriggerCoid = 98301;
    private const long GateDeleteRx = 98310;
    private const long GateClosedGfx = 98320;
    private const long MixedTriggerCoid = 98401;
    private const long MixedDeathRx = 98410;
    private const long MixedActivateRx = 98411;
    private const long MixedGfxCoid = 98420;
    private const long MixedCascadeTrigger = 98430;
    private const long MixedCascadeCreateRx = 98431;
    private const long MixedCascadeSpawn = 98440;
    private const long RemCondTriggerCoid = 98501;
    private const long RemCondDeleteRx = 98510;
    private const long RemCondGfx = 98520;
    // Live FAM — The Wastes (708)
    private const int WastesContinent = 708;
    private const int GateCrashersMissionId = 2990;
    private const long PikeRushTriggerCoid = 18585;
    private const long PikeSpawnA = 18570;
    private const long PikeSpawnB = 18571;
    private const long PikeSpawnC = 18572;
    private const int DunlapMissionId = 2966;
    private const long DunlapTriggerCoid = 16525;
    private const long DunlapClosedGfxCoid = 19090;
    private const long DunlapOpenGfxCoid = 19089;

    // Live FAM — Hestia Ark Bay 313 (707)
    private const int ArkBayContinent = 707;
    private const long CollapseTriggerCoid = 15823;
    private const long CollapseActivateReaction = 15844;
    private const long CollapseActivateTarget = 15821;
    private const long ScavSpawnA = 15839;
    private const long ScavSpawnB = 15840;
    private const long ScavSpawnC = 15841;
    private const long ScavSpawnD = 15842;
    private const long GunnyInitiateCoid = 14130;
    private const long GunnyActivateTarget = 16283;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    // ------------------------------------------------------------------
    // Characterization / desired mid-play policy
    // ------------------------------------------------------------------

    [TestMethod]
    public void RemoteMissionChange_CollisionAmbushDoesNotFire()
    {
        var (character, vehicle, map) = CreateAmbushWorld();
        PlaceFar(character, vehicle);

        GrantActiveMission(character, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        var trigger = (Trigger)map.GetObjectByCoid(AmbushTriggerCoid);
        var spawn = (SpawnPoint)map.GetObjectByCoid(AmbushSpawnCoid);
        Assert.AreEqual(0, trigger.FireCount,
            "Collision ambush must stay dormant when mission state changes outside the volume");
        Assert.IsFalse(spawn.HasLiveSpawn());
    }

    [TestMethod]
    public void RemoteMissionChange_DoesNotIncrementCollisionTriggerFireCount()
    {
        var (character, vehicle, map) = CreateAmbushWorld();
        PlaceFar(character, vehicle);

        GrantActiveMission(character, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount,
            "Remote mission re-eval must not consume ActivationCount / FireCount");
    }

    [TestMethod]
    public void RemoteMissionChange_DoesNotMaterializeSpawnPointChildren()
    {
        var (character, vehicle, map) = CreateAmbushWorld();
        PlaceFar(character, vehicle);

        GrantActiveMission(character, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        var spawn = (SpawnPoint)map.GetObjectByCoid(AmbushSpawnCoid);
        Assert.IsFalse(spawn.HasLiveSpawn());
        Assert.AreEqual(0, CountOwned(map, AmbushSpawnCoid));
    }

    [TestMethod]
    public void RemoteMissionChange_DoesNotActivateEncounterCascade()
    {
        var (character, vehicle, map) = CreateMixedWorld();
        PlaceFar(character, vehicle);

        GrantActiveMission(character, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(MixedTriggerCoid)).FireCount);
        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(MixedCascadeTrigger)).FireCount,
            "Activate cascade must not run from a remote mission-state change");
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(MixedCascadeSpawn)).HasLiveSpawn());
    }

    [TestMethod]
    public void RemoteMissionChange_PersistentGraphicsStillUpdates()
    {
        var (character, vehicle, map) = CreateGraphicsGateWorld();
        PlaceFar(character, vehicle);

        character.CompletedMissionIds.Add(SynthDoneMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(GateClosedGfx),
            "Persisted graphics gates must still apply remotely on mid-play complete");
        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(GateTriggerCoid)).FireCount,
            "Graphics restore must not consume the collision trigger FireCount");
    }

    [TestMethod]
    public void MixedGraph_RemoteChangeAppliesGraphicsButNotAmbush()
    {
        var (character, vehicle, map) = CreateMixedWorld();
        PlaceFar(character, vehicle);

        GrantActiveMission(character, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(MixedGfxCoid),
            "Mixed-graph Death of FAM graphics is persistent world state");
        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(MixedTriggerCoid)).FireCount);
        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(MixedCascadeTrigger)).FireCount);
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(MixedCascadeSpawn)).HasLiveSpawn());
    }

    [TestMethod]
    public void CollisionEncounter_FiresWhenPlayerLaterEntersVolume()
    {
        var (character, vehicle, map) = CreateAmbushWorld();
        PlaceFar(character, vehicle);

        GrantActiveMission(character, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount);

        PlaceAtOrigin(character, vehicle);
        TriggerManager.Instance.CheckTriggersFor(vehicle);

        Assert.AreEqual(1, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount);
        Assert.IsTrue(((SpawnPoint)map.GetObjectByCoid(AmbushSpawnCoid)).HasLiveSpawn(),
            "Entering the authored volume after the condition is true must spawn the encounter");
    }

    [TestMethod]
    public void CollisionEncounter_FiresExactlyOnce()
    {
        var (character, vehicle, map) = CreateAmbushWorld();
        PlaceAtOrigin(character, vehicle);
        GrantActiveMission(character, SynthMissionId);

        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        TriggerManager.Instance.CheckTriggersFor(vehicle);

        Assert.AreEqual(1, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount);
        Assert.AreEqual(1, CountOwned(map, AmbushSpawnCoid));
    }

    [TestMethod]
    public void CollisionEncounter_ConditionFalseAtEntryDoesNotFire()
    {
        var (_, vehicle, map) = CreateAmbushWorld();
        PlaceAtOrigin(null, vehicle);

        TriggerManager.Instance.CheckTriggersFor(vehicle);

        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount);
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(AmbushSpawnCoid)).HasLiveSpawn());
    }

    [TestMethod]
    public void MissionCondition_TrueThenFalseBeforeArrivalDoesNotLatch()
    {
        var (character, vehicle, map) = CreateAmbushWorld();
        PlaceFar(character, vehicle);

        GrantActiveMission(character, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        character.CurrentQuests.Clear();
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        PlaceAtOrigin(character, vehicle);
        TriggerManager.Instance.CheckTriggersFor(vehicle);

        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount,
            "A condition that flipped false before arrival must not have been latched remotely");
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(AmbushSpawnCoid)).HasLiveSpawn());
    }

    [TestMethod]
    public void PlayerAlreadyInsideVolume_MissionStateChangeMatchesRetail()
    {
        // Retail StepTriggers → DoPhantomCollisions re-scans current overlaps each tick.
        // Standing inside when the journal condition becomes true must fire without exit/re-enter.
        var (character, vehicle, map) = CreateAmbushWorld();
        PlaceAtOrigin(character, vehicle);

        GrantActiveMission(character, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.AreEqual(1, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount);
        Assert.IsTrue(((SpawnPoint)map.GetObjectByCoid(AmbushSpawnCoid)).HasLiveSpawn());
    }

    [TestMethod]
    public void EntryReplay_VolumeOnlyAmbushStillDoesNotFire()
    {
        var (character, vehicle, map) = CreateAmbushWorld();
        PlaceFar(character, vehicle);
        GrantActiveMission(character, SynthMissionId);

        SimulateWorldEntry(character, vehicle, map);

        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount);
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(AmbushSpawnCoid)).HasLiveSpawn());
    }

    [TestMethod]
    public void EntryReplay_PersistedGateStillRestores()
    {
        var (character, vehicle, map) = CreateGraphicsGateWorld();
        PlaceFar(character, vehicle);
        character.CompletedMissionIds.Add(SynthDoneMissionId);

        SimulateWorldEntry(character, vehicle, map);

        Assert.IsTrue(character.MapPresence.IsSuppressed(GateClosedGfx));
        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(GateTriggerCoid)).FireCount);
    }

    [TestMethod]
    public void NonCollisionConditionTrigger_StillFiresOnMissionStateChange()
    {
        var (character, vehicle, map) = CreateRemoteConditionWorld();
        PlaceFar(character, vehicle);

        character.CompletedMissionIds.Add(SynthDoneMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(RemCondGfx),
            "DoCollision=false condition watchers must still fire on journal change");
        Assert.AreEqual(1, ((Trigger)map.GetObjectByCoid(RemCondTriggerCoid)).FireCount);
    }

    [TestMethod]
    public void MissionRemoteTriggerPolicy_IsIdempotent()
    {
        var (character, vehicle, map) = CreateMixedWorld();
        PlaceFar(character, vehicle);
        GrantActiveMission(character, SynthMissionId);

        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        var packets = _sent.OfType<GroupReactionCallPacket>().Count();
        var objects = map.Objects.Count;
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(MixedGfxCoid));
        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(MixedTriggerCoid)).FireCount);
        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(MixedCascadeTrigger)).FireCount);
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(MixedCascadeSpawn)).HasLiveSpawn());
        Assert.AreEqual(objects, map.Objects.Count);
        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Count() <= packets + 1);
    }

    [TestMethod]
    public void SharedMap_RemoteMissionChangeDoesNotSpawnEncounterForOtherPlayer()
    {
        var (playerA, vehicleA, map) = CreateAmbushWorld();
        PlaceFar(playerA, vehicleA);

        var playerB = NewCharacter(270);
        var vehicleB = NewVehicle(271);
        playerB.SetCurrentVehicleForTests(vehicleB);
        playerB.SetMap(map);
        vehicleB.SetMap(map);
        PlaceAtOrigin(playerB, vehicleB);

        GrantActiveMission(playerA, SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicleA);

        Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(AmbushTriggerCoid)).FireCount);
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(AmbushSpawnCoid)).HasLiveSpawn(),
            "Player A's remote journal change must not globally spawn a volume encounter");
    }

    // ------------------------------------------------------------------
    // Live FAM graphs
    // ------------------------------------------------------------------

    [TestMethod]
    public void Wastes18585_RemoteMissionChangeDoesNotSpawnPikes()
    {
        WithLiveAssets((glm, wad) =>
        {
            var live = ReadFam(glm, "sec_f_b_map_mis_a3_1_wastes", "The Wastes", WastesContinent);
            AssertPikeGraph(live);

            var map = CreateMap(WastesContinent);
            PlaceLiveTriggerGraph(live, map, PikeRushTriggerCoid);
            SeedWadMission(wad, GateCrashersMissionId);

            var (character, vehicle) = PlacePlayer(map, 708860, 708861);
            PlaceFar(character, vehicle);
            GrantWadQuest(character, GateCrashersMissionId);

            TriggerManager.Instance.OnMissionStateChanged(vehicle);

            Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(PikeRushTriggerCoid)).FireCount,
                "Wastes 18585 must not fire when mission 2990 becomes active across the map");
            Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(PikeSpawnA)).HasLiveSpawn());
            Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(PikeSpawnB)).HasLiveSpawn());
            Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(PikeSpawnC)).HasLiveSpawn());
            Assert.IsFalse(character.MapPresence.IsMaterialized(18852),
                "18585 explosion Create is encounter FX, not a persisted gate");
            Assert.IsFalse(character.MapPresence.IsMaterialized(18853));
        });
    }

    [TestMethod]
    public void Wastes18585_EnteringVolumeSpawnsPikes()
    {
        WithLiveAssets((glm, wad) =>
        {
            var live = ReadFam(glm, "sec_f_b_map_mis_a3_1_wastes", "The Wastes", WastesContinent);
            var map = CreateMap(WastesContinent);
            PlaceLiveTriggerGraph(live, map, PikeRushTriggerCoid);
            SeedWadMission(wad, GateCrashersMissionId);

            var (character, vehicle) = PlacePlayer(map, 708862, 708863);
            PlaceFar(character, vehicle);
            GrantWadQuest(character, GateCrashersMissionId);
            TriggerManager.Instance.OnMissionStateChanged(vehicle);
            Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(PikeRushTriggerCoid)).FireCount);

            var trigger = (Trigger)map.GetObjectByCoid(PikeRushTriggerCoid);
            vehicle.Position = trigger.Position;
            character.Position = trigger.Position;
            TriggerManager.Instance.CheckTriggersFor(vehicle);

            Assert.AreEqual(1, trigger.FireCount,
                "Driving into 18585 after 2990 is active must fire the pike Creates once");
        });
    }

    [TestMethod]
    public void ArkBay15823_RemoteChangeDoesNotActivateScavAmbush()
    {
        WithLiveAssets((glm, wad) =>
        {
            var live = ReadFam(glm, "sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", ArkBayContinent);
            AssertCollapseGraph(live);

            var map = CreateMap(ArkBayContinent);
            PlaceLiveTriggerGraph(live, map, CollapseTriggerCoid);
            PlaceLiveObject(live, map, CollapseActivateTarget);
            PlaceLiveObject(live, map, ScavSpawnA);
            PlaceLiveObject(live, map, ScavSpawnB);
            PlaceLiveObject(live, map, ScavSpawnC);
            PlaceLiveObject(live, map, ScavSpawnD);

            var (character, vehicle) = PlacePlayer(map, 707860, 707861);
            PlaceFar(character, vehicle);
            SatisfyLiveConditions(live, character, CollapseTriggerCoid);

            TriggerManager.Instance.OnMissionStateChanged(vehicle);

            Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(CollapseTriggerCoid)).FireCount,
                "Ark Bay 15823 must not consume FireCount remotely");
            if (map.GetObjectByCoid(CollapseActivateTarget) is Trigger cascade)
                Assert.AreEqual(0, cascade.FireCount, "Activate 15844 must wait for the volume");
        });
    }

    [TestMethod]
    public void ArkBay15823_PersistentGraphicsBehaviorMatchesRetail()
    {
        WithLiveAssets((glm, wad) =>
        {
            var live = ReadFam(glm, "sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", ArkBayContinent);
            var map = CreateMap(ArkBayContinent);
            PlaceLiveTriggerGraph(live, map, CollapseTriggerCoid);

            var (character, vehicle) = PlacePlayer(map, 707862, 707863);
            PlaceFar(character, vehicle);
            SatisfyLiveConditions(live, character, CollapseTriggerCoid);

            TriggerManager.Instance.OnMissionStateChanged(vehicle);

            var deathTargets = CollectReactionTargets(live, CollapseTriggerCoid, ReactionType.Death);
            Assert.IsTrue(deathTargets.Count > 0, "15823 must author Death graphics");
            foreach (var gfx in deathTargets)
            {
                if (map.GetObjectByCoid(gfx) is GraphicsObject)
                {
                    Assert.IsTrue(character.MapPresence.IsSuppressed(gfx),
                        $"15823 Death target {gfx} is persistent graphics and must apply remotely");
                }
            }

            Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(CollapseTriggerCoid)).FireCount);
        });
    }

    [TestMethod]
    public void ArkBay14130_RemainsVolumeControlled()
    {
        WithLiveAssets((glm, wad) =>
        {
            var live = ReadFam(glm, "sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", ArkBayContinent);
            Assert.IsTrue(live.Templates.TryGetValue(GunnyInitiateCoid, out var raw));
            Assert.IsInstanceOfType(raw, typeof(TriggerTemplate));
            var gunny = (TriggerTemplate)raw;
            Assert.IsTrue(gunny.DoCollision);
            Assert.IsTrue(gunny.DoConditionals);
            Assert.IsTrue(gunny.Reactions.Count > 0);

            var map = CreateMap(ArkBayContinent);
            PlaceLiveTriggerGraph(live, map, GunnyInitiateCoid);
            PlaceLiveObject(live, map, GunnyActivateTarget);

            var (character, vehicle) = PlacePlayer(map, 707864, 707865);
            PlaceFar(character, vehicle);
            SatisfyLiveConditions(live, character, GunnyInitiateCoid);

            TriggerManager.Instance.OnMissionStateChanged(vehicle);

            Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(GunnyInitiateCoid)).FireCount,
                "Gunny initiate 14130 must stay volume-controlled");
            if (map.GetObjectByCoid(GunnyActivateTarget) is Trigger rem)
                Assert.AreEqual(0, rem.FireCount);
        });
    }

    [TestMethod]
    public void RealMissionMaps_CollisionAmbushBaseline()
    {
        WithLiveAssets((glm, wad) =>
        {
            var wastes = ReadFam(glm, "sec_f_b_map_mis_a3_1_wastes", "The Wastes", 708);
            AssertPikeGraph(wastes);
            AssertDunlapGraph(wastes);

            var ark = ReadFam(glm, "sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", 707);
            AssertCollapseGraph(ark);
            Assert.IsTrue(ark.Templates[GunnyInitiateCoid] is TriggerTemplate gunny
                          && gunny.DoCollision
                          && gunny.DoConditionals);

            var tr = ReadFam(glm, "sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 698);
            var archon = (TriggerTemplate)tr.Templates[3744];
            Assert.IsTrue(archon.DoCollision);
            Assert.IsTrue(archon.DoConditionals);

            var dump = DumpConditionCollisionTriggers(new[]
            {
                (wastes, "The Wastes", 708),
                (ark, "Hestia Ark Bay 313", 707),
                (tr, "Tierra Roja Dam", 698),
            });
            Console.WriteLine(dump);
            Assert.IsTrue(dump.Contains("18585"));
            Assert.IsTrue(dump.Contains("15823"));
            Assert.IsTrue(dump.Contains("14130"));
        });
    }

    // ------------------------------------------------------------------
    // Worlds
    // ------------------------------------------------------------------

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateAmbushWorld()
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);
        SeedMission(SynthMissionId, SynthObjectiveId);
        var map = CreateMap(SynthContId);
        SeedActiveVars(map);
        PlaceInactiveCreatureSpawn(map, AmbushSpawnCoid);
        PlaceCreateReaction(map, AmbushCreateRx, AmbushSpawnCoid);
        PlaceCollisionTrigger(map, AmbushTriggerCoid, SynthActiveVar, SynthConstOne,
            new[] { AmbushCreateRx }, "synth_coll_ambush");
        return PlaceOnMap(map, 260, 261);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateGraphicsGateWorld()
    {
        SeedMission(SynthDoneMissionId, SynthDoneObjectiveId);
        var map = CreateMap(SynthContId);
        map.MapData.Variables[SynthDoneVar] = Variable.CreateForTests(
            SynthDoneVar, LogicVariableStore.TypeHasCompletedMission, SynthDoneMissionId, SynthDoneMissionId, "done");
        map.MapData.Variables[SynthConstOne] = Variable.CreateForTests(
            SynthConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");
        PlaceGraphics(map, GateClosedGfx, famActive: true);
        PlaceDeleteReaction(map, GateDeleteRx, GateClosedGfx);
        PlaceCollisionTrigger(map, GateTriggerCoid, SynthDoneVar, SynthConstOne,
            new[] { GateDeleteRx }, "synth_coll_gate");
        return PlaceOnMap(map, 262, 263);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateMixedWorld()
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);
        SeedMission(SynthMissionId, SynthObjectiveId);
        var map = CreateMap(SynthContId);
        SeedActiveVars(map);

        PlaceGraphics(map, MixedGfxCoid, famActive: true);
        PlaceDeleteReaction(map, MixedDeathRx, MixedGfxCoid);
        // Death reaction type (not Delete) — matches Ark Bay collapse.
        var deathTpl = (ReactionTemplate)map.MapData.Templates[MixedDeathRx];
        deathTpl.ReactionType = ReactionType.Death;
        deathTpl.Name = "synth_death_gfx";

        PlaceInactiveCreatureSpawn(map, MixedCascadeSpawn);
        PlaceCreateReaction(map, MixedCascadeCreateRx, MixedCascadeSpawn);
        PlaceActivateTargetTrigger(map, MixedCascadeTrigger, MixedCascadeCreateRx);

        var activateTpl = new ReactionTemplate
        {
            COID = (int)MixedActivateRx,
            ReactionType = ReactionType.Activate,
            Name = "synth_activate_ambush",
        };
        activateTpl.Objects.Add(MixedCascadeTrigger);
        map.MapData.Templates[MixedActivateRx] = activateTpl;
        var activate = new Reaction(activateTpl);
        activate.SetCoid(MixedActivateRx, false);
        activate.SetMap(map);

        PlaceCollisionTrigger(map, MixedTriggerCoid, SynthActiveVar, SynthConstOne,
            new[] { MixedDeathRx, MixedActivateRx }, "synth_coll_mixed");
        return PlaceOnMap(map, 264, 265);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateRemoteConditionWorld()
    {
        SeedMission(SynthDoneMissionId, SynthDoneObjectiveId);
        var map = CreateMap(SynthContId);
        map.MapData.Variables[SynthDoneVar] = Variable.CreateForTests(
            SynthDoneVar, LogicVariableStore.TypeHasCompletedMission, SynthDoneMissionId, SynthDoneMissionId, "done");
        map.MapData.Variables[SynthConstOne] = Variable.CreateForTests(
            SynthConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");
        PlaceGraphics(map, RemCondGfx, famActive: true);
        PlaceDeleteReaction(map, RemCondDeleteRx, RemCondGfx);

        var tpl = new TriggerTemplate
        {
            COID = (int)RemCondTriggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 1f,
            DoCollision = false,
            DoConditionals = true,
            AllConditionsNeeded = true,
            ActivationCount = 1,
            Name = "synth_rem_cond",
        };
        tpl.Reactions.Add(RemCondDeleteRx);
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = SynthDoneVar,
            RightId = SynthConstOne,
            Type = ConditionalType.EqualTo,
        });
        map.MapData.Templates[RemCondTriggerCoid] = tpl;
        var trigger = new Trigger(tpl);
        trigger.SetCoid(RemCondTriggerCoid, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 1f;
        trigger.SetMap(map);

        return PlaceOnMap(map, 266, 267);
    }

    private void SeedActiveVars(SectorMap map)
    {
        map.MapData.Variables[SynthActiveVar] = Variable.CreateForTests(
            SynthActiveVar, LogicVariableStore.TypeHasActiveMission, SynthMissionId, SynthMissionId, "act");
        map.MapData.Variables[SynthConstOne] = Variable.CreateForTests(
            SynthConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");
        map.MapData.Variables[SynthLatchVar] = Variable.CreateForTests(
            SynthLatchVar, LogicVariableStore.TypeConstant, 0f, 0f, "latch");
    }

    private static void SeedMission(int missionId, int objectiveId)
    {
        AssetManager.Instance.SetTestMission(
            MissionDef.CreateForTests(missionId, MissionObjective.CreateForTests(objectiveId, 0, missionId, 1)));
    }

    private static void GrantActiveMission(Character character, int missionId)
    {
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void SimulateWorldEntry(Character character, Vehicle vehicle, SectorMap map)
    {
        character.BeginWorldEntry();
        map.ApplyMissionPhaseWorldState(vehicle);
        character.CompleteWorldEntry();
    }

    private static void PlaceFar(Character character, Vehicle vehicle)
    {
        var far = new Vector3(4000, 0, 4000);
        vehicle.Position = far;
        if (character != null)
            character.Position = far;
    }

    private static void PlaceAtOrigin(Character character, Vehicle vehicle)
    {
        vehicle.Position = new Vector3(0, 0, 0);
        if (character != null)
            character.Position = vehicle.Position;
    }

    private static (Character Character, Vehicle Vehicle, SectorMap Map) PlaceOnMap(
        SectorMap map, long charCoid, long vehCoid)
    {
        var (character, vehicle) = PlacePlayer(map, charCoid, vehCoid);
        return (character, vehicle, map);
    }

    private static (Character Character, Vehicle Vehicle) PlacePlayer(SectorMap map, long charCoid, long vehCoid)
    {
        var character = NewCharacter(charCoid);
        var vehicle = NewVehicle(vehCoid);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle);
    }

    private static Character NewCharacter(long coid)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(coid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        return character;
    }

    private static Vehicle NewVehicle(long coid)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        return vehicle;
    }

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_ambush_{continentId}",
            DisplayName = "ambush-test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static void PlaceInactiveCreatureSpawn(SectorMap map, long coid)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)coid,
            OriginalIsActive = false,
            IsActive = false,
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[coid] = tpl;
        var spawn = (SpawnPoint)tpl.Create();
        spawn.SetCoid(coid, false);
        spawn.Position = new Vector3(0, 0, 0);
        spawn.SetMap(map);
    }

    private static void PlaceCreateReaction(SectorMap map, long reactionCoid, long targetCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Create,
            Name = "synth_create_spawn",
        };
        tpl.Objects.Add(targetCoid);
        map.MapData.Templates[reactionCoid] = tpl;
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static void PlaceDeleteReaction(SectorMap map, long reactionCoid, long targetCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Delete,
            Name = "synth_delete_gfx",
        };
        tpl.Objects.Add(targetCoid);
        map.MapData.Templates[reactionCoid] = tpl;
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static void PlaceGraphics(SectorMap map, long coid, bool famActive)
    {
        var tpl = new GraphicsObjectTemplate(GraphicsObjectType.Graphics)
        {
            COID = (int)coid,
            IsActive = famActive,
        };
        map.MapData.Templates[coid] = tpl;
        var obj = new GraphicsObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = new Vector3(0, 0, 0);
        obj.SetMap(map);
    }

    private static void PlaceCollisionTrigger(
        SectorMap map, long triggerCoid, int leftVar, int rightVar, long[] reactions, string name)
    {
        var tpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 25f,
            DoCollision = true,
            DoConditionals = true,
            AllConditionsNeeded = true,
            ActivationCount = 1,
            Name = name,
        };
        foreach (var rx in reactions)
            tpl.Reactions.Add(rx);
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = leftVar,
            RightId = rightVar,
            Type = ConditionalType.EqualTo,
        });
        map.MapData.Templates[triggerCoid] = tpl;
        var trigger = new Trigger(tpl);
        trigger.SetCoid(triggerCoid, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 25f;
        trigger.SetMap(map);
    }

    private static void PlaceActivateTargetTrigger(SectorMap map, long triggerCoid, long createRx)
    {
        var tpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 2f,
            DoCollision = false,
            DoConditionals = false,
            DoOnActivate = true,
            ActivationCount = 1,
            Name = "synth_rem_ambush_cascade",
        };
        tpl.Reactions.Add(createRx);
        map.MapData.Templates[triggerCoid] = tpl;
        var trigger = new Trigger(tpl);
        trigger.SetCoid(triggerCoid, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 2f;
        trigger.SetMap(map);
    }

    private static int CountOwned(SectorMap map, long spawnCoid)
        => map.Objects.Values.Count(o =>
            (o is Creature c && c is not Character && c.SpawnOwner == spawnCoid)
            || (o is Vehicle v && v.SpawnOwnerCoid == spawnCoid));

    // ------------------------------------------------------------------
    // Live FAM helpers
    // ------------------------------------------------------------------

    private static void AssertPikeGraph(MapData mapData)
    {
        var trigger = (TriggerTemplate)mapData.Templates[PikeRushTriggerCoid];
        Assert.IsTrue(trigger.DoCollision);
        Assert.IsTrue(trigger.DoConditionals);
        var targets = CollectReactionTargets(mapData, PikeRushTriggerCoid, ReactionType.Create);
        CollectionAssert.Contains(targets, PikeSpawnA);
        CollectionAssert.Contains(targets, PikeSpawnB);
        CollectionAssert.Contains(targets, PikeSpawnC);
        Assert.IsInstanceOfType(mapData.Templates[PikeSpawnA], typeof(SpawnPointTemplate));
    }

    private static void AssertDunlapGraph(MapData mapData)
    {
        var trigger = (TriggerTemplate)mapData.Templates[DunlapTriggerCoid];
        Assert.IsTrue(trigger.DoCollision);
        Assert.IsTrue(trigger.DoConditionals);
        Assert.IsInstanceOfType(mapData.Templates[DunlapClosedGfxCoid], typeof(GraphicsObjectTemplate));
        Assert.IsInstanceOfType(mapData.Templates[DunlapOpenGfxCoid], typeof(GraphicsObjectTemplate));
    }

    private static void AssertCollapseGraph(MapData mapData)
    {
        var trigger = (TriggerTemplate)mapData.Templates[CollapseTriggerCoid];
        Assert.IsTrue(trigger.DoCollision);
        Assert.IsTrue(trigger.DoConditionals);
        Assert.IsTrue(trigger.Reactions.Contains(CollapseActivateReaction));
        var activates = CollectReactionTargets(mapData, CollapseTriggerCoid, ReactionType.Activate);
        CollectionAssert.Contains(activates, CollapseActivateTarget);
        Assert.IsTrue(CollectReactionTargets(mapData, CollapseTriggerCoid, ReactionType.Death).Count > 0
                      || CollectReactionTargets(mapData, CollapseTriggerCoid, ReactionType.Delete).Count > 0);
    }

    private static List<long> CollectReactionTargets(MapData mapData, long triggerCoid, ReactionType type)
    {
        var result = new List<long>();
        var trigger = (TriggerTemplate)mapData.Templates[triggerCoid];
        foreach (var rxCoid in trigger.Reactions)
        {
            if (!mapData.Templates.TryGetValue(rxCoid, out var tpl) || tpl is not ReactionTemplate rx)
                continue;
            if (rx.ReactionType != type)
                continue;
            result.AddRange(rx.Objects);
        }

        return result;
    }

    private static void PlaceLiveTriggerGraph(MapData live, SectorMap map, long triggerCoid)
    {
        PlaceLiveObject(live, map, triggerCoid);
        if (!live.Templates.TryGetValue(triggerCoid, out var raw) || raw is not TriggerTemplate trigger)
            return;

        foreach (var cond in trigger.Conditions)
        {
            CopyVar(live, map, cond.LeftId);
            CopyVar(live, map, cond.RightId);
        }

        foreach (var rxCoid in trigger.Reactions)
        {
            PlaceLiveObject(live, map, rxCoid);
            if (!live.Templates.TryGetValue(rxCoid, out var rxRaw) || rxRaw is not ReactionTemplate rx)
                continue;
            foreach (var target in rx.Objects)
            {
                PlaceLiveObject(live, map, target);
                if (live.Templates.TryGetValue(target, out var nested) && nested is TriggerTemplate nestedTrig)
                {
                    foreach (var nestedRx in nestedTrig.Reactions)
                    {
                        PlaceLiveObject(live, map, nestedRx);
                        if (live.Templates.TryGetValue(nestedRx, out var nestedRxTpl)
                            && nestedRxTpl is ReactionTemplate nestedReaction)
                        {
                            foreach (var nestedTarget in nestedReaction.Objects)
                                PlaceLiveObject(live, map, nestedTarget);
                        }
                    }
                }
            }
        }
    }

    private static void PlaceLiveObject(MapData live, SectorMap map, long coid)
    {
        if (map.GetObjectByCoid(coid) != null)
            return;
        if (!live.Templates.TryGetValue(coid, out var tpl) || tpl == null)
            return;

        map.MapData.Templates[coid] = tpl;
        var obj = tpl.Create();
        if (obj.ObjectId.Coid <= 0)
            obj.SetCoid(tpl.COID != 0 ? tpl.COID : coid, false);
        obj.SetMap(map);
    }

    private static void CopyVar(MapData live, SectorMap map, int id)
    {
        if (live.Variables.TryGetValue(id, out var v))
            map.MapData.Variables[id] = v;
    }

    private static void SeedWadMission(WADLoader wad, int missionId)
    {
        if (wad.Missions.TryGetValue(missionId, out var mission))
            AssetManager.Instance.SetTestMission(mission);
        else
            AssetManager.Instance.SetTestMission(
                MissionDef.CreateForTests(missionId, MissionObjective.CreateForTests(missionId + 1000, 0, missionId, 1)));
    }

    private static void GrantWadQuest(Character character, int missionId)
    {
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void SatisfyLiveConditions(MapData live, Character character, long triggerCoid)
    {
        var trigger = (TriggerTemplate)live.Templates[triggerCoid];
        foreach (var cond in trigger.Conditions)
        {
            if (!live.Variables.TryGetValue(cond.LeftId, out var def) || def == null)
                continue;

            switch (def.Type)
            {
                case LogicVariableStore.TypeHasActiveMission:
                    SeedWadOrSynthetic((int)def.Value);
                    if (character.CurrentQuests.All(q => q.MissionId != (int)def.Value))
                    {
                        var quest = new CharacterQuest((int)def.Value, 0);
                        quest.PopulateFromAssets();
                        character.CurrentQuests.Add(quest);
                    }
                    break;
                case LogicVariableStore.TypeHasCompletedMission:
                    SeedWadOrSynthetic((int)def.Value);
                    character.CompletedMissionIds.Add((int)def.Value);
                    break;
                case LogicVariableStore.TypeHasActiveObjective:
                    EnsureObjectiveActive(character, (int)def.Value);
                    break;
                case LogicVariableStore.TypeHasCompletedObjective:
                    break;
            }
        }
    }

    private static void SeedWadOrSynthetic(int missionId)
    {
        if (AssetManager.Instance.GetMission(missionId) != null)
            return;
        AssetManager.Instance.SetTestMission(
            MissionDef.CreateForTests(missionId, MissionObjective.CreateForTests(missionId + 2000, 0, missionId, 1)));
    }

    private static void EnsureObjectiveActive(Character character, int objectiveId)
    {
        foreach (var quest in character.CurrentQuests)
        {
            var existing = AssetManager.Instance.GetMission(quest.MissionId);
            if (existing?.Objectives != null
                && existing.Objectives.Values.Any(o => o.ObjectiveId == objectiveId))
            {
                return;
            }
        }

        var mission = FindMissionForObjective(objectiveId);
        if (mission != null)
        {
            AssetManager.Instance.SetTestMission(mission);
            if (character.CurrentQuests.All(q => q.MissionId != mission.Id))
            {
                var quest = new CharacterQuest(mission.Id, 0);
                quest.PopulateFromAssets();
                character.CurrentQuests.Add(quest);
            }

            return;
        }

        var synthetic = MissionDef.CreateForTests(
            90_000 + objectiveId,
            MissionObjective.CreateForTests(objectiveId, 0, 90_000 + objectiveId, 1));
        AssetManager.Instance.SetTestMission(synthetic);
        var q2 = new CharacterQuest(synthetic.Id, 0);
        q2.PopulateFromAssets();
        character.CurrentQuests.Add(q2);
    }

    private static MissionDef FindMissionForObjective(int objectiveId)
    {
        // WAD missions are keyed by mission id; walk the test/WAD cache via known tutorial ids.
        foreach (var missionId in new[] { 554, 2966, 2967, 2970, 2990, 3032, 3037, 3041, 3050 })
        {
            var mission = AssetManager.Instance.GetMission(missionId);
            if (mission?.Objectives == null)
                continue;
            if (mission.Objectives.Values.Any(o => o.ObjectiveId == objectiveId))
                return mission;
        }

        return null;
    }

    private static string DumpConditionCollisionTriggers(
        IEnumerable<(MapData Data, string Label, int ContinentId)> maps)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("| Map | Trigger | Condition | Distance/Volume | Reactions | Combat target? | Should remote-fire? |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- |");
        foreach (var (data, label, _) in maps)
        {
            foreach (var trigger in data.Templates.Values.OfType<TriggerTemplate>()
                         .Where(t => t.DoCollision && t.DoConditionals && t.Conditions.Count > 0)
                         .OrderBy(t => t.COID))
            {
                var conds = string.Join("; ", trigger.Conditions.Select(c => DescribeVar(data, c.LeftId) + " " + c.Type));
                var rxs = new List<string>();
                var combat = false;
                var graphics = false;
                foreach (var rxCoid in trigger.Reactions)
                {
                    if (!data.Templates.TryGetValue(rxCoid, out var tpl) || tpl is not ReactionTemplate rx)
                        continue;
                    rxs.Add($"{rx.ReactionType}");
                    foreach (var target in rx.Objects)
                    {
                        if (!data.Templates.TryGetValue(target, out var t) || t == null)
                            continue;
                        if (t is SpawnPointTemplate)
                            combat = true;
                        if (t is GraphicsObjectTemplate)
                            graphics = true;
                        if (rx.ReactionType == ReactionType.Activate)
                            combat = true;
                    }
                }

                var remote = graphics && !combat ? "graphics only" : combat ? "NO (volume)" : "n/a";
                sb.AppendLine(
                    $"| {label} | {trigger.COID} | {conds} | coll scale={trigger.Scale:0.##} act={trigger.ActivationCount} | {string.Join(",", rxs)} | {combat} | {remote} |");
            }
        }

        return sb.ToString();
    }

    private static string DescribeVar(MapData mapData, int id)
    {
        if (!mapData.Variables.TryGetValue(id, out var v) || v == null)
            return $"var{id}?";
        var typeName = v.Type switch
        {
            LogicVariableStore.TypeConstant => "const",
            LogicVariableStore.TypeHasCompletedMission => "doneMis",
            LogicVariableStore.TypeHasCompletedObjective => "doneObj",
            LogicVariableStore.TypeHasActiveMission => "actMis",
            LogicVariableStore.TypeHasActiveObjective => "actObj",
            _ => $"t{v.Type}",
        };
        return $"{typeName}({v.Value},'{v.Name}')";
    }

    private static MapData ReadFam(GLMLoader glm, string famName, string label, int continentId)
    {
        using var famStream = glm.GetStream($"{famName}.fam");
        Assert.IsNotNull(famStream, $"{famName}.fam missing from GLM packs");
        var mapData = new MapData(new ContinentObject
        {
            Id = continentId,
            MapFileName = famName,
            DisplayName = label,
            IsTown = false,
            IsPersistent = continentId is 698 or 707 or 708,
        });
        using var reader = new BinaryReader(famStream);
        mapData.Read(reader);
        return mapData;
    }

    private static void WithLiveAssets(Action<GLMLoader, WADLoader> body)
    {
        if (!File.Exists(Path.Combine(InstallPath, "clonebase.wad")))
        {
            Assert.Inconclusive($"clonebase.wad not at {InstallPath}");
            return;
        }

        var wad = (WADLoader)typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        var glm = (GLMLoader)typeof(AssetManager)
            .GetProperty("GLMLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;

        var loadedWadHere = wad.CloneBases.Count == 0;
        var loadedGlmHere = !glm.CanGetReader("sec_f_b_map_mis_a3_1_wastes.fam");

        if (loadedGlmHere)
            Assert.IsTrue(glm.Load(InstallPath), "GLM load failed");

        if (loadedWadHere)
        {
            wad.Missions.Clear();
            wad.Skills.Clear();
            wad.CloneBases.Clear();
            wad.ArmorPrefixes.Clear();
            wad.PowerPlantPrefixes.Clear();
            wad.WeaponPrefixes.Clear();
            wad.VehiclePrefixes.Clear();
            wad.OrnamentPrefixes.Clear();
            wad.RaceItemPrefixes.Clear();
            Assert.IsTrue(wad.Load(Path.Combine(InstallPath, "clonebase.wad")), "WAD load failed");
        }

        TriggerManager.Instance.ClearAllForTests();
        try
        {
            body(glm, wad);
        }
        finally
        {
            TriggerManager.Instance.ClearAllForTests();
        }
    }
}
