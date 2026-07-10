using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.TNL;
using global::TNL.Entities;
using global::TNL.Structures;

/// <summary>
/// Stage 6 (optional): <see cref="GhostConnection.ObjectInScope"/> re-scopes an already-ghosted
/// object by walking its GhostInfo chain (C++ TNL parity) instead of scanning all slots — and must
/// not allocate a second GhostInfo for the same object on the same connection.
/// </summary>
[TestClass]
public class ObjectInScopeChainWalkTests
{
    [TestMethod]
    public void ReScopingAlreadyGhostedObject_SetsInScope_WithoutAllocatingNewGhostInfo()
    {
        var connection = new ScopeProbeConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.ActivateGhosting(); // sets Scoping = true

        var creature = new Creature();
        creature.SetCoid(1, true);
        creature.CreateGhost();
        var obj = creature.Ghost;

        connection.ObjectInScope(obj);
        var allocatedAfterFirst = connection.FreeCount;
        var firstGhostInfo = obj.GetFirstObjectRef();

        Assert.IsNotNull(firstGhostInfo, "First scope must attach a GhostInfo to the object.");
        Assert.AreEqual(1, allocatedAfterFirst, "First scope must consume exactly one GhostInfo slot.");
        Assert.AreNotEqual(0u, firstGhostInfo.Flags & (uint)GhostInfoFlags.InScope, "First scope must set InScope.");

        // New packet cycle: InScope is cleared (as PrepareWritePacket would), then the object is
        // re-scoped while still ghosted.
        firstGhostInfo.Flags &= ~(uint)GhostInfoFlags.InScope;
        connection.ObjectInScope(obj);

        Assert.AreEqual(allocatedAfterFirst, connection.FreeCount,
            "Re-scoping an already-ghosted object must not allocate a new GhostInfo.");
        Assert.AreSame(firstGhostInfo, obj.GetFirstObjectRef(),
            "Re-scoping must reuse the existing GhostInfo, not prepend a new one.");
        Assert.AreNotEqual(0u, obj.GetFirstObjectRef().Flags & (uint)GhostInfoFlags.InScope,
            "Re-scoping must re-set InScope on the existing GhostInfo.");
    }

    private sealed class ScopeProbeConnection : TNLConnection
    {
        /// <summary>Number of GhostInfo slots currently allocated (protected GhostFreeIndex).</summary>
        public int FreeCount => GhostFreeIndex;
    }
}
