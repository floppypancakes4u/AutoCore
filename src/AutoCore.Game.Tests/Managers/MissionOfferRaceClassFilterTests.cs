using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;

/// <summary>
/// Client CVOGCharacter_CheckMissionRequirements filters offerable missions by
/// sinReqRace / sinReqClass (0xFFFF = unrestricted). Class report missions after
/// Room and Motherboard must only offer the player's class.
/// </summary>
[TestClass]
public class MissionOfferRaceClassFilterTests
{
    private const int ContId = 8960;
    private const int NpcCbid = 11786;
    private const int BodyCbid = 77001;
    private const int PrereqMissionId = 3981;

    [TestInitialize]
    public void SetUp()
    {
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestCleanup]
    public void TearDown()
    {
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestMethod]
    public void MeetsRaceClass_Unrestricted_AlwaysTrueWithoutBody()
    {
        var mission = Mission.CreateForTests(1);
        mission.ReqRace = -1;
        mission.ReqClass = -1;
        var character = new Character();
        character.SetCoid(1, true);

        Assert.IsTrue(NpcInteractHandler.MeetsRaceClassRequirements(character, mission));
    }

    [TestMethod]
    public void MeetsRaceClass_ReqClassWithoutBody_False()
    {
        var mission = Mission.CreateForTests(1);
        mission.ReqClass = 1; // Engineer
        var character = new Character();
        character.SetCoid(1, true);

        Assert.IsFalse(NpcInteractHandler.MeetsRaceClassRequirements(character, mission));
    }

    [TestMethod]
    public void MeetsRaceClass_MatchingClass_True()
    {
        var mission = Mission.CreateForTests(1);
        mission.ReqRace = 0;
        mission.ReqClass = 2; // Lieutenant
        var character = CreateCharacterWithRaceClass(race: 0, classId: 2);

        Assert.IsTrue(NpcInteractHandler.MeetsRaceClassRequirements(character, mission));
    }

    [TestMethod]
    public void MeetsRaceClass_WrongClass_False()
    {
        var mission = Mission.CreateForTests(1);
        mission.ReqClass = 0; // Commando
        var character = CreateCharacterWithRaceClass(race: 0, classId: 3); // Bounty Hunter

        Assert.IsFalse(NpcInteractHandler.MeetsRaceClassRequirements(character, mission));
    }

    [TestMethod]
    public void MeetsRaceClass_WrongRace_False()
    {
        var mission = Mission.CreateForTests(1);
        mission.ReqRace = 0; // Human
        mission.ReqClass = -1;
        var character = CreateCharacterWithRaceClass(race: 1, classId: 0); // Mutant

        Assert.IsFalse(NpcInteractHandler.MeetsRaceClassRequirements(character, mission));
    }

    [TestMethod]
    public void CanOffer_ClassReportQuests_OnlyMatchingClass()
    {
        // Retail Hutchins follow-ups after Room and Motherboard (3981).
        SeedClassReport(2939, reqClass: 0); // Commando
        SeedClassReport(2941, reqClass: 1); // Engineer
        SeedClassReport(2943, reqClass: 2); // Lieutenant
        SeedClassReport(2945, reqClass: 3); // Bounty Hunter
        NpcInteractHandler.InvalidateMissionIndex();

        var (character, map) = CreatePlayerOnMap(classId: 1); // Engineer
        character.CompletedMissionIds.Add(PrereqMissionId);
        character.SetMap(map);

        var offers = NpcInteractHandler.GetOfferableMissionsForTests(character, NpcCbid);
        CollectionAssert.AreEqual(new[] { 2941 }, offers.ToArray(),
            "Only the Engineer class report must be offerable");
    }

    [TestMethod]
    public void CanOffer_ClassZero_IsCommandoNotUnrestricted()
    {
        SeedClassReport(2939, reqClass: 0);
        NpcInteractHandler.InvalidateMissionIndex();

        var (commando, map) = CreatePlayerOnMap(classId: 0);
        commando.CompletedMissionIds.Add(PrereqMissionId);
        commando.SetMap(map);

        var (engineer, _) = CreatePlayerOnMap(classId: 1);
        engineer.CompletedMissionIds.Add(PrereqMissionId);
        engineer.SetMap(map);

        Assert.IsTrue(NpcInteractHandler.CanOfferMissionForTests(commando, 2939, NpcCbid));
        Assert.IsFalse(NpcInteractHandler.CanOfferMissionForTests(engineer, 2939, NpcCbid),
            "ReqClass=0 is Commando, not 'any class'");
    }

    [TestMethod]
    public void CanOffer_RegressionGates_AllClassIds()
    {
        SeedClassReport(2939, reqClass: 0);
        SeedClassReport(2941, reqClass: 1);
        SeedClassReport(2943, reqClass: 2);
        SeedClassReport(2945, reqClass: 3);
        NpcInteractHandler.InvalidateMissionIndex();

        for (byte classId = 0; classId <= 3; classId++)
        {
            var (character, map) = CreatePlayerOnMap(classId);
            character.CompletedMissionIds.Add(PrereqMissionId);
            character.SetMap(map);
            var offers = NpcInteractHandler.GetOfferableMissionsForTests(character, NpcCbid);
            Assert.AreEqual(1, offers.Count, $"class {classId} must see exactly one offer");
            var expected = 2939 + classId * 2; // 2939,2941,2943,2945
            Assert.AreEqual(expected, offers[0], $"class {classId} wrong mission");
        }
    }

