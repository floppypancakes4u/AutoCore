using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Generic mission-state → trigger re-evaluation (logic vars type 9/11/12 + volume recheck).
/// Synthetic ids only — no retail mission/chain constants.
/// </summary>
[TestClass]
public class MissionStateTriggerReevalTests
{
    private const int MissionId = 91050;
    private const int ObjectiveId = 92050;
    private const int ContId = 707;
    private const int VarActiveMission = 101;
    private const int VarConstOne = 102;
    private const int VarCompleted = 103;
    private const int VarConstZero = 104;
    private const long VolumeTriggerCoid = 96001;
    private const long RemoteTriggerCoid = 96002;
    private const long DeleteReactionCoid = 96010;
    private const long GateObjectCoid = 96020;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void LogicVariableStore_Type11_ActiveMission_ReflectsCurrentQuests()
    {
        var (character, _, map) = CreatePlayer();
        SeedMissionVars(map);
        var store = character.EnsureLogicVariables();

        Assert.AreEqual(0f, store.Get(VarActiveMission));

        character.CurrentQuests.Add(new CharacterQuest(MissionId, 0));
        Assert.AreEqual(1f, store.Get(VarActiveMission));
    }

    [TestMethod]
    public void LogicVariableStore_Type9_CompletedMission_ReflectsHistory()
    {
        var (character, _, map) = CreatePlayer();
        SeedMissionVars(map);
        var store = character.EnsureLogicVariables();

        Assert.AreEqual(0f, store.Get(VarCompleted));
        character.CompletedMissionIds.Add(MissionId);
        Assert.AreEqual(1f, store.Get(VarCompleted));
    }

