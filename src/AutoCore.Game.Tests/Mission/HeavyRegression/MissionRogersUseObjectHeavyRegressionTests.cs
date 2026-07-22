using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Heavy regression for Rogers / New Day / Live-and-Direct UseObject shapes:
/// Mission.NPC=-1 deliver-only turn-in, same-NPC follow-up offer, GLM XML backfill,
/// ObjectUseManager fall-through, range/wrong-target negatives.
/// Synthetic ids only (not retail 554/2477/3032).
/// </summary>
[TestClass]
public class MissionRogersUseObjectHeavyRegressionTests
{
    private MissionHeavyRegressionFixture _fx = null!;

    private const int NewDayMid = 94201;
    private const int LiveDirectMid = 94202;
    private const int NewDayObj = 95201;
    private const int LiveDirectObj = 95202;
    private const int RogersCbid = 96201;
    private const int OtherCbid = 96202;
    private const long RogersCoid = 97201;
    private const long OtherCoid = 97202;
    private const long NearbyStoreCoid = 9810;
    private const long NearbyOpenStoreReactionCoid = 5445;

    [TestInitialize]
    public void SetUp()
    {
        _fx = new MissionHeavyRegressionFixture();
        SectorMap.SendGroupReactionCall = true;
        VendorStoreService.ResetSessionsForTests();
    }

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    // --- NPC=-1 deliver turn-in (New Day @ Rogers) ---

