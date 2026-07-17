using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Structures;

[TestClass]
public class CharacterQuestAndMissionStringTests
{
    [TestInitialize]
    public void SetUp() => AssetManager.Instance.ClearTestMissions();

    [TestCleanup]
    public void TearDown() => AssetManager.Instance.ClearTestMissions();

    [TestMethod]
    public void CharacterQuest_Write_SerializesVerifiedClientSavedStateLayout()
    {
        var objective = MissionObjective.CreateForTests(5541, 1, 554, 2);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(554, objective));
        var quest = new CharacterQuest(554, 1) { State = 0 };
        quest.ObjectiveProgress[1] = 1;
        quest.ObjectiveMax[1] = 2;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        quest.Write(writer);

        Assert.AreEqual(CharacterQuest.StructureSize, ms.Length);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        Assert.AreEqual(554, reader.ReadInt32());
        Assert.AreEqual(0, reader.ReadInt32());
        for (var i = 0; i < 10; ++i)
            Assert.AreEqual(-1, reader.ReadInt32());
        Assert.AreEqual(5541, reader.ReadInt32());
        Assert.AreEqual(0.5f, reader.ReadSingle());
    }

    [TestMethod]
    public void CharacterQuest_PopulateFromMission_GrowsArraysForHighSequence()
    {
        var o0 = MissionObjective.CreateForTests(1, 0, 900, 2);
        var o9 = MissionObjective.CreateForTests(2, 9, 900, 4); // sequence beyond default 8
        var mission = Mission.CreateForTests(900, o0, o9);

        var quest = new CharacterQuest(900, 0);
        quest.PopulateFromMission(mission);

        Assert.IsTrue(quest.ObjectiveProgress.Length >= 10);
        Assert.AreEqual(2, quest.ObjectiveMax[0]);
        Assert.AreEqual(4, quest.ObjectiveMax[9]);
    }

    [TestMethod]
    public void CharacterQuest_PopulateFromMission_NullOrEmpty_NoThrow()
    {
        var quest = new CharacterQuest(1, 0);
        quest.PopulateFromMission(null);
        quest.PopulateFromMission(Mission.CreateForTests(2));
        Assert.AreEqual(CharacterQuest.MaxObjectives, quest.ObjectiveProgress.Length);
    }

    [TestMethod]
    public void CharacterQuest_PopulateFromMission_UseItemRepeatCount_BecomesObjectiveMax()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 9100, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementUseItem(obj) { RepeatCount = 3, FirstStateSlot = 0 });
        var mission = Mission.CreateForTests(9100, obj);

        var quest = new CharacterQuest(9100, 0);
        quest.PopulateFromMission(mission);

        Assert.AreEqual(3, quest.ObjectiveMax[0]);
    }

    [TestMethod]
    public void CharacterQuest_PopulateFromMission_CompleteCount_TakesPrecedenceOverUseItemRepeat()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 9101, completeCount: 5);
        obj.Requirements.Add(new ObjectiveRequirementUseItem(obj) { RepeatCount = 2, FirstStateSlot = 0 });
        var mission = Mission.CreateForTests(9101, obj);

        var quest = new CharacterQuest(9101, 0);
        quest.PopulateFromMission(mission);

        Assert.AreEqual(5, quest.ObjectiveMax[0]);
    }

    [TestMethod]
    public void CharacterQuest_Write_UseItem_UsesAbsoluteSlotProgress()
    {
        var obj = MissionObjective.CreateForTests(77, 0, 9102, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementUseItem(obj)
        {
            RepeatCount = 4,
            FirstStateSlot = 1,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(9102, obj));

        var quest = new CharacterQuest(9102, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 2;

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            quest.Write(writer);

        // Layout: missionId i32, reserved i32, 10×i32 saved state, objectiveId i32, 4×float slots, reserved i32
        ms.Position = 4 + 4 + 40 + 4; // start of float slots
        using var reader = new BinaryReader(ms);
        var slot0 = reader.ReadSingle();
        var slot1 = reader.ReadSingle();
        Assert.AreEqual(0f, slot0, 0.001f);
        Assert.AreEqual(2f, slot1, 0.001f, "UseItem client Eval expects absolute use count, not 0..1 ratio");
    }

    [TestMethod]
    public void CharacterQuest_PopulateFromMission_KillNumToKill_BecomesObjectiveMax_WhenCompleteCountZero()
    {
        // A Grouchy Gun: CompleteCount=0, NumToKill=5
        var obj = MissionObjective.CreateForTests(614, 0, 470, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            NumToKill = 5,
            TargetCBID = 2531,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(470, obj);

        var quest = new CharacterQuest(470, 0);
        quest.PopulateFromMission(mission);

        Assert.AreEqual(5, quest.ObjectiveMax[0]);
    }

    [TestMethod]
    public void CharacterQuest_Write_Kill_UsesAbsoluteSlotProgress()
    {
        var obj = MissionObjective.CreateForTests(614, 0, 470, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            NumToKill = 5,
            TargetCBID = 2531,
            FirstStateSlot = 0,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(470, obj));

        var quest = new CharacterQuest(470, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 3;

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            quest.Write(writer);

        ms.Position = 4 + 4 + 40 + 4;
        using var reader = new BinaryReader(ms);
        var slot0 = reader.ReadSingle();
        Assert.AreEqual(3f, slot0, 0.001f, "Kill client Eval expects absolute kill count, not 0..1 ratio");
        Assert.AreNotEqual(0.6f, slot0, 0.001f);
    }

    [TestMethod]
    public void CharacterQuest_PopulateFromAssets_UsesTestMission()
    {
        var obj = MissionObjective.CreateForTests(11, 0, 12, 5);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(12, obj));

        var quest = new CharacterQuest(12, 0);
        quest.PopulateFromAssets();
        Assert.AreEqual(5, quest.ObjectiveMax[0]);
    }

    [TestMethod]
    public void MissionString_Read_MapVersion17_TypeDefaultsZero()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(42); // string id
            w.Write(7);  // owner
            // no type byte for mapVersion < 18
            var text = System.Text.Encoding.UTF8.GetBytes("hello");
            w.Write(text.Length);
            w.Write(text);
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var s = MissionString.Read(reader, mapVersion: 17);
        Assert.AreEqual(42, s.StringId);
        Assert.AreEqual(7, s.OwnerId);
        Assert.AreEqual(0, s.Type);
        Assert.AreEqual("hello", s.Text);
    }

    [TestMethod]
    public void MissionString_Read_MapVersion18_IncludesType()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1);
            w.Write(2);
            w.Write((byte)5);
            w.Write(0); // empty string
        }

        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        var s = MissionString.Read(reader, mapVersion: 18);
        Assert.AreEqual(5, s.Type);
        Assert.AreEqual("", s.Text);
    }

    [TestMethod]
    public void Mission_CreateForTests_ExposesObjectiveDictionary()
    {
        var o = MissionObjective.CreateForTests(99, 0, 50, 1);
        var m = Mission.CreateForTests(50, o);
        m.NPC = 12447;
        m.Continent = 707;
        m.ReqLevelMin = 1;
        m.ReqLevelMax = 100;
        m.IsRepeatable = 0;
        m.ReqMissionId = new[] { -1, -1, -1, -1 };

        Assert.AreEqual(50, m.Id);
        Assert.AreEqual(12447, m.NPC);
        Assert.AreEqual(1, m.NumberOfObjectives);
        Assert.IsTrue(m.Objectives.ContainsKey(0));
        Assert.AreEqual(99, m.Objectives[0].ObjectiveId);
        Assert.AreEqual("mission_50", m.Name);
    }

    [TestMethod]
    public void Mission_CreateForTests_WithoutObjectives_ProducesUsableEmptyMission()
    {
        var mission = Mission.CreateForTests(77);

        Assert.AreEqual(77, mission.Id);
        Assert.AreEqual(0, mission.NumberOfObjectives);
        Assert.IsNotNull(mission.Objectives);
        Assert.AreEqual(0, mission.Objectives.Count);
        Assert.AreEqual("mission_77", mission.Name);
    }

    [TestMethod]
    public void Mission_OptionalTextMetadata_RoundTripsForJournalAndDialogConsumers()
    {
        var mission = new Mission
        {
            Title = "Title",
            InternalName = "internal",
            Description = "Description",
            OnLineAccept = "Accepted",
            OnLineReject = "Rejected",
            NotCompleteText = "Incomplete",
            CompleteText = "Complete",
            FailText = "Failed",
            CoreMission = true,
        };

        Assert.AreEqual("Title", mission.Title);
        Assert.AreEqual("internal", mission.InternalName);
        Assert.AreEqual("Description", mission.Description);
        Assert.AreEqual("Accepted", mission.OnLineAccept);
        Assert.AreEqual("Rejected", mission.OnLineReject);
        Assert.AreEqual("Incomplete", mission.NotCompleteText);
        Assert.AreEqual("Complete", mission.CompleteText);
        Assert.AreEqual("Failed", mission.FailText);
        Assert.IsTrue(mission.CoreMission);
    }

    [TestMethod]
    public void Mission_Read_DeserializesTemplateFieldsWithoutOptionalXml()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(901);
            WriteFixedUtf16(writer, "mission_901", 65);
            writer.Write((byte)MissionType.Deliver);
            writer.Write((byte)0);
            writer.Write(404); // NPC
            writer.Write(7); // priority
            writer.Write((short)2);
            writer.Write((short)3);
            writer.Write(4);
            writer.Write(55);
            foreach (var id in new[] { 10, 11, 12, 13 }) writer.Write(id);
            writer.Write((short)1);
            writer.Write((short)0);
            foreach (var value in new[] { 1, 2, 3, 4 }) writer.Write(value);
            foreach (var value in new[] { 5, 6, 7, 8 }) writer.Write(value);
            foreach (var value in new[] { 1.5f, 2.5f, 3.5f, 4.5f }) writer.Write(value);
            foreach (var value in new short[] { 1, 0, 1, 0 }) writer.Write(value);
            foreach (var value in new[] { 9, 10, 11, 12 }) writer.Write(value);
            writer.Write((short)1); // auto assign
            writer.Write((short)2); // objective override
            foreach (var value in new[] { 707, 88, 9, 10, 11, 12, 13, 14 }) writer.Write(value);
            writer.Write((short)15); // target level
            writer.Write((short)0);
            foreach (var value in new[] { 16, 17, 18, 19 }) writer.Write(value);
            writer.Write((byte)0); // NumberOfObjectives
            writer.Write(new byte[7]);
            writer.Write(0); // objective records
        }

        stream.Position = 0;
        var mission = Mission.Read(new BinaryReader(stream));

        Assert.AreEqual(901, mission.Id);
        Assert.AreEqual("mission_901", mission.Name);
        Assert.AreEqual((byte)MissionType.Deliver, mission.Type);
        Assert.AreEqual(404, mission.NPC);
        Assert.AreEqual(707, mission.Continent);
        CollectionAssert.AreEqual(new[] { 10, 11, 12, 13 }, mission.ReqMissionId);
        CollectionAssert.AreEqual(new[] { 9, 10, 11, 12 }, mission.ItemQuantity);
        Assert.AreEqual(0, mission.Objectives.Count);
    }

    [TestMethod]
    public void MissionObjective_ReadNew_DeserializesBinaryFieldsWhenXmlIsAbsent()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(700);
            writer.Write(701);
            writer.Write((byte)2);
            writer.Write((byte)0);
            WriteFixedUtf16(writer, "objective name", 65);
            WriteFixedUtf16(writer, "test_map", 65);
            writer.Write((short)0);
            writer.Write(702);
            writer.Write(703);
            writer.Write((byte)4);
            writer.Write(new byte[3]);
            foreach (var value in new[] { 5, 6, 7, 8, 9 }) writer.Write(value);
            writer.Write((short)10);
            writer.Write((short)11);
            writer.Write(1.25f);
            writer.Write(2.5f);
            writer.Write(3.75f);
        }

        stream.Position = 0;
        var owner = Mission.CreateForTests(700);
        var objective = MissionObjective.ReadNew(new BinaryReader(stream), owner, null);

        Assert.AreEqual(700, objective.QuestId);
        Assert.AreEqual(701, objective.ObjectiveId);
        Assert.AreEqual(2, objective.Sequence);
        Assert.AreSame(owner, objective.Owner);
        Assert.AreEqual("objective name", objective.ObjectiveName);
        Assert.AreEqual("test_map", objective.MapName);
        Assert.AreEqual(5, objective.XP);
        Assert.AreEqual(3.75f, objective.CreditScaler);
        Assert.AreEqual(0, objective.Requirements.Count);
    }

    private static void WriteFixedUtf16(BinaryWriter writer, string value, int characters)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(value);
        writer.Write(bytes);
        writer.Write(new byte[characters * 2 - bytes.Length]);
    }
}
