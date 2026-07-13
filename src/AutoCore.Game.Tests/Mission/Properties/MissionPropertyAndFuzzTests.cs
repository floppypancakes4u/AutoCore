using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.Properties;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;
using System.Xml.Linq;

/// <summary>
/// Seeded property loops and malformed-input safety (no external property framework).
/// </summary>
[TestClass]
public class MissionPropertyAndFuzzTests
{
    private MissionTestFixture _fx = null!;

    [TestInitialize]
    public void SetUp() => _fx = new MissionTestFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PackUnpack_RoundTrip_PropertyAcrossLengths()
    {
        var rng = new Random(0x4D15510);
        for (var i = 0; i < 50; i++)
        {
            var len = rng.Next(0, 16);
            var progress = new int[len];
            for (var j = 0; j < len; j++)
                progress[j] = rng.Next(int.MinValue / 4, int.MaxValue / 4);

            var packed = MissionPersistence.PackProgress(progress);
            var restored = MissionPersistence.UnpackProgress(packed);
            CollectionAssert.AreEqual(progress, restored, $"seed iteration {i}");
        }
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PackUnpack_TruncatedAndOddBytes_Safe()
    {
        Assert.AreEqual(0, MissionPersistence.UnpackProgress(Array.Empty<byte>()).Length);
        Assert.AreEqual(0, MissionPersistence.UnpackProgress(new byte[] { 1 }).Length);
        Assert.AreEqual(0, MissionPersistence.UnpackProgress(new byte[] { 1, 2, 3 }).Length);
        // 5 bytes → 1 int (first 4), trailing byte ignored.
        var five = new byte[] { 1, 0, 0, 0, 0xFF };
        var restored = MissionPersistence.UnpackProgress(five);
        Assert.AreEqual(1, restored.Length);
        Assert.AreEqual(1, restored[0]);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void KillProgress_NeverExceedsNumToKill_AcrossSeededRuns()
    {
        const int missionId = 98001;
        const int objectiveId = 98002;
        const int cbid = 98003;
        var rng = new Random(42);

        for (var trial = 0; trial < 20; trial++)
        {
            var numToKill = rng.Next(1, 6);
            AssetManager.Instance.ClearTestMissions();
            var o0 = _fx.CreateKillObjective(objectiveId, 0, missionId, cbid, numToKill);
            _fx.SeedMission(missionId, 0, o0);
            var player = _fx.CreatePlayer(characterCoid: 800_000 + trial, vehicleCoid: 810_000 + trial);
            _fx.GiveQuest(player.Character, missionId);

            var kills = rng.Next(0, numToKill + 3);
            for (var k = 0; k < kills; k++)
            {
                var prop = _fx.PlaceKillTarget(player.Map, _fx.NextCoid(), cbid);
                prop.SetMurderer(player.Vehicle);
                prop.OnDeath(DeathType.Silent);
            }

            if (player.Character.CurrentQuests.Count > 0)
            {
                var q = player.Character.CurrentQuests[0];
                Assert.IsTrue(q.ObjectiveProgress[0] <= Math.Max(numToKill, q.ObjectiveMax[0]));
                Assert.IsTrue(q.ObjectiveProgress[0] >= 0);
            }
            else
            {
                Assert.IsTrue(player.Character.CompletedMissionIds.Contains(missionId));
            }
        }
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void RequirementXml_Malformed_DoesNotCrashCreate()
    {
        var owner = MissionObjective.CreateForTests(1, 0, 1, 1);
        var cases = new[]
        {
            "<kill></kill>",
            "<kill><NumToKill>not-a-number</NumToKill></kill>",
            "<kill><TargetCBID></TargetCBID></kill>",
            "<deliver><TargetNPCCBID>-1</TargetNPCCBID></deliver>",
            "<unknown_type><x/></unknown_type>",
        };

        foreach (var xml in cases)
        {
            try
            {
                var elem = XElement.Parse(xml);
                // Unknown type may return null; known types may throw on bad casts — both must not hang.
                _ = ObjectiveRequirement.Create(owner, elem);
            }
            catch (Exception ex)
            {
                // Explicit failure is acceptable; crash types that escape are not.
                Assert.IsFalse(ex is OutOfMemoryException or StackOverflowException);
            }
        }
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void UseObject_MalformedPackets_DoNotCrash()
    {
        var player = _fx.CreatePlayer();
        NpcInteractHandler.HandleUseObject(null, null);
        NpcInteractHandler.HandleUseObject(player.Connection, null);
        NpcInteractHandler.HandleUseObject(player.Connection, new UseObjectPacket());
        NpcInteractHandler.HandleUseObject(player.Connection, new UseObjectPacket
        {
            Target = new TFID(long.MinValue, true),
            ObjectiveId = int.MinValue,
        });
        NpcInteractHandler.HandleMissionDialogResponse(null, null);
        NpcInteractHandler.HandleMissionDialogResponse(player.Connection, new MissionDialogResponsePacket
        {
            MissionId = -1,
            Accepted = true,
            MissionGiver = new TFID(0, false),
        });
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void CharacterQuest_ProgressClamp_NormalizedSlots()
    {
        const int missionId = 98101;
        var o0 = MissionObjective.CreateForTests(98102, 0, missionId, 10);
        _fx.SeedMission(missionId, 0, o0);
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1000; // over max
        quest.ObjectiveMax[0] = 10;

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.Unicode, leaveOpen: true))
            quest.Write(writer);

        Assert.AreEqual(CharacterQuest.StructureSize, ms.Length);
        // Write clamps normalized progress to [0,1] for slots — must not throw.
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void InvalidTransition_AdvanceWithoutQuest_NoOp()
    {
        const int missionId = 98201;
        var o0 = _fx.CreateSimpleObjective(98202, 0, missionId);
        _fx.SeedMission(missionId, 0, o0);
        var player = _fx.CreatePlayer();
        var detached = new CharacterQuest(missionId, 0);
        detached.PopulateFromAssets();
        var mission = AssetManager.Instance.GetMission(missionId)!;

        NpcInteractHandler.AdvanceOrCompleteObjective(
            player.Connection, player.Character, detached, mission, o0, source: "Fuzz");

        Assert.AreEqual(0, player.Character.CurrentQuests.Count);
        Assert.IsFalse(player.Character.CompletedMissionIds.Contains(missionId));
    }
}
