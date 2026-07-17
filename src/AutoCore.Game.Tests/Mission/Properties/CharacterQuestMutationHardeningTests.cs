using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Properties;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;

/// <summary>
/// Tight observable asserts that kill high-value CharacterQuest / pack survivors.
/// </summary>
[TestClass]
public class CharacterQuestMutationHardeningTests
{
    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_MissingMission_WritesObjectiveIdMinusOne_AndZeroSlots()
    {
        var quest = new CharacterQuest(missionId: 424242, activeObjectiveSequence: 0);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.Unicode, leaveOpen: true))
            quest.Write(w);

        Assert.AreEqual(CharacterQuest.StructureSize, ms.Length);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        Assert.AreEqual(424242, r.ReadInt32()); // mission id
        Assert.AreEqual(0, r.ReadInt32()); // reserved
        for (var i = 0; i < 10; i++)
            Assert.AreEqual(-1, r.ReadInt32());
        Assert.AreEqual(-1, r.ReadInt32()); // no objective
        for (var i = 0; i < 4; i++)
            Assert.AreEqual(0f, r.ReadSingle());
        Assert.AreEqual(0, r.ReadInt32()); // trailing reserved
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_KillProgress_UsesAbsoluteCountInAuthoredSlot()
    {
        // Client Kill_Eval / UI treat slot floats as absolute kill counts (0,1,2…), not 0..1 ratios.
        const int missionId = 99001;
        const int objectiveId = 99002;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 4);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { TargetCBID = 1, NumToKill = 4, FirstStateSlot = 1 });
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.Unicode, leaveOpen: true))
            quest.Write(w);

        ms.Position = 0;
        using var r = new BinaryReader(ms);
        r.BaseStream.Position = 4 + 4 + (10 * 4); // skip to objective id
        Assert.AreEqual(objectiveId, r.ReadInt32());
        var slots = new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
        Assert.AreEqual(0f, slots[0], 0.001f);
        Assert.AreEqual(2f, slots[1], 0.001f);
        Assert.AreEqual(0f, slots[2], 0.001f);
        Assert.AreEqual(0f, slots[3], 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_ProgressAboveMax_ClampsNormalizedToOne()
    {
        const int missionId = 99011;
        const int objectiveId = 99012;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 2);
        _fx.SeedMission(missionId, 0, obj);
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 99;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.Unicode, leaveOpen: true))
            quest.Write(w);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        r.BaseStream.Position = 4 + 4 + (10 * 4) + 4;
        Assert.AreEqual(1f, r.ReadSingle(), 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PopulateFromMission_UsesCompleteCount_AndGrowsBeyondDefaultEight()
    {
        const int missionId = 99021;
        var o0 = MissionObjective.CreateForTests(1, 0, missionId, completeCount: 3);
        var o9 = MissionObjective.CreateForTests(2, 9, missionId, completeCount: 7);
        var mission = Mission.CreateForTests(missionId, o0, o9);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromMission(mission);

        Assert.IsTrue(quest.ObjectiveMax.Length >= 10);
        Assert.AreEqual(3, quest.ObjectiveMax[0]);
        Assert.AreEqual(7, quest.ObjectiveMax[9]);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PopulateFromMission_ZeroCompleteCount_DefaultsMaxToOne()
    {
        const int missionId = 99031;
        var o0 = MissionObjective.CreateForTests(1, 0, missionId, completeCount: 0);
        var mission = Mission.CreateForTests(missionId, o0);
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromMission(mission);
        Assert.AreEqual(1, quest.ObjectiveMax[0]);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Constructor_InitializesEightSlotsToZeroProgressMaxOne()
    {
        var quest = new CharacterQuest(5, 2);
        Assert.AreEqual(CharacterQuest.MaxObjectives, quest.ObjectiveProgress.Length);
        Assert.AreEqual(2, quest.ActiveObjectiveSequence);
        for (var i = 0; i < CharacterQuest.MaxObjectives; i++)
        {
            Assert.AreEqual(0, quest.ObjectiveProgress[i]);
            Assert.AreEqual(1, quest.ObjectiveMax[i]);
        }
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void ResetPersistenceForTests_DisablesAutoFlush_AndFlushDrainsQueue()
    {
        // Reset forces AutoFlushOnEnqueue=false so unit tests stay deterministic (no ThreadPool race).
        MissionPersistence.Instance.ResetPersistenceForTests();
        Assert.IsFalse(MissionPersistence.Instance.AutoFlushOnEnqueue);

        var writes = 0;
        MissionPersistence.Instance.PersistQuestRow = (_, _, _) => writes++;
        var character = new AutoCore.Game.Entities.Character();
        character.SetCoid(55, true);
        MissionPersistence.Instance.OnQuestChanged(character, new CharacterQuest(12, 0));
        Assert.AreEqual(1, MissionPersistence.Instance.PendingPersistCount);
        Assert.AreEqual(1, MissionPersistence.Instance.FlushPending());
        Assert.AreEqual(1, writes);
        MissionPersistence.Instance.ResetPersistenceForTests();
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void DeleteActiveForCharacter_InvokesActiveDeleteHook()
    {
        MissionPersistence.Instance.ResetPersistenceForTests();
        long? seen = null;
        MissionPersistence.Instance.DeleteActiveRows = c => seen = c;
        MissionPersistence.Instance.OnQuestChanged(
            MakeChar(66), new CharacterQuest(1, 0));
        MissionPersistence.Instance.DeleteActiveForCharacter(66);
        Assert.AreEqual(66L, seen);
        MissionPersistence.Instance.ResetPersistenceForTests();
    }

    private static AutoCore.Game.Entities.Character MakeChar(long coid)
    {
        var c = new AutoCore.Game.Entities.Character();
        c.SetCoid(coid, true);
        return c;
    }
}
