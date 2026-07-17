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
/// Soft-pedal after dialog turn-in: suppress GroupReactionCall (0x206C) briefly while server
/// reactions still run (client interact FX / MSXML crash window @ 0x007B6DB0).
/// </summary>
[TestClass]
public class MissionClientSoftPedalTests
{
    private const int ContId = 707;
    private const int ReactionCoid = 991001;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        MissionClientSoftPedal.ResetForTests();
        SectorMap.SendGroupReactionCall = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        MissionClientSoftPedal.ResetForTests();
        SectorMap.SendGroupReactionCall = true;
        _sent.Clear();
    }

    [TestMethod]
    public void ArmAfterDialogTurnIn_SuppressesThenExpires()
    {
        const long coid = 18325;
        MissionClientSoftPedal.GroupReactionSuppressMs = 5_000;
        MissionClientSoftPedal.ArmAfterDialogTurnIn(coid);

        Assert.IsTrue(MissionClientSoftPedal.ShouldSuppressGroupReactionCall(coid));
        Assert.IsTrue(MissionClientSoftPedal.HasPendingSuppressForTests(coid));

        MissionClientSoftPedal.DebugExpireForTests(coid);
        Assert.IsFalse(MissionClientSoftPedal.ShouldSuppressGroupReactionCall(coid),
            "Expired suppress must clear");
        Assert.IsFalse(MissionClientSoftPedal.HasPendingSuppressForTests(coid));
    }

    [TestMethod]
    public void TriggerReactions_WhileSuppressed_QueuesGroupReactionCall_DoesNotSendYet()
    {
        var (character, vehicle, map) = CreatePlayerWithBoostReaction();
        // Prevent auto-flush during this assertion window.
        MissionClientSoftPedal.GroupReactionSuppressMs = 10_000;
        MissionClientSoftPedal.ScheduleDelayedWork = (_, _) => { /* no auto flush */ };
        MissionClientSoftPedal.ArmAfterDialogTurnIn(character.ObjectId.Coid);
        _sent.Clear();

        map.TriggerReactions(vehicle, new List<long> { ReactionCoid });

        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count(),
            "0x206C must not send during soft-pedal");
        Assert.IsTrue(MissionClientSoftPedal.PendingBatchCountForTests(character.ObjectId.Coid) > 0,
            "Suppressed 0x206C must be queued for later flush (gate Create/Delete)");
    }

    [TestMethod]
    public void TriggerReactions_WhileSuppressed_FlushAfterExpire_SendsQueuedGroupReactionCall()
    {
        var (character, vehicle, map) = CreatePlayerWithBoostReaction();
        MissionClientSoftPedal.GroupReactionSuppressMs = 10_000;
        MissionClientSoftPedal.ScheduleDelayedWork = (_, _) => { };
        MissionClientSoftPedal.ArmAfterDialogTurnIn(character.ObjectId.Coid);
        _sent.Clear();

        map.TriggerReactions(vehicle, new List<long> { ReactionCoid });
        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count());

        MissionClientSoftPedal.DebugExpireForTests(character.ObjectId.Coid);
        MissionClientSoftPedal.FlushPendingGroupReactions(character.ObjectId.Coid);

        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Any(),
            "Queued 0x206C must flush after soft-pedal so client gets gate Create/Delete");
        Assert.AreEqual(0, MissionClientSoftPedal.PendingBatchCountForTests(character.ObjectId.Coid));
    }

    [TestMethod]
    public void TriggerReactions_AfterSuppressExpires_SendsGroupReactionCall()
    {
        var (character, vehicle, map) = CreatePlayerWithBoostReaction();
        MissionClientSoftPedal.GroupReactionSuppressMs = 10_000;
        MissionClientSoftPedal.ArmAfterDialogTurnIn(character.ObjectId.Coid);
        MissionClientSoftPedal.DebugExpireForTests(character.ObjectId.Coid);
        _sent.Clear();

        map.TriggerReactions(vehicle, new List<long> { ReactionCoid });

        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Any(),
            "After soft-pedal expires, GroupReactionCall must send again");
    }

    /// <summary>
    /// Biomek Dunlap-class: type-9 complete during soft-pedal must still deliver gate Delete
    /// 0x206C after the window (not drop ActivationCount one-shot forever).
    /// </summary>
    [TestMethod]
    public void MissionGateOpen_DuringSoftPedal_FlushesCreateDeleteToClient()
    {
        const int missionId = 91060;
        const int objectiveId = 92060;
        const int varCompleted = 201;
        const int varOne = 202;
        const long triggerCoid = 96050;
        const long deleteRxCoid = 96051;
        const long gateCoid = 96052;

        AssetManager.Instance.ClearTestMissions();
        AssetManager.Instance.SetTestMission(
            Mission.CreateForTests(missionId, MissionObjective.CreateForTests(objectiveId, 0, missionId, 1)));
        TriggerManager.Instance.ClearAllForTests();

        var (character, vehicle, map) = CreatePlayerWithBoostReaction();
        // Replace boost with gate setup
        map.MapData.Variables[varCompleted] = Variable.CreateForTests(
            varCompleted, LogicVariableStore.TypeHasCompletedMission, missionId, 0f, "done");
        map.MapData.Variables[varOne] = Variable.CreateForTests(
            varOne, LogicVariableStore.TypeConstant, 1f, 1f, "one");

        var delTpl = new ReactionTemplate
        {
            COID = (int)deleteRxCoid,
            ReactionType = ReactionType.Delete,
        };
        delTpl.Objects.Add(gateCoid);
        var delRx = new Reaction(delTpl);
        delRx.SetCoid(deleteRxCoid, false);
        delRx.SetMap(map);

        var gate = new SimpleObject(GraphicsObjectType.Graphics);
        gate.SetCoid(gateCoid, false);
        gate.Position = new Vector3(0, 0, 0);
        gate.SetMap(map);

        var trigTpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 25f,
            DoCollision = true,
            DoConditionals = true,
            AllConditionsNeeded = true,
            ActivationCount = 1,
        };
        trigTpl.Reactions.Add(deleteRxCoid);
        trigTpl.Conditions.Add(new TriggerConditional
        {
            LeftId = varCompleted,
            RightId = varOne,
            Type = ConditionalType.EqualTo,
        });
        var trigger = new Trigger(trigTpl) { Position = new Vector3(0, 0, 0), Scale = 25f };
        trigger.SetCoid(triggerCoid, false);
        trigger.SetMap(map);

        character.CompletedMissionIds.Add(missionId);
        character.EnsureLogicVariables();

        MissionClientSoftPedal.GroupReactionSuppressMs = 10_000;
        MissionClientSoftPedal.ScheduleDelayedWork = (_, _) => { };
        MissionClientSoftPedal.ArmAfterDialogTurnIn(character.ObjectId.Coid);
        _sent.Clear();

        // Outside volume — mission re-eval path.
        vehicle.Position = new Vector3(500, 0, 500);
        TriggerManager.Instance.OnMissionStateChanged(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(gateCoid), "Server Delete must run under soft-pedal");
        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count(), "0x206C held during soft-pedal");
        Assert.IsTrue(MissionClientSoftPedal.PendingBatchCountForTests(character.ObjectId.Coid) > 0);

        MissionClientSoftPedal.DebugExpireForTests(character.ObjectId.Coid);
        MissionClientSoftPedal.FlushPendingGroupReactions(character.ObjectId.Coid);

        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Any(),
            "Client must receive gate Delete after soft-pedal flush");
        Assert.AreEqual(1, trigger.FireCount, "One-shot ActivationCount must not need a second fire");

        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    private (Character character, Vehicle vehicle, SectorMap map) CreatePlayerWithBoostReaction()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_softpedal_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18325, true);
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(18326, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);

        var tpl = new ReactionTemplate
        {
            COID = ReactionCoid,
            Name = "softpedal_boost",
            ReactionType = ReactionType.Boost,
            ActOnActivator = true,
            GenericVar1 = 10,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);

        return (character, vehicle, map);
    }
}
