using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Structures;

[TestClass]
public class CharacterQuestAndMissionStringTests
{
    [TestInitialize]
    public void SetUp() => AssetManager.Instance.ClearTestMissions();

    [TestCleanup]
    public void TearDown() => AssetManager.Instance.ClearTestMissions();

    [TestMethod]
    public void CharacterQuest_Write_SerializesProgressSlots()
    {
        var quest = new CharacterQuest(554, 1) { State = 0 };
        quest.ObjectiveProgress[0] = 2;
        quest.ObjectiveMax[0] = 3;
        quest.ObjectiveProgress[1] = 1;
        quest.ObjectiveMax[1] = 1;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        quest.Write(writer);

        Assert.AreEqual(CharacterQuest.StructureSize, ms.Length);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        Assert.AreEqual(554, reader.ReadInt32());
        Assert.AreEqual(1, reader.ReadByte());
        Assert.AreEqual(0, reader.ReadByte());
        reader.ReadInt16(); // pad
        Assert.AreEqual(2, reader.ReadInt32());
        Assert.AreEqual(3, reader.ReadInt32());
        Assert.AreEqual(1, reader.ReadInt32());
        Assert.AreEqual(1, reader.ReadInt32());
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
}
