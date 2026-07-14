using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission;

using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Client 0x2071 applies slot floats always, but only invokes requirement callbacks
/// for bits set in lChangeBitmask (requirement index, not FirstStateSlot).
/// </summary>
[TestClass]
public class ObjectiveStateBuilderTests
{

    [TestMethod]
    public void Build_SetsBitmaskForRequirementIndex_AndSlotFloatFromProgress()
    {
        var obj = MissionObjective.CreateForTests(objectiveId: 5001, sequence: 0, questId: 4001, completeCount: 4);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            NumToKill = 4,
            TargetCBID = 9,
            FirstStateSlot = 0,
        });

        var packet = ObjectiveStateBuilder.Build(obj, progress: 2, maximum: 4);

        Assert.AreEqual(5001, packet.ObjectiveId);
        Assert.AreEqual(1u, packet.ObjectiveBitmask, "bit 0 = first requirement index");
        Assert.AreEqual(0.5f, packet.SlotProgress[0], 0.001f);
        Assert.AreEqual(0f, packet.SlotProgress[1]);
    }

    [TestMethod]
    public void Build_HighFirstStateSlot_StillSetsBitmask_SkipsOutOfRangeFloat()
    {
        var obj = MissionObjective.CreateForTests(5002, 0, 4002, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 1,
            NPCTargetCompletes = true,
            FirstStateSlot = 99,
        });

        var packet = ObjectiveStateBuilder.Build(obj, progress: 1, maximum: 1);

        Assert.AreEqual(1u, packet.ObjectiveBitmask, "requirement index bit still published");
        for (var i = 0; i < ObjectiveStatePacket.SlotCount; i++)
            Assert.AreEqual(0f, packet.SlotProgress[i]);
    }

    [TestMethod]
    public void Build_MultipleRequirements_SetsBitsAndSlotsPerIndex()
    {
        var obj = MissionObjective.CreateForTests(5003, 0, 4003, 1);
        obj.Requirements.Add(new ObjectiveRequirementPatrol(obj) { FirstStateSlot = 0, AutoComplete = true });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 2,
            NPCTargetCompletes = true,
            FirstStateSlot = 2,
        });

        // Turn-in ready: full progress on objective-level count.
        var packet = ObjectiveStateBuilder.Build(obj, progress: 1, maximum: 1);

        Assert.AreEqual(0b11u, packet.ObjectiveBitmask);
        Assert.AreEqual(1.0f, packet.SlotProgress[0], 0.001f);
        Assert.AreEqual(0f, packet.SlotProgress[1]);
        Assert.AreEqual(1.0f, packet.SlotProgress[2], 0.001f);
    }

    [TestMethod]
    public void Build_ZeroProgress_StillSetsRequirementBits_ForClientResync()
    {
        var obj = MissionObjective.CreateForTests(5004, 1, 4004, 1);
        obj.Requirements.Add(new ObjectiveRequirementPatrol(obj) { FirstStateSlot = 0, AutoComplete = true });

        var packet = ObjectiveStateBuilder.Build(obj, progress: 0, maximum: 1);

        Assert.AreEqual(5004, packet.ObjectiveId);
        Assert.AreEqual(1u, packet.ObjectiveBitmask, "fresh next objective must notify req callbacks");
        Assert.AreEqual(0f, packet.SlotProgress[0]);
    }

    [TestMethod]
    public void Build_NullObjective_ReturnsNull()
    {
        Assert.IsNull(ObjectiveStateBuilder.Build(null, 0, 1));
    }

    [TestMethod]
    public void Build_ClampsProgressRatio()
    {
        var obj = MissionObjective.CreateForTests(5005, 0, 4005, 1);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { NumToKill = 1, FirstStateSlot = 1 });

        var over = ObjectiveStateBuilder.Build(obj, progress: 9, maximum: 3);
        Assert.AreEqual(1.0f, over.SlotProgress[1], 0.001f);

        var zeroMax = ObjectiveStateBuilder.Build(obj, progress: 1, maximum: 0);
        Assert.AreEqual(1.0f, zeroMax.SlotProgress[1], 0.001f);
    }

    [TestMethod]
    public void BuildUseItemCount_WritesAbsoluteSlotFloat()
    {
        var obj = MissionObjective.CreateForTests(5006, 0, 4006, 0);
        var use = new ObjectiveRequirementUseItem(obj)
        {
            RepeatCount = 3,
            FirstStateSlot = 0,
        };
        obj.Requirements.Add(use);

        var packet = ObjectiveStateBuilder.BuildUseItemCount(obj, use, usesCompleted: 2);

        Assert.AreEqual(5006, packet.ObjectiveId);
        Assert.AreEqual(1u, packet.ObjectiveBitmask);
        Assert.AreEqual(2f, packet.SlotProgress[0], 0.001f);
        Assert.AreNotEqual(2f / 3f, packet.SlotProgress[0], 0.001f);
    }

    [TestMethod]
    public void Build_FromQuest_UseItem_UsesAbsoluteProgress()
    {
        var obj = MissionObjective.CreateForTests(5007, 0, 4007, 0);
        obj.Requirements.Add(new ObjectiveRequirementUseItem(obj)
        {
            RepeatCount = 4,
            FirstStateSlot = 0,
        });
        var quest = new CharacterQuest(4007, 0)
        {
            ObjectiveProgress = new[] { 1 },
            ObjectiveMax = new[] { 4 },
        };

        var packet = ObjectiveStateBuilder.Build(obj, quest);

        Assert.AreEqual(1f, packet.SlotProgress[0], 0.001f, "UseItem must not normalize to 0..1");
    }
}
