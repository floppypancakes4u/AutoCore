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
/// Pins HAD-E4Rs (Twin Lakes HARP, map 426) offer eligibility to the retail WAD gates:
/// continent 426, Human-only, min level 2/5, Bug Bites requires Just a Little Crush.
/// </summary>
[TestClass]
public class MissionOfferAfterMapTransferTests
{
    private const int HarpContinentId = 426;
    private const int UpsideContinentId = 558;
    private const int Hade4RsCbid = 3061;
    private const int BodyCbid = 78001;
    private const int JustALittleCrush = 3801;
    private const int BugBites = 3804;
    private const int SettleTheSpore = 3670;
    private const int FuelBomb = 750;

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
    public void Hade4Rs_HumanOn426_OffersOpeners()
    {
        SeedHade4RsMissions();
        var (character, map) = CreatePlayerOnMap(HarpContinentId, race: 0, classId: 0, level: 6);
        character.SetMap(map);

        var offers = NpcInteractHandler.GetOfferableMissionsForTests(character, Hade4RsCbid);

        CollectionAssert.AreEquivalent(new[] { JustALittleCrush, SettleTheSpore, FuelBomb }, offers);
    }

    [TestMethod]
    public void Hade4Rs_MutantOn426_OffersNothing()
    {
        SeedHade4RsMissions();
        var (character, map) = CreatePlayerOnMap(HarpContinentId, race: 1, classId: 0, level: 6);
        character.SetMap(map);

        CollectionAssert.AreEqual(
            Array.Empty<int>(),
            NpcInteractHandler.GetOfferableMissionsForTests(character, Hade4RsCbid));
    }

    [TestMethod]
    public void Hade4Rs_HumanOnWrongContinent_OffersNothing()
    {
        SeedHade4RsMissions();
        var (character, map) = CreatePlayerOnMap(UpsideContinentId, race: 0, classId: 0, level: 6);
        character.SetMap(map);

        CollectionAssert.AreEqual(
            Array.Empty<int>(),
            NpcInteractHandler.GetOfferableMissionsForTests(character, Hade4RsCbid));
    }

    [TestMethod]
    public void Hade4Rs_After3801_OffersBugBitesNotOpener()
    {
        SeedHade4RsMissions();
        var (character, map) = CreatePlayerOnMap(HarpContinentId, race: 0, classId: 0, level: 6);
        character.SetMap(map);
        character.CompletedMissionIds.Add(JustALittleCrush);

        var offers = NpcInteractHandler.GetOfferableMissionsForTests(character, Hade4RsCbid);

        CollectionAssert.Contains(offers, BugBites);
        CollectionAssert.DoesNotContain(offers, JustALittleCrush);
    }

    static void SeedHade4RsMissions()
    {
        SeedMission(JustALittleCrush, reqLevelMin: 2, reqMissions: new[] { -1, -1, -1, -1 });
        SeedMission(BugBites, reqLevelMin: 2, reqMissions: new[] { JustALittleCrush, -1, -1, -1 });
        SeedMission(SettleTheSpore, reqLevelMin: 2, reqMissions: new[] { -1, -1, -1, -1 });
        SeedMission(FuelBomb, reqLevelMin: 5, reqMissions: new[] { -1, -1, -1, -1 });
        NpcInteractHandler.InvalidateMissionIndex();
    }

    static void SeedMission(int missionId, int reqLevelMin, int[] reqMissions)
    {
        var obj = MissionObjective.CreateForTests(missionId * 10, 0, missionId);
        var mission = Mission.CreateForTests(missionId, obj);
        mission.NPC = Hade4RsCbid;
        mission.Continent = HarpContinentId;
        mission.ReqRace = 0;
        mission.ReqClass = -1;
        mission.ReqLevelMin = reqLevelMin;
        mission.ReqLevelMax = 2000;
        mission.ReqMissionId = reqMissions;
        mission.IsRepeatable = 0;
        AssetManager.Instance.SetTestMission(mission);
    }

    static (Character Character, SectorMap Map) CreatePlayerOnMap(
        int continentId,
        byte race,
        byte classId,
        byte level)
    {
        var cbid = BodyCbid + race * 10 + classId;
        AssetManagerTestHelper.RegisterCharacterCloneBase(cbid, race, classId);
        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = continentId,
            MapFileName = "tm_hade4rs_offer",
            DisplayName = "t",
            IsPersistent = true,
        }, new Vector4());
        var character = new Character();
        character.SetCoid(3000 + race * 10 + classId, true);
        character.LoadCloneBase(cbid);
        character.AttachTestDataForTests("Hade4RsPilot");
        character.SetLevel(level);
        return (character, map);
    }
}
