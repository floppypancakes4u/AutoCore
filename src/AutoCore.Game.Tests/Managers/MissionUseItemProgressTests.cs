using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory;
using AutoCore.Game.TNL;

/// <summary>
/// Generic UseItem progress — synthetic missions only (no retail mission ids).
/// </summary>
[TestClass]
public class MissionUseItemProgressTests
{
    private const int MissionId = 92001;
    private const int ObjectiveId = 93001;
    private const long WorldCoid = 94001;
    private const int ContId = 801;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void SecondaryCbidRequired_MissingCargo_NoProgress()
    {
        SeedUse(primaryCoid: WorldCoid, primaryCbid: -1, configure: u =>
        {
            u.SecondaryCBID = 5501;
            u.RepeatCount = 1;
        });
        var (conn, character, map) = CreatePlayer(withInventory: true);
        PlaceWorld(map, WorldCoid);
        GiveQuest(character);

        NpcInteractHandler.HandleUseObject(conn, Packet(WorldCoid));

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void SecondaryGiveAtStart_GrantMission_PutsCargo()
    {
        const int secondary = 5502;
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 0);
        obj.Requirements.Add(new ObjectiveRequirementUseItem(obj)
        {
            PrimaryItem = WorldCoid,
            SecondaryCBID = secondary,
            SecondaryGiveAtStart = true,
            SecondaryMultipleUse = true,
            RepeatCount = 1,
            FirstStateSlot = 0,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer(withInventory: true);
        PlaceWorld(map, WorldCoid);

        NpcInteractHandler.GrantMission(conn, character, MissionId);

        Assert.IsTrue(character.Inventory.CountByCbid(secondary) >= 1);
        var create = _sent.OfType<CreateSimpleObjectPacket>()
            .FirstOrDefault(p => p.CBID == secondary && p.IsInInventory);
        Assert.IsNotNull(create, "Grant must send CreateSimpleObject for SecondaryGiveAtStart cargo");
        Assert.IsTrue(create.PossibleMissionItem, "Quest/useitem gear must flag PossibleMissionItem for mission inventory UI");
    }

    [TestMethod]
    public void MissingSecondary_GiveAtStartTopUp_AllowsUseAfterGrant()
    {
        const int secondary = 5509;
        SeedUse(primaryCoid: WorldCoid, primaryCbid: -1, configure: u =>
        {
            u.SecondaryCBID = secondary;
            u.SecondaryGiveAtStart = true;
            u.SecondaryMultipleUse = true;
            u.RepeatCount = 1;
        });
        var (conn, character, map) = CreatePlayer(withInventory: true);
        PlaceWorld(map, WorldCoid);
        // Active quest without cargo (simulates missed accept-time grant).
        GiveQuest(character);
        Assert.AreEqual(0, character.Inventory.CountByCbid(secondary));

        NpcInteractHandler.HandleUseObject(conn, Packet(WorldCoid));

        Assert.IsTrue(character.Inventory.CountByCbid(secondary) >= 1
            || character.CompletedMissionIds.Contains(MissionId),
            "top-up grant should supply secondary so use can complete");
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void SecondaryDestroy_RemovesOneFromCargoOnUse()
    {
        const int secondary = 5503;
        SeedUse(primaryCoid: WorldCoid, primaryCbid: -1, configure: u =>
        {
            u.SecondaryCBID = secondary;
            u.SecondaryDestroy = true;
            u.RepeatCount = 1;
        });
        var (conn, character, map) = CreatePlayer(withInventory: true);
        PlaceWorld(map, WorldCoid);
        GiveQuest(character);
        GrantCargo(character, secondary, 1);

        Assert.AreEqual(1, character.Inventory.CountByCbid(secondary));

        NpcInteractHandler.HandleUseObject(conn, Packet(WorldCoid));

        Assert.AreEqual(0, character.Inventory.CountByCbid(secondary));
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void TwoSequentialUseItemThenDeliver_AdvancesSequences()
    {
        const int mid = 92010;
        const int o1 = 93101;
        const int o2 = 93102;
        const int o3 = 93103;
        const long coidA = 94101;
        const long coidB = 94102;
        const int npcCbid = 7701;

        var use0 = MissionObjective.CreateForTests(o1, 0, mid, 0);
        use0.Requirements.Add(new ObjectiveRequirementUseItem(use0)
        {
            PrimaryItem = coidA,
            PrimaryInWorld = true,
            PrimaryDestroy = true,
            RepeatCount = 1,
            FirstStateSlot = 0,
        });
        var use1 = MissionObjective.CreateForTests(o2, 1, mid, 0);
        use1.Requirements.Add(new ObjectiveRequirementUseItem(use1)
        {
            PrimaryItem = coidB,
            PrimaryInWorld = true,
            PrimaryDestroy = true,
            RepeatCount = 1,
            FirstStateSlot = 0,
        });
        var del = MissionObjective.CreateForTests(o3, 2, mid, 0);
        del.Requirements.Add(new ObjectiveRequirementDeliver(del)
        {
            NPCTargetCBID = npcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(mid, use0, use1, del));

        var (conn, character, map) = CreatePlayer(withInventory: true);
        PlaceWorld(map, coidA);
        PlaceWorld(map, coidB);
        var quest = new CharacterQuest(mid, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(coidA, false),
            ObjectiveId = -1,
        });
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(character.MapPresence.IsSuppressed(coidA));

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(coidB, false),
            ObjectiveId = -1,
        });
        Assert.AreEqual(2, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(character.MapPresence.IsSuppressed(coidB));
    }

    [TestMethod]
    public void PrimaryCbidMatch_Completes()
    {
        const int cbid = 6601;
        SeedUse(primaryCoid: -1, primaryCbid: cbid, configure: null);
        var (conn, character, map) = CreatePlayer(withInventory: false);
        PlaceWorld(map, WorldCoid, cbid);
        GiveQuest(character);

        NpcInteractHandler.HandleUseObject(conn, Packet(WorldCoid));

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void WorldPrimaryDestroy_Explode_SendsDoDeathTrue()
    {
        SeedUse(primaryCoid: WorldCoid, primaryCbid: -1, configure: u =>
        {
            u.PrimaryDestroy = true;
            u.PrimaryInWorld = true;
            u.PrimaryExplode = true;
            u.RepeatCount = 1;
        });
        var (conn, character, map) = CreatePlayer(withInventory: false);
        PlaceWorld(map, WorldCoid);
        GiveQuest(character);

        NpcInteractHandler.HandleUseObject(conn, Packet(WorldCoid));

        var remove = _sent.OfType<InitCreateObjectPacket>().Single(p => !p.Create);
        Assert.AreEqual(WorldCoid, remove.ObjectCoid);
        Assert.IsTrue(remove.DoDeath);
        Assert.IsTrue(character.MapPresence.IsSuppressed(WorldCoid));
    }

    [TestMethod]
    public void WorldPrimaryDestroy_NoExplode_SendsDoDeathFalse()
    {
        SeedUse(primaryCoid: WorldCoid, primaryCbid: -1, configure: u =>
        {
            u.PrimaryDestroy = true;
            u.PrimaryInWorld = true;
            u.PrimaryExplode = false;
            u.RepeatCount = 1;
        });
        var (conn, character, map) = CreatePlayer(withInventory: false);
        PlaceWorld(map, WorldCoid);
        GiveQuest(character);

        NpcInteractHandler.HandleUseObject(conn, Packet(WorldCoid));

        var remove = _sent.OfType<InitCreateObjectPacket>().Single(p => !p.Create);
        Assert.IsFalse(remove.DoDeath);
        Assert.IsTrue(character.MapPresence.IsSuppressed(WorldCoid));
    }

    private static UseObjectPacket Packet(long coid) => new()
    {
        Target = new TFID(coid, false),
        ObjectiveId = -1,
    };

    private static void SeedUse(long primaryCoid, int primaryCbid, Action<ObjectiveRequirementUseItem> configure)
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 0);
        var use = new ObjectiveRequirementUseItem(obj)
        {
            PrimaryItem = primaryCoid,
            PrimaryCBID = primaryCbid,
            FirstStateSlot = 0,
            RepeatCount = 1,
        };
        configure?.Invoke(use);
        obj.Requirements.Add(use);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
    }

    private static void GiveQuest(Character character)
    {
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void PlaceWorld(SectorMap map, long coid, int cbid = 0)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        if (cbid > 0)
            obj.SetCbidForTests(cbid);
        obj.Position = new Vector3(0, 0, 0);
        obj.SetMap(map);
    }

    private static void GrantCargo(Character character, int cbid, int qty)
    {
        var coid = character.Map.LocalCoidCounter++;
        character.Inventory.GrantMissionCargoItem(
            cbid,
            CloneBaseObjectType.Item,
            $"item_{cbid}",
            coid,
            character.ObjectId.Coid,
            qty);
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer(bool withInventory)
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_useitem_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(250, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(251, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);

        if (withInventory)
        {
            var harness = new InventoryTestHarness();
            character.AttachInventoryForTests(harness.Inventory);
        }

        return (connection, character, map);
    }
}
