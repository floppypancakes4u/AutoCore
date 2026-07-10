using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using System.Reflection;

[TestClass]
public class GlobalVehicleScopeTests
{
    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        ResetScopeLevers();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        ResetScopeLevers();
    }

    private static void ResetScopeLevers()
    {
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
        SectorMap.SendGroupReactionCall = true;
    }

    [TestMethod]
    public void PerformScopeQuery_FirstGlobalNpcVehicle_SendsCreateBeforeRegisteringGhost()
    {
        const int vehicleCbid = 650_001;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 18_134;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var npcVehicle = new Vehicle
        {
            Position = new Vector3(25f, 2.349889f, 0f),
            Rotation = new Quaternion(0f, 0.00005f, 0f, 1f),
        };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();

        var packets = new List<BasePacket>();
        var createWasSentBeforeScopeRegistration = false;
        TNLConnection.TestPacketSink = (_, packet) =>
        {
            packets.Add(packet);
            createWasSentBeforeScopeRegistration = npcVehicle.Ghost.GetFirstObjectRef() == null;
        };

        map.PerformScopeQuery(null, self, connection);

        var create = packets.OfType<CreateVehiclePacket>().SingleOrDefault();
        Assert.IsNotNull(create, "A global map vehicle needs an external create before its first ghost update");
        Assert.IsTrue(createWasSentBeforeScopeRegistration, "CreateVehicle must be queued before ObjectInScope");
        Assert.AreEqual(vehicleCoid, create.ObjectId.Coid);
        Assert.IsTrue(create.ObjectId.Global);
        Assert.AreEqual(npcVehicle.Position, create.Position);
        Assert.AreEqual(npcVehicle.Rotation, create.Rotation);
        Assert.IsNotNull(npcVehicle.Ghost.GetFirstObjectRef(), "vehicle ghost must still be scoped after create");

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(1, packets.OfType<CreateVehiclePacket>().Count(),
            "Re-scoping an existing global vehicle must not duplicate its create packet");
    }

    [TestMethod]
    public void PerformScopeQuery_ScopeGlobalVehiclesFalse_SkipsCreateAndGhost()
    {
        var (map, npcVehicle, self, connection, packets) = ArrangeScopedNpc();
        SectorMap.ScopeGlobalVehicles = false;

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(0, packets.OfType<CreateVehiclePacket>().Count());
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef());
    }

    [TestMethod]
    public void PerformScopeQuery_ScopeGlobalVehicleCreateFalse_NoCreateButStillGhosts()
    {
        var (map, npcVehicle, self, connection, packets) = ArrangeScopedNpc();
        SectorMap.ScopeGlobalVehicleCreate = false;

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(0, packets.OfType<CreateVehiclePacket>().Count());
        Assert.IsNotNull(npcVehicle.Ghost.GetFirstObjectRef(),
            "Ghost lever still on — ObjectInScope should run (probe only; live client may die without create)");
    }

    [TestMethod]
    public void PerformScopeQuery_ScopeGlobalVehicleGhostFalse_CreateWithoutGhost()
    {
        var (map, npcVehicle, self, connection, packets) = ArrangeScopedNpc();
        SectorMap.ScopeGlobalVehicleGhost = false;

        map.PerformScopeQuery(null, self, connection);

        Assert.IsNotNull(packets.OfType<CreateVehiclePacket>().SingleOrDefault());
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef(),
            "Ghost lever off must skip ObjectInScope after create");
    }

    private static (SectorMap Map, Vehicle Npc, Character Self, TNLConnection Connection, List<BasePacket> Packets)
        ArrangeScopedNpc()
    {
        const int vehicleCbid = 650_002;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 18_135;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var npcVehicle = new Vehicle
        {
            Position = new Vector3(25f, 2.349889f, 0f),
            Rotation = new Quaternion(0f, 0.00005f, 0f, 1f),
        };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        return (map, npcVehicle, self, connection, packets);
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 707,
            MapFileName = "sec_f_h_map_tut_j2_arkbaytutorial",
            DisplayName = "Hestia Ark Bay 313",
            IsTown = false,
        };

        var map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
        InitializeScopeBuffer(map, "_scopeNearby");
        InitializeScopeBuffer(map, "_scopeMissionGivers");
        InitializeScopeBuffer(map, "_scopeSelected");
        return map;
    }

    private static void InitializeScopeBuffer(SectorMap map, string fieldName)
    {
        typeof(SectorMap).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, new List<ClonedObjectBase>());
    }
}
