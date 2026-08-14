using System.Net;
using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 11 — ghost lifetime / DestroyObject / descope ordering.
///
/// Client FUN_008078B0 calls GhostCreature vtable slot 10 (<c>FUN_005D2600</c>) only when
/// a TFID already exists and the object is still waiting for a ghost. That apply
/// dereferences <c>wrapper-&gt;vtbl+0x1D8()</c> with no NULL check (AV at 0x005D262A).
/// These tests pin the AutoCore sequences that must not manufacture that waiting
/// wrapper-with-NULL-view state.
/// </summary>
[TestClass]
public class GhostLifetimeOrderingTests
{
    private const int CreatureCbid = 811_100;
    private const long ObserverCoid = MapNpcIdentity.CoidBase + 81_099;
    private const long NpcCoid = MapNpcIdentity.CoidBase + 81_001;
    private const long ForeignCharCoid = MapNpcIdentity.CoidBase + 81_002;
    private const long ForeignVehicleCoid = MapNpcIdentity.CoidBase + 81_003;
    private const int SourceContinentId = 558;
    private const int DestContinentId = 693;

    private int _savedHoldMs;
    private int _savedHoldQueries;
    private Func<int, SectorMap> _previousResolver;
    private bool _previousSuppress;

    [TestInitialize]
    public void Init()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, maxHitPoint: 40);
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
        _savedHoldMs = TNLConnection.ForeignGhostScopeHoldMilliseconds;
        _savedHoldQueries = TNLConnection.ForeignGhostScopeHoldQueries;
        TNLConnection.ForeignGhostScopeHoldQueries = 1;
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 0;
        _previousResolver = MapManager.Instance.ResolveMapForTests;
        _previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        TNLConnection.MissionFlushForTests = () => { };
        TNLConnection.WorldStatePersistenceForTests = new NoopWorldStatePersistence();
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.MissionFlushForTests = null;
        TNLConnection.WorldStatePersistenceForTests = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.ForeignGhostScopeHoldMilliseconds = _savedHoldMs;
        TNLConnection.ForeignGhostScopeHoldQueries = _savedHoldQueries;
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
        MapManager.Instance.ResolveMapForTests = _previousResolver;
        MapManager.Instance.SuppressCreatePacketsForTests = _previousSuppress;
        ObjectManager.Instance.Remove(ObserverCoid);
        ObjectManager.Instance.Remove(NpcCoid);
        ObjectManager.Instance.Remove(ForeignCharCoid);
        ObjectManager.Instance.Remove(ForeignVehicleCoid);
    }

    [TestMethod]
    public void PermanentCreatureDespawn_GhostAndDestroyOrdering()
    {
        var scene = ArrangeWalkingNpcInRange();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Packets.OfType<CreateCreaturePacket>().Any(p => p.ObjectId.Coid == NpcCoid),
            "First sighting must emit CreateCreature before any later DestroyObject.");
        Assert.IsTrue(scene.Npc.Ghost.IsGhostedTo(scene.Connection),
            "First sighting ObjectInScope's GhostCreature in the same query.");

        scene.Packets.Clear();
        scene.Npc.OnDeath(DeathType.Violent);

        var destroy = scene.Packets.OfType<DestroyObjectPacket>().SingleOrDefault();
        Assert.IsNotNull(destroy, "Permanent creature death must send DestroyObject (0x2020).");
        Assert.AreEqual(NpcCoid, destroy.ObjectId.Coid);
        Assert.AreEqual(DeathType.Violent, destroy.DeathType);
        Assert.IsFalse(destroy.Force, "Combat death is non-silent CompletelyDestroyObject, not force-teardown.");
        Assert.IsNull(scene.Npc.Map, "OnDeath must SetMap(null) so the next interest query cannot re-scope it.");
        Assert.AreEqual(0, scene.Packets.OfType<CreateCreaturePacket>().Count(),
            "Death must not emit another CreateCreature.");
    }

    [TestMethod]
    public void TemporaryDescope_DoesNotSendDestroyObject()
    {
        var scene = ArrangeWalkingNpcInRange();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Npc.Ghost.IsGhostedTo(scene.Connection));
        scene.Packets.Clear();

        scene.Observer.Position = new Vector3(10_000f, 0f, 10_000f);
        scene.Observer.CurrentVehicle.Position = scene.Observer.Position;
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Leave-range descope is TNL KillGhost only. DestroyObject would leave a waiting wrapper for slot 10.");
        Assert.AreSame(scene.Map, scene.Npc.Map, "Temporary descope must not remove the server object.");
    }

    [TestMethod]
    public void CreatureDeath_DoesNotLeaveGhostScopedAfterDestroy()
    {
        var scene = ArrangeWalkingNpcInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Npc.Ghost.IsGhostedTo(scene.Connection));

        scene.Npc.OnDeath(DeathType.Violent);
        Assert.IsNull(scene.Npc.Map);

        scene.Packets.Clear();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        // TNL DetachObject runs in WritePacket, not in PerformScopeQuery. The server contract
        // is: the corpse is no longer interest-selected, so the next pack emits KillGhost
        // instead of another Create/Destroy pair (which would feed slot 10 a torn-down view).
        Assert.AreEqual(0, scene.Packets.OfType<CreateCreaturePacket>().Count());
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "A second query must not emit another DestroyObject.");
        Assert.IsNull(scene.Npc.Map);
    }

    [TestMethod]
    public void PlayerLogout_RemovesForeignGhostBeforeObjectDisposal()
    {
        var scene = ArrangeForeignPlayerInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Foreign.Ghost.IsGhostedTo(scene.Connection),
            "Foreign GhostCharacter is immediate (Pass 8).");

        scene.Packets.Clear();
        scene.Foreign.SetMap(null);
        scene.Foreign.CurrentVehicle?.SetMap(null);
        scene.Foreign.ClearGhost();
        scene.Foreign.CurrentVehicle?.ClearGhost();

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Logout is SetMap(null) + ClearGhost. Other clients lose the body via TNL descope, not DestroyObject.");
        Assert.IsNull(scene.Foreign.Ghost,
            "Owning session ClearGhost drops the shared GhostCharacter instance.");

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count());
        Assert.AreEqual(0, scene.Packets.OfType<CreateCharacterPacket>().Count());
    }

    [TestMethod]
    public void MapTransfer_ResetGhostingPrecedesWorldTeardown()
    {
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        var npc = PlaceWalkingNpc(character.Map, NpcCoid);
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();
        character.CreateGhost();
        connection.SetScopeObject(character.Ghost);

        character.Map.PerformScopeQuery(null, character, connection);
        Assert.IsTrue(npc.Ghost.IsGhostedTo(connection), "precondition: old-map NPC is ghosted");

        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);
        MapManager.Instance.ResolveMapForTests = _ => dest;

        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));

        Assert.IsFalse(connection.IsScopingForTests,
            "ResetGhosting must run before MapInfo so rpcEndGhosting deletes local ghosts first.");
        Assert.IsFalse(connection.IsGhosting());
        Assert.IsTrue(sent.OfType<MapInfoPacket>().Any(),
            "MapInfo is the first destination world packet and must follow ResetGhosting.");
        Assert.IsFalse(sent.OfType<CreateCreaturePacket>().Any(),
            "Destination Creates wait for Stage3 ack (Pass 3).");
        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
    }

    [TestMethod]
    public void SameTickCreateThenDestroy_DoesNotEmitUnsafeSequence()
    {
        var scene = ArrangeWalkingNpcInRange();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Npc.OnDeath(DeathType.Violent);

        var createIdx = scene.Packets.FindIndex(p => p is CreateCreaturePacket cp && cp.ObjectId.Coid == NpcCoid);
        var destroyIdx = scene.Packets.FindIndex(p => p is DestroyObjectPacket dp && dp.ObjectId.Coid == NpcCoid);
        Assert.IsTrue(createIdx >= 0, "CreateCreature must still be emitted for the first sighting.");
        Assert.IsTrue(destroyIdx > createIdx,
            "Same-window death must queue DestroyObject after CreateCreature on the ordered RPC channel. " +
            "Client FUN_008078B0 still applies the ghost record first, then the game queue, so this is " +
            "create-from-ghost then DestroyObject — not slot 10 on a torn-down view.");
        Assert.IsNull(scene.Npc.Map);
    }

    [TestMethod]
    public void RepeatedDespawn_IsIdempotent()
    {
        var scene = ArrangeWalkingNpcInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        scene.Npc.OnDeath(DeathType.Violent);
        var first = scene.Packets.OfType<DestroyObjectPacket>().Count(p => p.ObjectId.Coid == NpcCoid);
        Assert.AreEqual(1, first);

        scene.Packets.Clear();
        scene.Npc.OnDeath(DeathType.Violent);
        scene.Npc.SetMap(null);

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "A second OnDeath after SetMap(null) must not emit another DestroyObject.");
        Assert.AreEqual(0, scene.Packets.OfType<CreateCreaturePacket>().Count());
    }

    [TestMethod]
    public void RequestObjectForDestroyedObject_DoesNotResurrectIt()
    {
        var scene = ArrangeWalkingNpcInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Npc.OnDeath(DeathType.Violent);
        Assert.IsNull(scene.Npc.Map);

        scene.Packets.Clear();
        InvokeRequestObject(scene.Connection, NpcCoid, global: true);

        Assert.AreEqual(0, scene.Packets.OfType<CreateCreaturePacket>().Count(),
            "RequestObject for a TFID that left the map must not resend CreateCreature.");
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "A missing object is a no-op, not a second DestroyObject.");
        Assert.IsNull(scene.Npc.Map);
    }

    [TestMethod]
    public void FirstScope_WalkingCreature_SendsCreateThenGhostsImmediately()
    {
        var scene = ArrangeWalkingNpcInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(1, scene.Packets.OfType<CreateCreaturePacket>().Count());
        Assert.IsTrue(scene.Npc.Ghost.IsGhostedTo(scene.Connection),
            "Walking NPCs are not held. Client ghosts-first therefore materializes from the " +
            "synth buffer (FUN_0080AF70), not the waiting-object slot-10 path.");
    }

    [TestMethod]
    public void DestroyObjectPacket_WritesVictimTfidAndDeathType()
    {
        var victim = new TFID { Coid = NpcCoid, Global = true };
        var murderer = new TFID { Coid = ObserverCoid, Global = true };
        var packet = new DestroyObjectPacket(victim, DeathType.Fiery, murderer, force: false);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        writer.Flush();

        Assert.AreEqual(GameOpcode.DestroyObject, packet.Opcode);
        Assert.AreEqual(0x2020u, (uint)packet.Opcode);
        Assert.AreEqual(NpcCoid, packet.ObjectId.Coid);
        Assert.AreEqual(DeathType.Fiery, packet.DeathType);
        Assert.AreEqual(ObserverCoid, packet.Murderer.Coid);
        Assert.IsTrue(stream.Length >= 0x44,
            "Opcode + 0x40 body (victim TFID, extras, guard, death type, murderer, force).");
    }

    private Scene ArrangeWalkingNpcInRange()
    {
        var map = CreateFieldMap();
        var npc = PlaceWalkingNpc(map, NpcCoid);

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCoid(ObserverCoid, true);
        observer.AttachTestDataForTests("LifetimeObserver");
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        observer.SetMap(map);

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        observer.SetOwningConnection(connection);
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);
        return new Scene(map, observer, npc, null, null, connection, packets);
    }

    private Scene ArrangeForeignPlayerInRange()
    {
        var map = CreateFieldMap();
        var foreign = new Character { Position = new Vector3(20f, 0f, 0f) };
        foreign.SetCoid(ForeignCharCoid, true);
        foreign.AttachTestDataForTests("LifetimeForeign");
        foreign.CreateGhost();
        foreign.SetMap(map);

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCoid(ObserverCoid, true);
        observer.AttachTestDataForTests("LifetimeObserver");
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        observer.SetMap(map);

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);
        return new Scene(map, observer, null, foreign, null, connection, packets);
    }

    private static Creature PlaceWalkingNpc(SectorMap map, long coid)
    {
        var npc = new Creature { Level = 3, Position = new Vector3(15f, 0f, 0f) };
        npc.SetCoid(coid, true);
        npc.LoadCloneBase(CreatureCbid);
        npc.SetupCBFields();
        npc.CreateGhost();
        npc.SetMap(map);
        return npc;
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 708,
            MapFileName = "sec_lifetime_field",
            DisplayName = "Ghost Lifetime Field",
            IsTown = false,
        };

        var map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
        EnsureScopeLists(map);
        return map;
    }

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_lifetime_{continentId}",
            DisplayName = "lifetime-xfer",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(10f, 20f, 30f, 0f));
        EnsureScopeLists(map);
        return map;
    }

    private static void EnsureScopeLists(SectorMap map)
    {
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }
    }

    private static (Character Character, TNLConnection Connection) CreateTransferableOnSourceMap()
    {
        var source = CreateMap(SourceContinentId);

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));

        var character = new Character();
        character.SetCoid(ObserverCoid, true);
        character.AttachTestDataForTests("XferLifetime");
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(ForeignVehicleCoid, true);
        vehicle.AttachTestDataForTests();
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(source);
        vehicle.SetMap(source);

        ObjectManager.Instance.Add(character);
        return (character, connection);
    }

    private static void InvokeRequestObject(TNLConnection connection, long coid, bool global)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(new byte[3]);
        writer.WriteTFID(coid, global);
        writer.Flush();

        var method = typeof(TNLConnection).GetMethod(
            "HandleRequestObjectPacket",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }

    private sealed class NoopWorldStatePersistence : ICharacterWorldStatePersistence
    {
        public void Save(CharacterWorldStateSnapshot snapshot)
        {
        }
    }

    private sealed record Scene(
        SectorMap Map,
        Character Observer,
        Creature Npc,
        Character Foreign,
        Vehicle Vehicle,
        TNLConnection Connection,
        List<BasePacket> Packets);
}
