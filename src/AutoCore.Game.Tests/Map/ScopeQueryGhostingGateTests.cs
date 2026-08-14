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
/// Live freeze (2026-08-14): transferring to Back Range parked the client on a full loading bar
/// forever. <c>ResetGhosting</c> clears <c>Ghosting</c> for the duration of the client's map load,
/// and <c>GhostConnection.WritePacket</c> writes ghost data only <c>if (Ghosting &amp;&amp;
/// ScopeObject != null)</c> — so nothing can be ghosted during that window. The scope query kept
/// running anyway, and because the un-transmitted ghosts never stuck, <see cref="GhostObject.IsGhostedTo"/>
/// stayed false and it re-sent <c>CreateCreature</c> for every foreign creature on every packet:
/// 3,966 creates, 61 per COID. Those are guaranteed-ordered events, and they buried the
/// <c>rpcStartGhosting</c> RPC that would have set <c>Ghosting</c> true — 10,833 events deep.
/// Self-sustaining: the flood existed because ghosting was off, and ghosting stayed off because the
/// flood buried the RPC that turns it on.
/// <para>
/// Client-side (Ghidra, autoassault.exe): a duplicate create is a no-op —
/// <c>Process_EMSG_Sector_CreateCreature</c> @0080af70 returns immediately when
/// <c>CVOGClonedObjectList::Fetch</c> finds the COID. But the create is also the *only* thing that
/// binds a parked ghost: <c>AssignPendingGhostObject</c> @00807550 is called from the CreateCreature
/// and CreateVehicle handlers and nowhere else, and <c>TNLConnection::AddGhost</c> @005a0b30 is a
/// bare map insert that never consults the object list. So creates must keep travelling *with*
/// their ghosts — deduplicating them independently would strand ghosts unbound, and
/// <c>GhostObject::unpackUpdate</c> @005b17b0 discards every pose/HP update while
/// <c>m_pParent == NULL</c>. Gating the whole query on <c>Ghosting</c> preserves that pairing.
/// </para>
/// </summary>
[TestClass]
public class ScopeQueryGhostingGateTests
{
    private const int ContinentId = 8841;
    private const int CreatureCbid = 12448;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, maxHitPoint: 50);
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        _sent.Clear();
    }

    /// <summary>The regression: the flood that buried rpcStartGhosting.</summary>
    [TestMethod]
    public void NotGhosting_RepeatedScopeQueries_SendNoForeignCreates()
    {
        var (map, self, connection) = Scene();
        connection.ActivateGhosting();
        Assert.IsFalse(connection.IsGhosting(),
            "ActivateGhosting alone must not report ghosting — the client has not answered yet");

        for (var i = 0; i < 40; i++)
            map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(0, _sent.OfType<CreateCreaturePacket>().Count(),
            "no ghost can be transmitted while Ghosting is false, so a create sent now is pure "
            + "reliable-event pressure on the queue rpcStartGhosting is waiting in");
    }

    /// <summary>Before ActivateGhosting there is not even a scope object; nothing may go out.</summary>
    [TestMethod]
    public void BeforeActivateGhosting_ScopeQuery_SendsNothing()
    {
        var (map, self, connection) = Scene();

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void Ghosting_ScopeQuery_SendsCreateForForeignCreature()
    {
        var (map, self, connection) = Scene();
        connection.BeginGhostingForTests();

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(1, _sent.OfType<CreateCreaturePacket>().Count(p => p.CBID == CreatureCbid),
            "once ghosting is live the create must go out so it can bind the ghost");
    }

    /// <summary>
    /// The pairing that matters: the create must accompany the ghost, so the first query after
    /// ghosting goes live still emits it. Suppressing it there is what would strand the ghost.
    /// </summary>
    [TestMethod]
    public void GhostingStartsLate_FirstQueryAfterwards_StillSendsCreate()
    {
        var (map, self, connection) = Scene();
        connection.ActivateGhosting();
        for (var i = 0; i < 10; i++)
            map.PerformScopeQuery(null, self, connection);
        Assert.AreEqual(0, _sent.OfType<CreateCreaturePacket>().Count());

        connection.ForceGhostingForTests(true);
        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(1, _sent.OfType<CreateCreaturePacket>().Count(p => p.CBID == CreatureCbid),
            "the create the client needs to bind its ghost must not be lost to the gate");
    }

    /// <summary>
    /// Steady state, ghosting live: ObjectInScope makes IsGhostedTo true, so the create is not
    /// re-sent. This is the behaviour that was already correct and must stay that way.
    /// </summary>
    [TestMethod]
    public void Ghosting_RepeatedScopeQueries_DoNotResendCreate()
    {
        var (map, self, connection) = Scene();
        connection.BeginGhostingForTests();

        for (var i = 0; i < 40; i++)
            map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(1, _sent.OfType<CreateCreaturePacket>().Count(p => p.CBID == CreatureCbid),
            "a creature already ghosted to this connection must not be re-created every packet");
    }

    private (SectorMap Map, Character Self, TNLConnection Connection) Scene()
    {
        var continent = new ContinentObject
        {
            Id = ContinentId,
            MapFileName = $"tm_scopegate_{ContinentId}",
            DisplayName = "scope-gate",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        // CreateForTests builds via GetUninitializedObject, which skips field initializers — the
        // scope scratch buffers have to be supplied explicitly.
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }

        var creature = new Creature { Position = new Vector3(25f, 0f, 0f), Level = 5 };
        var counter = map.LocalCoidCounter;
        SpawnPoint.AssignMapNpcIdentity(creature, ref counter);
        map.LocalCoidCounter = counter;
        creature.LoadCloneBase(CreatureCbid);
        creature.SetupCBFields();
        creature.IsMissionGiver = true;
        creature.CreateGhost();
        creature.SetMap(map);

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);

        return (map, self, connection);
    }
}
