using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Regression cover for the map-transfer freeze of 2026-08-14 (client stuck on a full loading bar
/// after warping to Back Range).
///
/// <para><b>The failure.</b> <c>ResetGhosting</c> clears <c>Ghosting</c> for the whole duration of
/// the client's map load. <c>GhostConnection.WritePacket</c> emits ghost data only
/// <c>if (Ghosting &amp;&amp; ScopeObject != null)</c>, so during that window nothing can be
/// ghosted — yet the scope query kept running on every packet. Because the un-transmitted ghosts
/// never stuck, <see cref="GhostObject.IsGhostedTo"/> reverted to false each pass and the foreign
/// branch re-sent <c>CreateCreature</c> for every creature, forever. Live capture: 3,966 creates,
/// 61 per COID, 10,833 guaranteed-ordered events backed up. <c>rpcStartGhosting</c> — the RPC whose
/// reply sets <c>Ghosting</c> true — was queued behind all of it and never reached the client, so
/// the client never answered and <c>Ghosting</c> never became true. The flood existed because
/// ghosting was off, and ghosting stayed off because the flood buried its own cure.</para>
///
/// <para><b>The constraint the fix must respect.</b> From the client (Ghidra, autoassault.exe):
/// <c>Process_EMSG_Sector_CreateCreature</c> @0080af70 no-ops on a COID it already has, but the
/// create is the only thing that binds a parked ghost — <c>AssignPendingGhostObject</c> @00807550
/// is reached from the CreateCreature/CreateVehicle handlers and nowhere else, and
/// <c>TNLConnection::AddGhost</c> @005a0b30 is a bare map insert that never consults the object
/// list. A ghost whose create never arrives keeps <c>m_pParent == NULL</c>, and
/// <c>GhostObject::unpackUpdate</c> @005b17b0 discards every pose/HP update in that state. So
/// creates must keep travelling <i>with</i> their ghosts: suppressing creates alone would have
/// traded a frozen loading screen for NPCs frozen at spawn.</para>
///
/// <para>These drive the real <c>PrepareWritePacket</c> → <c>GhostCharacter.PerformScopeQuery</c> →
/// <c>SectorMap.PerformScopeQuery</c> chain rather than calling the map directly, so they also fail
/// if the gate is moved somewhere that path does not reach.</para>
/// </summary>
[TestClass]
public class MapTransferGhostFloodRegressionTests
{
    private const int ContinentId = 8861;
    private const int CreatureCbid = 12448;
    private const int PlayerVehicleCbid = 12449;

    /// <summary>Creature-dense like Back Range, where the flood crossed the queue budget first.</summary>
    private const int CreatureCount = 30;

    /// <summary>Packets a slow client map load spans (live: ~4.3 s of Stage3-ack latency).</summary>
    private const int LoadWindowPackets = 100;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, maxHitPoint: 50);
        // With two players on one map each sees the other's vehicle as a foreign global vehicle,
        // which the scope query serializes — so it needs a real clonebase behind it.
        AssetManagerTestHelper.RegisterVehicleCloneBase(PlayerVehicleCbid);
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        _sent.Clear();
    }

    /// <summary>
    /// The flood itself. Pre-fix this emitted a create per creature per packet — the 3,966-create
    /// storm that buried rpcStartGhosting.
    /// </summary>
    [TestMethod]
    public void LoadWindow_ManyWritePackets_EmitNothingAtAll()
    {
        var scene = Scene();
        scene.Connection.ActivateGhosting();
        Assert.IsFalse(scene.Connection.IsGhosting(), "client has not answered rpcStartGhosting yet");
        _sent.Clear();

        for (var i = 0; i < LoadWindowPackets; i++)
            scene.Connection.PrepareWritePacket();

        Assert.AreEqual(0, _sent.Count,
            $"the scope query must stay silent for the whole client load; emitted {_sent.Count} "
            + $"packets across {LoadWindowPackets} write-packets "
            + $"({string.Join(", ", _sent.Select(p => p.GetType().Name).Distinct())})");
    }

    /// <summary>
    /// Bounds the regression in the units that actually broke the connection: reliable events
    /// queued ahead of rpcStartGhosting. Pre-fix this was CreatureCount * LoadWindowPackets.
    /// </summary>
    [TestMethod]
    public void LoadWindow_QueuePressureAheadOfStartGhosting_IsZero()
    {
        var scene = Scene();
        scene.Connection.ActivateGhosting();
        _sent.Clear();

        for (var i = 0; i < LoadWindowPackets; i++)
            scene.Connection.PrepareWritePacket();

        var wouldHaveBeen = CreatureCount * LoadWindowPackets;
        Assert.AreEqual(0, _sent.OfType<CreateCreaturePacket>().Count(),
            $"pre-fix this path produced up to {wouldHaveBeen} creates, each a guaranteed-ordered "
            + "event queued ahead of the rpcStartGhosting the client is waiting for");
    }

    /// <summary>Client answers: the world must populate, exactly once per creature.</summary>
    [TestMethod]
    public void ClientReady_PopulatesEveryCreature_ExactlyOnce()
    {
        var scene = Scene();
        scene.Connection.ActivateGhosting();
        for (var i = 0; i < LoadWindowPackets; i++)
            scene.Connection.PrepareWritePacket();
        _sent.Clear();

        scene.Connection.ForceGhostingForTests(true);
        scene.Connection.PrepareWritePacket();

        Assert.AreEqual(CreatureCount, _sent.OfType<CreateCreaturePacket>().Count(),
            "every foreign creature must be created once ghosting is live");
        Assert.AreEqual(CreatureCount, DistinctCreatedCoids().Count,
            "one create per distinct creature, not repeats of a few");
    }

    /// <summary>
    /// The client-side invariant from Ghidra: a ghost may never be scoped to a connection that was
    /// never sent that object's create, or it parks with m_pParent == NULL and silently discards
    /// every update. This is the test that would have caught the naive de-duplication fix.
    /// </summary>
    [TestMethod]
    public void NoCreatureIsEverGhostedWithoutItsCreate()
    {
        var scene = Scene();
        scene.Connection.ActivateGhosting();

        for (var i = 0; i < LoadWindowPackets; i++)
        {
            scene.Connection.PrepareWritePacket();
            AssertNoGhostWithoutCreate(scene);
        }

        scene.Connection.ForceGhostingForTests(true);

        for (var i = 0; i < 20; i++)
        {
            scene.Connection.PrepareWritePacket();
            AssertNoGhostWithoutCreate(scene);
        }
    }

    /// <summary>Steady state must not drift back into re-sending once ghosting is live.</summary>
    [TestMethod]
    public void SteadyState_ManyWritePackets_DoNotResendCreates()
    {
        var scene = Scene();
        scene.Connection.BeginGhostingForTests();
        _sent.Clear();

        for (var i = 0; i < LoadWindowPackets; i++)
            scene.Connection.PrepareWritePacket();

        Assert.AreEqual(CreatureCount, _sent.OfType<CreateCreaturePacket>().Count(),
            "already-ghosted creatures must not be re-created on every packet");
    }

    /// <summary>
    /// Repeated warping is the real usage pattern. Creates must stay linear in the number of map
    /// sessions — the client wipes its object table on MapInfo, so one create per creature per
    /// session is correct, and anything super-linear is the flood returning.
    /// </summary>
    [TestMethod]
    public void RepeatedTransferCycles_KeepCreatesLinearInSessions()
    {
        var scene = Scene();
        const int cycles = 5;

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            scene.Connection.ResetGhosting();
            scene.Connection.EnsureGhostsAndScopeAfterMapTransfer(scene.Self);

            for (var i = 0; i < LoadWindowPackets; i++)
                scene.Connection.PrepareWritePacket();

            scene.Connection.ForceGhostingForTests(true);
            scene.Connection.PrepareWritePacket();
        }

        Assert.AreEqual(CreatureCount * cycles, _sent.OfType<CreateCreaturePacket>().Count(),
            "one create per creature per map session — no more, and no fewer");
    }

    /// <summary>
    /// A transfer must not silence a second player who is already in-world on the same map. The
    /// gate is per connection; a shared-map regression here would blank everyone else's world.
    /// </summary>
    [TestMethod]
    public void OneConnectionTransferring_DoesNotSilenceAnother()
    {
        var scene = Scene();
        var observer = AddObserver(scene.Map);
        observer.Connection.BeginGhostingForTests();

        scene.Connection.ResetGhosting();
        scene.Connection.EnsureGhostsAndScopeAfterMapTransfer(scene.Self);
        _sent.Clear();

        scene.Connection.PrepareWritePacket();
        observer.Connection.PrepareWritePacket();

        Assert.AreEqual(CreatureCount, _sent.OfType<CreateCreaturePacket>().Count(),
            "the transferring connection contributes nothing; the established one still populates");
    }

    /// <summary>
    /// The gate covers the interest query only. The local player's own scope object and ghosts are
    /// established by the world-entry path and must survive the window — without them the client
    /// has no self to render when ghosting starts.
    /// </summary>
    [TestMethod]
    public void LoadWindow_LeavesLocalPlayerScopeObjectIntact()
    {
        var scene = Scene();
        scene.Connection.ResetGhosting();
        scene.Connection.EnsureGhostsAndScopeAfterMapTransfer(scene.Self);

        for (var i = 0; i < LoadWindowPackets; i++)
            scene.Connection.PrepareWritePacket();

        Assert.AreSame(scene.Self.Ghost, scene.Connection.GetScopeObject(),
            "local player scope object must persist through the load window");
        Assert.IsNotNull(scene.Self.CurrentVehicle.Ghost,
            "local vehicle ghost must persist through the load window");
    }

    private void AssertNoGhostWithoutCreate(SceneParts scene)
    {
        var created = DistinctCreatedCoids();
        foreach (var creature in scene.Creatures)
        {
            if (creature.Ghost != null && creature.Ghost.IsGhostedTo(scene.Connection))
            {
                Assert.IsTrue(created.Contains(creature.ObjectId.Coid),
                    $"creature {creature.ObjectId.Coid} was ghosted without its CreateCreature; the "
                    + "client would park that ghost with m_pParent == NULL and discard its updates");
            }
        }
    }

    private HashSet<long> DistinctCreatedCoids()
    {
        var coids = new HashSet<long>();
        foreach (var packet in _sent.OfType<CreateCreaturePacket>())
        {
            if (packet.ObjectId != null)
                coids.Add(packet.ObjectId.Coid);
        }

        return coids;
    }

    private sealed class SceneParts
    {
        public SectorMap Map;
        public Character Self;
        public TNLConnection Connection;
        public List<Creature> Creatures;
    }

    private SceneParts Scene()
    {
        var continent = new ContinentObject
        {
            Id = ContinentId,
            MapFileName = $"tm_flood_{ContinentId}",
            DisplayName = "flood-regression",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        InitScopeBuffers(map);

        var creatures = new List<Creature>(CreatureCount);
        var counter = map.LocalCoidCounter;
        for (var i = 0; i < CreatureCount; i++)
        {
            var creature = new Creature { Position = new Vector3(10f + i, 0f, 0f), Level = 5 };
            SpawnPoint.AssignMapNpcIdentity(creature, ref counter);
            creature.LoadCloneBase(CreatureCbid);
            creature.SetupCBFields();
            creature.IsMissionGiver = true;
            creature.CreateGhost();
            creature.SetMap(map);
            creatures.Add(creature);
        }

        map.LocalCoidCounter = counter;

        var (self, connection) = AttachPlayer(map, 9_086_000_000L);
        return new SceneParts { Map = map, Self = self, Connection = connection, Creatures = creatures };
    }

    private static (Character Self, TNLConnection Connection) AddObserver(SectorMap map)
        => AttachPlayer(map, 9_087_000_000L);

    private static (Character Self, TNLConnection Connection) AttachPlayer(SectorMap map, long coidBase)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCoid(coidBase, true);
        self.AttachTestDataForTests();
        self.SetOwningConnection(connection);
        connection.CurrentCharacter = self;

        var vehicle = new Vehicle { Position = new Vector3(0f, 0f, 0f) };
        vehicle.SetCoid(coidBase + 1, true);
        vehicle.AttachTestDataForTests();
        vehicle.LoadCloneBase(PlayerVehicleCbid);
        self.SetCurrentVehicleForTests(vehicle);

        self.SetMap(map);
        vehicle.SetMap(map);

        connection.SuppressCreatePacketsForTests = true;
        self.CreateGhost();
        vehicle.CreateGhost();
        connection.SetScopeObject(self.Ghost);

        return (self, connection);
    }

    private static void InitScopeBuffers(SectorMap map)
    {
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }
    }
}