    [TestMethod]
    public void NpcMinusOne_ActiveDeliver_UseObject_OpensDialog()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, NewDayObj);

        var dialog = _fx.LastNpcMissionDialog();
        Assert.IsNotNull(dialog);
        CollectionAssert.Contains(dialog.MissionIds, NewDayMid);
        Assert.AreEqual(RogersCoid, dialog.NpcTfid.Coid);
        Assert.IsTrue(_fx.Sent.OfType<ObjectiveStatePacket>().Any());
    }

    [TestMethod]
    public void NpcMinusOne_UseObject_Twice_StillOpensDialog()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, NewDayObj);
        _fx.Sent.Clear();
        _fx.UseObject(conn, RogersCoid, NewDayObj);

        Assert.IsNotNull(_fx.LastNpcMissionDialog(), "Repeat UseObject must still open turn-in dialog.");
    }

    [TestMethod]
    public void NpcMinusOne_ObjectiveHintNegative_StillOpensWhenOwned()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, objectiveId: -1);

        Assert.IsNotNull(_fx.LastNpcMissionDialog());
    }

    [TestMethod]
    public void NpcMinusOne_WrongNpcCbid_NoDialog()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, OtherCoid, OtherCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, OtherCoid, NewDayObj);

        Assert.AreEqual(0, _fx.CountNpcMissionDialog());
    }

    [TestMethod]
    public void NpcMinusOne_OutOfRange_NoDialog()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (conn, ch, map, vehicle) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(500, 0, 0));
        vehicle.Position = new Vector3(0, 0, 0);
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, NewDayObj);

        Assert.AreEqual(0, _fx.CountNpcMissionDialog());
    }

    [TestMethod]
    public void NpcMinusOne_WithinGrace_OpensDialog()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (conn, ch, map, vehicle) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(100, 0, 0));
        vehicle.Position = new Vector3(0, 0, 0);
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, NewDayObj);

        Assert.IsNotNull(_fx.LastNpcMissionDialog());
    }

    [TestMethod]
    public void NpcMinusOne_NoActiveQuest_NoTurnInDialog()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (conn, _, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));

        _fx.UseObject(conn, RogersCoid, NewDayObj);

        Assert.AreEqual(0, _fx.CountNpcMissionDialog());
    }

    // --- Same NPC offers follow-up (Live and Direct) ---

    [TestMethod]
    public void AfterNpcMinusOneComplete_SameNpc_OffersFollowUp()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj, reqMissionId: NewDayMid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        ch.CompletedMissionIds.Add(NewDayMid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, RogersCoid);

        var dialog = _fx.LastNpcMissionDialog();
        Assert.IsNotNull(dialog);
        CollectionAssert.Contains(dialog.MissionIds, LiveDirectMid);
        CollectionAssert.DoesNotContain(dialog.MissionIds, NewDayMid);
    }

    [TestMethod]
    public void FollowUpOffer_WithoutPrereqComplete_NotOffered()
    {
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj, reqMissionId: NewDayMid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        // NewDay not completed — offer must not appear.
        _fx.Sent.Clear();

        _fx.UseObject(conn, RogersCoid);

        Assert.AreEqual(0, _fx.CountNpcMissionDialog());
    }

    [TestMethod]
    public void FollowUpOffer_WrongContinent_NotOffered()
    {
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, continentId: 9999, objectiveId: LiveDirectObj);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        ch.CompletedMissionIds.Add(NewDayMid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, RogersCoid);

        Assert.AreEqual(0, _fx.CountNpcMissionDialog());
    }

    [TestMethod]
    public void ActiveNpcMinusOne_TakesPriorityOverFollowUpOffer()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, NewDayObj);

        var dialog = _fx.LastNpcMissionDialog();
        Assert.IsNotNull(dialog);
        CollectionAssert.Contains(dialog.MissionIds, NewDayMid);
        // Deliver path returns early — offer list is not merged when turn-in is ready.
        CollectionAssert.DoesNotContain(dialog.MissionIds, LiveDirectMid);
    }

    // --- Full turn-in then offer chain ---

    [TestMethod]
    public void TurnInNpcMinusOne_ThenUseObject_OffersFollowUp()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj, reqMissionId: NewDayMid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, NewDayObj);
        _fx.Sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = NewDayMid,
            Accepted = false,
            MissionGiver = new TFID(RogersCoid, false),
        });

        Assert.AreEqual(0, ch.CurrentQuests.Count(q => q.MissionId == NewDayMid));
        Assert.IsTrue(ch.CompletedMissionIds.Contains(NewDayMid));

        _fx.Sent.Clear();
        _fx.UseObject(conn, RogersCoid);

        var dialog = _fx.LastNpcMissionDialog();
        Assert.IsNotNull(dialog, "After New Day turn-in, Rogers must offer Live-and-Direct shape.");
        CollectionAssert.Contains(dialog.MissionIds, LiveDirectMid);
    }

    [TestMethod]
    public void AcceptFollowUp_GrantsMission()
    {
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj, reqMissionId: NewDayMid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        ch.CompletedMissionIds.Add(NewDayMid);

        _fx.UseObject(conn, RogersCoid);
        Assert.IsNotNull(_fx.LastNpcMissionDialog());
        _fx.Sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = LiveDirectMid,
            Accepted = true,
            MissionGiver = new TFID(RogersCoid, false),
        });

        Assert.IsTrue(ch.CurrentQuests.Any(q => q.MissionId == LiveDirectMid));
    }

    // --- GLM XML backfill (WAD-before-GLM race) ---

    [TestMethod]
    public void MissingDeliverReqs_NoDialog_UntilApplyGlmXml()
    {
        var mission = _fx.SeedNpcMinusOneDeliverWithoutRequirements(NewDayMid, NewDayObj);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, NewDayObj);
        Assert.AreEqual(0, _fx.CountNpcMissionDialog(), "Empty requirements must not open turn-in.");

        var xml = XDocument.Parse($"""
            <Mission name="{mission.Name}" ID="{NewDayMid}">
              <Title>New Day</Title>
              <Objective name="obj" map="obj" ID="{NewDayObj}" sequence="0">
                <Requirement type="deliver" slot="0">
                  <CBIDItem>-1</CBIDItem>
                  <TargetNPCCBID>{RogersCbid}</TargetNPCCBID>
                  <ContinentID>{MissionHeavyRegressionFixture.ContId}</ContinentID>
                  <NPCTargetCompletes>1</NPCTargetCompletes>
                </Requirement>
              </Objective>
            </Mission>
            """);

        Assert.IsTrue(mission.ApplyGlmXml(xml.Root));
        Assert.IsTrue(
            mission.Objectives[0].Requirements.OfType<ObjectiveRequirementDeliver>()
                .Any(d => d.NPCTargetCBID == RogersCbid));

        NpcInteractHandler.InvalidateMissionIndex();
        _fx.Sent.Clear();
        _fx.UseObject(conn, RogersCoid, NewDayObj);

        Assert.IsNotNull(_fx.LastNpcMissionDialog());
    }

    [TestMethod]
    public void MissingDeliverReqs_ButGiverOfferReady_OpensOfferDialog()
    {
        // Race left New Day without deliver reqs; Live-and-Direct still offerable from binary NPC field.
        _fx.SeedNpcMinusOneDeliverWithoutRequirements(NewDayMid, NewDayObj);
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        _fx.UseObject(conn, RogersCoid, NewDayObj);

        var dialog = _fx.LastNpcMissionDialog();
        Assert.IsNotNull(dialog, "Offer path must still work when deliver XML is missing.");
        CollectionAssert.Contains(dialog.MissionIds, LiveDirectMid);
    }

    // --- ObjectUseManager / fall-through ---

    [TestMethod]
    public void PureMissionNpc_UseObject_DoesNotRequireStore()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, NewDayMid);

        ObjectUseManager.Handle(conn, MissionHeavyRegressionFixture.UsePacket(RogersCoid, NewDayObj));

        Assert.IsNotNull(_fx.LastNpcMissionDialog());
        Assert.AreEqual(0, _fx.Sent.OfType<GroupReactionCallPacket>().Count(),
            "Mission dialog must consume UseObject before store/facility reactions.");
    }

    [TestMethod]
    public void EmptyDialogMissionNpc_UseObject_NoDialogNoCrash()
    {
        // Mission NPC with nothing to say — consume UseObject; no dialog / no store fallthrough.
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj, reqMissionId: NewDayMid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        PlaceNearbyOpenStore(map, storePos: new Vector3(6, 0, 0));

        ObjectUseManager.Handle(conn, MissionHeavyRegressionFixture.UsePacket(RogersCoid));

        Assert.AreEqual(0, _fx.CountNpcMissionDialog());
        Assert.AreEqual(0, _fx.Sent.OfType<GroupReactionCallPacket>().Count(),
            "Empty mission NPC must not fall through to OpenStore.");
        Assert.AreEqual(0L, VendorStoreService.GetOpenStoreCoidForTests(ch.ObjectId.Coid));
    }

    [TestMethod]
    public void EmptyDialogMissionNpc_NearOpenStore_DoesNotOpenStore()
    {
        // Live repro: Kid Gareth (mission CBID) with nothing to give/receive while standing
        // near a town OpenStore — spatial VendorStore must not win the UseObject pipeline.
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj, reqMissionId: NewDayMid);
        Assert.IsTrue(NpcInteractHandler.IsMissionGiverCbid(RogersCbid));

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        PlaceNearbyOpenStore(map, storePos: new Vector3(6, 0, 0));

        ObjectUseManager.Handle(conn, MissionHeavyRegressionFixture.UsePacket(RogersCoid));

        Assert.AreEqual(0, _fx.CountNpcMissionDialog());
        Assert.AreEqual(0, _fx.Sent.OfType<GroupReactionCallPacket>().Count(),
            "Known mission NPC with empty dialog must consume UseObject before OpenStore.");
        Assert.AreEqual(0L, VendorStoreService.GetOpenStoreCoidForTests(ch.ObjectId.Coid),
            "No vendor session should open for an empty mission NPC.");
    }

    private static void PlaceNearbyOpenStore(SectorMap map, Vector3 storePos)
    {
        var store = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        store.SetCoid(NearbyStoreCoid, false);
        store.Position = storePos;
        store.SetMap(map);

        var template = new ReactionTemplate
        {
            COID = (int)NearbyOpenStoreReactionCoid,
            ReactionType = ReactionType.OpenStore,
            ActOnActivator = true,
            ObjectiveIDCheck = -1,
            GenericVar1 = (int)NearbyStoreCoid,
            Name = "L1_openstore_general",
        };

        var reaction = new Reaction(template);
        reaction.SetCoid(NearbyOpenStoreReactionCoid, false);
        reaction.Position = storePos;
        reaction.SetMap(map);
    }

    // --- Multi-player isolation ---

    [TestMethod]
    public void TwoPlayers_NpcMinusOneTurnIn_Isolated()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        var (connA, chA, map, _) = _fx.CreatePlayer(charCoid: 18100, vehicleCoid: 18101);
        var (connB, chB, _, _) = _fx.CreatePlayer(charCoid: 18200, vehicleCoid: 18201);
        chB.SetMap(map);
        chB.CurrentVehicle.SetMap(map);
        chB.CurrentVehicle.Position = new Vector3(0, 0, 0);

        MissionHeavyRegressionFixture.PlaceNpc(map, RogersCoid, RogersCbid, new Vector3(5, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(chA, NewDayMid);
        MissionHeavyRegressionFixture.GiveQuest(chB, NewDayMid);

        _fx.UseObject(connA, RogersCoid, NewDayObj);
        NpcInteractHandler.HandleMissionDialogResponse(connA, new MissionDialogResponsePacket
        {
            MissionId = NewDayMid,
            Accepted = false,
            MissionGiver = new TFID(RogersCoid, false),
        });

        Assert.IsTrue(chA.CompletedMissionIds.Contains(NewDayMid));
        Assert.IsFalse(chB.CompletedMissionIds.Contains(NewDayMid));
        Assert.IsTrue(chB.CurrentQuests.Any(q => q.MissionId == NewDayMid));
    }

    // --- IsMissionGiverCbid index ---

    [TestMethod]
    public void DeliverTarget_IsMissionGiverCbid_EvenWhenMissionNpcMinusOne()
    {
        _fx.SeedNpcMinusOneDeliver(NewDayMid, NewDayObj, RogersCbid);
        Assert.IsTrue(NpcInteractHandler.IsMissionGiverCbid(RogersCbid));
        Assert.IsFalse(NpcInteractHandler.IsMissionGiverCbid(OtherCbid));
    }

    [TestMethod]
    public void GiverOffer_IsMissionGiverCbid()
    {
        _fx.SeedGiverOffer(LiveDirectMid, RogersCbid, objectiveId: LiveDirectObj);
        Assert.IsTrue(NpcInteractHandler.IsMissionGiverCbid(RogersCbid));
    }
}
