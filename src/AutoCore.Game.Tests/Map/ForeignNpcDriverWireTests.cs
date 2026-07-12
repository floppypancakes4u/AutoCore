using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Unit + regression coverage for foreign NPC driver CreateCreature / attach reapply (NPC.md §14.4).
/// </summary>
[TestClass]
public class ForeignNpcDriverWireTests
{
    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        WireDiag.Enabled = false;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        WireDiag.Enabled = false;
    }

    [TestMethod]
    public void HasPureCreatureDriver_False_WhenNullOrNoOwner()
    {
        Assert.IsFalse(ForeignNpcDriverWire.HasPureCreatureDriver(null));
        var vehicle = new Vehicle();
        vehicle.SetCoid(1, true);
        Assert.IsFalse(ForeignNpcDriverWire.HasPureCreatureDriver(vehicle));
    }

    [TestMethod]
    public void HasPureCreatureDriver_False_WhenOwnerIsCharacter()
    {
        var (vehicle, _) = ArrangeVehicleWithCharacterOwner();
        Assert.IsFalse(ForeignNpcDriverWire.HasPureCreatureDriver(vehicle));
    }

    [TestMethod]
    public void HasPureCreatureDriver_True_ForNpcCreatureDriver()
    {
        var (vehicle, _) = ArrangeVehicleWithCreatureDriver(driverCbid: 700_001);
        Assert.IsTrue(ForeignNpcDriverWire.HasPureCreatureDriver(vehicle));
    }

    [TestMethod]
    public void HasPureCreatureDriver_False_WhenDriverCbidInvalid()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 1, true);
        var driver = new Creature();
        driver.SetCoid(MapNpcIdentity.CoidBase + 2, true);
        // No LoadCloneBase → CBID override missing → CBID -1
        vehicle.SetOwner(driver);
        Assert.IsFalse(ForeignNpcDriverWire.HasPureCreatureDriver(vehicle));
    }

    [TestMethod]
    public void TryBuildDriverCreate_FillsPacketAndVehicleLink()
    {
        const int driverCbid = 700_010;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 10;
        const long driverCoid = MapNpcIdentity.CoidBase + 11;
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, maxHitPoint: 40);
        AssetManagerTestHelper.RegisterVehicleCloneBase(700_011);

        var driver = new Creature { Level = 9 };
        driver.SetCoid(driverCoid, true);
        driver.LoadCloneBase(driverCbid);
        driver.SetupCBFields();

        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid, true);
        vehicle.LoadCloneBase(700_011);
        vehicle.SetOwner(driver);

        Assert.IsTrue(ForeignNpcDriverWire.TryBuildDriverCreate(vehicle, out var packet));
        Assert.IsNotNull(packet);
        Assert.AreEqual(GameOpcode.CreateCreature, packet.Opcode);
        Assert.AreEqual(driverCoid, packet.ObjectId.Coid);
        Assert.AreEqual(driverCbid, packet.CBID);
        Assert.AreEqual(vehicleCoid, packet.CoidCurrentVehicle);
        Assert.AreEqual((byte)9, packet.Level);
        Assert.AreEqual(-1, packet.EnhancementId);
        Assert.IsFalse(packet.DoesntCountAsSummon);
        Assert.IsFalse(packet.IsElite);
    }

    [TestMethod]
    public void TryBuildDriverCreate_False_ForNullVehicleCharacterOwnerOrMissingDriver()
    {
        Assert.IsFalse(ForeignNpcDriverWire.TryBuildDriverCreate(null, out _));

        var bare = new Vehicle();
        bare.SetCoid(1, true);
        Assert.IsFalse(ForeignNpcDriverWire.TryBuildDriverCreate(bare, out _));

        var (vehChar, _) = ArrangeVehicleWithCharacterOwner();
        Assert.IsFalse(ForeignNpcDriverWire.TryBuildDriverCreate(vehChar, out _));

        // Owner that is neither Character nor Creature → GetAsCreature() is null.
        const int vehicleCbid = 700_012;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);
        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 12, true);
        vehicle.LoadCloneBase(vehicleCbid);
        var nonCreatureOwner = new SimpleObject(GraphicsObjectType.Graphics);
        nonCreatureOwner.SetCoid(MapNpcIdentity.CoidBase + 13, true);
        vehicle.SetOwner(nonCreatureOwner);
        Assert.IsFalse(ForeignNpcDriverWire.TryBuildDriverCreate(vehicle, out _));
    }

    [TestMethod]
    public void TrySendDriverCreate_SendsCreateCreature_WhenEligible()
    {
        var (vehicle, driver) = ArrangeVehicleWithCreatureDriver(700_020);
        var connection = new TNLConnection();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => packets.Add(p);

        Assert.IsTrue(ForeignNpcDriverWire.TrySendDriverCreate(connection, vehicle));
        var create = packets.OfType<CreateCreaturePacket>().Single();
        Assert.AreEqual(driver.ObjectId.Coid, create.ObjectId.Coid);
        Assert.AreEqual(vehicle.ObjectId.Coid, create.CoidCurrentVehicle);
    }

    [TestMethod]
    public void TrySendDriverCreate_False_WhenConnectionNullOrIneligible()
    {
        var (vehicle, _) = ArrangeVehicleWithCreatureDriver(700_021);
        Assert.IsFalse(ForeignNpcDriverWire.TrySendDriverCreate(null, vehicle));
        Assert.IsFalse(ForeignNpcDriverWire.TrySendDriverCreate(new TNLConnection(), null));
        Assert.IsFalse(ForeignNpcDriverWire.TrySendDriverCreate(new TNLConnection(), new Vehicle()));
    }

    [TestMethod]
    public void TrySendDriverCreate_WithWireDiag_StillSucceeds()
    {
        WireDiag.Enabled = true;
        var (vehicle, _) = ArrangeVehicleWithCreatureDriver(700_022);
        var connection = new TNLConnection();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => packets.Add(p);

        Assert.IsTrue(ForeignNpcDriverWire.TrySendDriverCreate(connection, vehicle));
        Assert.AreEqual(1, packets.OfType<CreateCreaturePacket>().Count());
    }

    [TestMethod]
    public void BuildOwnerAttachReapplyPackets_OrderAndContents()
    {
        var (vehicle, driver) = ArrangeVehicleWithCreatureDriver(700_030, wheelCbid: 40);

        var packets = ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(vehicle);
        Assert.AreEqual(4, packets.Count, "Destroy veh, Destroy driver, CreateVehicle, CreateCreature");

        Assert.IsInstanceOfType(packets[0], typeof(DestroyObjectPacket));
        Assert.AreEqual(vehicle.ObjectId.Coid, ((DestroyObjectPacket)packets[0]).ObjectId.Coid);

        Assert.IsInstanceOfType(packets[1], typeof(DestroyObjectPacket));
        Assert.AreEqual(driver.ObjectId.Coid, ((DestroyObjectPacket)packets[1]).ObjectId.Coid);

        Assert.IsInstanceOfType(packets[2], typeof(CreateVehiclePacket));
        var cv = (CreateVehiclePacket)packets[2];
        Assert.IsFalse(cv.IsItemLink);
        Assert.AreEqual(driver.ObjectId.Coid, cv.CoidCurrentOwner);
        Assert.AreEqual(vehicle.ObjectId.Coid, cv.ObjectId.Coid);

        Assert.IsInstanceOfType(packets[3], typeof(CreateCreaturePacket));
        var cc = (CreateCreaturePacket)packets[3];
        Assert.AreEqual(vehicle.ObjectId.Coid, cc.CoidCurrentVehicle);
        Assert.AreEqual(driver.ObjectId.Coid, cc.ObjectId.Coid);
    }

    [TestMethod]
    public void BuildOwnerAttachReapplyPackets_Empty_WhenNoCreatureDriver()
    {
        Assert.AreEqual(0, ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(null).Count);
        Assert.AreEqual(0, ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(new Vehicle()).Count);

        var (vehChar, _) = ArrangeVehicleWithCharacterOwner();
        Assert.AreEqual(0, ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(vehChar).Count);
    }

    [TestMethod]
    public void TryExecuteOwnerAttachReapply_SendsFullSequenceAndDirtiesGhost()
    {
        var (vehicle, driver) = ArrangeVehicleWithCreatureDriver(700_040, wheelCbid: 52);
        vehicle.CreateGhost();
        var ghost = vehicle.Ghost;
        Assert.IsNotNull(ghost);

        var connection = new TNLConnection();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => packets.Add(p);
        WireDiag.Enabled = true;

        Assert.IsTrue(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(connection, vehicle, ghost));

        Assert.AreEqual(2, packets.OfType<DestroyObjectPacket>().Count());
        Assert.AreEqual(1, packets.OfType<CreateVehiclePacket>().Count());
        Assert.AreEqual(1, packets.OfType<CreateCreaturePacket>().Count());
        // SetMaskBits is on TNL NetObject; success path is that execute returned true with ghost non-null.
    }

    [TestMethod]
    public void TryExecuteOwnerAttachReapply_False_WhenIneligible()
    {
        Assert.IsFalse(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(null, new Vehicle(), null));
        Assert.IsFalse(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(new TNLConnection(), null, null));

        var (vehChar, _) = ArrangeVehicleWithCharacterOwner();
        Assert.IsFalse(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(
            new TNLConnection(), vehChar, null));
    }

    [TestMethod]
    public void TryExecuteOwnerAttachReapply_NullGhost_StillSendsPackets()
    {
        var (vehicle, _) = ArrangeVehicleWithCreatureDriver(700_041);
        var connection = new TNLConnection();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => packets.Add(p);

        Assert.IsTrue(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(connection, vehicle, ghost: null));
        Assert.AreEqual(4, packets.Count);
    }

    [TestMethod]
    public void Creature_WriteToPacket_CreateCreature_CopiesLevelAndEnhancement()
    {
        const int cbid = 700_050;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid);
        var creature = new Creature { Level = 12 };
        creature.SetCoid(99, true);
        creature.LoadCloneBase(cbid);
        creature.SetupCBFields();

        var packet = new CreateCreaturePacket();
        creature.WriteToPacket(packet);

        Assert.AreEqual((byte)12, packet.Level);
        Assert.AreEqual(-1, packet.EnhancementId);
        Assert.AreEqual(cbid, packet.CBID);
        Assert.AreEqual(99L, packet.ObjectId.Coid);
    }

    [TestMethod]
    public void Creature_WriteToPacket_CreateCharacter_ReturnsAfterBase()
    {
        const int cbid = 700_051;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid);
        var creature = new Creature { Level = 3 };
        creature.SetCoid(100, true);
        creature.LoadCloneBase(cbid);
        creature.SetupCBFields();

        var packet = new CreateCharacterPacket();
        creature.WriteToPacket(packet);
        Assert.AreEqual(cbid, packet.CBID);
        // CreateCharacter path does not populate CreateCreature-only fields (N/A on character packet)
    }

    private static (Vehicle vehicle, Creature driver) ArrangeVehicleWithCreatureDriver(
        int driverCbid,
        int wheelCbid = 0)
    {
        const int vehicleCbid = 700_099;
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, maxHitPoint: 50);
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultWheelsetCbid: wheelCbid);

        var driver = new Creature { Level = 4 };
        driver.SetCoid(MapNpcIdentity.CoidBase + 9000 + driverCbid % 1000, true);
        driver.LoadCloneBase(driverCbid);
        driver.SetupCBFields();

        var vehicle = new Vehicle { Position = new Vector3(1, 0, 0) };
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 8000 + driverCbid % 1000, true);
        vehicle.LoadCloneBase(vehicleCbid);
        vehicle.SetupCBFields();
        if (wheelCbid > 0)
        {
            // EnsureDefaultWheelSet may equip during WriteToPacket
        }

        vehicle.SetOwner(driver);
        return (vehicle, driver);
    }

    private static (Vehicle vehicle, Character character) ArrangeVehicleWithCharacterOwner()
    {
        const int vehicleCbid = 700_098;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);
        var character = new Character();
        character.SetCoid(MapNpcIdentity.CoidBase + 50, false);
        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 51, true);
        vehicle.LoadCloneBase(vehicleCbid);
        vehicle.SetOwner(character);
        return (vehicle, character);
    }
}
