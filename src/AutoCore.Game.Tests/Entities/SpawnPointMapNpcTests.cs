using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

[TestClass]
public class SpawnPointMapNpcTests
{
    [TestMethod]
    public void AssignMapNpcIdentity_SetsGlobalHighRangeCoid()
    {
        var creature = new Creature();
        long counter = 42;

        SpawnPoint.AssignMapNpcIdentity(creature, ref counter);

        Assert.IsTrue(creature.ObjectId.Global);
        Assert.AreEqual(MapNpcIdentity.CoidBase + 42, creature.ObjectId.Coid);
        Assert.AreEqual(43, counter);
        Assert.IsTrue(MapNpcIdentity.IsMapNpcIdentity(creature.ObjectId));
    }

    [TestMethod]
    public void AssignMapNpcIdentity_DoesNotCollideWithClientLocalSpawnSpace()
    {
        var creature = new Creature();
        long counter = 5;
        const long mapHighestCoid = 1000;

        SpawnPoint.AssignMapNpcIdentity(creature, ref counter);

        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(creature.ObjectId, mapHighestCoid));
        Assert.IsTrue(creature.ObjectId.Global, "Must be global to avoid client-local map object match (0x005D262A).");
    }

    [TestMethod]
    public void AssignMapNpcIdentity_NullCreature_Throws()
    {
        long counter = 0;
        Assert.ThrowsException<ArgumentNullException>(() =>
            SpawnPoint.AssignMapNpcIdentity(null, ref counter));
    }

    [TestMethod]
    public void MapNpcCreature_CreateGhost_ProducesGhostCreature()
    {
        var creature = new Creature();
        long counter = 0;
        SpawnPoint.AssignMapNpcIdentity(creature, ref counter);

        creature.CreateGhost();

        Assert.IsNotNull(creature.Ghost);
        Assert.IsInstanceOfType(creature.Ghost, typeof(GhostCreature));
    }

    [TestMethod]
    public void AssignMapNpcIdentity_SequentialSpawns_GetDistinctCoids()
    {
        long counter = 0;
        var a = new Creature();
        var b = new Creature();

        SpawnPoint.AssignMapNpcIdentity(a, ref counter);
        SpawnPoint.AssignMapNpcIdentity(b, ref counter);

        Assert.AreNotEqual(a.ObjectId.Coid, b.ObjectId.Coid);
        Assert.AreEqual(a.ObjectId.Coid + 1, b.ObjectId.Coid);
    }

    [TestMethod]
    public void CalculateSpawnLevel_ClampsToByteRange()
    {
        Assert.AreEqual(1, SpawnPoint.CalculateSpawnLevel(1, 0));
        Assert.AreEqual(8, SpawnPoint.CalculateSpawnLevel(5, 3));
        Assert.AreEqual(1, SpawnPoint.CalculateSpawnLevel(0, 0));
        Assert.AreEqual(1, SpawnPoint.CalculateSpawnLevel(1, -10));
        Assert.AreEqual(255, SpawnPoint.CalculateSpawnLevel(200, 100));
    }

    [TestMethod]
    public void ApplyStaticNpcSpawnHeight_CombatIsNpcZero_Unchanged()
    {
        var spawn = new Vector3(10f, 50f, 20f);
        var specific = new CreatureSpecific { IsNPC = 0, FlyingHeight = 2f, PhysicsScale = 1f };

        var result = SpawnPoint.ApplyStaticNpcSpawnHeight(spawn, specific, "creature");

        Assert.AreEqual(spawn.X, result.X);
        Assert.AreEqual(spawn.Y, result.Y);
        Assert.AreEqual(spawn.Z, result.Z);
    }

    [TestMethod]
    public void ApplyStaticNpcSpawnHeight_IsNpc_AddsPhysicsFootAndFlyingHeight()
    {
        var spawn = new Vector3(1f, 100f, 3f);
        var specific = new CreatureSpecific
        {
            IsNPC = 1,
            FlyingHeight = 1.25f,
            PhysicsScale = 1f
        };

        var result = SpawnPoint.ApplyStaticNpcSpawnHeight(spawn, specific, "creature");

        Assert.AreEqual(1f, result.X);
        Assert.AreEqual(3f, result.Z);
        Assert.AreEqual(
            100f + 1.25f + SpawnPoint.CreaturePhysicsFootOffset,
            result.Y,
            0.0001f);
    }

    [TestMethod]
    public void ApplyStaticNpcSpawnHeight_IsNpc_ScalesFootByPhysicsScale()
    {
        var spawn = new Vector3(0f, 10f, 0f);
        var specific = new CreatureSpecific
        {
            IsNPC = 1,
            FlyingHeight = 0f,
            PhysicsScale = 1.2f
        };

        var result = SpawnPoint.ApplyStaticNpcSpawnHeight(spawn, specific, "humanoid");

        Assert.AreEqual(10f + SpawnPoint.CreaturePhysicsFootOffset * 1.2f, result.Y, 0.0001f);
    }

    [TestMethod]
    public void ApplyStaticNpcSpawnHeight_IsNpc_ZeroFlyingHeight_StillAddsFoot()
    {
        var spawn = new Vector3(0f, 42f, 0f);
        var specific = new CreatureSpecific
        {
            IsNPC = 1,
            FlyingHeight = 0f,
            PhysicsScale = 1f
        };

        var result = SpawnPoint.ApplyStaticNpcSpawnHeight(spawn, specific, "creature");

        Assert.AreEqual(42f + SpawnPoint.CreaturePhysicsFootOffset, result.Y, 0.0001f);
    }

    [TestMethod]
    public void ApplyStaticNpcSpawnHeight_NullSpecific_Unchanged()
    {
        var spawn = new Vector3(5f, 6f, 7f);
        var result = SpawnPoint.ApplyStaticNpcSpawnHeight(spawn, null, "creature");
        Assert.AreEqual(6f, result.Y);
    }

    [TestMethod]
    public void ResolvePhysicsFootOffset_CreatureAndHumanoid_KnownOffset()
    {
        Assert.AreEqual(SpawnPoint.CreaturePhysicsFootOffset, SpawnPoint.ResolvePhysicsFootOffset("creature"));
        Assert.AreEqual(SpawnPoint.CreaturePhysicsFootOffset, SpawnPoint.ResolvePhysicsFootOffset("HUMANOID"));
        Assert.AreEqual(SpawnPoint.CreaturePhysicsFootOffset, SpawnPoint.ResolvePhysicsFootOffset(null));
        Assert.AreEqual(SpawnPoint.CreaturePhysicsFootOffset, SpawnPoint.ResolvePhysicsFootOffset(""));
        Assert.AreEqual(0f, SpawnPoint.ResolvePhysicsFootOffset("sphere"));
        Assert.AreEqual(0f, SpawnPoint.ResolvePhysicsFootOffset("mine"));
    }
}
