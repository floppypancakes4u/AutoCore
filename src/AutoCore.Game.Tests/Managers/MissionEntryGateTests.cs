using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// SS-51: world-entry gate for mission-state trigger re-evaluation.
///
/// Live incident 2026-08-10 (map 661 "Ground Zero", fresh character): SectorMap.EnterMap fires
/// ApplyMissionPhaseWorldState the moment a quest-holding character's Map is set — which on both
/// login and /warp happens BEFORE the client's CreateCharacter/CreateVehicle packets. On 661 that
/// mass-fired 68 collision condition triggers from outside their volumes (l0_empress_take-all_*,
/// l0_*_take-*), cascading hundreds of Death/Create/Delete reactions plus 0x206C client calls into
/// a client that had not yet materialized its own objects. Result: incomplete vehicle, wrong
/// position, unresponsive controls.
///
/// Two rules are pinned here:
///   R1 mission-state changes during world entry are deferred and coalesced, then flushed once
///      after the create stream (Character.CompleteWorldEntry).
///   R3 the entry-time flush requires collision triggers to be IN volume. Out-of-volume gate
///      opening stays available for genuine mid-play state changes (dialog turn-in etc.) — see
///      MissionStateTriggerReevalTests.DialogDeliverTurnIn_CompletedMission_OpensCollisionGateOutsideVolume.
///
/// Map-global on-entry content still fires (R2): small non-collision remote watchers below, plus
/// PerPlayerLoad (SectorMap.FireOnLoadPlayerMissions) and ReplayMissionWorldSetup, which run
/// after the create packets on their own paths.
/// </summary>
[TestClass]
public class MissionEntryGateTests
{
    private const int MissionId = 91060;
    private const int ObjectiveId = 92060;
    private const int ContId = 661;
    private const int VarActiveMission = 111;
    private const int VarConstOne = 112;
    private const long VolumeTriggerCoid = 96101;
    private const long RemoteTriggerCoid = 96102;
    private const long DeleteReactionCoid = 96110;
    private const long GateObjectCoid = 96120;

    [TestInitialize]
    public void SetUp()
    {
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestMethod]
    public void MissionStateChange_DuringWorldEntry_IsDeferredUntilEntryCompletes()
    {
        // SS-51: nothing may fire for a player between "map assigned" and "create packets sent".
        var (character, vehicle, map) = CreatePlayer();
        SeedMissionVars(map);
        PlaceConditionalTrigger(map, RemoteTriggerCoid, scale: 1f, reactionCoid: DeleteReactionCoid);
        PlaceDeletableObject(map, GateObjectCoid);
        GiveActiveQuest(character);
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = vehicle.Position;

        character.BeginWorldEntry();
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(GateObjectCoid),
            "mission re-eval must not fire while the client is still loading in");

        character.CompleteWorldEntry();

