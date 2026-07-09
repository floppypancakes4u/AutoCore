using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Game.Entities;
using AutoCore.Game.Map;
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
}
