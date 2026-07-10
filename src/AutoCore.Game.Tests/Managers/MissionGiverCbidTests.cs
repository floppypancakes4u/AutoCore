using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;

/// <summary>
/// Stage 6: <see cref="NpcInteractHandler.IsMissionGiverCbid"/> — data-driven from mission
/// givers and deliver turn-in targets, no hardcoded ids.
/// </summary>
[TestClass]
public class MissionGiverCbidTests
{
    private const int MissionId = 96400;
    private const int GiverCbid = 96401;
    private const int DeliverCbid = 96402;
    private const int UnrelatedCbid = 96403;

    [TestInitialize]
    public void SetUp()
    {
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestCleanup]
    public void TearDown()
    {
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestMethod]
    public void IsMissionGiverCbid_GiverNpc_IsTrue()
    {
        SeedMission();
        Assert.IsTrue(NpcInteractHandler.IsMissionGiverCbid(GiverCbid),
            "A mission's giver NPC must be recognised as a mission giver.");
    }

    [TestMethod]
    public void IsMissionGiverCbid_DeliverTarget_IsTrue()
    {
        SeedMission();
        Assert.IsTrue(NpcInteractHandler.IsMissionGiverCbid(DeliverCbid),
            "A deliver-objective turn-in NPC must be recognised as a mission giver.");
    }

    [TestMethod]
    public void IsMissionGiverCbid_UnrelatedNpc_IsFalse()
    {
        SeedMission();
        Assert.IsFalse(NpcInteractHandler.IsMissionGiverCbid(UnrelatedCbid),
            "An NPC with no mission involvement must not be a mission giver.");
    }

    [TestMethod]
    public void IsMissionGiverCbid_NonPositive_IsFalse()
    {
        SeedMission();
        Assert.IsFalse(NpcInteractHandler.IsMissionGiverCbid(0));
        Assert.IsFalse(NpcInteractHandler.IsMissionGiverCbid(-1));
    }

    private static void SeedMission()
    {
        var objective = MissionObjective.CreateForTests(97400, 0, MissionId, 1);
        objective.Requirements.Add(new ObjectiveRequirementDeliver(objective)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });

        var mission = Mission.CreateForTests(MissionId, objective);
        mission.NPC = GiverCbid;
        AssetManager.Instance.SetTestMission(mission);
    }
}
