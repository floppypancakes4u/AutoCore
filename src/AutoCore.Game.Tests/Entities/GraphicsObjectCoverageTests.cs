using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Branch coverage for GraphicsObject combat HP / death / ghost paths.
/// </summary>
[TestClass]
public class GraphicsObjectCoverageTests
{
    private const int ContId = 844;
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        MapPropCorpseDespawn.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        MapPropCorpseDespawn.ResetForTests();
        GraphicsObject.ForceNetworkHelperFailureForTests = false;
    }

    [TestMethod]
    public void TakeDamage_ZeroOrNegative_ReturnsZero()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(10);
        Assert.AreEqual(0, prop.TakeDamage(0));
        Assert.AreEqual(0, prop.TakeDamage(-5));
        Assert.AreEqual(10, prop.GetCurrentHP());
    }

    [TestMethod]
    public void TakeDamage_Corpse_ReturnsZero()
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.InitializeHealthForTests(10);
        prop.OnDeath(DeathType.Silent); // not on map
        Assert.IsTrue(prop.IsCorpse);
        Assert.AreEqual(0, prop.TakeDamage(5));
    }

    [TestMethod]
    public void Revive_RestoresHpAndClearsCorpse()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(20);
        prop.TakeDamage(20);
        prop.OnDeath(DeathType.Silent);
        Assert.IsTrue(prop.IsCorpse);

        prop.Revive();
        Assert.IsFalse(prop.IsCorpse);
        Assert.AreEqual(20, prop.GetCurrentHP());
        Assert.AreEqual(20, prop.GetMaximumHP());
    }

    [TestMethod]
    public void EnsureHealthInitialized_FallbackWithoutClonebase()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        // MaxHP still 0
        Assert.AreEqual(0, prop.GetMaximumHP());
        var dealt = prop.TakeDamage(10);
        Assert.AreEqual(10, dealt);
        Assert.AreEqual(490, prop.GetCurrentHP()); // 500 default - 10
        Assert.AreEqual(500, prop.GetMaximumHP());
    }

    [TestMethod]
    public void OnDeath_WithoutMap_DoesNotThrow()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(1);
        prop.OnDeath(DeathType.Silent);
        Assert.IsTrue(prop.IsCorpse);
        Assert.IsFalse(_sent.OfType<DestroyObjectPacket>().Any());
    }

    [TestMethod]
    public void OnDeath_WithPlayerOnMap_SchedulesDelayedDestroy()
    {
        var (character, map) = CreatePlayer();
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.InitializeHealthForTests(1);
        prop.SetCoid(77, false);
        prop.SetMap(map);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.IsNotNull(map.GetObjectByCoid(77), "corpse stays until delayed despawn");
        Assert.IsFalse(_sent.OfType<DestroyObjectPacket>().Any(), "no DestroyObject at kill time");
        MapPropCorpseDespawn.FlushAllForTests();
        Assert.IsNull(map.GetObjectByCoid(77));
        Assert.IsTrue(_sent.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == 77));
    }

    [TestMethod]
    public void CreateGhost_Idempotent()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(5);
        prop.CreateGhost();
        var g = prop.Ghost;
        prop.CreateGhost();
        Assert.AreSame(g, prop.Ghost);
    }

    [TestMethod]
    public void GetBareTeamFaction_ReturnsFaction()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.Faction = 12;
        Assert.AreEqual(12, prop.GetBareTeamFaction());
    }

    [TestMethod]
    public void SetInvincible_True_DoesNotRequireGhost()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(5);
        prop.SetInvincible(true);
        Assert.IsTrue(prop.IsInvincible);
        // may or may not have ghost from prior; force path
        Assert.AreEqual(0, prop.TakeDamage(1));
    }

    [TestMethod]
    public void SimpleObject_DoesNotRemoveFromMapOnDeath()
    {
        var (character, map) = CreatePlayer();
        var item = new SimpleObject(GraphicsObjectType.Graphics);
        item.SetCoid(88, false);
        item.SetMap(map);
        // SimpleObject has HP from ctor
        item.TakeDamage(100000);
        item.OnDeath(DeathType.Silent);
        // still registered — RemoveFromMapOnDeath is false
        Assert.IsNotNull(map.GetObjectByCoid(88));
    }

    [TestMethod]
    public void OnBecameDamagable_DoesNotCreatePlainGhostObject()
    {
        var (character, map) = CreatePlayer();
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(5);
        prop.SetCoid(90, false);
        prop.SetMap(map);
        prop.SetInvincible(true);
        prop.SetInvincible(false);
        // Explicit CreateGhost still works for tests; automatic combat ghosting is disabled
        // (plain GhostObject local TFID → client AV 0x005B0EFF).
        Assert.IsNull(prop.Ghost);
        prop.CreateGhost();
        Assert.IsNotNull(prop.Ghost);
    }

    [TestMethod]
    public void OnBecameDamagable_CharacterWithoutConnection_StillNoAutoGhost()
    {
        var map = CreateMapOnly();
        var character = new Character();
        character.SetCoid(33, true);
        // no OwningConnection
        character.SetMap(map);

        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.InitializeHealthForTests(5);
        prop.SetCoid(91, false);
        prop.SetMap(map);
        prop.SetInvincible(false);
        Assert.IsNull(prop.Ghost);
    }

    [TestMethod]
    public void OnCloneBaseLoaded_WithoutCloneBase_SetsMinHpOne()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        var method = typeof(GraphicsObject).GetMethod(
            "OnCloneBaseLoaded",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(prop, null);
        Assert.AreEqual(1, prop.GetMaximumHP());
        Assert.AreEqual(1, prop.GetCurrentHP());
    }

    [TestMethod]
    public void EnsureHealthInitialized_UsesOnCloneBaseLoadedWhenMaxZeroButClonebaseMissing()
    {
        // MaxHP=0, CloneBaseObject null → fallback 500 via TakeDamage path already tested;
        // Invoke EnsureHealthInitialized after forcing MaxHP=0 with reflection-like re-init.
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        // After OnCloneBaseLoaded without clonebase MaxHP becomes 1; damage to zero then revive with Ensure
        var onLoad = typeof(GraphicsObject).GetMethod(
            "OnCloneBaseLoaded",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        onLoad.Invoke(prop, null);
        prop.TakeDamage(1);
        Assert.AreEqual(0, prop.GetCurrentHP());
        prop.Revive();
        Assert.AreEqual(1, prop.GetCurrentHP());
    }

    [TestMethod]
    public void ObjectType_Preserved()
    {
        var a = new GraphicsObject(GraphicsObjectType.Graphics);
        var b = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        Assert.AreEqual(GraphicsObjectType.Graphics, a.ObjectType);
        Assert.AreEqual(GraphicsObjectType.GraphicsPhysics, b.ObjectType);
    }

    [TestMethod]
    public void OnDeath_AlreadyGhosted_SchedulesDestroyAndStaysCorpse()
    {
        var (character, map) = CreatePlayer();
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(1);
        prop.SetCoid(92, false);
        prop.SetMap(map);
        prop.CreateGhost();
        prop.OnDeath(DeathType.Violent);
        Assert.IsTrue(prop.IsCorpse);
        Assert.IsFalse(_sent.OfType<DestroyObjectPacket>().Any());
        MapPropCorpseDespawn.FlushAllForTests();
        Assert.IsTrue(_sent.OfType<DestroyObjectPacket>().Any());
    }

    [TestMethod]
    public void ScopeGhost_ForcedFailure_OnlyWhenGhostExists()
    {
        var (character, map) = CreatePlayer();
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(5);
        prop.SetCoid(93, false);
        prop.SetMap(map);
        try
        {
            GraphicsObject.ForceNetworkHelperFailureForTests = true;
            prop.SetInvincible(false);
            Assert.IsNull(prop.Ghost, "auto combat ghost disabled (client AV 0x005B0EFF)");
            // Explicit ghost + scope still exercises error path.
            prop.CreateGhost();
            prop.SetInvincible(false);
            Assert.IsNotNull(prop.Ghost);
        }
        finally
        {
            GraphicsObject.ForceNetworkHelperFailureForTests = false;
        }
    }

    [TestMethod]
    public void BroadcastDestroy_ForcedFailure_DoesNotThrow()
    {
        var (character, map) = CreatePlayer();
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(1);
        prop.SetCoid(94, false);
        prop.SetMap(map);
        try
        {
            GraphicsObject.ForceNetworkHelperFailureForTests = true;
            prop.OnDeath(DeathType.Silent);
            Assert.IsTrue(prop.IsCorpse);
        }
        finally
        {
            GraphicsObject.ForceNetworkHelperFailureForTests = false;
        }
    }

    [TestMethod]
    public void OnCloneBaseLoaded_WithCloneBaseObject_UsesMaxHitPoint()
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        var clone = (CloneBaseObject)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(CloneBaseObject));
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { MaxHitPoint = 42 };
        typeof(ClonedObjectBase)
            .GetProperty(nameof(ClonedObjectBase.CloneBaseObject))!
            .SetValue(prop, clone);

        var method = typeof(GraphicsObject).GetMethod(
            "OnCloneBaseLoaded",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method!.Invoke(prop, null);

        Assert.AreEqual(42, prop.GetMaximumHP());
        Assert.AreEqual(42, prop.GetCurrentHP());

        // EnsureHealthInitialized short-circuits when MaxHP already set.
        prop.TakeDamage(2);
        Assert.AreEqual(40, prop.GetCurrentHP());

        // Reset MaxHP to 0 and force EnsureHealthInitialized via TakeDamage after zeroing with reflection
        typeof(GraphicsObject)
            .GetProperty("MaxHP", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(prop, 0);
        typeof(GraphicsObject)
            .GetProperty("HP", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(prop, 0);
        // CloneBaseObject still set → OnCloneBaseLoaded path inside EnsureHealthInitialized
        Assert.AreEqual(42, prop.TakeDamage(42)); // re-inits to 42 then takes 42
        Assert.AreEqual(0, prop.GetCurrentHP());
    }

    private static SectorMap CreateMapOnly()
    {
        var continent = new ContinentObject
        {
            Id = ContId + 1,
            MapFileName = $"tm_goc_empty_{ContId}",
            DisplayName = "t",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private (Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_goc_{ContId}",
            DisplayName = "t",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(1, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle();
        vehicle.SetCoid(2, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, map);
    }
}
