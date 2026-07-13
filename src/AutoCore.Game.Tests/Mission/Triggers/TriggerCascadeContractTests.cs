using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Triggers;

using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;
using Vector3 = AutoCore.Game.Structures.Vector3;

/// <summary>
/// Contract tests for cascade depth, re-entrancy, activation counts, and latch isolation.
/// AutoCore triggers are template-driven (not C# subclasses) — fire paths are the contract surface.
/// </summary>
[TestClass]
public class TriggerCascadeContractTests
{
    private const int MissionId = 97201;
    private const int ObjectiveId = 97301;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void FireTriggerReactions_SelfActivate_DoesNotStackOverflow_AndFiresOnce()
    {
        var player = _fx.CreatePlayer();
        var pulseCoid = _fx.NextCoid();
        var activateCoid = _fx.NextCoid();

        PlaceActivateReaction(player.Map, activateCoid, targetTriggerCoid: pulseCoid);
        var trigger = PlaceTrigger(player.Map, pulseCoid, activateCoid, activationCount: -1);

        TriggerManager.Instance.FireTriggerReactions(player.Vehicle, trigger);

        Assert.AreEqual(1, trigger.FireCount);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void FireTriggerReactions_ActivationCountOne_SecondFireNoOp()
    {
        var player = _fx.CreatePlayer();
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);

        var giveCoid = _fx.NextCoid();
        var triggerCoid = _fx.NextCoid();
        _fx.PlaceReaction(player.Map, giveCoid, ReactionType.GiveMission, MissionId);
        var trigger = PlaceTrigger(player.Map, triggerCoid, giveCoid, activationCount: 1);

        TriggerManager.Instance.FireTriggerReactions(player.Vehicle, trigger);
        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
        Assert.AreEqual(1, trigger.FireCount);

        TriggerManager.Instance.FireTriggerReactions(player.Vehicle, trigger);
        Assert.AreEqual(1, trigger.FireCount, "ActivationCount=1 must not fire again");
        Assert.AreEqual(1, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void FireTriggerReactions_ActivationCountZero_NeverFires()
    {
        var player = _fx.CreatePlayer();
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);

        var giveCoid = _fx.NextCoid();
        var triggerCoid = _fx.NextCoid();
        _fx.PlaceReaction(player.Map, giveCoid, ReactionType.GiveMission, MissionId);
        var trigger = PlaceTrigger(player.Map, triggerCoid, giveCoid, activationCount: 0);

        TriggerManager.Instance.FireTriggerReactions(player.Vehicle, trigger);
        Assert.AreEqual(0, trigger.FireCount);
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void FireTriggerReactions_NullActivatorOrTrigger_NoThrow()
    {
        var player = _fx.CreatePlayer();
        var trigger = PlaceTrigger(player.Map, _fx.NextCoid(), _fx.NextCoid(), -1);
        TriggerManager.Instance.FireTriggerReactions(null, trigger);
        TriggerManager.Instance.FireTriggerReactions(player.Vehicle, null);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void CascadeDepth_DeepActivateChain_DoesNotCrash()
    {
        // Build MaxCascadeDepth+2 linked Activate triggers. Depth guard must stop recursion cleanly.
        var player = _fx.CreatePlayer();
        var triggerCoids = new long[TriggerManager.MaxCascadeDepth + 3];
        var reactionCoids = new long[TriggerManager.MaxCascadeDepth + 3];
        for (var i = 0; i < triggerCoids.Length; i++)
        {
            triggerCoids[i] = _fx.NextCoid();
            reactionCoids[i] = _fx.NextCoid();
        }

        for (var i = 0; i < triggerCoids.Length - 1; i++)
        {
            PlaceActivateReaction(player.Map, reactionCoids[i], targetTriggerCoid: triggerCoids[i + 1]);
            PlaceTrigger(player.Map, triggerCoids[i], reactionCoids[i], activationCount: -1);
        }

        // Terminal trigger: no further cascade.
        PlaceTrigger(player.Map, triggerCoids[^1], reactionCoids[^1], activationCount: -1);
        _fx.PlaceReaction(player.Map, reactionCoids[^1], ReactionType.ClientText, 0);

        var root = player.Map.GetObjectByCoid(triggerCoids[0]) as Trigger;
        Assert.IsNotNull(root);
        TriggerManager.Instance.FireTriggerReactions(player.Vehicle, root);

        Assert.IsTrue(root.FireCount >= 1);
        // Depth guard may leave later triggers unfired — must not throw or leave cascade depth stuck.
        TriggerManager.Instance.ClearAllForTests(); // also verifies reset of depth counters
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void VolumeLatch_PlayerIsolation_KeysPerObject()
    {
        // Two vehicles on same map entering the same trigger volume must latch independently.
        var a = _fx.CreatePlayer(characterCoid: 720001, vehicleCoid: 720002);
        var b = _fx.CreatePlayer(characterCoid: 720003, vehicleCoid: 720004);
        b.Character.SetMap(a.Map);
        b.Vehicle.SetMap(a.Map);

        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);

        var giveCoid = _fx.NextCoid();
        var triggerCoid = _fx.NextCoid();
        _fx.PlaceReaction(a.Map, giveCoid, ReactionType.GiveMission, MissionId);
        var trigger = PlaceTrigger(a.Map, triggerCoid, giveCoid, activationCount: -1, scale: 50f, doCollision: true);
        trigger.Position = new Vector3(0, 0, 0);

        a.Vehicle.Position = new Vector3(0, 0, 0);
        b.Vehicle.Position = new Vector3(0, 0, 0);

        TriggerManager.Instance.CheckTriggersFor(a.Vehicle, nowMs: 1000);
        Assert.AreEqual(1, a.Character.CurrentQuests.Count);

        // B has not been granted yet — independent latch.
        TriggerManager.Instance.CheckTriggersFor(b.Vehicle, nowMs: 1000);
        Assert.AreEqual(1, b.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionContract")]
    public void LeaveVolume_ClearsLatch_AllowsRefireWhenActivationAllows()
    {
        var player = _fx.CreatePlayer();
        var o0 = _fx.CreateSimpleObjective(ObjectiveId, 0, MissionId);
        _fx.SeedMission(MissionId, isRepeatable: 1, o0);

        var giveCoid = _fx.NextCoid();
        var triggerCoid = _fx.NextCoid();
        _fx.PlaceReaction(player.Map, giveCoid, ReactionType.GiveMission, MissionId);
        var trigger = PlaceTrigger(player.Map, triggerCoid, giveCoid, activationCount: -1, scale: 10f, doCollision: true);
        trigger.Position = new Vector3(0, 0, 0);

        player.Vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(player.Vehicle, nowMs: 1000);
        Assert.AreEqual(1, player.Character.CurrentQuests.Count);

        // Leave volume.
        player.Vehicle.Position = new Vector3(1000, 0, 1000);
        TriggerManager.Instance.CheckTriggersFor(player.Vehicle, nowMs: 2000);

        // Clear active quest so re-enter can grant again (GiveMission declines if already active).
        player.Character.CurrentQuests.Clear();
        player.Vehicle.Position = new Vector3(0, 0, 0);
        TriggerManager.Instance.CheckTriggersFor(player.Vehicle, nowMs: 3000);
        Assert.AreEqual(1, player.Character.CurrentQuests.Count, "Re-enter after leave should re-fire when ActivationCount allows");
    }

    private static void PlaceActivateReaction(SectorMap map, long reactionCoid, long targetTriggerCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Activate,
        };
        tpl.Objects.Add(targetTriggerCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static Trigger PlaceTrigger(
        SectorMap map,
        long triggerCoid,
        long reactionCoid,
        int activationCount,
        float scale = 10f,
        bool doCollision = false)
    {
        var tpl = new TriggerTemplate
        {
            COID = (int)triggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = scale,
            DoCollision = doCollision,
            ActivationCount = activationCount,
        };
        tpl.Reactions.Add(reactionCoid);
        var trigger = new Trigger(tpl);
        trigger.SetCoid(triggerCoid, false);
        trigger.Position = new Vector3(0, 0, 0);
        trigger.Scale = scale;
        trigger.SetMap(map);
        return trigger;
    }
}
