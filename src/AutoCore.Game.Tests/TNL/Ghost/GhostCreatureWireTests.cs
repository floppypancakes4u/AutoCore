using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Stage 4: GhostCreature.PackUpdate wires the real AiCombatState byte under StateMask.
/// </summary>
[TestClass]
public class GhostCreatureWireTests
{
    [TestCleanup]
    public void TearDown()
    {
        NetObject.PIsInitialUpdate = false;
    }

    [TestMethod]
    public void GhostCreature_StateMask_WritesAiCombatState()
    {
        var creature = new Creature();
        creature.SetCoid(9201, false);
        creature.AiCombatState = 5;
        creature.CreateGhost();

        var stream = new BitStream(new byte[512], 512);
        NetObject.PIsInitialUpdate = false;

        creature.Ghost.PackUpdate(null, GhostCreature.StateMask, stream);

        stream.SetBitPosition(0);

        Assert.IsFalse(stream.ReadFlag()); // MurdererMask block flag (not set)
        Assert.IsFalse(stream.ReadFlag()); // HealthMask block flag (not set)
        Assert.IsFalse(stream.ReadFlag()); // HealthMaxMask block flag (not set)
        Assert.IsTrue(stream.ReadFlag());  // StateMask block flag
        stream.Read(out byte state);
        Assert.AreEqual((byte)5, state);
    }
}