    [TestMethod]
    public void OnMissionStateChanged_InVolume_FiresWhenActiveMissionConditionMet()
    {
        SeedMission(MissionId, ObjectiveId);
        var (character, vehicle, map) = CreatePlayer();
        SeedMissionVars(map);

        // Volume trigger: active mission == const 1, player already standing on it.
        PlaceConditionalTrigger(
            map,
            VolumeTriggerCoid,
            scale: 50f,
            leftVar: VarActiveMission,
            rightVar: VarConstOne,
            reactionCoid: DeleteReactionCoid,
            gateObjectCoid: GateObjectCoid);

        PlaceDeletableObject(map, GateObjectCoid, new Vector3(0, 0, 0));
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = vehicle.Position;

        // Condition fails (no active mission) — no fire.
        TriggerManager.Instance.OnMissionStateChanged(vehicle);
        Assert.IsNotNull(map.GetObjectByCoid(GateObjectCoid));

        // Grant mission → type 11 becomes true → re-eval deletes gate object.
        character.CurrentQuests.Add(new CharacterQuest(MissionId, 0));
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsNull(map.GetObjectByCoid(GateObjectCoid), "Gate object should be deleted by mission-gated trigger");
        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Any());
    }

    [TestMethod]
    public void OnMissionStateChanged_RemoteTrigger_FiresWithoutVolume()
    {
        SeedMission(MissionId, ObjectiveId);
        var (character, vehicle, map) = CreatePlayer();
        SeedMissionVars(map);

        // Remote (scale 1): completed mission == 1
        PlaceConditionalTrigger(
            map,
            RemoteTriggerCoid,
            scale: 1f,
            leftVar: VarCompleted,
            rightVar: VarConstOne,
            reactionCoid: DeleteReactionCoid,
            gateObjectCoid: GateObjectCoid);

        PlaceDeletableObject(map, GateObjectCoid, new Vector3(100, 0, 100));
        // Player far from object; remote condition-only fire.
        vehicle.Position = new Vector3(0, 0, 0);

        character.CompletedMissionIds.Add(MissionId);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsNull(map.GetObjectByCoid(GateObjectCoid));
    }

    [TestMethod]
    public void GrantMission_ViaDialog_TriggersMissionStateReeval()
    {
        SeedMission(MissionId, ObjectiveId);
        var (character, vehicle, map) = CreatePlayer();
        SeedMissionVars(map);
        character.SetOwningConnection(character.OwningConnection);
        // Ensure connection has character for interact handler
        character.OwningConnection.CurrentCharacter = character;

        PlaceConditionalTrigger(
            map,
            VolumeTriggerCoid,
            scale: 50f,
            leftVar: VarActiveMission,
            rightVar: VarConstOne,
            reactionCoid: DeleteReactionCoid,
            gateObjectCoid: GateObjectCoid);
        PlaceDeletableObject(map, GateObjectCoid, new Vector3(0, 0, 0));
        vehicle.Position = new Vector3(0, 0, 0);

        var npcCoid = 97001L;
        var npc = new Creature();
        npc.SetCoid(npcCoid, false);
        npc.SetCbidForTests(93050);
        npc.Position = new Vector3(1, 0, 0);
        npc.SetMap(map);

        // Offerable mission from this NPC
        var offer = Mission.CreateForTests(MissionId, MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1));
        offer.NPC = 93050;
        offer.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(offer);

        NpcInteractHandler.HandleMissionDialogResponse(character.OwningConnection, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = true,
            MissionGiver = new TFID(npcCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsNull(map.GetObjectByCoid(GateObjectCoid), "Accept should re-eval mission triggers");
    }

    [TestMethod]
    public void VariableSet_FiresWatchingRemoteTrigger()
    {
        var (character, vehicle, map) = CreatePlayer();
        // Mutable flag var 200, const 1 at 201
        map.MapData.Variables[200] = Variable.CreateForTests(200, LogicVariableStore.TypeConstant, 0f, 0f, "flag");
        map.MapData.Variables[201] = Variable.CreateForTests(201, LogicVariableStore.TypeConstant, 1f, 1f, "one");
        character.EnsureLogicVariables();

        PlaceConditionalTrigger(
            map,
            RemoteTriggerCoid,
            scale: 1f,
            leftVar: 200,
            rightVar: 201,
            reactionCoid: DeleteReactionCoid,
            gateObjectCoid: GateObjectCoid);
        PlaceDeletableObject(map, GateObjectCoid, new Vector3(0, 0, 0));

        // VariableSet reaction: var[200] = var[201]
        var setTpl = new ReactionTemplate
        {
            COID = 98001,
            ReactionType = ReactionType.VariableSet,
            GenericVar1 = 200,
            GenericVar3 = 201,
        };
        var setReaction = new Reaction(setTpl);
        setReaction.SetCoid(98001L, false);
        setReaction.SetMap(map);

        Assert.IsTrue(setReaction.TriggerIfPossible(vehicle));
        Assert.IsNull(map.GetObjectByCoid(GateObjectCoid));
    }

    [TestMethod]
    public void OnMissionStateChanged_DoesNotFirePureActivateTarget_GunnyInitiatorStyle()
    {
        // Ark Bay 14134: scale=2, doColl=0, doCond=0, doOnAct=1, conds hasdeleted==0.
        // Must NOT fire on objective progress — only via Activate cascade after Final Exam
        // initiate volume. False fire deleted standing Gunny and spawned pathing combat car.
        var (character, vehicle, map) = CreatePlayer();
        PlaceDeletableObject(map, GateObjectCoid, new Vector3(0, 0, 0));
        PlaceDeleteReaction(map, DeleteReactionCoid, GateObjectCoid);

        // Latch-style condition (const 0 == const 0) always true — like hasdeleted==0.
        map.MapData.Variables[VarConstZero] = Variable.CreateForTests(
            VarConstZero, LogicVariableStore.TypeConstant, 0f, 0f, "zero");
        character.EnsureLogicVariables();

        var remTpl = new TriggerTemplate
        {
            COID = (int)RemoteTriggerCoid,
            Name = "l1_rem_gunnysioux_initiator",
            TargetType = TriggerTargetType.Players,
            Scale = 2f,
            DoCollision = false,
            DoConditionals = false,
            DoOnActivate = true,
            AllConditionsNeeded = false,
            ActivationCount = -1,
        };
        remTpl.Reactions.Add(DeleteReactionCoid);
        remTpl.Conditions.Add(new TriggerConditional
        {
            LeftId = VarConstZero,
            RightId = VarConstZero,
            Type = ConditionalType.EqualTo,
        });
        var rem = new Trigger(remTpl);
        rem.SetCoid(RemoteTriggerCoid, false);
        rem.Position = new Vector3(0, 0, 0);
        rem.Scale = 2f;
        rem.SetMap(map);

        vehicle.Position = new Vector3(0, 0, 0);
        character.CurrentQuests.Add(new CharacterQuest(MissionId, 0));
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsNotNull(map.GetObjectByCoid(GateObjectCoid),
            "Pure Activate-target trigger must not fire on mission re-eval");
        Assert.AreEqual(0, rem.FireCount);

        // Activate cascade still works (Final Exam initiate path).
        var actTpl = new ReactionTemplate
        {
            COID = 98050,
            ReactionType = ReactionType.Activate,
        };
        actTpl.Objects.Add(RemoteTriggerCoid);
        var act = new Reaction(actTpl);
        act.SetCoid(98050L, false);
        act.SetMap(map);
        Assert.IsTrue(act.TriggerIfPossible(vehicle));
        Assert.IsNull(map.GetObjectByCoid(GateObjectCoid),
            "Activate cascade must still fire the rem initiator");
    }

    [TestMethod]
    public void CheckTriggersFor_IgnoresNonCollisionTriggers()
    {
        var (character, vehicle, map) = CreatePlayer();
        PlaceDeletableObject(map, GateObjectCoid, new Vector3(0, 0, 0));
        PlaceDeleteReaction(map, DeleteReactionCoid, GateObjectCoid);

        var remTpl = new TriggerTemplate
        {
            COID = (int)RemoteTriggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 50f,
            DoCollision = false,
            DoOnActivate = true,
            ActivationCount = -1,
        };
        remTpl.Reactions.Add(DeleteReactionCoid);
        var rem = new Trigger(remTpl);
        rem.SetCoid(RemoteTriggerCoid, false);
        rem.Position = new Vector3(0, 0, 0);
        rem.Scale = 50f;
        rem.SetMap(map);

        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(vehicle);
        Assert.IsNotNull(map.GetObjectByCoid(GateObjectCoid));
        Assert.AreEqual(0, rem.FireCount);
    }

    [TestMethod]
    public void Activate_CascadesToTriggerReactions()
    {
        var (character, vehicle, map) = CreatePlayer();
        PlaceDeletableObject(map, GateObjectCoid, new Vector3(0, 0, 0));

        // Trigger with delete reaction
        var triggerTpl = new TriggerTemplate
        {
            COID = (int)VolumeTriggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            ActivationCount = -1,
        };
        triggerTpl.Reactions.Add(DeleteReactionCoid);
        var trigger = new Trigger(triggerTpl);
        trigger.SetCoid(VolumeTriggerCoid, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 10f;
        trigger.SetMap(map);

        PlaceDeleteReaction(map, DeleteReactionCoid, GateObjectCoid);

        var activateTpl = new ReactionTemplate
        {
            COID = 98002,
            ReactionType = ReactionType.Activate,
        };
        activateTpl.Objects.Add(VolumeTriggerCoid);
        var activate = new Reaction(activateTpl);
        activate.SetCoid(98002L, false);
        activate.SetMap(map);

        Assert.IsTrue(activate.TriggerIfPossible(vehicle));
        Assert.IsNull(map.GetObjectByCoid(GateObjectCoid));
    }

    [TestMethod]
    public void Activate_SelfTargetingTrigger_DoesNotStackOverflow()
    {
        // Map authors often wire Activate → same trigger (pulse / re-arm). Without a re-entrancy
        // guard this recurses until StackOverflowException.
        var (character, vehicle, map) = CreatePlayer();

        const long pulseTriggerCoid = 96100;
        const long activateReactionCoid = 96101;

        var activateTpl = new ReactionTemplate
        {
            COID = (int)activateReactionCoid,
            ReactionType = ReactionType.Activate,
        };
        activateTpl.Objects.Add(pulseTriggerCoid);
        var activateReaction = new Reaction(activateTpl);
        activateReaction.SetCoid(activateReactionCoid, false);
        activateReaction.SetMap(map);

        var triggerTpl = new TriggerTemplate
        {
            COID = (int)pulseTriggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
            ActivationCount = -1,
        };
        triggerTpl.Reactions.Add(activateReactionCoid);
        var trigger = new Trigger(triggerTpl);
        trigger.SetCoid(pulseTriggerCoid, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 10f;
        trigger.SetMap(map);

        // Must not throw StackOverflowException.
        TriggerManager.Instance.FireTriggerReactions(vehicle, trigger);
        Assert.AreEqual(1, trigger.FireCount, "Self-Activate should fire once then re-entry-guard");
    }

    [TestMethod]
    public void OnMissionStateChanged_NestedGiveMission_DoesNotStackOverflow()
    {
        // GiveMission during re-eval calls OnMissionStateChanged again — must coalesce, not recurse.
        SeedMission(MissionId, ObjectiveId);
        var (character, vehicle, map) = CreatePlayer();
        SeedMissionVars(map);

        const long giveReactionCoid = 96200;
        const long condTriggerCoid = 96201;

        var giveTpl = new ReactionTemplate
        {
            COID = (int)giveReactionCoid,
            ReactionType = ReactionType.GiveMission,
            GenericVar1 = MissionId,
        };
        var giveReaction = new Reaction(giveTpl);
        giveReaction.SetCoid(giveReactionCoid, false);
        giveReaction.SetMap(map);

        var tpl = new TriggerTemplate
        {
            COID = (int)condTriggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 50f,
            DoCollision = true,
            DoConditionals = true,
            AllConditionsNeeded = true,
            ActivationCount = -1,
        };
        tpl.Reactions.Add(giveReactionCoid);
        // Always-true condition (0 == 0).
        tpl.Conditions.Add(new TriggerConditional
        {
            LeftId = VarConstZero,
            RightId = VarConstZero,
            Type = ConditionalType.EqualTo,
        });
        var trigger = new Trigger(tpl);
        trigger.SetCoid(condTriggerCoid, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = 50f;
        trigger.SetMap(map);

        vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.AreEqual(1, character.CurrentQuests.Count(q => q.MissionId == MissionId));
    }

    private static void SeedMission(int missionId, int objectiveId)
    {
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(missionId, MissionObjective.CreateForTests(objectiveId, 0, missionId, 1)));
    }

    private static void SeedMissionVars(SectorMap map)
    {
        map.MapData.Variables[VarActiveMission] = Variable.CreateForTests(
            VarActiveMission, LogicVariableStore.TypeHasActiveMission, MissionId, 0f, "has_active");
        map.MapData.Variables[VarConstOne] = Variable.CreateForTests(
            VarConstOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");
        map.MapData.Variables[VarCompleted] = Variable.CreateForTests(
            VarCompleted, LogicVariableStore.TypeHasCompletedMission, MissionId, 0f, "has_done");
        map.MapData.Variables[VarConstZero] = Variable.CreateForTests(
            VarConstZero, LogicVariableStore.TypeConstant, 0f, 0f, "zero");
    }

    private static void PlaceConditionalTrigger(
        SectorMap map,
        long triggerCoid,
        float scale,
        int leftVar,
        int rightVar,
        long reactionCoid,
        long gateObjectCoid)
    {
        PlaceDeleteReaction(map, reactionCoid, gateObjectCoid);

        var tpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = scale,
            // Large scale → collision volume; small scale → remote condition watcher.
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

    private static void PlaceDeletableObject(SectorMap map, long coid, Vector3 position)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = position;
        obj.SetMap(map);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_mission_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(150, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(151, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}
