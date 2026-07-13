using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Client 0x206C Create uses map-local COIDs; server pad NPCs use global MapNpcIdentity.
/// UseObject must still open deliver dialog.
/// </summary>
[TestClass]
public class UseObjectDeliverResolveTests
{
    private const int ContId = 8830;
    private const int MissionId = 98300;
    private const int ObjectiveId = 98301;
    private const int DeliverCbid = 12448;
    private const long ClientLocalClickCoid = 15820; // map spawn / client-only body
    private const long ServerNpcCoid = MapNpcIdentity.CoidBase + 88_448;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCreatureCloneBase(DeliverCbid, maxHitPoint: 50);
        // IsNPC=1 for IsNpc()
        var cb = AssetManager.Instance.GetCloneBase(DeliverCbid) as AutoCore.Game.CloneBases.CloneBaseCreature;
        if (cb != null)
            cb.CreatureSpecific.IsNPC = 1;
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NpcInteractHandler.InvalidateMissionIndex();
        _sent.Clear();
    }

    [TestMethod]
    public void UseObject_ClientLocalCoid_ResolvesServerDeliverNpc()
    {
        SeedDeliverMission();
        var (conn, character, map) = CreatePlayer();

        var npc = new Creature { Position = new Vector3(5, 0, 0) };
        npc.SetCoid(ServerNpcCoid, true);
        npc.LoadCloneBase(DeliverCbid);
        npc.SetupCBFields();
        npc.IsMissionGiver = true;
        npc.SetMap(map);

        character.CurrentQuests.Add(MakeQuest());
        character.CurrentVehicle.Position = new Vector3(5, 0, 0);
        character.Position = new Vector3(5, 0, 0);

        // Client clicks map-local body that does not exist on the server.
        Assert.IsNull(map.GetObjectByCoid(ClientLocalClickCoid));

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(ClientLocalClickCoid, false),
            ObjectiveId = ObjectiveId,
        });

        Assert.IsTrue(
            _sent.OfType<NpcMissionDialogPacket>().Any(),
            "Deliver dialog must open when click COID is client-local but server pad NPC is nearby. Packets: "
            + string.Join(", ", _sent.Select(p => p.GetType().Name)));
    }

    [TestMethod]
    public void TryResolveNearbyDeliverNpc_FindsByCbidInRange()
    {
        SeedDeliverMission();
        var (_, character, map) = CreatePlayer();
        var npc = new Creature { Position = new Vector3(2, 0, 0) };
        npc.SetCoid(ServerNpcCoid, true);
        npc.LoadCloneBase(DeliverCbid);
        npc.SetupCBFields();
        npc.SetMap(map);
        character.CurrentQuests.Add(MakeQuest());

        var resolved = NpcInteractHandler.TryResolveNearbyDeliverNpc(
            character, ObjectiveId, new Vector3(0, 0, 0), ClientLocalClickCoid);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(ServerNpcCoid, resolved.ObjectId.Coid);
        Assert.AreEqual(DeliverCbid, resolved.CBID);
    }

    private void SeedDeliverMission()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private static CharacterQuest MakeQuest()
    {
        var q = new CharacterQuest(MissionId, 0);
        q.PopulateFromAssets();
        return q;
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_use_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(700, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        vehicle.SetCoid(701, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (connection, character, map);
    }
}
