using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;

/// <summary>
/// Client <c>CVOGCreature::CreateFromPacket</c> @0x004c82b0 only calls
/// <c>CVOGSectorMap::CreateMissionFlow</c> @0x004d4040 when CreateCreature +0x128
/// (on-use trigger) is not −1. Back Range town givers have no FAM TriggerEvents;
/// without those IDs the client never synthesizes the GiveMissionDialog trigger and
/// <c>CheckForAvailableMissionsByObject</c> never runs — no ! icon, no UseObject.
/// </summary>
[TestClass]
public class MissionFlowIdentityTests
{
    [TestMethod]
    public void TryEnsure_NonGiver_LeavesUnset()
    {
        var creature = new Creature { IsMissionGiver = false };
        creature.SetCbidForTests(99);
        creature.SetCoid(1, true);
        Assert.IsFalse(MissionFlowIdentity.TryEnsure(creature));
        Assert.AreEqual(-1, creature.OnUseTriggerCoid);
        Assert.AreEqual(-1, creature.OnUseReactionCoid);
    }

    [TestMethod]
    public void TryEnsure_Giver_AssignsDistinctTwentyBitLocalIds()
    {
        var creature = new Creature { IsMissionGiver = true, SpawnOwner = 1496 };
        creature.SetCbidForTests(11788);
        creature.SetCoid(0x5000_0100, true);

        Assert.IsTrue(MissionFlowIdentity.TryEnsure(creature));
        Assert.AreNotEqual(-1, creature.OnUseTriggerCoid);
        Assert.AreNotEqual(-1, creature.OnUseReactionCoid);
        Assert.AreNotEqual(creature.OnUseTriggerCoid, creature.OnUseReactionCoid);
        Assert.IsTrue(creature.OnUseTriggerCoid > 0 && creature.OnUseTriggerCoid < (1 << 20),
            "GhostCreature packs on-use COIDs in 20 bits.");
        Assert.IsTrue(creature.OnUseReactionCoid > 0 && creature.OnUseReactionCoid < (1 << 20));
        Assert.AreEqual(
            MissionFlowIdentity.CoidFor(1496, reaction: false),
            creature.OnUseTriggerCoid);
        Assert.AreEqual(
            MissionFlowIdentity.CoidFor(1496, reaction: true),
            creature.OnUseReactionCoid);
    }

    [TestMethod]
    public void TryEnsure_IsIdempotent()
    {
        var creature = new Creature { IsMissionGiver = true, SpawnOwner = 1496 };
        creature.SetCbidForTests(11788);
        Assert.IsTrue(MissionFlowIdentity.TryEnsure(creature));
        var trigger = creature.OnUseTriggerCoid;
        var reaction = creature.OnUseReactionCoid;
        Assert.IsTrue(MissionFlowIdentity.TryEnsure(creature));
        Assert.AreEqual(trigger, creature.OnUseTriggerCoid);
        Assert.AreEqual(reaction, creature.OnUseReactionCoid);
    }

    [TestMethod]
    public void TryEnsure_WithoutSpawnOwner_UsesCbid()
    {
        var creature = new Creature { IsMissionGiver = true, SpawnOwner = -1 };
        creature.SetCbidForTests(11788);
        Assert.IsTrue(MissionFlowIdentity.TryEnsure(creature));
        Assert.AreEqual(MissionFlowIdentity.CoidFor(11788, reaction: false), creature.OnUseTriggerCoid);
    }

}
