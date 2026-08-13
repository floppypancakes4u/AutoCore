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

    // ---- Boundary / equality mutants on Write + PopulateFromMission ----

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_ActiveSequenceEqualToArrayLength_DoesNotIndexAndUsesZeroProgress()
    {
        // Kills: ActiveObjectiveSequence < Length → <= / always-true (would IndexOutOfRange).
        // Objective exists at sequence 1, but progress/max arrays are deliberately length 1
        // so index 1 is out of range for both arrays.
        const int missionId = 99101;
        const int objectiveId = 99102;
        var obj = MissionObjective.CreateForTests(objectiveId, sequence: 1, missionId, completeCount: 4);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { TargetCBID = 1, NumToKill = 4, FirstStateSlot = 0 });
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, activeObjectiveSequence: 1)
        {
            ObjectiveProgress = new[] { 99 },
            ObjectiveMax = new[] { 4 },
        };

        var slots = ReadFloatSlots(quest);
        Assert.AreEqual(0f, slots[0], 0.001f, "OOB sequence must not index progress/max arrays");
        Assert.AreEqual(0f, slots[1], 0.001f);
        Assert.AreEqual(0f, slots[2], 0.001f);
        Assert.AreEqual(0f, slots[3], 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_FirstStateSlotEqualToFour_DoesNotWriteBeyondSlotArray()
    {
        // Kills: slot < slots.Length → <= (would IndexOutOfRange writing slots[4]).
        const int missionId = 99111;
        const int objectiveId = 99112;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 4);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = 1,
            NumToKill = 4,
            FirstStateSlot = 4, // slots.Length == 4; valid bound is exclusive
        });
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.Unicode, leaveOpen: true))
            quest.Write(w);

        Assert.AreEqual(CharacterQuest.StructureSize, ms.Length);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        r.BaseStream.Position = 4 + 4 + (10 * 4) + 4;
        for (var i = 0; i < 4; i++)
            Assert.AreEqual(0f, r.ReadSingle(), 0.001f, "out-of-range FirstStateSlot must leave all slots zero");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_UseItem_SlotEqualToFour_DoesNotWriteBeyondSlotArray()
    {
        const int missionId = 99113;
        const int objectiveId = 99114;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementUseItem(obj)
        {
            RepeatCount = 3,
            FirstStateSlot = 4,
        });
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.Unicode, leaveOpen: true))
            quest.Write(w);

        ms.Position = 0;
        using var r = new BinaryReader(ms);
        r.BaseStream.Position = 4 + 4 + (10 * 4) + 4;
        for (var i = 0; i < 4; i++)
            Assert.AreEqual(0f, r.ReadSingle(), 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_MultiPadPatrol_UsesAbsoluteProgressInSlot()
    {
        // Kills: CountListedTargets > 1 → >= 1 (single pad would incorrectly take multi path),
        // and multi-pad absolute slot write path.
        const int missionId = 99121;
        const int objectiveId = 99122;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            FirstStateSlot = 2,
            Laps = 1,
            TargetCount = 2,
        };
        patrol.GenericTargets[0] = 1001;
        patrol.GenericTargets[1] = 1002;
        obj.Requirements.Add(patrol);
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1;

        var slots = ReadFloatSlots(quest);
        Assert.AreEqual(0f, slots[0], 0.001f);
        Assert.AreEqual(0f, slots[1], 0.001f);
        Assert.AreEqual(1f, slots[2], 0.001f, "multi-pad patrol must write absolute pad count");
        Assert.AreEqual(0f, slots[3], 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_SinglePadSingleLap_DoesNotUseMultiPadAbsolutePath()
    {
        // Single listed target + Laps=1 is NOT multi-pad; normalized path applies.
        const int missionId = 99123;
        const int objectiveId = 99124;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 2);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            FirstStateSlot = 0,
            Laps = 1,
            TargetCount = 1,
        };
        patrol.GenericTargets[0] = 1001;
        obj.Requirements.Add(patrol);
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1; // half of CompleteCount=2 → normalized 0.5

        var slots = ReadFloatSlots(quest);
        Assert.AreEqual(0.5f, slots[0], 0.001f, "single-pad single-lap must stay on normalized path");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_MultiLapSinglePad_UsesAbsoluteProgress()
    {
        // Kills: Laps > 1 → >=1 / <1 / OR rewrites of (Laps>1 && targets>0).
        const int missionId = 99125;
        const int objectiveId = 99126;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            FirstStateSlot = 1,
            Laps = 2,
            TargetCount = 1,
        };
        patrol.GenericTargets[0] = 1001;
        obj.Requirements.Add(patrol);
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        var slots = ReadFloatSlots(quest);
        Assert.AreEqual(0f, slots[0], 0.001f);
        Assert.AreEqual(2f, slots[1], 0.001f, "multi-lap patrol must write absolute progress");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_Collect_UsesAbsoluteCountInAuthoredSlot()
    {
        const int missionId = 99131;
        const int objectiveId = 99132;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementCollect(obj)
        {
            NumToCollect = 5,
            ItemCBID = 77,
            FirstStateSlot = 3,
        });
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 4;

        var slots = ReadFloatSlots(quest);
        Assert.AreEqual(0f, slots[0], 0.001f);
        Assert.AreEqual(0f, slots[1], 0.001f);
        Assert.AreEqual(0f, slots[2], 0.001f);
        Assert.AreEqual(4f, slots[3], 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_NormalizedPath_IgnoresAuthoredSlotsAtOrAboveFour()
    {
        // Kills: slot < slots.Length → <= / > on normalized authored-slot filter.
        // Single-pad patrol is not multi-pad absolute; falls through to normalized path.
        const int missionId = 99141;
        const int objectiveId = 99142;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 4);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            FirstStateSlot = 4, // filtered out of authoredSlots (slots.Length == 4)
            Laps = 1,
            TargetCount = 1,
        };
        patrol.GenericTargets[0] = 1;
        obj.Requirements.Add(patrol);
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        var slots = ReadFloatSlots(quest);
        Assert.AreEqual(0.5f, slots[0], 0.001f, "when all authored slots are OOB, default to slot 0");
        Assert.AreEqual(0f, slots[1], 0.001f);
        Assert.AreEqual(0f, slots[2], 0.001f);
        Assert.AreEqual(0f, slots[3], 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PopulateFromMission_LengthEqualCapacity_DoesNotWipeExistingProgress()
    {
        // Kills: Length < capacity → <= (would reallocate same size and zero progress).
        const int missionId = 99151;
        var o0 = MissionObjective.CreateForTests(1, 0, missionId, completeCount: 3);
        var o7 = MissionObjective.CreateForTests(2, 7, missionId, completeCount: 5);
        var mission = Mission.CreateForTests(missionId, o0, o7);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromMission(mission);
        Assert.AreEqual(CharacterQuest.MaxObjectives, quest.ObjectiveProgress.Length);

        quest.ObjectiveProgress[0] = 2;
        quest.ObjectiveProgress[7] = 4;
        quest.PopulateFromMission(mission); // capacity still == Length

        Assert.AreEqual(2, quest.ObjectiveProgress[0], "re-populate must not wipe progress when size is already sufficient");
        Assert.AreEqual(4, quest.ObjectiveProgress[7]);
        Assert.AreEqual(3, quest.ObjectiveMax[0]);
        Assert.AreEqual(5, quest.ObjectiveMax[7]);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PopulateFromMission_ResetsAllMaxSlotsToOneBeforeApplyingTemplate()
    {
        // Kills: for (i < Length) → i > Length (loop never runs; residual zeros after grow).
        const int missionId = 99161;
        var o0 = MissionObjective.CreateForTests(1, 0, missionId, completeCount: 2);
        var o9 = MissionObjective.CreateForTests(2, 9, missionId, completeCount: 4);
        var mission = Mission.CreateForTests(missionId, o0, o9);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromMission(mission);

        Assert.IsTrue(quest.ObjectiveMax.Length >= 10);
        Assert.AreEqual(2, quest.ObjectiveMax[0]);
        Assert.AreEqual(1, quest.ObjectiveMax[1], "unauthored sequences default to max 1");
        Assert.AreEqual(1, quest.ObjectiveMax[5]);
        Assert.AreEqual(4, quest.ObjectiveMax[9]);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void ResolveObjectiveMax_KillAggregateAndCollect_DriveDerivedMax()
    {
        const int missionId = 99171;
        var killAggObj = MissionObjective.CreateForTests(1, 0, missionId, completeCount: 0);
        killAggObj.Requirements.Add(new ObjectiveRequirementKillAggregate(killAggObj) { NumToKill = 6 });
        Assert.AreEqual(6, CharacterQuest.ResolveObjectiveMax(killAggObj));

        var collectObj = MissionObjective.CreateForTests(2, 1, missionId, completeCount: 0);
        collectObj.Requirements.Add(new ObjectiveRequirementCollect(collectObj) { NumToCollect = 9 });
        Assert.AreEqual(9, CharacterQuest.ResolveObjectiveMax(collectObj));

        Assert.AreEqual(1, CharacterQuest.ResolveObjectiveMax(null));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void ResolveObjectiveMax_MultipleRequirements_TakesHighestPositive()
    {
        // Distinguishes > derived from equivalent rewrites when several counts compete.
        const int missionId = 99181;
        var obj = MissionObjective.CreateForTests(1, 0, missionId, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementUseItem(obj) { RepeatCount = 2 });
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { NumToKill = 5 });
        obj.Requirements.Add(new ObjectiveRequirementCollect(obj) { NumToCollect = 3 });
        Assert.AreEqual(5, CharacterQuest.ResolveObjectiveMax(obj));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void ResolveObjectiveMax_MultiPadPatrol_UsesNeededCount()
    {
        var obj = MissionObjective.CreateForTests(1237, 0, 874, completeCount: 0);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            TargetCount = 5,
            Laps = 1,
            Sequential = true,
        };
        for (var i = 0; i < 5; i++)
            patrol.GenericTargets[i] = 74751 + i;
        obj.Requirements.Add(patrol);

        Assert.AreEqual(5, CharacterQuest.ResolveObjectiveMax(obj));

        var quest = new CharacterQuest(874, 0);
        quest.PopulateFromMission(Mission.CreateForTests(874, obj));
        Assert.AreEqual(5, quest.ObjectiveMax[0]);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_NegativeProgress_ClampedToZeroOnAbsolutePaths()
    {
        const int missionId = 99191;
        const int objectiveId = 99192;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = 1,
            NumToKill = 4,
            FirstStateSlot = 0,
        });
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = -3;

        var slots = ReadFloatSlots(quest);
        Assert.AreEqual(0f, slots[0], 0.001f, "Math.Max(0, progress) must clamp negatives");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_MultiLapWithZeroTargets_DoesNotTakeMultiPadAbsolutePath()
    {
        // Kills: CountListedTargets > 0 → >= 0 (zero listed targets must not become multi-pad).
        const int missionId = 99201;
        const int objectiveId = 99202;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 4);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            FirstStateSlot = 1,
            Laps = 3,
            TargetCount = 0,
        };
        obj.Requirements.Add(patrol);
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        var slots = ReadFloatSlots(quest);
        // Normalized 2/4 = 0.5 into authored slot 1 (not absolute 2 into multi-pad).
        Assert.AreEqual(0f, slots[0], 0.001f);
        Assert.AreEqual(0.5f, slots[1], 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_MultiPad_SlotEqualToFour_DoesNotWriteBeyondArray()
    {
        const int missionId = 99211;
        const int objectiveId = 99212;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            FirstStateSlot = 4,
            Laps = 1,
            TargetCount = 2,
        };
        patrol.GenericTargets[0] = 1;
        patrol.GenericTargets[1] = 2;
        obj.Requirements.Add(patrol);
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1;

        var slots = ReadFloatSlots(quest);
        for (var i = 0; i < 4; i++)
            Assert.AreEqual(0f, slots[i], 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_Collect_SlotEqualToFour_DoesNotWriteBeyondArray()
    {
        const int missionId = 99221;
        const int objectiveId = 99222;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementCollect(obj)
        {
            NumToCollect = 3,
            FirstStateSlot = 4,
        });
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        var slots = ReadFloatSlots(quest);
        for (var i = 0; i < 4; i++)
            Assert.AreEqual(0f, slots[i], 0.001f);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Write_Normalized_AuthoredSlotOne_WritesOnlyThatSlot()
    {
        // Kills: slot < Length → slot > Length (would drop valid slot 1 and fall back to 0).
        const int missionId = 99231;
        const int objectiveId = 99232;
        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 4);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            FirstStateSlot = 1,
            Laps = 1,
            TargetCount = 1,
        };
        patrol.GenericTargets[0] = 1;
        obj.Requirements.Add(patrol);
        _fx.SeedMission(missionId, 0, obj);

        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        var slots = ReadFloatSlots(quest);
        Assert.AreEqual(0f, slots[0], 0.001f);
        Assert.AreEqual(0.5f, slots[1], 0.001f);
        Assert.AreEqual(0f, slots[2], 0.001f);
        Assert.AreEqual(0f, slots[3], 0.001f);
    }

    private static float[] ReadFloatSlots(CharacterQuest quest)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.Unicode, leaveOpen: true))
            quest.Write(w);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        r.BaseStream.Position = 4 + 4 + (10 * 4) + 4;
        return new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
    }

    private static AutoCore.Game.Entities.Character MakeChar(long coid)
    {
        var c = new AutoCore.Game.Entities.Character();
        c.SetCoid(coid, true);
        return c;
    }
}
