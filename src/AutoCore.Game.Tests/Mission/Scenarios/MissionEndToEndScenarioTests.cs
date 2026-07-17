using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Scenarios;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;
using Vector3 = AutoCore.Game.Structures.Vector3;

/// <summary>
/// Full mission paths using production handlers with synthetic fixtures (no retail content ids).
/// </summary>
[TestClass]
public class MissionEndToEndScenarioTests
{
    private const int MissionId = 97401;
    private const int ObjectiveA = 97501;
    private const int ObjectiveB = 97502;
    private const int TargetCbid = 97601;
    private const int NpcCbid = 97602;
    private const long NpcCoid = 97603;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionScenario")]
    public void Scenario_Grant_KillComplete_PersistComplete()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 1);
        var o1 = _fx.CreateSimpleObjective(ObjectiveB, 1, MissionId);
        _fx.SeedMission(MissionId, 0, o0, o1);
        var player = _fx.CreatePlayer();

        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);
        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);

        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);

        // Mid-chain kill auto-advances; final complete still via advance path below.
        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 1);
        var mission = AssetManager.Instance.GetMission(MissionId)!;
        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, player.Character.CurrentQuests[0], mission, o1, source: "Scenario");

        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
        _fx.FlushPersist();
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Complete));
        Assert.IsTrue(_fx.CountPackets<CompleteDynamicObjectivePacket>() >= 1);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionScenario")]
    public void Scenario_PartialKill_FlushReload_Finish()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 2);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        NpcInteractHandler.GrantMission(player.Connection, player.Character, MissionId);

        var prop1 = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop1.SetMurderer(player.Vehicle);
        prop1.OnDeath(DeathType.Silent);
        Assert.AreEqual(1, player.Character.CurrentQuests[0].ObjectiveProgress[0]);

        // Simulate disconnect: capture row, clear memory, reload.
        var progress = MissionPersistence.PackProgress(player.Character.CurrentQuests[0].ObjectiveProgress);
        var seq = player.Character.CurrentQuests[0].ActiveObjectiveSequence;
        player.Character.CurrentQuests.Clear();

        _fx.LoadFromRows(
            player.Character,
            new[]
            {
                new CharacterQuestData
                {
                    CharacterCoid = player.Character.ObjectId.Coid,
                    MissionId = MissionId,
                    ActiveObjectiveSequence = seq,
                    State = 0,
                    ObjectiveProgress = progress,
                },
            },
            Array.Empty<CharacterCompletedMissionData>());

        Assert.AreEqual(1, player.Character.CurrentQuests[0].ObjectiveProgress[0]);

        var prop2 = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop2.SetMurderer(player.Vehicle);
        prop2.OnDeath(DeathType.Silent);

        // Final kill-only: ready for giver turn-in, not auto-completed on last kill.
        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 0);
        Assert.AreEqual(2, player.Character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsTrue(MissionKillProgress.IsKillOnlyObjective(o0));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionScenario")]
    public void Scenario_KillThenAdvance_MultiObjective()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 1);
        var o1 = _fx.CreateSimpleObjective(ObjectiveB, 1, MissionId);
        _fx.SeedMission(MissionId, 0, o0, o1);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);

        MissionInvariantAssertions.AssertActiveMission(player.Character, MissionId, 1);

        var mission = AssetManager.Instance.GetMission(MissionId)!;
        var quest = player.Character.CurrentQuests[0];
        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, quest, mission, o1, source: "Scenario");

        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionScenario")]
    public void Scenario_TwoMissions_SameKill_ProgressIndependently()
    {
        const int missionB = 97402;
        const int objB = 97503;
        var oA = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, numToKill: 2);
        var oB = _fx.CreateKillObjective(objB, 0, missionB, TargetCbid, numToKill: 2);
        _fx.SeedMission(MissionId, 0, oA);
        _fx.SeedMission(missionB, 0, oB);

        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);
        _fx.GiveQuest(player.Character, missionB);

        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);

        // Kill handler processes first matching quest then returns — document actual behavior.
        var progressed = player.Character.CurrentQuests
            .Where(q => q.ObjectiveProgress[0] > 0)
            .Select(q => q.MissionId)
            .ToList();

        Assert.IsTrue(progressed.Count >= 1, "At least one mission must receive kill credit");
        // Characterize: current implementation returns after first matching mission.
        Assert.AreEqual(1, progressed.Count,
            "Current MissionKillProgress credits the first matching active quest only (documented contract)");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionScenario")]
    public void Scenario_CompletedNonRepeatable_RejectsRegrant_AndKillDoesNotReopen()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, 1);
        var mission = Mission.CreateForTests(MissionId, o0);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);
        // Kill-only final stays active until giver turn-in.
        Assert.AreEqual(1, player.Character.CurrentQuests.Count);

        PlaceNpc(player.Map, NpcCoid, NpcCbid, new Vector3(2, 0, 0));
        NpcInteractHandler.HandleMissionDialogResponse(player.Connection, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });
        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);

        var reaction = _fx.PlaceReaction(player.Map, _fx.NextCoid(), ReactionType.GiveMission, MissionId);
        Assert.IsFalse(reaction.TriggerIfPossible(player.Character));

        var prop2 = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop2.SetMurderer(player.Vehicle);
        prop2.OnDeath(DeathType.Silent);

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.AreEqual(1, player.Character.CompletedMissionIds.Count(id => id == MissionId));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionScenario")]
    public void Scenario_DeliverDialogTurnIn_CompletesAndPersists()
    {
        SeedDeliverMission(MissionId, ObjectiveA, NpcCbid);
        var player = _fx.CreatePlayer();
        PlaceNpc(player.Map, NpcCoid, NpcCbid, new Vector3(2, 0, 0));
        player.Vehicle.Position = new Vector3(0, 0, 0);
        _fx.GiveQuest(player.Character, MissionId);

        NpcInteractHandler.HandleMissionDialogResponse(player.Connection, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);
        _fx.FlushPersist();
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Complete));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionScenario")]
    public void Scenario_IneligibleCompleted_DialogAccept_DoesNotRegrant()
    {
        SeedOfferMission(MissionId, ObjectiveA, NpcCbid);
        var player = _fx.CreatePlayer();
        PlaceNpc(player.Map, NpcCoid, NpcCbid, new Vector3(2, 0, 0));
        player.Vehicle.Position = new Vector3(0, 0, 0);
        player.Character.CompletedMissionIds.Add(MissionId);

        NpcInteractHandler.HandleMissionDialogResponse(player.Connection, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    [TestCategory("MissionScenario")]
    public void Scenario_ForceComplete_ThenEvents_NoProgress()
    {
        var o0 = _fx.CreateKillObjective(ObjectiveA, 0, MissionId, TargetCbid, 1);
        _fx.SeedMission(MissionId, 0, o0);
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, MissionId);

        NpcInteractHandler.ForceCompleteMission(player.Connection, player.Character, MissionId);
        MissionInvariantAssertions.AssertCompleted(player.Character, MissionId);

        var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), TargetCbid);
        prop.SetMurderer(player.Vehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
    }

    private void SeedDeliverMission(int missionId, int objectiveId, int npcCbid)
    {
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = npcCbid,
            NPCTargetCompletes = true,
            RequireItemToComplete = false,
            ItemCBID = -1,
        });
        var mission = Mission.CreateForTests(missionId, obj);
        mission.IsRepeatable = 0;
        mission.NPC = npcCbid;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }

    private void SeedOfferMission(int missionId, int objectiveId, int npcCbid)
    {
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, 1);
        var mission = Mission.CreateForTests(missionId, obj);
        mission.IsRepeatable = 0;
        mission.NPC = npcCbid;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }

    private static void PlaceNpc(SectorMap map, long coid, int cbid, Vector3 position)
    {
        var npc = new Creature();
        npc.SetCoid(coid, false);
        npc.SetCbidForTests(cbid);
        npc.Position = position;
        npc.SetMap(map);
    }
}