    [TestMethod]
    public void CanOffer_MissingMission_False()
    {
        var (character, map) = CreatePlayerOnMap(0);
        character.SetMap(map);
        Assert.IsFalse(NpcInteractHandler.CanOfferMissionForTests(character, 999999, NpcCbid));
    }

    [TestMethod]
    public void CanOffer_AlreadyActive_False()
    {
        SeedClassReport(2939, reqClass: 0);
        NpcInteractHandler.InvalidateMissionIndex();
        var (character, map) = CreatePlayerOnMap(0);
        character.CompletedMissionIds.Add(PrereqMissionId);
        character.CurrentQuests.Add(new CharacterQuest(2939, 0));
        character.SetMap(map);
        Assert.IsFalse(NpcInteractHandler.CanOfferMissionForTests(character, 2939, NpcCbid));
    }

    [TestMethod]
    public void CanOffer_AlreadyCompletedNonRepeatable_False()
    {
        SeedClassReport(2939, reqClass: 0);
        NpcInteractHandler.InvalidateMissionIndex();
        var (character, map) = CreatePlayerOnMap(0);
        character.CompletedMissionIds.Add(PrereqMissionId);
        character.CompletedMissionIds.Add(2939);
        character.SetMap(map);
        Assert.IsFalse(NpcInteractHandler.CanOfferMissionForTests(character, 2939, NpcCbid));
    }

    [TestMethod]
    public void CanOffer_WrongNpcCbid_False()
    {
        SeedClassReport(2939, reqClass: 0);
        NpcInteractHandler.InvalidateMissionIndex();
        var (character, map) = CreatePlayerOnMap(0);
        character.CompletedMissionIds.Add(PrereqMissionId);
        character.SetMap(map);
        Assert.IsFalse(NpcInteractHandler.CanOfferMissionForTests(character, 2939, npcCbid: 999));
    }

