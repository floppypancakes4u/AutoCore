using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Regression tests for the TNL NetObject dirty-list (setMaskBits / clearMaskBits / collapse).
/// The C# port of clearMaskBits had a head-unlink typo that corrupts the static list and can
/// contribute to NullReferenceException inside SetMaskBits under NPC pose-dirty load.
/// </summary>
[TestClass]
public class NetObjectDirtyListTests
{
    [TestCleanup]
    public void TearDown()
    {
        // Always drain the static dirty list so tests do not leak state into each other.
        NetObject.CollapseDirtyList();
        Assert.IsNull(GetDirtyListHead(), "CollapseDirtyList must leave the static dirty list empty.");
    }

    [TestMethod]
    public void ClearMaskBits_WhenHeadOfList_PromotesNextAsDirtyListHead()
    {
        var head = new GhostObject();
        var tail = new GhostObject();

        // First dirty object becomes the initial head; second prepends and becomes the new head.
        tail.SetMaskBits(0x1UL);
        head.SetMaskBits(0x2UL);

        Assert.AreSame(head, GetDirtyListHead(), "Most recently dirtied object should be list head.");
        Assert.AreSame(tail, GetNextDirty(head), "Earlier object should sit behind the head.");

        // Clearing all bits on the head must unlink it and promote the next entry (Torque parity).
        head.ClearMaskBits(ulong.MaxValue);

        Assert.AreSame(tail, GetDirtyListHead(),
            "ClearMaskBits on the dirty-list head must set static _dirtyList to the former next node.");
        Assert.IsNull(GetPrevDirty(tail),
            "Promoted head must have a null prev link.");
        Assert.IsNull(GetNextDirty(head));
        Assert.IsNull(GetPrevDirty(head));
        Assert.AreEqual(0UL, GetDirtyMaskBits(head));
    }

    [TestMethod]
    public void SetMaskBits_AfterClearingHead_DoesNotThrow_AndRelinksCleanly()
    {
        var a = new GhostObject();
        var b = new GhostObject();
        var c = new GhostObject();

        a.SetMaskBits(0x1UL);
        b.SetMaskBits(0x2UL);
        // List head is b → a

        b.ClearMaskBits(ulong.MaxValue);
        // Correct: head is a. Bug left static head as b (bits 0) and orphaned a.

        // This is the ApplyServerMove / NPC tick path: dirties after prior list surgery.
        c.SetMaskBits(GhostObject.PositionMask);

        Assert.AreSame(c, GetDirtyListHead());
        Assert.AreEqual(GhostObject.PositionMask, GetDirtyMaskBits(c));
        // Former survivors must still be reachable from the new head (or be the head).
        Assert.IsTrue(
            ReferenceEquals(GetDirtyListHead(), a) ||
            ReferenceEquals(GetNextDirty(GetDirtyListHead()), a) ||
            ReferenceEquals(GetNextDirty(GetNextDirty(GetDirtyListHead())), a),
            "Object still carrying dirty bits must remain on the static dirty list after head clear + new dirty.");
    }

    [TestMethod]
    public void SetMaskBits_PrependsToNonNullDirtyList_WithoutThrowing()
    {
        var first = new GhostObject();
        var second = new GhostObject();

        first.SetMaskBits(0x1UL);
        second.SetMaskBits(0x2UL);

        Assert.AreSame(second, GetDirtyListHead());
        Assert.AreSame(first, GetNextDirty(second));
        Assert.AreSame(second, GetPrevDirty(first));
    }

    private static FieldInfo DirtyListField { get; } =
        typeof(NetObject).GetField("_dirtyList", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("NetObject._dirtyList field missing.");

    private static FieldInfo DirtyMaskBitsField { get; } =
        typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("NetObject._dirtyMaskBits field missing.");

    private static FieldInfo NextDirtyField { get; } =
        typeof(NetObject).GetField("_nextDirtyList", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("NetObject._nextDirtyList field missing.");

    private static FieldInfo PrevDirtyField { get; } =
        typeof(NetObject).GetField("_prevDirtyList", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("NetObject._prevDirtyList field missing.");

    private static NetObject GetDirtyListHead() => (NetObject)DirtyListField.GetValue(null);

    private static NetObject GetNextDirty(NetObject obj) => (NetObject)NextDirtyField.GetValue(obj);

    private static NetObject GetPrevDirty(NetObject obj) => (NetObject)PrevDirtyField.GetValue(obj);

    private static ulong GetDirtyMaskBits(NetObject obj) => (ulong)DirtyMaskBitsField.GetValue(obj);
}
