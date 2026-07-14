using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Heavy regression: ObjectiveStateBuilder + CharacterQuest.Write slot encoding
/// (ratio vs absolute multi-pad).
/// </summary>
[TestClass]
public class MissionObjectiveStateHeavyRegressionTests
{
    // --- Build ratio path (5+) ---

    [TestMethod]
    public void Build_HalfProgress_HalfRatio()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 4);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { NumToKill = 4, FirstStateSlot = 0 });
        var p = ObjectiveStateBuilder.Build(obj, 2, 4);
        Assert.AreEqual(0.5f, p.SlotProgress[0], 0.001f);
        Assert.AreEqual(1u, p.ObjectiveBitmask);
    }

    [TestMethod]
    public void Build_FullProgress_One()
    {
        var obj = MissionObjective.CreateForTests(2, 0, 1, 1);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { FirstStateSlot = 1 });
        var p = ObjectiveStateBuilder.Build(obj, 1, 1);
        Assert.AreEqual(1f, p.SlotProgress[1], 0.001f);
    }

    [TestMethod]
    public void Build_ZeroProgress_StillSetsBit()
    {
        var obj = MissionObjective.CreateForTests(3, 0, 1, 1);
        obj.Requirements.Add(new ObjectiveRequirementPatrol(obj) { FirstStateSlot = 0 });
        var p = ObjectiveStateBuilder.Build(obj, 0, 1);
        Assert.AreEqual(1u, p.ObjectiveBitmask);
        Assert.AreEqual(0f, p.SlotProgress[0]);
    }

    [TestMethod]
    public void Build_NullObjective_Null()
        => Assert.IsNull(ObjectiveStateBuilder.Build(null, 0, 1));

    [TestMethod]
    public void Build_ClampsOverMax()
    {
        var obj = MissionObjective.CreateForTests(4, 0, 1, 1);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { FirstStateSlot = 0 });
        var p = ObjectiveStateBuilder.Build(obj, 9, 3);
        Assert.AreEqual(1f, p.SlotProgress[0], 0.001f);
    }

    [TestMethod]
    public void Build_FromQuest_UsesProgressArrays()
    {
        var obj = MissionObjective.CreateForTests(5, 0, 1, 4);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { NumToKill = 4, FirstStateSlot = 0 });
        var quest = new CharacterQuest(1, 0);
        quest.ObjectiveProgress[0] = 1;
        quest.ObjectiveMax[0] = 4;
        var p = ObjectiveStateBuilder.Build(obj, quest);
        Assert.AreEqual(0.25f, p.SlotProgress[0], 0.001f);
    }

    // --- BuildPatrolPadCount absolute (5+) ---

    [TestMethod]
    public void PatrolPad_AbsoluteOne_NotRatio()
    {
        var obj = MissionObjective.CreateForTests(10, 0, 1, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            TargetCount = 7,
            FirstStateSlot = 0,
        };
        obj.Requirements.Add(patrol);
        var p = ObjectiveStateBuilder.BuildPatrolPadCount(obj, patrol, 1);
        Assert.AreEqual(1f, p.SlotProgress[0], 0.001f);
        Assert.AreNotEqual(1f / 7f, p.SlotProgress[0], 0.01f);
    }

    [TestMethod]
    public void PatrolPad_Zero_AbsoluteZero()
    {
        var obj = MissionObjective.CreateForTests(11, 0, 1, 1);
        var patrol = new ObjectiveRequirementPatrol(obj) { FirstStateSlot = 0 };
        obj.Requirements.Add(patrol);
        var p = ObjectiveStateBuilder.BuildPatrolPadCount(obj, patrol, 0);
        Assert.AreEqual(0f, p.SlotProgress[0]);
    }

    [TestMethod]
    public void PatrolPad_SixOfSeven()
    {
        var obj = MissionObjective.CreateForTests(12, 0, 1, 1);
        var patrol = new ObjectiveRequirementPatrol(obj) { TargetCount = 7, FirstStateSlot = 0 };
        obj.Requirements.Add(patrol);
        var p = ObjectiveStateBuilder.BuildPatrolPadCount(obj, patrol, 6);
        Assert.AreEqual(6f, p.SlotProgress[0], 0.001f);
    }

    [TestMethod]
    public void PatrolPad_NullArgs_Null()
    {
        var obj = MissionObjective.CreateForTests(13, 0, 1, 1);
        var patrol = new ObjectiveRequirementPatrol(obj);
        Assert.IsNull(ObjectiveStateBuilder.BuildPatrolPadCount(null, patrol, 1));
        Assert.IsNull(ObjectiveStateBuilder.BuildPatrolPadCount(obj, null, 1));
    }

    [TestMethod]
    public void PatrolPad_BitmaskUsesRequirementIndex()
    {
        var obj = MissionObjective.CreateForTests(14, 0, 1, 1);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { FirstStateSlot = 0 });
        var patrol = new ObjectiveRequirementPatrol(obj) { FirstStateSlot = 1 };
        obj.Requirements.Add(patrol);
        var p = ObjectiveStateBuilder.BuildPatrolPadCount(obj, patrol, 2);
        Assert.AreEqual(1u << 1, p.ObjectiveBitmask);
        Assert.AreEqual(2f, p.SlotProgress[1], 0.001f);
    }

    [TestMethod]
    public void PatrolPad_ObjectiveIdMatches()
    {
        var obj = MissionObjective.CreateForTests(5448, 0, 2945, 1);
        var patrol = new ObjectiveRequirementPatrol(obj) { FirstStateSlot = 0 };
        obj.Requirements.Add(patrol);
        var p = ObjectiveStateBuilder.BuildPatrolPadCount(obj, patrol, 3);
        Assert.AreEqual(5448, p.ObjectiveId);
    }

    // --- CharacterQuest.Write multi-pad absolute (5+) ---

    [TestMethod]
    public void QuestWrite_SinglePadKill_UsesRatio()
    {
        var obj = MissionObjective.CreateForTests(20, 0, 200, 4);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { NumToKill = 4, FirstStateSlot = 0 });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(200, obj));
        var quest = new CharacterQuest(200, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;
        quest.ObjectiveMax[0] = 4;
        var slots = ReadQuestSlots(quest);
        Assert.AreEqual(0.5f, slots[0], 0.001f);
        AssetManager.Instance.ClearTestMissions();
    }

    [TestMethod]
    public void QuestWrite_MultiPad_UsesAbsolute()
    {
        var obj = MissionObjective.CreateForTests(21, 0, 201, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            TargetCount = 7,
            FirstStateSlot = 0,
        };
        for (var i = 0; i < 7; i++)
            patrol.GenericTargets[i] = 6500 + i;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(201, obj));
        var quest = new CharacterQuest(201, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 3;
        quest.ObjectiveMax[0] = 7;
        var slots = ReadQuestSlots(quest);
        Assert.AreEqual(3f, slots[0], 0.001f);
        Assert.AreNotEqual(3f / 7f, slots[0], 0.05f);
        AssetManager.Instance.ClearTestMissions();
    }

    [TestMethod]
    public void QuestWrite_MultiPadZero_AbsoluteZero()
    {
        var obj = MissionObjective.CreateForTests(22, 0, 202, 1);
        var patrol = new ObjectiveRequirementPatrol(obj) { TargetCount = 3, FirstStateSlot = 0 };
        patrol.GenericTargets[0] = 1;
        patrol.GenericTargets[1] = 2;
        patrol.GenericTargets[2] = 3;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(202, obj));
        var quest = new CharacterQuest(202, 0);
        quest.PopulateFromAssets();
        var slots = ReadQuestSlots(quest);
        Assert.AreEqual(0f, slots[0]);
        AssetManager.Instance.ClearTestMissions();
    }

    [TestMethod]
    public void QuestWrite_MultiPadOne_AbsoluteOne()
    {
        var obj = MissionObjective.CreateForTests(23, 0, 203, 1);
        var patrol = new ObjectiveRequirementPatrol(obj) { TargetCount = 2, FirstStateSlot = 0 };
        patrol.GenericTargets[0] = 10;
        patrol.GenericTargets[1] = 20;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(203, obj));
        var quest = new CharacterQuest(203, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1;
        quest.ObjectiveMax[0] = 2;
        var slots = ReadQuestSlots(quest);
        Assert.AreEqual(1f, slots[0], 0.001f);
        AssetManager.Instance.ClearTestMissions();
    }

    [TestMethod]
    public void QuestWrite_SinglePadPatrol_UsesRatio()
    {
        var obj = MissionObjective.CreateForTests(24, 0, 204, 1);
        var patrol = new ObjectiveRequirementPatrol(obj) { TargetCount = 1, FirstStateSlot = 0 };
        patrol.GenericTargets[0] = 99;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(204, obj));
        var quest = new CharacterQuest(204, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1;
        quest.ObjectiveMax[0] = 1;
        var slots = ReadQuestSlots(quest);
        Assert.AreEqual(1f, slots[0], 0.001f);
        AssetManager.Instance.ClearTestMissions();
    }

    [TestMethod]
    public void QuestWrite_StructureSize_72Bytes()
    {
        Assert.AreEqual(72, CharacterQuest.StructureSize);
        var quest = new CharacterQuest(1, 0);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        quest.Write(w);
        Assert.AreEqual(72, ms.Length);
    }

    private static float[] ReadQuestSlots(CharacterQuest quest)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        quest.Write(w);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        r.ReadInt32(); // mission id
        r.ReadInt32(); // reserved
        for (var i = 0; i < 10; i++)
            r.ReadInt32(); // saved state
        r.ReadInt32(); // objective id
        var slots = new float[4];
        for (var i = 0; i < 4; i++)
            slots[i] = r.ReadSingle();
        return slots;
    }
}
