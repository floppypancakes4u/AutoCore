using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 10 — foreign remount / dismount contract.
///
/// Client: GhostCharacter carries CurrentVehicleCoid on the initial body only
/// (FUN_0060A230 / FUN_0060A820). Incremental GhostCharacter, duplicate
/// CreateCharacter, and GhostVehicle owner deltas cannot remount an existing
/// Character. CreateVehicle of a new chassis can call Creature::SetVehicle
/// (FUN_004C49D0) when map+0xe4e8 is set.
///
/// Server: AutoCore has no live remount/dismount path for an already-visible
/// foreign Character. These tests pin the packet primitives and the "no live
/// mutation" gate so a later mount feature cannot ship the wrong sequence.
/// </summary>
[TestClass]
public class ForeignRemountContractTests
{
    private int _savedHoldMs;
    private int _savedHoldQueries;
    private int _savedStaleMs;

    [TestInitialize]
    public void Init()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
        _savedHoldMs = TNLConnection.ForeignGhostScopeHoldMilliseconds;
        _savedHoldQueries = TNLConnection.ForeignGhostScopeHoldQueries;
        _savedStaleMs = TNLConnection.ForeignCreateHoldStaleGraceMilliseconds;
        TNLConnection.ForeignGhostScopeHoldQueries = 1;
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 0;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 15000;
        TNLConnection.ForceForeignCreateReapply = false;
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.ForeignGhostScopeHoldMilliseconds = _savedHoldMs;
        TNLConnection.ForeignGhostScopeHoldQueries = _savedHoldQueries;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = _savedStaleMs;
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
        NetObject.PIsInitialUpdate = false;
    }

    /// <summary>
    /// FUN_0060A230 initial writes one 64-bit vehicle COID after level.
    /// Incremental flags follow appearance/skills; there is no second vehicle field.
    /// </summary>
    [TestMethod]
    public void GhostCharacterInitial_ContainsCurrentVehicleOnlyOnce()
    {
        var character = MakeCharacter(8101, "PilotA", vehicleCoid: 9101);
        var stream = PackGhost(character, GhostObject.InitialMask, initial: true);

        stream.Read(out long coid);
        Assert.AreEqual(8101L, coid);
        stream.ReadFlag();
        stream.ReadInt(20);
        stream.ReadInt(18);
        stream.ReadInt(16);
        stream.ReadInt(16);

        stream.ReadString(out string name);
        Assert.AreEqual("PilotA", name);
        stream.ReadString(out _);
        stream.Read(out byte level);
        Assert.AreEqual((byte)9, level);

        stream.Read(out long vehicleCoid);
        Assert.AreEqual(9101L, vehicleCoid,
            "FUN_0060A820 writes the 64-bit vehicle COID into synth CreateCharacter +0xD8 once.");

        Assert.AreEqual((uint)character.HeadId & 0xFFFFu, stream.ReadInt(16),
            "Next field after the single vehicle COID is HeadId, not another vehicle.");
        Assert.AreEqual((uint)character.BodyId & 0xFFFFu, stream.ReadInt(16));
    }

    [TestMethod]
    public void GhostCharacterIncremental_DoesNotCarryCurrentVehicleOrUsingVehicle()
    {
        var character = MakeCharacter(8102, "PilotB", vehicleCoid: 9102);
        var stream = PackGhost(character, GhostObject.HealthMask | GhostObject.HealthMaxMask, initial: false);

        Assert.IsFalse(stream.ReadFlag(), "GM");
        Assert.IsFalse(stream.ReadFlag(), "Clan");
        Assert.IsFalse(stream.ReadFlag(), "Pet");
        Assert.IsFalse(stream.ReadFlag(), "Position");
        Assert.IsFalse(stream.ReadFlag(), "Target");
        Assert.IsFalse(stream.ReadFlag(), "Token");
    }

    /// <summary>
    /// Client_RecvCreateCharacter returns when the TFID already exists, so AutoCore
    /// must not try to remount by sending a later CreateCharacter. First field-map
    /// scope of a mounted foreign player emits CreateVehicle + GhostCharacter, never
    /// a game CreateCharacter.
    /// </summary>
    [TestMethod]
    public void DuplicateCreateCharacter_DoesNotReapplyVehicle()
    {
        var scene = ArrangeMountedForeignOnField();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(0, scene.Packets.OfType<CreateCharacterPacket>().Count(),
            "Client_RecvCreateCharacter is a no-op for an existing TFID. AutoCore does not " +
            "send CreateCharacter for a foreign player that is already materialized.");
        Assert.IsNotNull(scene.ForeignCharacter.Ghost.GetFirstObjectRef());
    }

    /// <summary>
    /// Vehicle_applyCreatePacket → FUN_004C49D0 attaches an existing Character when
    /// CreateVehicle.CoidCurrentOwner is that character. This is the proven remount
    /// primitive for a *new* vehicle object. Duplicate CreateVehicle of an existing
    /// TFID with IsItemLink=false is a client no-op.
    /// </summary>
    [TestMethod]
    public void CreateVehicleForExistingCharacter_CanRepairOrSwitchVehicle()
    {
        var scene = ArrangeMountedForeignOnField();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var create = scene.Packets.OfType<CreateVehiclePacket>().Single();
        Assert.AreEqual(scene.ForeignCharacter.ObjectId.Coid, create.CoidCurrentOwner,
            "FUN_004bb040 / FUN_004c49d0 look up packet +0xD8 and SetVehicle the owner.");
        Assert.IsFalse(create.IsItemLink,
            "IsItemLink re-apply zeros owner to -1; foreign create must stay false.");
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.ForeignVehicle.ObjectId.Coid),
            "New CreateVehicle must still open the Pass 5 GhostVehicle hold.");
    }

    /// <summary>
    /// RequestObject Character recovery writes CreateCharacter via WriteToPacket.
    /// The vehicle COID on that packet is DB ActiveVehicleCoid (the session chassis).
    /// GhostCharacter initial uses the live CurrentVehicle pointer instead.
    /// </summary>
    [TestMethod]
    public void RequestObjectCharacter_UsesCurrentVehicleCoid()
    {
        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);

        var viewer = CreateViewerOnMap(out var map, 8001);
        var client = CreateClient(viewer);

        var other = new Character();
        other.SetCoid(9201, true);
        other.AttachTestDataForTests("OtherPilot");
        other.AssignCloneBaseForTests(MakeCharacterCloneBase());
        other.Position = new Vector3(3, 0, 3);
        other.SetMap(map);

        var vehicle = new Vehicle();
        vehicle.SetCoid(9202, true);
        other.SetCurrentVehicleForTests(vehicle);
        SetActiveVehicleCoid(other, 9202);

        InvokeRequestObject(client, 9201, true);

        var packet = sent.OfType<CreateCharacterPacket>().Single();
        Assert.AreEqual(GameOpcode.CreateCharacter, packet.Opcode);
        Assert.AreEqual(9202L, packet.CurrentVehicleCoid,
            "WriteToPacket CurrentVehicleCoid must be the session chassis (ActiveVehicleCoid / CurrentVehicle).");
        Assert.AreEqual(other.CurrentVehicle.ObjectId.Coid, packet.CurrentVehicleCoid);
    }

    [TestMethod]
    public void FirstScope_DoesNotEmitEnterExitOrVehicleSwitch()
    {
        var scene = ArrangeMountedForeignOnField();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsFalse(scene.Packets.Any(p => p.Opcode == GameOpcode.EnterExitVehicle),
            "0x202B is not an S2C remount packet and AutoCore must not emit it.");
        Assert.IsFalse(scene.Packets.Any(p => p.Opcode == GameOpcode.VehicleSwitch),
            "0x2053 is C2S local chassis-switch request.");
        Assert.IsFalse(scene.Packets.Any(p => p.Opcode == GameOpcode.VehicleSwitchResponse),
            "0x2054 is local garage chassis-switch S2C, not foreign remount.");
    }

    [TestMethod]
    public void ForeignVehicleHold_StillDefersGhostVehicle()
    {
        var scene = ArrangeMountedForeignOnField();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef());
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.ForeignVehicle.ObjectId.Coid));

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef());
    }

    [TestMethod]
    public void Respawn_ReusesSameCurrentVehicle()
    {
        // RespawnManager.TryRespawnInSector revives and repositions the existing
        // chassis. It does not assign a new CurrentVehicle. Foreign observers
        // therefore never see a remount on the retail airlift path.
        var character = new Character();
        character.SetCoid(9301, true);
        var vehicle = new Vehicle();
        vehicle.SetCoid(9302, true);
        character.SetCurrentVehicleForTests(vehicle);

        Assert.AreSame(vehicle, character.CurrentVehicle);
        Assert.AreEqual(9302L, character.CurrentVehicle.ObjectId.Coid);
    }

    private static Character MakeCharacter(long coid, string name, long vehicleCoid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.AttachTestDataForTests(name);
        character.SetLevel(9);
        character.InitializeHealthForTests(100);

        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.CreateGhost();
        return character;
    }

    private static BitStream PackGhost(Character character, ulong mask, bool initial)
    {
        var stream = new BitStream(new byte[2048], 2048);
        NetObject.PIsInitialUpdate = initial;
        character.Ghost!.PackUpdate(null, mask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    private static Scene ArrangeMountedForeignOnField()
    {
        const int vehicleCbid = 650_400;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 81_001;
        const long characterCoid = MapNpcIdentity.CoidBase + 81_002;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var foreignChar = new Character { Position = new Vector3(25f, 0f, 0f) };
        foreignChar.SetCoid(characterCoid, true);
        foreignChar.AttachTestDataForTests("ForeignPilot");
        foreignChar.CreateGhost();

        var foreignVeh = new Vehicle { Position = new Vector3(25f, 0f, 0f) };
        foreignVeh.SetCoid(vehicleCoid, true);
        foreignVeh.LoadCloneBase(vehicleCbid);
        foreignVeh.SetupCBFields();
        foreignChar.SetCurrentVehicleForTests(foreignVeh);
        foreignVeh.SetMap(map);
        foreignVeh.CreateGhost();
        foreignChar.SetMap(map);

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCoid(MapNpcIdentity.CoidBase + 81_099, true);
        observer.AttachTestDataForTests("Observer");
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        return new Scene(map, observer, foreignChar, foreignVeh, connection, packets);
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 708,
            MapFileName = "sec_f_h_map_tut_j2_arkbaytutorial",
            DisplayName = "Foreign Remount Field",
            IsTown = false,
        };

        var map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }

        return map;
    }

    private static Character CreateViewerOnMap(out SectorMap map, long coid)
    {
        var continent = new ContinentObject
        {
            Id = 558,
            MapFileName = "sec_t_w_map_test",
            DisplayName = "Remount RequestObject",
            IsTown = true,
        };
        map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
        var character = new Character { Position = new Vector3(0, 0, 0) };
        character.SetCoid(coid, true);
        character.AttachTestDataForTests("Viewer");
        character.AssignCloneBaseForTests(MakeCharacterCloneBase());
        character.SetCurrentVehicleForTests(new Vehicle { Position = character.Position });
        character.SetMap(map);
        return character;
    }

    private static TNLConnection CreateClient(Character character)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));
        connection.SetInterface(new TNLInterface(doGhosting: false, skipNetworkBind: true));
        connection.CurrentCharacter = character;
        character.SetOwningConnection(connection);
        return connection;
    }

    private static void InvokeRequestObject(TNLConnection connection, long coid, bool global)
    {
        var method = typeof(TNLConnection).GetMethod(
            "HandleRequestObjectPacket",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(new byte[3]);
        writer.WriteTFID(coid, global);
        writer.Flush();
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }

    private static void SetActiveVehicleCoid(Character character, long vehicleCoid)
    {
        var db = typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(character) as CharacterData;
        Assert.IsNotNull(db);
        db!.ActiveVehicleCoid = vehicleCoid;
    }

    private static CloneBaseObject MakeCharacterCloneBase()
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific
        {
            CloneBaseId = 1,
            Type = (int)CloneBaseObjectType.Character,
            BaseValue = 0,
        };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            MaxHitPoint = 100,
            MaxUses = 0,
        };
        return clone;
    }

    private sealed record Scene(
        SectorMap Map,
        Character Observer,
        Character ForeignCharacter,
        Vehicle ForeignVehicle,
        TNLConnection Connection,
        List<BasePacket> Packets);
}