        Assert.IsTrue(character.MapPresence.IsSuppressed(GateObjectCoid),
            "the deferred re-eval must be flushed once world entry completes");
    }

    [TestMethod]
    public void EntryFlush_CollisionTriggerOutsideVolume_DoesNotFire()
    {
        // The 661 storm in miniature: a large collision gate whose conditions pass for a fresh
        // character standing far away must NOT fire just because they entered the map.
        var (character, vehicle, map) = CreatePlayer();
        SeedAlwaysTrueLatch(map);
        PlaceConditionalTrigger(map, VolumeTriggerCoid, scale: 25f, reactionCoid: DeleteReactionCoid,
            leftVar: VarConstOne, rightVar: VarConstOne);
        PlaceDeletableObject(map, GateObjectCoid);
        vehicle.Position = new Vector3(500, 0, 500);
        character.Position = vehicle.Position;

        character.BeginWorldEntry();
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        character.CompleteWorldEntry();

        Assert.IsFalse(character.MapPresence.IsSuppressed(GateObjectCoid),
            "entry re-eval must not open collision gates the player is nowhere near");
    }

    [TestMethod]
    public void EntryFlush_CollisionTriggerInsideVolume_StillFires()
    {
        // R2: a player who enters standing inside the volume still gets the gate opened.
        var (character, vehicle, map) = CreatePlayer();
        SeedMissionVars(map);
        PlaceConditionalTrigger(map, VolumeTriggerCoid, scale: 25f, reactionCoid: DeleteReactionCoid);
        PlaceDeletableObject(map, GateObjectCoid);
        GiveActiveQuest(character);
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = vehicle.Position;

        character.BeginWorldEntry();
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        character.CompleteWorldEntry();

        Assert.IsTrue(character.MapPresence.IsSuppressed(GateObjectCoid),
            "in-volume collision gates must still open on entry");
    }

    [TestMethod]
    public void EntryFlush_SmallRemoteWatcher_StillFiresRegardlessOfDistance()
    {
        // R2: map-global remote logic watchers (small, non-collision) are position-independent
        // and must keep firing on entry — that is the existing remote-watcher contract.
        var (character, vehicle, map) = CreatePlayer();
        SeedMissionVars(map);
        PlaceConditionalTrigger(map, RemoteTriggerCoid, scale: 1f, reactionCoid: DeleteReactionCoid);
        PlaceDeletableObject(map, GateObjectCoid);
        GiveActiveQuest(character);
        vehicle.Position = new Vector3(4000, 0, 4000);
        character.Position = vehicle.Position;

        character.BeginWorldEntry();
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        character.CompleteWorldEntry();

        Assert.IsTrue(character.MapPresence.IsSuppressed(GateObjectCoid),
            "non-collision remote watchers stay position-independent on entry");
    }

    [TestMethod]
    public void EnterMap_WhileEntryPending_DefersMissionPhaseUntilEntryCompletes()
    {
        // Models the live login: SectorMap.EnterMap runs ApplyMissionPhaseWorldState on SetMap,
        // long before the create stream. With entry pending it must defer instead.
        var (character, vehicle, map) = CreatePlayerDetached();
        SeedMissionVars(map);
        PlaceConditionalTrigger(map, RemoteTriggerCoid, scale: 1f, reactionCoid: DeleteReactionCoid);
        PlaceDeletableObject(map, GateObjectCoid);
        GiveActiveQuest(character);

        character.BeginWorldEntry();
        character.SetMap(map);
        vehicle.SetMap(map);

        Assert.IsFalse(character.MapPresence.IsSuppressed(GateObjectCoid),
            "EnterMap must not run mission phase world state before the client has loaded");

        character.CompleteWorldEntry();

        Assert.IsTrue(character.MapPresence.IsSuppressed(GateObjectCoid),
            "the deferred EnterMap mission phase must run once entry completes");
    }

    [TestMethod]
    public void ApplyMissionPhaseWorldState_DuringWorldEntry_DefersReplayHalfToo()
    {
        // ApplyMissionPhaseWorldState runs OnMissionStateChanged AND ReplayMissionWorldSetup.
        // Gating only the first left the second (-> FireMissionConditionTriggers) firing the
        // storm anyway: live 16:30:52, 116 gates between the deferral and the flush.
        var (character, vehicle, map) = CreatePlayer();
        SeedMissionVars(map);
        PlaceConditionalTrigger(map, RemoteTriggerCoid, scale: 1f, reactionCoid: DeleteReactionCoid);
        PlaceDeletableObject(map, GateObjectCoid);
        GiveActiveQuest(character);
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = vehicle.Position;

        character.BeginWorldEntry();
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(GateObjectCoid),
            "both halves of the mission phase must wait for the client to finish loading");

        character.CompleteWorldEntry();

        Assert.IsTrue(character.MapPresence.IsSuppressed(GateObjectCoid),
            "the deferred mission phase must still run once entry completes");
    }

    [TestMethod]
    public void ApplyMissionPhaseWorldState_DuringWorldEntry_DoesNotOpenDistantCollisionGate()
    {
        // The live 661 shape: quest-holding character enters, gate volume is far away.
        var (character, vehicle, map) = CreatePlayer();
        SeedAlwaysTrueLatch(map);
        PlaceConditionalTrigger(map, VolumeTriggerCoid, scale: 25f, reactionCoid: DeleteReactionCoid,
            leftVar: VarConstOne, rightVar: VarConstOne);
        PlaceDeletableObject(map, GateObjectCoid);
        vehicle.Position = new Vector3(500, 0, 500);
        character.Position = vehicle.Position;

        character.BeginWorldEntry();
        map.ApplyMissionPhaseWorldState(vehicle);
        character.CompleteWorldEntry();

        Assert.IsFalse(character.MapPresence.IsSuppressed(GateObjectCoid),
            "entering a map must not open collision gates across the continent");
    }

    [TestMethod]
    public void CompleteWorldEntry_WithNoDeferredWork_IsHarmless()
    {
        var (character, _, map) = CreatePlayer();
        SeedMissionVars(map);

        character.BeginWorldEntry();
        character.CompleteWorldEntry();
        character.CompleteWorldEntry();

        Assert.IsTrue(character.WorldEntryComplete,
            "completing entry twice must stay complete and must not throw");
    }

    private static void GiveActiveQuest(Character character)
    {
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(MissionId, MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1)));
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void SeedMissionVars(SectorMap map)
    {
        map.MapData.Variables[VarActiveMission] = Variable.CreateForTests(
            VarActiveMission, LogicVariableStore.TypeHasActiveMission, MissionId, 0f, "has_active");
        map.MapData.Variables[VarConstOne] = Variable.CreateForTests(
            VarConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");
    }

    /// <summary>
    /// Type-0 == type-0 is true for every player. Pins the SS-51 continent-wide storm
    /// without overlapping Pass 21 persisted journal-gate restore.
    /// </summary>
    private static void SeedAlwaysTrueLatch(SectorMap map)
    {
        map.MapData.Variables[VarConstOne] = Variable.CreateForTests(
            VarConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");
    }

    private static void PlaceConditionalTrigger(
        SectorMap map,
        long triggerCoid,
        float scale,
        long reactionCoid,
        int leftVar = VarActiveMission,
        int rightVar = VarConstOne)
    {
        PlaceDeleteReaction(map, reactionCoid, GateObjectCoid);

        var tpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = scale,
            DoCollision = scale > 2f,
            DoConditionals = true,
            AllConditionsNeeded = true,
            ActivationCount = -1,
        };
        tpl.Reactions.Add(reactionCoid);
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = leftVar,
            RightId = rightVar,
            Type = ConditionalType.EqualTo,
        });

        var trigger = new Trigger(tpl);
        trigger.SetCoid(triggerCoid, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = scale;
        trigger.SetMap(map);
    }

    private static void PlaceDeleteReaction(SectorMap map, long reactionCoid, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Delete,
        };
        tpl.Objects.Add(objectCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static void PlaceDeletableObject(SectorMap map, long coid)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = new Vector3(0, 0, 0);
        obj.SetMap(map);
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_entrygate_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var (character, vehicle, map) = CreatePlayerDetached();
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }

    /// <summary>Player built but not yet placed on the map (models pre-entry state).</summary>
    private static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayerDetached()
    {
        var map = CreateMap();
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(160, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(161, true);
        character.SetCurrentVehicleForTests(vehicle);

        return (character, vehicle, map);
    }
}
