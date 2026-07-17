using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Destroying map props / NPCs must progress kill objectives generically (CBID / template COID).
/// </summary>
[TestClass]
public class MissionKillProgressTests
{
    private const int MissionId = 93041;
    private const int ObjectiveId = 95446;
    private const int NextObjectiveId = 95447;
    private const int TargetCbid = 12001;
    private const long PropCoid = 9301;
    private const int ContId = 811;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        TriggerManager.Instance.ClearAllForTests();
        MapPropCorpseDespawn.ResetForTests();
        MissionClientSoftPedal.ResetForTests();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        TriggerManager.Instance.ClearAllForTests();
        MapPropCorpseDespawn.ResetForTests();
        MissionClientSoftPedal.ResetForTests();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void KillProp_MatchingCbid_FinalKillOnly_StaysActiveReadyForTurnIn()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, TargetCbid);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        // Final kill-only: progress full, quest remains until giver dialog turn-in.
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjectiveId));
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
        // Map props leave a corpse for a delay before despawn — still present until flush.
        Assert.IsNotNull(map.GetObjectByCoid(PropCoid));
        Assert.IsTrue(MapPropCorpseDespawn.PendingCountForTests >= 1);
        MapPropCorpseDespawn.FlushAllForTests();
        Assert.IsNull(map.GetObjectByCoid(PropCoid));
    }

    [TestMethod]
    public void KillProp_MatchingCbid_AdvancesToNextObjective()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: NextObjectiveId);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, TargetCbid);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveId));
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any());
    }

    [TestMethod]
    public void KillProp_WrongCbid_DoesNotAdvance()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, cbid: 99999);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    [TestMethod]
    public void KillProp_NumToKillTwo_RequiresSecondKill()
    {
        SeedKillMission(TargetCbid, numToKill: 2, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop1 = PlaceProp(map, PropCoid, TargetCbid);
        prop1.SetMurderer(character.CurrentVehicle);
        prop1.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        var partial = _sent.OfType<ObjectiveStatePacket>().LastOrDefault();
        Assert.IsNotNull(partial);
        Assert.AreEqual(1f, partial.SlotProgress[0], 0.001f, "partial kill progress is absolute count");

        var prop2 = PlaceProp(map, PropCoid + 1, TargetCbid);
        prop2.SetMurderer(character.CurrentVehicle);
        prop2.OnDeath(DeathType.Silent);

        // Final kill-only objective: full progress stays active until giver turn-in.
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(2, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
        var full = _sent.OfType<ObjectiveStatePacket>().LastOrDefault();
        Assert.IsNotNull(full);
        Assert.AreEqual(2f, full.SlotProgress[0], 0.001f);
    }

    /// <summary>
    /// A Grouchy Gun (mission 470): kill 5, then return to giver — not auto-complete on last kill.
    /// </summary>
    [TestMethod]
    public void KillProp_GrouchyGunShape_FiveKills_StaysActiveUntilGiverTurnIn()
    {
        const int urbanCrawlerCbid = 2531;
        const int grouchyMissionId = 470;
        const int grouchyObjectiveId = 614;
        const int continent = 693;
        const int giverCbid = 11787;

        var obj = MissionObjective.CreateForTests(grouchyObjectiveId, 0, grouchyMissionId, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = urbanCrawlerCbid,
            NumToKill = 5,
            ContinentId = continent,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(grouchyMissionId, obj);
        mission.NPC = giverCbid;
        AssetManager.Instance.SetTestMission(mission);

        var continentObj = new ContinentObject
        {
            Id = continent,
            MapFileName = $"tm_kill_{continent}",
            DisplayName = "backrange",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continentObj, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(88001, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle();
        vehicle.SetCoid(88002, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);

        var quest = new CharacterQuest(grouchyMissionId, 0);
        quest.PopulateFromAssets();
        Assert.AreEqual(5, quest.ObjectiveMax[0], "NumToKill must populate ObjectiveMax when CompleteCount is 0");
        character.CurrentQuests.Add(quest);

        for (var i = 0; i < 5; i++)
        {
            _sent.Clear();
            var prop = PlaceProp(map, PropCoid + i, urbanCrawlerCbid);
            prop.SetMurderer(character.CurrentVehicle);
            prop.OnDeath(DeathType.Silent);

            Assert.AreEqual(1, character.CurrentQuests.Count, "kill-only final stays active through last kill");
            Assert.AreEqual(i + 1, character.CurrentQuests[0].ObjectiveProgress[0]);
            var state = _sent.OfType<ObjectiveStatePacket>().LastOrDefault();
            Assert.IsNotNull(state);
            Assert.AreEqual(grouchyObjectiveId, state.ObjectiveId);
            Assert.AreEqual((float)(i + 1), state.SlotProgress[0], 0.001f);
            Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
        }

        Assert.IsFalse(character.CompletedMissionIds.Contains(grouchyMissionId));
        Assert.IsTrue(NpcInteractHandler.IsKillTurnInReady(
            character.CurrentQuests[0], obj, giverCbid));

        // Place mission giver so dialog response resolves CBID = mission.NPC.
        const long giverCoid = 99001;
        var giver = new Creature { Position = new Vector3(0, 0, 0) };
        giver.SetCoid(giverCoid, false);
        giver.SetCbidForTests(giverCbid);
        giver.SetMap(map);

        _sent.Clear();
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
        MissionClientSoftPedal.ResetForTests();
        MissionClientSoftPedal.GroupReactionSuppressMs = 0;
        NpcInteractHandler.HandleMissionDialogResponse(connection, new MissionDialogResponsePacket
        {
            MissionId = grouchyMissionId,
            Accepted = false,
            MissionGiver = new TFID(giverCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(grouchyMissionId),
            "giver dialog completes kill-only mission on server");
        Assert.AreEqual(0, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void UseObject_KillTurnInReadyGiver_OpensNpcMissionDialog()
    {
        const int killCbid = 2531;
        const int missionId = 470;
        const int objectiveId = 614;
        const int giverCbid = 11787;
        const long giverCoid = 99101;

        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = killCbid,
            NumToKill = 1,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(missionId, obj);
        mission.NPC = giverCbid;
        AssetManager.Instance.SetTestMission(mission);
        NpcInteractHandler.InvalidateMissionIndex();

        var (conn, character, map) = CreatePlayer();
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1; // kill threshold met
        character.CurrentQuests.Add(quest);

        var giver = new Creature { Position = new Vector3(1, 0, 0) };
        giver.SetCoid(giverCoid, false);
        giver.SetCbidForTests(giverCbid);
        giver.IsMissionGiver = true;
        giver.SetMap(map);

        _sent.Clear();
        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(giverCoid, false),
            ObjectiveId = objectiveId,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog, "kill-ready giver UseObject must open NpcMissionDialog (0x206D)");
        CollectionAssert.Contains(dialog.MissionIds, missionId);
    }

    [TestMethod]
    public void UseObject_KillTurnInReady_WrongClickCoid_ResolvesNearbyGiver()
    {
        // Client often UseObjects a map-local TFID that does not match server COID.
        const int killCbid = 2531;
        const int missionId = 471;
        const int objectiveId = 615;
        const int giverCbid = 11787;
        const long giverCoid = 99102;

        var obj = MissionObjective.CreateForTests(objectiveId, 0, missionId, completeCount: 0);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = killCbid,
            NumToKill = 1,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(missionId, obj);
        mission.NPC = giverCbid;
        AssetManager.Instance.SetTestMission(mission);
        NpcInteractHandler.InvalidateMissionIndex();

        var (conn, character, map) = CreatePlayer();
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1;
        character.CurrentQuests.Add(quest);

        var giver = new Creature { Position = new Vector3(2, 0, 0) };
        giver.SetCoid(giverCoid, false);
        giver.SetCbidForTests(giverCbid);
        giver.IsMissionGiver = true;
        giver.SetMap(map);

        _sent.Clear();
        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(999_888_777, false), // missing COID — nearby fallback
            ObjectiveId = objectiveId,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog, "nearby mission.NPC resolve must open dialog when click COID mismatches");
        CollectionAssert.Contains(dialog.MissionIds, missionId);
    }

    [TestMethod]
    public void KillProp_WithNextObjective_AutoAdvancesOnThreshold()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: NextObjectiveId);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, TargetCbid);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveId));
    }

    [TestMethod]
    public void KillProp_TemplateIdMatch_Advances()
    {
        // kill_aggregate with TEMPLATEID list (map COID) rather than CBID
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var kill = new ObjectiveRequirementKillAggregate(obj) { NumToKill = 1 };
        kill.TemplateTargets.Add((int)PropCoid);
        obj.Requirements.Add(kill);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, cbid: 1); // CBID irrelevant
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void KillProp_NoMurderer_DoesNotAdvance()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, TargetCbid);
        // no SetMurderer
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    private void SeedKillMission(int targetCbid, int numToKill, int? nextObjectiveId)
    {
        var objA = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, completeCount: Math.Max(1, numToKill));
        var kill = new ObjectiveRequirementKill(objA)
        {
            TargetCBID = targetCbid,
            NumToKill = numToKill,
        };
        objA.Requirements.Add(kill);

        if (nextObjectiveId.HasValue)
        {
            var objB = MissionObjective.CreateForTests(nextObjectiveId.Value, 1, MissionId, 1);
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA, objB));
        }
        else
        {
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA));
        }
    }

    private static void GiveQuest(Character character)
    {
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static GraphicsObject PlaceProp(SectorMap map, long coid, int cbid)
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.InitializeHealthForTests(15);
        prop.SetCbidForTests(cbid);
        prop.SetCoid(coid, false);
        prop.SetInvincible(false);
        prop.SetMap(map);
        return prop;
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_kill_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18248, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(18249, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (connection, character, map);
    }
}
