using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Game.Chat;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// /completemissiontree — seed transitive ReqMissionId completions without rewards.
/// </summary>
[TestClass]
public class CompleteMissionTreeCommandTests
{
    private const int RootId = 77001;
    private const int PrereqA = 77002;
    private const int PrereqB = 77003;
    private const int PrereqC = 77004;

    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    public void CompleteMissionTree_IsMutatingCommand()
    {
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/completemissiontree"));
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/completeMissionTree"));
    }

    [TestMethod]
    public void CompleteMissionTree_GmLevel0_Denied()
    {
        SeedChain();
        var player = _fx.CreatePlayer();
        player.Character.GMLevel = 0;

        var result = ChatCommandService.Instance.Execute(player.Character, $"/completemissiontree {RootId}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Permission denied");
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(PrereqA));
    }

    [TestMethod]
    public void CompleteMissionTree_UnknownMission_NoOp()
    {
        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, "/completemissiontree 99999999");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Unknown");
    }

    [TestMethod]
    public void CompleteMissionTree_NoPrereqs_ReportsNone()
    {
        var o0 = _fx.CreateSimpleObjective(77101, 0, RootId);
        _fx.SeedMission(RootId, 0, o0);
        var player = _fx.CreatePlayer();

        var result = ChatCommandService.Instance.Execute(player.Character, $"/completemissiontree {RootId}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "no prerequisite");
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(RootId));
    }

    [TestMethod]
    public void CompleteMissionTree_DeepChain_SeedsAllExceptRoot()
    {
        SeedChain();
        var player = _fx.CreatePlayer();

        var result = ChatCommandService.Instance.Execute(player.Character, $"/completemissiontree {RootId}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Seeded");
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqA));
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqB));
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqC));
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(RootId),
            "target mission must not be marked completed");
        _fx.FlushPersist();
        Assert.IsTrue(_fx.PersistWrites.Any(w => w.Kind == QuestPersistKind.Complete && w.MissionId == PrereqA));
    }

    [TestMethod]
    public void CompleteMissionTree_OredList_SeedsAllBranches()
    {
        SeedBare(PrereqA);
        SeedBare(PrereqB);
        var rootObj = _fx.CreateSimpleObjective(77111, 0, RootId);
        var root = Mission.CreateForTests(RootId, rootObj);
        root.ReqMissionId = new[] { PrereqA, PrereqB, -1, -1 };
        root.RequirementsOred = -1;
        AssetManager.Instance.SetTestMission(root);

        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, $"/completemissiontree {RootId}");

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqA));
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqB));
    }

    [TestMethod]
    public void CompleteMissionTree_Cycle_DoesNotInfiniteLoop()
    {
        // A requires B; B requires A.
        SeedWithPrereqs(PrereqA, PrereqB);
        SeedWithPrereqs(PrereqB, PrereqA);
        var rootObj = _fx.CreateSimpleObjective(77121, 0, RootId);
        var root = Mission.CreateForTests(RootId, rootObj);
        root.ReqMissionId = new[] { PrereqA, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(root);

        var player = _fx.CreatePlayer();
        var result = ChatCommandService.Instance.Execute(player.Character, $"/completemissiontree {RootId}");

        Assert.IsTrue(result.Handled);
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqA));
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqB));
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(RootId));
    }

    [TestMethod]
    public void CompleteMissionTree_AlreadyCompleted_Idempotent()
    {
        SeedChain();
        var player = _fx.CreatePlayer();
        player.Character.CompletedMissionIds.Add(PrereqA);
        player.Character.CompletedMissionIds.Add(PrereqB);
        player.Character.CompletedMissionIds.Add(PrereqC);

        var result = ChatCommandService.Instance.Execute(player.Character, $"/completemissiontree {RootId}");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Seeded 0");
    }

    [TestMethod]
    public void CompleteMissionTree_ActivePrereq_RemovedWithoutRewardsPath()
    {
        SeedChain();
        var player = _fx.CreatePlayer();
        _fx.GiveQuest(player.Character, PrereqA);
        Assert.AreEqual(1, player.Character.CurrentQuests.Count);

        var result = ChatCommandService.Instance.Execute(player.Character, $"/completemissiontree {RootId}");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, player.Character.CurrentQuests.Count(q => q.MissionId == PrereqA));
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqA));
    }

    [TestMethod]
    public void CollectPrerequisiteMissionIds_ExcludesRoot()
    {
        SeedChain();
        var ids = ChatCommandService.CollectPrerequisiteMissionIds(RootId);
        CollectionAssert.Contains(ids, PrereqA);
        CollectionAssert.Contains(ids, PrereqB);
        CollectionAssert.Contains(ids, PrereqC);
        CollectionAssert.DoesNotContain(ids, RootId);
    }

    [TestMethod]
    public void SeedCompleted_MarksIdsWithoutCompletingUnrelated()
    {
        SeedBare(PrereqA);
        var player = _fx.CreatePlayer();

        var result = ChatCommandService.Instance.Execute(player.Character, $"/seedcompleted {PrereqA} {PrereqB}");

        Assert.IsTrue(result.Handled);
        // PrereqB unknown asset still seeds id into completed set (seed is id-based).
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqA));
        Assert.IsTrue(player.Character.CompletedMissionIds.Contains(PrereqB));
    }

    void SeedBare(int missionId)
    {
        var o0 = _fx.CreateSimpleObjective(missionId + 1000, 0, missionId);
        _fx.SeedMission(missionId, 0, o0);
    }

    void SeedWithPrereqs(int missionId, params int[] prereqs)
    {
        var o0 = _fx.CreateSimpleObjective(missionId + 1000, 0, missionId);
        var mission = Mission.CreateForTests(missionId, o0);
        var slots = new[] { -1, -1, -1, -1 };
        for (var i = 0; i < prereqs.Length && i < 4; i++)
            slots[i] = prereqs[i];
        mission.ReqMissionId = slots;
        AssetManager.Instance.SetTestMission(mission);
    }

    void SeedChain()
    {
        // Root <- A <- B <- C
        SeedBare(PrereqC);
        SeedWithPrereqs(PrereqB, PrereqC);
        SeedWithPrereqs(PrereqA, PrereqB);
        SeedWithPrereqs(RootId, PrereqA);
    }
}
