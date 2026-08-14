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
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

/// <summary>
/// Pass 21 — persisted mission-state must restore condition-gated FAM graphics/gates
/// on login / transfer / re-entry without the player re-entering the original volume.
/// </summary>
[TestClass]
public class PersistedMissionGateTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    // Wastes (708) Dunlap completed-mission gate — live FAM.
    private const int WastesContinent = 708;
    private const int DunlapMissionId = 2966;
    private const long DunlapTriggerCoid = 16525;
    private const long DunlapCreateOpenRx = 19129;
    private const long DunlapDeleteClosedRx = 19130;
    private const long DunlapDeletePhysicsRx = 19220;
    private const long DunlapOpenGfxCoid = 19089;
    private const long DunlapClosedGfxCoid = 19090;
    private const long DunlapPhysicsGfxCoid = 19219;
    private const int DunlapDoneVar = 18;
    private const int DunlapConstOne = 20;

    // Tierra Roja (698) Archon mid-mission gate — live FAM.
    private const int TierraContinent = 698;
    private const int ArchonMissionId = 2970;
    private const long ArchonTriggerCoid = 3744;
    private const long ArchonDeathRx = 3749;
    private const long ArchonClosedGfxCoid = 251;
    private const int ArchonActiveVar = 15;
    private const int ArchonConstOne = 2;

    // Wastes pike ambush — volume-only negative control.
    private const long PikeRushTriggerCoid = 18585;
    private const long PikeCreateRx = 18574;
    private const long PikeSpawnA = 18570;
    private const long PikeSpawnB = 18571;
    private const long PikeSpawnC = 18572;
    private const int GateCrashersMissionId = 2990;
    private const int GateCrashersVar = 57;

    // Synthetic isolation ids (never collide with live FAM).
    private const int SynthContId = 8621;
    private const int SynthMissionId = 92166;
    private const int SynthObjectiveId = 95421;
    private const int SynthMidMissionId = 92170;
    private const int SynthMidObjectiveId = 95470;
    private const int SynthDoneVar = 301;
    private const int SynthActiveVar = 302;
    private const int SynthActiveObjVar = 303;
    private const int SynthConstOne = 304;
    private const long SynthVolumeTrigger = 97201;
    private const long SynthDeleteRx = 97210;
    private const long SynthCreateRx = 97211;
    private const long SynthClosedGate = 97220;
    private const long SynthOpenGate = 97221;
    private const long SynthAmbushTrigger = 97301;
    private const long SynthAmbushCreateRx = 97310;
    private const long SynthAmbushSpawn = 97320;

    private readonly List<BasePacket> _sent = new();

    /// <summary>
    /// Unload the retail catalog this suite loaded into the process-wide
    /// <see cref="AssetManager"/>. Without it every later test in the assembly resolves
    /// against real WAD data instead of its own fixtures. See <c>LiveAssetIsolationTests</c>.
    /// </summary>
    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void UnloadLiveAssets() => AssetManager.Instance.ClearLiveAssetsForTests();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    /// <summary>
    /// Characterization: player already completed the Dunlap-class mission, re-enters far
    /// from the original volume, and the closed-gate graphics must already be suppressed.
    /// Fails today because SS-51 _entryScopedReeval skips out-of-volume collision gates.
    /// </summary>
    [TestMethod]
    public void PersistedMissionGate_ReentryRestoresExpectedState()
    {
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld();
        PlacePlayerFarFromOrigin(character, vehicle);

        SimulateWorldEntry(character, vehicle, map);

        Assert.IsTrue(character.MapPresence.IsSuppressed(SynthClosedGate),
            "Completed-mission closed-gate graphic must be personally suppressed on re-entry without re-entering the volume");
        Assert.IsTrue(character.MapPresence.IsMaterialized(SynthOpenGate),
            "Completed-mission open-gate graphic must be personally materialized on re-entry");
        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Any(p => p.Count > 0),
            "Retail restore is 0x206C GroupReactionCall — server must send it before first scope");
    }

    [TestMethod]
    public void PersistedMissionGate_LoginAndTransferEquivalent()
    {
        var login = RunSyntheticCompletedEntry();
        TriggerManager.Instance.ClearAllForTests();
        var transfer = RunSyntheticCompletedEntry();

        Assert.AreEqual(login.ClosedSuppressed, transfer.ClosedSuppressed);
        Assert.AreEqual(login.OpenMaterialized, transfer.OpenMaterialized);
        Assert.AreEqual(login.SentReaction, transfer.SentReaction);
        Assert.IsTrue(login.ClosedSuppressed, "login and transfer must both restore the gate");
    }

    [TestMethod]
    public void PersistedMissionGate_FreshPlayerKeepsDefaultState()
    {
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld(completeMission: false);
        PlacePlayerFarFromOrigin(character, vehicle);

        SimulateWorldEntry(character, vehicle, map);

        Assert.IsFalse(character.MapPresence.IsSuppressed(SynthClosedGate),
            "A player who has not completed the mission must keep the default closed FAM gate");
        Assert.IsFalse(character.MapPresence.IsMaterialized(SynthOpenGate),
            "A fresh player must not receive the open-gate Create");
    }

    [TestMethod]
    public void PersistedMissionGate_ReplayIsIdempotent()
    {
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld();
        PlacePlayerFarFromOrigin(character, vehicle);

        SimulateWorldEntry(character, vehicle, map);
        var packetsAfterFirst = _sent.OfType<GroupReactionCallPacket>().Count();
        var objectsAfterFirst = map.Objects.Count;

        map.ApplyMissionPhaseWorldState(vehicle);
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(SynthClosedGate));
        Assert.AreSame(map.GetObjectByCoid(SynthClosedGate), map.GetObjectByCoid(SynthClosedGate));
        Assert.AreEqual(objectsAfterFirst, map.Objects.Count,
            "Repeated replay must not duplicate FAM objects");
        Assert.AreEqual(1, character.CompletedMissionIds.Count,
            "Replay must not advance or duplicate mission completion");
        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Count() <= packetsAfterFirst + 1,
            "Repeated Apply must not spam 0x206C");
    }

    [TestMethod]
    public void PersistedMissionGate_UsesRealFamTargetCoid()
    {
        WithLiveWastes((mapData, wad) =>
        {
            AssertLiveDunlapGraph(mapData);
            var (character, vehicle, map) = PlaceLiveDunlapGraph(mapData, wad, completed: true);
            PlacePlayerFarFromOrigin(character, vehicle);

            SimulateWorldEntry(character, vehicle, map);

            Assert.IsTrue(character.MapPresence.IsSuppressed(DunlapClosedGfxCoid),
                $"Wastes closed-gate FAM COID {DunlapClosedGfxCoid} must be suppressed from trigger {DunlapTriggerCoid}");
            Assert.IsTrue(character.MapPresence.IsMaterialized(DunlapOpenGfxCoid),
                $"Wastes open-gate FAM COID {DunlapOpenGfxCoid} must be materialized");
            Assert.IsTrue(character.MapPresence.IsSuppressed(DunlapPhysicsGfxCoid),
                $"Wastes physics-gate FAM COID {DunlapPhysicsGfxCoid} must be suppressed");
        });
    }

    [TestMethod]
    public void PersistedMissionGate_DoesNotCreateDuplicateFamObject()
    {
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld();
        var closedBefore = map.GetObjectByCoid(SynthClosedGate);
        Assert.IsNotNull(closedBefore);
        PlacePlayerFarFromOrigin(character, vehicle);

        SimulateWorldEntry(character, vehicle, map);
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.AreSame(closedBefore, map.GetObjectByCoid(SynthClosedGate),
            "Replay must not instantiate a second FAM-local graphics object");
        Assert.AreEqual(1, map.Objects.Values.Count(o => o.ObjectId.Coid == SynthClosedGate));
        Assert.AreEqual(1, map.Objects.Values.Count(o => o.ObjectId.Coid == SynthOpenGate));
    }

    [TestMethod]
    public void PersistedMissionGate_AppliesBeforeFirstScope()
    {
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld();
        PlacePlayerFarFromOrigin(character, vehicle);

        character.BeginWorldEntry();
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsFalse(character.MapPresence.IsSuppressed(SynthClosedGate),
            "SS-51: nothing may fire before the create stream");

        character.CompleteWorldEntry();

        Assert.IsTrue(character.MapPresence.IsSuppressed(SynthClosedGate),
            "Persisted gate restore must run in CompleteWorldEntry, before first PerformScopeQuery");
    }

    [TestMethod]
    public void PersistedMissionGate_LiveMissionAdvanceUpdatesState()
    {
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld(completeMission: false);
        PlacePlayerFarFromOrigin(character, vehicle);
        Assert.IsTrue(character.WorldEntryComplete);

        Assert.IsFalse(character.MapPresence.IsSuppressed(SynthClosedGate));

        character.CompletedMissionIds.Add(SynthMissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(SynthClosedGate),
            "Mid-play complete must open the gate immediately (existing out-of-volume path)");

        var packets = _sent.OfType<GroupReactionCallPacket>().Count();
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsTrue(character.MapPresence.IsSuppressed(SynthClosedGate));
        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Count() <= packets + 1,
            "Re-entry replay after a live advance must stay idempotent");
    }

    [TestMethod]
    public void PersistedMissionGate_CompletedMissionRestoresState()
    {
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld();
        PlacePlayerFarFromOrigin(character, vehicle);
        SimulateWorldEntry(character, vehicle, map);

        Assert.IsTrue(character.CompletedMissionIds.Contains(SynthMissionId));
        Assert.IsTrue(character.MapPresence.IsSuppressed(SynthClosedGate));
        Assert.IsTrue(character.MapPresence.IsMaterialized(SynthOpenGate));
    }

    [TestMethod]
    public void PersistedMissionGate_MidMissionObjectiveRestoresState()
    {
        var (character, vehicle, map) = CreateSyntheticMidMissionGateWorld();
        PlacePlayerFarFromOrigin(character, vehicle);

        SimulateWorldEntry(character, vehicle, map);

        Assert.IsTrue(character.CurrentQuests.Any(q => q.MissionId == SynthMidMissionId));
        Assert.IsTrue(character.MapPresence.IsSuppressed(SynthClosedGate),
            "Persisted mid-mission type-12/11 gate must restore on re-entry");
    }

    [TestMethod]
    public void PersistedMissionReaction_MissingTargetLogsContext()
    {
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld();
        var missingRx = new ReactionTemplate
        {
            COID = 97410,
            ReactionType = ReactionType.Delete,
            Name = "synth_delete_missing",
        };
        missingRx.Objects.Add(99_999_111);
        var missing = new Reaction(missingRx);
        missing.SetCoid(97410, false);
        missing.SetMap(map);
        var trigger = (Trigger)map.GetObjectByCoid(SynthVolumeTrigger);
        trigger.Template.Reactions.Add(97410);
        PlacePlayerFarFromOrigin(character, vehicle);

        using var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            SimulateWorldEntry(character, vehicle, map);
        }
        finally
        {
            Console.SetOut(original);
        }

        var log = writer.ToString();
        StringAssert.Contains(log, "MissionWorldState");
        StringAssert.Contains(log, "99999111");
        StringAssert.Contains(log, SynthContId.ToString());
    }

    [TestMethod]
    public void VolumeOnlyAmbush_DoesNotReplayOnEntry()
    {
        var (character, vehicle, map) = CreateSyntheticAmbushWorld();
        PlacePlayerFarFromOrigin(character, vehicle);

        SimulateWorldEntry(character, vehicle, map);

        var spawn = (SpawnPoint)map.GetObjectByCoid(SynthAmbushSpawn);
        Assert.IsFalse(spawn.HasLiveSpawn(),
            "Volume-only ambush Create must not remotely fire just because the player entered the map");
        Assert.IsFalse(character.MapPresence.IsMaterialized(SynthAmbushSpawn));
        Assert.AreEqual(0, spawn.Template.OriginalIsActive ? 1 : 0);
    }

    [TestMethod]
    public void VolumeOnlyTrigger_DoesNotFakeTriggerEnter()
    {
        var (character, vehicle, map) = CreateSyntheticAmbushWorld();
        PlacePlayerFarFromOrigin(character, vehicle);

        SimulateWorldEntry(character, vehicle, map);

        var trigger = map.GetObjectByCoid(SynthAmbushTrigger) as Trigger;
        Assert.IsNotNull(trigger);
        Assert.AreEqual(0, trigger.FireCount,
            "Entry restore must not fake TriggerEnter / increment FireCount on volume-only ambushes");
    }

    [TestMethod]
    public void SharedMap_PerPlayerGateStateDoesNotLeakIfRetailPersonal()
    {
        var (playerA, vehicleA, map) = CreateSyntheticCompletedGateWorld();
        PlacePlayerFarFromOrigin(playerA, vehicleA);
        SimulateWorldEntry(playerA, vehicleA, map);

        var playerB = NewCharacter(170);
        var vehicleB = NewVehicle(171);
        playerB.SetCurrentVehicleForTests(vehicleB);
        playerB.SetMap(map);
        vehicleB.SetMap(map);
        PlacePlayerFarFromOrigin(playerB, vehicleB);
        SimulateWorldEntry(playerB, vehicleB, map);

        Assert.IsTrue(playerA.MapPresence.IsSuppressed(SynthClosedGate));
        Assert.IsFalse(playerB.MapPresence.IsSuppressed(SynthClosedGate),
            "Retail Delete/Death is personal 0x206C — player B without the mission must keep the default gate");
        Assert.IsNotNull(map.GetObjectByCoid(SynthClosedGate),
            "Shared map must keep the FAM object; only personal presence changes");
    }

    [TestMethod]
    public void PrivateInstance_PersistedGateRestoresCorrectly()
    {
        var previous = InstancedContinents.ActiveSet;
        InstancedContinents.SetForTests(new HashSet<int> { SynthContId, WastesContinent });
        try
        {
            var (character, vehicle, map) = CreateSyntheticCompletedGateWorld();
            Assert.IsTrue(InstancedContinents.IsInstanced(SynthContId));
            PlacePlayerFarFromOrigin(character, vehicle);
            SimulateWorldEntry(character, vehicle, map);

            Assert.IsTrue(character.MapPresence.IsSuppressed(SynthClosedGate),
                "Private tutorial-instance replay uses the same persisted-condition path");
        }
        finally
        {
            InstancedContinents.SetForTests(previous);
        }
    }

    [TestMethod]
    public void RealMissionGate_BeforeAfterMatchesRetailData()
    {
        WithLiveWastes((mapData, wad) =>
        {
            AssertLiveDunlapGraph(mapData);
            var (character, vehicle, map) = PlaceLiveDunlapGraph(mapData, wad, completed: true);
            PlacePlayerFarFromOrigin(character, vehicle);

            Assert.IsFalse(character.MapPresence.IsSuppressed(DunlapClosedGfxCoid), "before: default FAM closed");
            Assert.IsFalse(character.MapPresence.IsMaterialized(DunlapOpenGfxCoid), "before: open mesh not created");

            SimulateWorldEntry(character, vehicle, map);

            Assert.IsTrue(character.MapPresence.IsSuppressed(DunlapClosedGfxCoid), "after: closed mesh gone");
            Assert.IsTrue(character.MapPresence.IsMaterialized(DunlapOpenGfxCoid), "after: open mesh present");
        });
    }

    [TestMethod]
    public void RealMissionMaps_TierraRojaArchonGateRestoresOnMidMissionReentry()
    {
        WithLiveAssets((glm, wad) =>
        {
            var live = ReadFam(glm, "sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", TierraContinent);
            var triggerTpl = (TriggerTemplate)live.Templates[ArchonTriggerCoid];
            Assert.IsTrue(triggerTpl.Reactions.Contains(ArchonDeathRx));

            if (wad.Missions.TryGetValue(ArchonMissionId, out var mission))
                AssetManager.Instance.SetTestMission(mission);
            else
                AssetManager.Instance.SetTestMission(
                    MissionDef.CreateForTests(ArchonMissionId,
                        MissionObjective.CreateForTests(5270, 0, ArchonMissionId, 1)));

            var map = CreateMap(TierraContinent);
            CopyVar(live, map, ArchonActiveVar);
            CopyVar(live, map, ArchonConstOne);
            CopyAndPlace(live, map, ArchonTriggerCoid);
            CopyAndPlace(live, map, ArchonDeathRx);
            CopyAndPlace(live, map, ArchonClosedGfxCoid);

            var (character, vehicle) = PlacePlayer(map, 698160, 698161);
            var quest = new CharacterQuest(ArchonMissionId, 0);
            quest.PopulateFromAssets();
            character.CurrentQuests.Add(quest);
            PlacePlayerFarFromOrigin(character, vehicle);

            Assert.IsFalse(character.MapPresence.IsSuppressed(ArchonClosedGfxCoid));
            SimulateWorldEntry(character, vehicle, map);
            Assert.IsTrue(character.MapPresence.IsSuppressed(ArchonClosedGfxCoid),
                $"TR Archon closed-gate FAM {ArchonClosedGfxCoid} must restore from persisted mission {ArchonMissionId}");
        });
    }

    [TestMethod]
    public void RealMissionMaps_WastesPikeAmbushDoesNotReplayOnEntry()
    {
        WithLiveAssets((glm, wad) =>
        {
            var live = ReadFam(glm, "sec_f_b_map_mis_a3_1_wastes", "The Wastes", WastesContinent);
            if (wad.Missions.TryGetValue(GateCrashersMissionId, out var mission))
                AssetManager.Instance.SetTestMission(mission);

            var map = CreateMap(WastesContinent);
            CopyVar(live, map, GateCrashersVar);
            CopyVar(live, map, DunlapConstOne);
            CopyAndPlace(live, map, PikeRushTriggerCoid);
            CopyAndPlace(live, map, PikeCreateRx);
            CopyAndPlace(live, map, PikeSpawnA);
            CopyAndPlace(live, map, PikeSpawnB);
            CopyAndPlace(live, map, PikeSpawnC);

            var (character, vehicle) = PlacePlayer(map, 708260, 708261);
            var quest = new CharacterQuest(GateCrashersMissionId, 0);
            quest.PopulateFromAssets();
            character.CurrentQuests.Add(quest);
            PlacePlayerFarFromOrigin(character, vehicle);

            SimulateWorldEntry(character, vehicle, map);

            Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(PikeSpawnA)).HasLiveSpawn(),
                "Wastes pike camp 18570 must not remotely Create on map entry");
            Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(PikeSpawnB)).HasLiveSpawn());
            Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(PikeSpawnC)).HasLiveSpawn());
            Assert.AreEqual(0, ((Trigger)map.GetObjectByCoid(PikeRushTriggerCoid)).FireCount);
        });
    }

    [TestMethod]
    public void RealMissionMaps_ConditionGatedGraphicsBaseline()
    {
        WithLiveAssets((glm, wad) =>
        {
            var wastes = ReadFam(glm, "sec_f_b_map_mis_a3_1_wastes", "The Wastes", 708);
            AssertLiveDunlapGraph(wastes);

            var tr = ReadFam(glm, "sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 698);
            var archon = (TriggerTemplate)tr.Templates[ArchonTriggerCoid];
            Assert.IsTrue(archon.DoCollision);
            Assert.IsTrue(archon.DoConditionals);
            Assert.IsTrue(archon.Reactions.Contains(ArchonDeathRx));

            var ark = ReadFam(glm, "sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", 707);
            var door = (TriggerTemplate)ark.Templates[14097];
            Assert.AreEqual("l1_coll_opendoor_1", door.Name);
            Assert.IsTrue(door.DoCollision);
        });
    }

    private (bool ClosedSuppressed, bool OpenMaterialized, bool SentReaction) RunSyntheticCompletedEntry()
    {
        _sent.Clear();
        var (character, vehicle, map) = CreateSyntheticCompletedGateWorld();
        PlacePlayerFarFromOrigin(character, vehicle);
        SimulateWorldEntry(character, vehicle, map);
        return (
            character.MapPresence.IsSuppressed(SynthClosedGate),
            character.MapPresence.IsMaterialized(SynthOpenGate),
            _sent.OfType<GroupReactionCallPacket>().Any(p => p.Count > 0));
    }

    private static void SimulateWorldEntry(Character character, Vehicle vehicle, SectorMap map)
    {
        character.BeginWorldEntry();
        map.ApplyMissionPhaseWorldState(vehicle);
        character.CompleteWorldEntry();
    }

    private static void PlacePlayerFarFromOrigin(Character character, Vehicle vehicle)
    {
        var far = new Vector3(4000, 0, 4000);
        vehicle.Position = far;
        character.Position = far;
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateSyntheticCompletedGateWorld(
        bool completeMission = true)
    {
        AssetManager.Instance.SetTestMission(
            MissionDef.CreateForTests(SynthMissionId, MissionObjective.CreateForTests(SynthObjectiveId, 0, SynthMissionId, 1)));

        var map = CreateMap(SynthContId);
        map.MapData.Variables[SynthDoneVar] = Variable.CreateForTests(
            SynthDoneVar, LogicVariableStore.TypeHasCompletedMission, SynthMissionId, SynthMissionId, "done");
        map.MapData.Variables[SynthConstOne] = Variable.CreateForTests(
            SynthConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");

        PlaceGraphics(map, SynthClosedGate, famActive: true);
        PlaceGraphics(map, SynthOpenGate, famActive: false);
        PlaceDeleteReaction(map, SynthDeleteRx, SynthClosedGate);
        PlaceCreateReaction(map, SynthCreateRx, SynthOpenGate);
        PlaceCollisionGateTrigger(map, SynthVolumeTrigger, SynthDoneVar, SynthConstOne,
            new[] { SynthDeleteRx, SynthCreateRx });

        var (character, vehicle) = PlacePlayer(map, 160, 161);
        if (completeMission)
            character.CompletedMissionIds.Add(SynthMissionId);
        return (character, vehicle, map);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateSyntheticMidMissionGateWorld()
    {
        var objective = MissionObjective.CreateForTests(SynthMidObjectiveId, 0, SynthMidMissionId, 1);
        AssetManager.Instance.SetTestMission(MissionDef.CreateForTests(SynthMidMissionId, objective));

        var map = CreateMap(SynthContId);
        map.MapData.Variables[SynthActiveVar] = Variable.CreateForTests(
            SynthActiveVar, LogicVariableStore.TypeHasActiveMission, SynthMidMissionId, SynthMidMissionId, "act");
        map.MapData.Variables[SynthActiveObjVar] = Variable.CreateForTests(
            SynthActiveObjVar, LogicVariableStore.TypeHasActiveObjective, SynthMidObjectiveId, SynthMidObjectiveId, "obj");
        map.MapData.Variables[SynthConstOne] = Variable.CreateForTests(
            SynthConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");

        PlaceGraphics(map, SynthClosedGate, famActive: true);
        PlaceDeleteReaction(map, SynthDeleteRx, SynthClosedGate);
        PlaceCollisionGateTrigger(map, SynthVolumeTrigger, SynthActiveObjVar, SynthConstOne,
            new[] { SynthDeleteRx });

        var (character, vehicle) = PlacePlayer(map, 160, 161);
        var quest = new CharacterQuest(SynthMidMissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
        return (character, vehicle, map);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateSyntheticAmbushWorld()
    {
        var objective = MissionObjective.CreateForTests(SynthMidObjectiveId, 0, GateCrashersMissionId, 1);
        AssetManager.Instance.SetTestMission(MissionDef.CreateForTests(GateCrashersMissionId, objective));

        var map = CreateMap(SynthContId);
        map.MapData.Variables[SynthActiveVar] = Variable.CreateForTests(
            SynthActiveVar, LogicVariableStore.TypeHasActiveMission, GateCrashersMissionId, GateCrashersMissionId, "crashers");
        map.MapData.Variables[SynthConstOne] = Variable.CreateForTests(
            SynthConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");

        var spawnTpl = new SpawnPointTemplate
        {
            COID = (int)SynthAmbushSpawn,
            OriginalIsActive = false,
        };
        spawnTpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = 13564,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
            IsTemplate = false,
        });
        map.MapData.Templates[SynthAmbushSpawn] = spawnTpl;
        var spawn = (SpawnPoint)spawnTpl.Create();
        spawn.SetCoid(SynthAmbushSpawn, false);
        spawn.SetMap(map);

        PlaceCreateReaction(map, SynthAmbushCreateRx, SynthAmbushSpawn);
        PlaceCollisionGateTrigger(map, SynthAmbushTrigger, SynthActiveVar, SynthConstOne,
            new[] { SynthAmbushCreateRx });

        var (character, vehicle) = PlacePlayer(map, 160, 161);
        var quest = new CharacterQuest(GateCrashersMissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
        return (character, vehicle, map);
    }

    private static void AssertLiveDunlapGraph(MapData mapData)
    {
        Assert.IsTrue(mapData.Templates.TryGetValue(DunlapTriggerCoid, out var t));
        Assert.IsInstanceOfType(t, typeof(TriggerTemplate));
        var trigger = (TriggerTemplate)t;
        Assert.AreEqual("tsp_btut1_coll_givemission_talk-to-dunlap-complete_2", trigger.Name);
        Assert.IsTrue(trigger.DoCollision);
        Assert.IsTrue(trigger.DoConditionals);
        Assert.IsTrue(trigger.Reactions.Contains(DunlapCreateOpenRx));
        Assert.IsTrue(trigger.Reactions.Contains(DunlapDeleteClosedRx));
        Assert.IsTrue(trigger.Reactions.Contains(DunlapDeletePhysicsRx));

        var closed = (GraphicsObjectTemplate)mapData.Templates[DunlapClosedGfxCoid];
        Assert.IsTrue(closed.IsActive);
        var open = (GraphicsObjectTemplate)mapData.Templates[DunlapOpenGfxCoid];
        Assert.IsFalse(open.IsActive);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) PlaceLiveDunlapGraph(
        MapData live, WADLoader wad, bool completed)
    {
        if (wad.Missions.TryGetValue(DunlapMissionId, out var mission))
            AssetManager.Instance.SetTestMission(mission);
        else
            AssetManager.Instance.SetTestMission(
                MissionDef.CreateForTests(DunlapMissionId, MissionObjective.CreateForTests(5400, 0, DunlapMissionId, 1)));

        var map = CreateMap(WastesContinent);
        CopyVar(live, map, DunlapDoneVar);
        CopyVar(live, map, DunlapConstOne);
        CopyAndPlace(live, map, DunlapTriggerCoid);
        CopyAndPlace(live, map, DunlapCreateOpenRx);
        CopyAndPlace(live, map, DunlapDeleteClosedRx);
        CopyAndPlace(live, map, DunlapDeletePhysicsRx);
        CopyAndPlace(live, map, DunlapOpenGfxCoid);
        CopyAndPlace(live, map, DunlapClosedGfxCoid);
        CopyAndPlace(live, map, DunlapPhysicsGfxCoid);

        var (character, vehicle) = PlacePlayer(map, 708160, 708161);
        if (completed)
            character.CompletedMissionIds.Add(DunlapMissionId);
        return (character, vehicle, map);
    }

    private static void CopyVar(MapData live, SectorMap map, int id)
    {
        if (live.Variables.TryGetValue(id, out var v))
            map.MapData.Variables[id] = v;
    }

    private static void CopyAndPlace(MapData live, SectorMap map, long coid)
    {
        if (!live.Templates.TryGetValue(coid, out var tpl) || tpl == null)
            Assert.Fail($"live FAM missing COID {coid}");

        map.MapData.Templates[coid] = tpl;
        var obj = tpl.Create();
        if (obj.ObjectId.Coid <= 0)
            obj.SetCoid(tpl.COID != 0 ? tpl.COID : coid, false);
        obj.SetMap(map);
    }

    private static void PlaceCollisionGateTrigger(
        SectorMap map, long triggerCoid, int leftVar, int rightVar, long[] reactions)
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
            Name = "synth_coll_open-gate",
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

    private static void PlaceDeleteReaction(SectorMap map, long reactionCoid, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Delete,
            Name = "synth_delete_gate",
        };
        tpl.Objects.Add(objectCoid);
        map.MapData.Templates[reactionCoid] = tpl;
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static void PlaceCreateReaction(SectorMap map, long reactionCoid, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Create,
            Name = "synth_create_gate",
        };
        tpl.Objects.Add(objectCoid);
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

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_gate_{continentId}",
            DisplayName = "gate-test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
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

    private static void WithLiveWastes(Action<MapData, WADLoader> body)
    {
        WithLiveAssets((glm, wad) =>
        {
            var mapData = ReadFam(glm, "sec_f_b_map_mis_a3_1_wastes", "The Wastes", WastesContinent);
            body(mapData, wad);
        });
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
