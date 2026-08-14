using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

/// <summary>
/// PackUpdate coverage for <see cref="GhostCreature"/> beyond StateMask-only wire tests.
/// </summary>
[TestClass]
public class GhostCreaturePackTests
{
    [TestCleanup]
    public void TearDown() => NetObject.PIsInitialUpdate = false;

    [TestMethod]
    public void PackUpdate_WithoutParent_Throws()
    {
        var ghost = new GhostCreature();
        Assert.ThrowsException<Exception>(() =>
            ghost.PackUpdate(null, GhostObject.InitialMask, new BitStream(new byte[32], 32)));
    }

    [TestMethod]
    public void PackUpdate_Initial_WritesLevelAndOptionalSpawnOwner()
    {
        var creature = MakeCreature(5001);
        creature.Level = 12;
        creature.SpawnOwner = 99;
        creature.InitializeHealthForTests(80);

        var stream = Pack(creature, GhostObject.InitialMask, initial: true);

        // PackCommon
        stream.Read(out long coid);
        Assert.AreEqual(5001L, coid);
        stream.ReadFlag();
        stream.ReadInt(20);
        stream.ReadInt(18);
        stream.ReadInt(16);
        stream.ReadInt(16);

        Assert.IsFalse(stream.ReadFlag()); // EnhancementId
        Assert.IsFalse(stream.ReadFlag()); // OnUseTrigger
        Assert.IsFalse(stream.ReadFlag()); // OnUseReaction
        Assert.IsFalse(stream.ReadFlag()); // Summoner
        Assert.IsTrue(stream.ReadFlag());  // SpawnOwner
        stream.Read(out long spawnOwner);
        Assert.AreEqual(99L, spawnOwner);
        Assert.IsFalse(stream.ReadFlag()); // DoesntCountAsSummon
        stream.Read(out byte level);
        Assert.AreEqual((byte)12, level);
        Assert.IsFalse(stream.ReadFlag()); // IsElite
        // PackSkills: skill count byte
        stream.Read(out byte skillCount);
        Assert.AreEqual((byte)0, skillCount);
    }

    [TestMethod]
    public void PackUpdate_HealthMaxPositionTargetMasks()
    {
        var creature = MakeCreature(5002);
        creature.InitializeHealthForTests(60);
        creature.Position = new Vector3(1f, 2f, 3f);
        creature.Rotation = Quaternion.Default;
        creature.ApplyServerMove(
            creature.Position,
            creature.Rotation,
            new Vector3(0.5f, 0f, 0f),
            new Vector3(10f, 0f, 10f));

        var target = new Creature();
        target.SetCoid(6000, false);
        creature.SetTargetObject(target);

        var mask = GhostObject.HealthMask
                   | GhostObject.HealthMaxMask
                   | GhostObject.PositionMask
                   | GhostObject.TargetMask
                   | GhostObject.MurdererMask;
        var stream = Pack(creature, mask, initial: false);

        Assert.IsTrue(stream.ReadFlag()); // Murderer
        Assert.AreEqual(0u, stream.ReadInt(32));
        Assert.AreEqual(0u, stream.ReadInt(32));

        Assert.IsTrue(stream.ReadFlag()); // Health
        Assert.AreEqual(60u, stream.ReadInt(18));
        Assert.IsFalse(stream.ReadFlag()); // corpse

        Assert.IsTrue(stream.ReadFlag()); // HealthMax
        Assert.AreEqual(60u, stream.ReadInt(18));

        Assert.IsFalse(stream.ReadFlag()); // State (not set)

        Assert.IsTrue(stream.ReadFlag()); // Position
        stream.Read(out float x);
        Assert.AreEqual(1f, x);
        // Remaining: Y/Z + quat(4) + vel(3) + targetPos(3) = 12 floats
        for (var i = 0; i < 12; i++)
            stream.Read(out float _);

        Assert.IsTrue(stream.ReadFlag()); // Target
        stream.Read(out long tCoid);
        Assert.AreEqual(6000L, tCoid);
        Assert.IsFalse(stream.ReadFlag()); // target global
    }

    [TestMethod]
    public void PackUpdate_TargetMask_NullTarget()
    {
        // Client GhostCreature::packUpdate writes cfidEmpty (coid=-1) when target is null.
        var creature = MakeCreature(5003);
        creature.InitializeHealthForTests(10);
        var stream = Pack(creature, GhostObject.TargetMask, initial: false);

        Assert.IsFalse(stream.ReadFlag()); // Murderer
        Assert.IsFalse(stream.ReadFlag()); // Health
        Assert.IsFalse(stream.ReadFlag()); // HealthMax
        Assert.IsFalse(stream.ReadFlag()); // State
        Assert.IsFalse(stream.ReadFlag()); // Position
        Assert.IsTrue(stream.ReadFlag());  // Target
        stream.Read(out long coid);
        Assert.AreEqual(-1L, coid);
        Assert.IsFalse(stream.ReadFlag());
    }

    [TestMethod]
    public void PackUpdate_Initial_NoSpawnOwner_FlagFalse()
    {
        var creature = MakeCreature(5004);
        creature.SpawnOwner = -1;
        creature.InitializeHealthForTests(10);
        var stream = Pack(creature, GhostObject.InitialMask, initial: true);

        SkipPackCommon(stream);
        Assert.IsFalse(stream.ReadFlag()); // Enhancement
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag()); // SpawnOwner absent
    }

    private static Creature MakeCreature(long coid)
    {
        var creature = new Creature();
        creature.SetCoid(coid, true);
        creature.CreateGhost();
        return creature;
    }

    private static BitStream Pack(Creature creature, ulong mask, bool initial)
    {
        var stream = new BitStream(new byte[2048], 2048);
        NetObject.PIsInitialUpdate = initial;
        creature.Ghost!.PackUpdate(null, mask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    private static void SkipPackCommon(BitStream stream)
    {
        stream.Read(out long _);
        stream.ReadFlag();
        stream.ReadInt(20);
        stream.ReadInt(18);
        stream.ReadInt(16);
        stream.ReadInt(16);
    }
}
