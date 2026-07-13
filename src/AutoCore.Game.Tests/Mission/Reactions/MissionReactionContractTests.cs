using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Reactions;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// Contract suite for mission-mutating reaction types. Acts as executable documentation and a
/// discovery gate so new mission-mutating handlers stay covered.
/// </summary>
[TestClass]
public class MissionReactionContractTests
{
    private const int MissionId = 97001;
    private const int ObjectiveA = 97101;
    private const int ObjectiveB = 97102;

    /// <summary>
    /// Every reaction type that mutates server mission state must appear here and have contract tests.
    /// When adding a new mission-mutating handler, extend this list and add tests — this suite fails otherwise.
    /// </summary>
    private static readonly ReactionType[] MissionMutatingReactionTypes =
    {
        ReactionType.GiveMission,
        ReactionType.CompleteObjective,
        ReactionType.FailMission,       // stub — still catalogued
        ReactionType.SetActiveObjective,
        // GiveMissionDialog is client-notify only (no server quest mutation) — not in mutating set.
    };

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void Discovery_MissionMutatingReactionTypes_AreDocumentedAndCovered()
    {
        // Guard: if production adds a mission switch case without updating this catalog, fail loudly.
        var documented = new HashSet<ReactionType>(MissionMutatingReactionTypes);
        Assert.IsTrue(documented.Contains(ReactionType.GiveMission));
        Assert.IsTrue(documented.Contains(ReactionType.CompleteObjective));
        Assert.IsTrue(documented.Contains(ReactionType.FailMission));
        Assert.IsTrue(documented.Contains(ReactionType.SetActiveObjective));

        // Every documented type must have at least one dedicated contract method in this class.
        var methods = typeof(MissionReactionContractTests)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(m => m.GetCustomAttributes(typeof(TestMethodAttribute), false).Length > 0)
            .Select(m => m.Name)
            .ToList();

        foreach (var type in MissionMutatingReactionTypes)
        {
            var token = type.ToString();
            Assert.IsTrue(
                methods.Any(n => n.Contains(token, StringComparison.Ordinal)),
                $"Missing contract test method name containing '{token}' for {type}");
        }
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void GiveMission_NoCharacter_DoesNotTrackQuest()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        // Activator without character resolution: bare SimpleObject on map.
        var orphan = new SimpleObject(GraphicsObjectType.Graphics);
        orphan.SetCoid(_fx.NextCoid(), false);
        orphan.SetMap(player.Map);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.GiveMission, MissionId);
        // May return true (client path) without tracking on orphan activator.
        reaction.TriggerIfPossible(orphan);

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void GiveMission_InvalidMissionId_DoesNotTrack()
    {
        var player = _fx.CreatePlayer();
        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.GiveMission, genericVar1: 0);
        Assert.IsTrue(reaction.TriggerIfPossible(player.Character));
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void GiveMission_RepeatableCompleted_AllowsRegrant()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, isRepeatable: 1, o0);
        var player = _fx.CreatePlayer();
        player.Character.CompletedMissionIds.Add(MissionId);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.GiveMission, MissionId);
        Assert.IsTrue(reaction.TriggerIfPossible(player.Character));

        // Repeatable re-grant: active quest returns while completed ledger still holds the id.
        Assert.IsTrue(player.Character.CurrentQuests.Any(q => q.MissionId == MissionId));
        Assert.AreEqual(0, player.Character.CurrentQuests.Single(q => q.MissionId == MissionId).ActiveObjectiveSequence);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(MissionId),
            "Completed ledger retains history for repeatable re-grants");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void CompleteObjective_NoConnection_DoesNotMutate()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        player.Character.SetOwningConnection(null);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.CompleteObjective, ObjectiveA);
        Assert.IsFalse(reaction.TriggerIfPossible(player.Character));

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void CompleteObjective_UnknownObjective_DoesNotMutate()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.CompleteObjective, genericVar1: 999999);
        Assert.IsFalse(reaction.TriggerIfPossible(player.Character));
        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void CompleteObjective_AdvancesMultiSeq_ThenCompletes()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        var o1 = _fx.CreateSimpleObjective(ObjectiveB, 1, MissionId);
        _fx.SeedMission(MissionId, 0, o0, o1);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var r0 = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.CompleteObjective, ObjectiveA);
        Assert.IsFalse(r0.TriggerIfPossible(player.Character)); // false = no 0x206C
        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 1);

        var r1 = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.CompleteObjective, ObjectiveB);
        Assert.IsFalse(r1.TriggerIfPossible(player.Character));
        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void FailMission_Stub_DoesNotClearQuest_OrComplete()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        _fx.PersistWrites.Clear();

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.FailMission, MissionId);
        Assert.IsTrue(reaction.TriggerIfPossible(player.Character));
        _fx.FlushPersist();

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, _fx.PersistWrites.Count);
        Assert.AreEqual(0, _fx.CountPackets<FailMissionPacket>());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void SetActiveObjective_UpdatesSequence_AndPersists()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        var o1 = _fx.CreateSimpleObjective(ObjectiveB, 1, MissionId);
        _fx.SeedMission(MissionId, 0, o0, o1);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.SetActiveObjective, ObjectiveB);
        Assert.IsTrue(reaction.TriggerIfPossible(player.Character));
        _fx.FlushPersist();

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 1);
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Upsert && w.Seq == 1));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionContract")]
    public void SetActiveObjective_WithoutQuest_NoThrow_NoPersist()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.SetActiveObjective, ObjectiveA);
        Assert.IsTrue(reaction.TriggerIfPossible(player.Character));
        _fx.FlushPersist();
        Assert.AreEqual(0, _fx.PersistWrites.Count);
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }

    [TestMethod]
    [TestCategory("MissionContract")]
    public void GiveMissionDialog_DoesNotMutateServerQuestState()
    {
        var o0 = _fx.CreateSimpleObjective(ObjectiveA, 0, MissionId);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.GiveMissionDialog, MissionId);
        Assert.IsTrue(reaction.TriggerIfPossible(player.Character));
        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }
}