    [TestMethod]
    public void CanOffer_LevelTooHigh_False()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 5001, 1);
        var mission = Mission.CreateForTests(5001, obj);
        mission.NPC = NpcCbid;
        mission.Continent = ContId;
        mission.ReqLevelMax = 1; // character Level defaults to 1 — edge: use max 0 means no max
        // ReqLevelMax > 0 and character.Level > max: Level is 1, set max to 0 is "no max" in CanOffer
        // Force fail with ReqLevelMin high.
        mission.ReqLevelMin = 50;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
        NpcInteractHandler.InvalidateMissionIndex();

        var (character, map) = CreatePlayerOnMap(0);
        character.SetMap(map);
        Assert.IsFalse(NpcInteractHandler.CanOfferMissionForTests(character, 5001, NpcCbid));
    }

    [TestMethod]
    public void CanOffer_ReqLevelMaxExceeded_False()
    {
        var obj = MissionObjective.CreateForTests(2, 0, 5002, 1);
        var mission = Mission.CreateForTests(5002, obj);
        mission.NPC = NpcCbid;
        mission.Continent = ContId;
        mission.ReqLevelMax = 5;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
        NpcInteractHandler.InvalidateMissionIndex();

        var (character, map) = CreatePlayerOnMap(0);
        character.AttachTestDataForTests("LevelMaxChar");
        character.SetLevel(10); // > ReqLevelMax 5
        character.SetMap(map);

        Assert.IsFalse(NpcInteractHandler.CanOfferMissionForTests(character, 5002, NpcCbid),
            "Character above ReqLevelMax must not receive offer");
    }

    [TestMethod]
    public void CanOffer_ReqLevelMax_AtCap_True()
    {
        var obj = MissionObjective.CreateForTests(3, 0, 5003, 1);
        var mission = Mission.CreateForTests(5003, obj);
        mission.NPC = NpcCbid;
        mission.Continent = ContId;
        mission.ReqLevelMax = 5;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
        NpcInteractHandler.InvalidateMissionIndex();

        var (character, map) = CreatePlayerOnMap(0);
        character.AttachTestDataForTests("LevelCapChar");
        character.SetLevel(5);
        character.SetMap(map);

        Assert.IsTrue(NpcInteractHandler.CanOfferMissionForTests(character, 5003, NpcCbid));
    }

    [TestMethod]
    public void MeetsRaceClass_NullMission_False()
    {
        var character = CreateCharacterWithRaceClass(0, 0);
        Assert.IsFalse(NpcInteractHandler.MeetsRaceClassRequirements(character, null));
    }

    [TestMethod]
    public void TryGetCharacterRaceClass_NullCharacter_False()
    {
        Assert.IsFalse(NpcInteractHandler.TryGetCharacterRaceClass(null, out _, out _));
    }

    [TestMethod]
    public void TryGetCharacterRaceClass_WithBody_True()
    {
        var character = CreateCharacterWithRaceClass(1, 2);
        Assert.IsTrue(NpcInteractHandler.TryGetCharacterRaceClass(character, out var race, out var classId));
        Assert.AreEqual(1, race);
        Assert.AreEqual(2, classId);
    }

    [TestMethod]
    public void GetOfferableMissions_InvalidNpc_Empty()
    {
        var (character, map) = CreatePlayerOnMap(0);
        character.SetMap(map);
        Assert.AreEqual(0, NpcInteractHandler.GetOfferableMissionsForTests(character, 0).Count);
        Assert.AreEqual(0, NpcInteractHandler.GetOfferableMissionsForTests(character, -1).Count);
    }

    [TestMethod]
    public void CanOfferMission_RequirementsOred_OneOfFourClassReports_Offers()
    {
        // Freelancer/Shields Up pattern: any one class report unlocks the offer.
        const int terraCbid = 11792;
        const int freelancerId = 6101;
        var obj = MissionObjective.CreateForTests(61010, 0, freelancerId, 1);
        var mission = Mission.CreateForTests(freelancerId, obj);
        mission.NPC = terraCbid;
        mission.Continent = ContId;
        mission.ReqRace = 0;
        mission.ReqClass = -1;
        mission.ReqLevelMin = 1;
        mission.ReqLevelMax = 2000;
        mission.ReqMissionId = new[] { 2945, 2939, 2941, 2943 };
        mission.RequirementsOred = -1;
        mission.IsRepeatable = 0;
        AssetManager.Instance.SetTestMission(mission);
        NpcInteractHandler.InvalidateMissionIndex();

        var (character, map) = CreatePlayerOnMap(classId: 3);
        character.SetMap(map);
        character.CompletedMissionIds.Add(2945); // only bounty-hunter report

        Assert.IsTrue(
            NpcInteractHandler.CanOfferMissionForTests(character, freelancerId, terraCbid),
            "OR prereqs: one completed class report must unlock Freelancer-style offers");
    }

    [TestMethod]
    public void CanOfferMission_RequirementsAnd_StillRequiresAll()
    {
        const int missionId = 6102;
        var obj = MissionObjective.CreateForTests(61020, 0, missionId, 1);
        var mission = Mission.CreateForTests(missionId, obj);
        mission.NPC = NpcCbid;
        mission.Continent = ContId;
        mission.ReqRace = 0;
        mission.ReqClass = -1;
        mission.ReqLevelMin = 1;
        mission.ReqLevelMax = 2000;
        mission.ReqMissionId = new[] { 10, 20, -1, -1 };
        mission.RequirementsOred = 0;
        mission.IsRepeatable = 0;
        AssetManager.Instance.SetTestMission(mission);

        var (character, map) = CreatePlayerOnMap(0);
        character.SetMap(map);
        character.CompletedMissionIds.Add(10);
        Assert.IsFalse(NpcInteractHandler.CanOfferMissionForTests(character, missionId, NpcCbid));
        character.CompletedMissionIds.Add(20);
        Assert.IsTrue(NpcInteractHandler.CanOfferMissionForTests(character, missionId, NpcCbid));
    }

    [TestMethod]
    public void CreateForTests_DefaultsRaceClassUnrestricted()
    {
        var m = Mission.CreateForTests(42);
        Assert.AreEqual(-1, m.ReqRace);
        Assert.AreEqual(-1, m.ReqClass);
    }

    static void SeedClassReport(int missionId, short reqClass)
    {
        var obj = MissionObjective.CreateForTests(missionId * 10, 0, missionId, 1);
        var mission = Mission.CreateForTests(missionId, obj);
        mission.NPC = NpcCbid;
        mission.Continent = ContId;
        mission.ReqRace = 0;
        mission.ReqClass = reqClass;
        mission.ReqLevelMin = 1;
        mission.ReqLevelMax = 2000;
        mission.ReqMissionId = new[] { PrereqMissionId, -1, -1, -1 };
        mission.IsRepeatable = 0;
        AssetManager.Instance.SetTestMission(mission);
    }

    static Character CreateCharacterWithRaceClass(byte race, byte classId)
    {
        AssetManagerTestHelper.RegisterCharacterCloneBase(BodyCbid + race * 10 + classId, race, classId);
        var character = new Character();
        character.SetCoid(1000 + race * 10 + classId, true);
        character.LoadCloneBase(BodyCbid + race * 10 + classId);
        return character;
    }

    static (Character Character, SectorMap Map) CreatePlayerOnMap(byte classId)
    {
        var cbid = BodyCbid + 100 + classId;
        AssetManagerTestHelper.RegisterCharacterCloneBase(cbid, race: 0, classId: classId);
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId,
            MapFileName = "tm_class_offer",
            DisplayName = "t",
            IsPersistent = true,
        }, new Vector4());
        var character = new Character();
        character.SetCoid(2000 + classId, true);
        character.LoadCloneBase(cbid);
        // Level defaults to 1 without DBData (meets ReqLevelMin=1).
        return (character, map);
    }
}
