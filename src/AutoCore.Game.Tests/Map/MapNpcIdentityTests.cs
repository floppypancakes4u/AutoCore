using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Game.Map;
using AutoCore.Game.Structures;

[TestClass]
public class MapNpcIdentityTests
{
    [TestMethod]
    public void AllocateCoid_IsGlobalAndInHighRange()
    {
        long counter = 10;
        var id = MapNpcIdentity.AllocateCoid(ref counter);

        Assert.IsTrue(id.Global, "Map NPC COIDs must be Global so they do not match client-local map objects.");
        Assert.IsTrue(id.Coid >= MapNpcIdentity.CoidBase, "Map NPC COIDs must sit above the reserved base.");
        Assert.AreEqual(MapNpcIdentity.CoidBase + 10, id.Coid);
        Assert.AreEqual(11, counter, "Counter must advance after allocation.");
        Assert.IsTrue(MapNpcIdentity.IsMapNpcIdentity(id));
    }

    [TestMethod]
    public void AllocateCoid_SequentialIdsDoNotCollideWithLocalSpawnSpace()
    {
        long counter = 0;
        var first = MapNpcIdentity.AllocateCoid(ref counter);
        var second = MapNpcIdentity.AllocateCoid(ref counter);

        Assert.AreEqual(first.Coid + 1, second.Coid);
        Assert.AreNotEqual(first.Coid, second.Coid);

        // Historical bug: Global=false with coid = HighestCoid+N collided with client locals.
        const long mapHighestCoid = 1000;
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(first, mapHighestCoid));
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(second, mapHighestCoid));

        var oldStyleLocal = new TFID(mapHighestCoid + 1, global: false);
        Assert.IsTrue(MapNpcIdentity.IsUnsafeLocalSpawnCoid(oldStyleLocal, mapHighestCoid));
    }

    [TestMethod]
    public void AllocateCoid_RejectsNegativeCounter()
    {
        long counter = -1;
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => MapNpcIdentity.AllocateCoid(ref counter));
    }

    [TestMethod]
    public void IsMapNpcIdentity_RejectsNullLocalAndLowGlobal()
    {
        Assert.IsFalse(MapNpcIdentity.IsMapNpcIdentity(null));
        Assert.IsFalse(MapNpcIdentity.IsMapNpcIdentity(new TFID(1, global: false)));
        Assert.IsFalse(MapNpcIdentity.IsMapNpcIdentity(new TFID(3374, global: true))); // player-like
        Assert.IsTrue(MapNpcIdentity.IsMapNpcIdentity(new TFID(MapNpcIdentity.CoidBase, global: true)));
    }

    [TestMethod]
    public void IsUnsafeLocalSpawnCoid_EdgeCases()
    {
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(null, 100));
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(new TFID(50, true), 100));
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(new TFID(100, false), 100));
        Assert.IsTrue(MapNpcIdentity.IsUnsafeLocalSpawnCoid(new TFID(101, false), 100));
    }

    [TestMethod]
    public void CoidBase_IsStableRegressionConstant()
    {
        // Freeze the crash-fix constant so a silent change reintroduces 0x005D262A risk.
        Assert.AreEqual(0x5000_0000L, MapNpcIdentity.CoidBase);
    }
}
