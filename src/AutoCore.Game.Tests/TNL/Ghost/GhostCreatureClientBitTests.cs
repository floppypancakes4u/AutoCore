using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// PDB Pass 6. Decode order and widths from client <c>FUN_005d2800</c> (GhostCreature pack)
/// and <c>GhostCreature_UnpackUpdate</c> <c>0x005D2E40</c>. Mask numbers are the bits
/// that function tests: murderer 0x20, health 0x08, health-max 0x40, AI 0x80000000,
/// position 0x02, target 0x04.
/// </summary>
[TestClass]
public class GhostCreatureClientBitTests
{
    [TestCleanup]
    public void TearDown() => NetObject.PIsInitialUpdate = false;

    [TestMethod]
    public void ClientMaskConstants_MatchPackUpdateBitTests()
    {
        Assert.AreEqual(0x002ul, GhostObject.PositionMask);
        Assert.AreEqual(0x004ul, GhostObject.TargetMask);
        Assert.AreEqual(0x008ul, GhostObject.HealthMask);
        Assert.AreEqual(0x020ul, GhostObject.MurdererMask);
        Assert.AreEqual(0x040ul, GhostObject.HealthMaxMask);
        Assert.AreEqual(0x80000000ul, GhostCreature.StateMask);
    }

    [TestMethod]
    public void InitialUpdate_PackCommonThenOptionalFlagsThenLevel()
    {
        var creature = MakeCreature(7101);
        creature.Level = 9;
        creature.SpawnOwner = -1;

        var stream = Pack(creature, GhostObject.InitialMask, initial: true);

        stream.Read(out long coid);
        Assert.AreEqual(7101L, coid);
        stream.ReadFlag();
        stream.ReadInt(20);
        stream.ReadInt(18);
        stream.ReadInt(16);
        stream.ReadInt(16);

        Assert.IsFalse(stream.ReadFlag(), "EnhancementId absent → flag 0; unpack writes +0xd8 = −1.");
        Assert.IsFalse(stream.ReadFlag(), "OnUseTrigger absent.");
        Assert.IsFalse(stream.ReadFlag(), "OnUseReaction absent.");
        Assert.IsFalse(stream.ReadFlag(), "Summoner TFID absent.");
        Assert.IsFalse(stream.ReadFlag(), "SpawnOwner pointer/id absent.");
        Assert.IsFalse(stream.ReadFlag(), "DoesntCountAsSummon pack flag (client inverts into +0xf0).");
        stream.Read(out byte level);
        Assert.AreEqual((byte)9, level);
        Assert.IsFalse(stream.ReadFlag(), "IsElite.");
        stream.Read(out byte skillCount);
        Assert.AreEqual((byte)0, skillCount);
    }

    [TestMethod]
    public void HealthDelta_Is18BitsPlusCorpseFlag()
    {
        var creature = MakeCreature(7102);
        creature.InitializeHealthForTests(77);

        var stream = Pack(creature, GhostObject.HealthMask, initial: false);

        Assert.IsFalse(stream.ReadFlag(), "Murderer mask not set.");
        Assert.IsTrue(stream.ReadFlag(), "Health mask.");
        Assert.AreEqual(77u, stream.ReadInt(18));
        Assert.IsFalse(stream.ReadFlag(), "corpse flag");
        Assert.IsFalse(stream.ReadFlag(), "HealthMax not set.");
        Assert.IsFalse(stream.ReadFlag(), "State not set.");
        Assert.IsFalse(stream.ReadFlag(), "Position not set.");
        Assert.IsFalse(stream.ReadFlag(), "Target not set.");
    }

    [TestMethod]
    public void AiStateDelta_Is8BitsUnderHighBitMask()
    {
        var creature = MakeCreature(7103);
        creature.AiCombatState = 4;

        var stream = Pack(creature, GhostCreature.StateMask, initial: false);

        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsTrue(stream.ReadFlag(), "StateMask 0x80000000.");
        stream.Read(out byte ai);
        Assert.AreEqual((byte)4, ai);
    }

    [TestMethod]
    public void PositionDelta_IsThirteenFloats()
    {
        var creature = MakeCreature(7104);
        creature.Position = new Vector3(1f, 2f, 3f);
        creature.Rotation = Quaternion.Default;
        creature.ApplyServerMove(
            creature.Position,
            creature.Rotation,
            new Vector3(0.5f, 0f, 0f),
            new Vector3(10f, 0f, 10f));

        var stream = Pack(creature, GhostObject.PositionMask, initial: false);

        SkipFlagsUntilPosition(stream);
        Assert.IsTrue(stream.ReadFlag(), "PositionMask 0x02.");
        stream.Read(out float x);
        stream.Read(out float y);
        stream.Read(out float z);
        Assert.AreEqual(1f, x);
        Assert.AreEqual(2f, y);
        Assert.AreEqual(3f, z);
        for (var i = 0; i < 10; ++i)
            stream.Read(out float _);
    }

    [TestMethod]
    public void TargetDelta_Is64BitCoidPlusGlobalFlag()
    {
        var creature = MakeCreature(7105);
        var target = new Creature();
        target.SetCoid(8001, true);
        creature.SetTargetObject(target);

        var stream = Pack(creature, GhostObject.TargetMask, initial: false);

        SkipFlagsUntilTarget(stream);
        Assert.IsTrue(stream.ReadFlag(), "TargetMask 0x04.");
        stream.Read(out long coid);
        Assert.AreEqual(8001L, coid);
        Assert.IsTrue(stream.ReadFlag());
    }

    private static void SkipFlagsUntilPosition(BitStream stream)
    {
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
    }

    private static void SkipFlagsUntilTarget(BitStream stream)
    {
        SkipFlagsUntilPosition(stream);
        Assert.IsFalse(stream.ReadFlag());
    }

    private static BitStream Pack(Creature creature, ulong mask, bool initial)
    {
        NetObject.PIsInitialUpdate = initial;
        var stream = new BitStream(new byte[512], 512);
        creature.Ghost.PackUpdate(null, mask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    private static Creature MakeCreature(long coid)
    {
        var creature = new Creature();
        creature.SetCoid(coid, true);
        creature.CreateGhost();
        return creature;
    }
}
