using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Creature ghost pose priority. Measured live (2026-08-15, dense town, one client) with the
/// sector's 2 s <c>PathPoseForce</c> diag:
/// <code>
/// movingCreatures=~45  creaturePosePacks2s=60..254  → ~1.7 Hz per creature (worst 0.68 Hz)
/// pathVehicles=~18     posePacks2s=185..409         → ~8.3 Hz per vehicle
/// </code>
/// Vehicles took ~67% of the pose budget for ~29% of the entities, and creature packs collapsed
/// exactly when vehicle packs spiked — creatures were the residual. The cause is the base weight
/// (creature 0.15 vs moving vehicle 0.5), not the starvation term and not the creature population:
/// ~225 pose slots/sec is ample for 45 creatures.
/// <para>
/// Worst-case 0.68 Hz means ~1.5 s between poses, and the client hard-snaps rather than blending
/// (<c>CVOGPhysicsBase::DoPositionUpdate</c> @0053eec0), so that gap is rendered as a teleport
/// backwards — the reported symptom.
/// </para>
/// </summary>
[TestClass]
public class GhostCreaturePriorityTests
{
    [TestInitialize]
    public void SetUp() => GhostCreature.EnableCreatureMovingPriority = true;

    [TestCleanup]
    public void TearDown() => GhostCreature.EnableCreatureMovingPriority = true;

    [TestMethod]
    public void GetUpdatePriority_MovingCreature_OutranksIdleCreature()
    {
        var viewer = MakeCharacter(1, 0f);
        var idle = MakeCreature(2, 100f);
        var moving = MakeCreature(3, 100f, speed: 4f);

        var idleP = idle.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);
        var movingP = moving.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

        Assert.IsTrue(movingP > idleP,
            $"a walking creature needs pose far more than a standing one ({movingP} vs {idleP})");
    }

    /// <summary>
    /// The regression that matters. A creature passed over twice must be able to take a slot from a
    /// just-packed vehicle; before this it needed ~6 extra skips, which is the measured 5x rate gap.
    /// </summary>
    [TestMethod]
    public void GetUpdatePriority_MovingCreature_TakesSlotFromVehicleWithinTwoSkips()
    {
        var viewer = MakeCharacter(1, 0f);
        var creature = MakeCreature(2, 100f, speed: 4f);
        var vehicle = MakeVehicle(3, 100f);

        var creatureP = creature.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 2);
        var vehicleP = vehicle.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

        Assert.IsTrue(creatureP > vehicleP,
            $"creatures must not be the residual of the vehicle pose stream ({creatureP} vs {vehicleP})");
    }

    /// <summary>Players still come first — the fix must not buy creature smoothness with player lag.</summary>
    [TestMethod]
    public void GetUpdatePriority_MovingCreature_NeverOutranksEquallyStarvedPlayer()
    {
        var viewer = MakeCharacter(1, 0f);
        var player = MakeCharacter(2, 100f);
        var creature = MakeCreature(3, 100f, speed: 4f);

        foreach (var skips in new[] { 0, 5, 20, 100 })
        {
            var playerP = player.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, skips);
            var creatureP = creature.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, skips);

            Assert.IsTrue(playerP >= creatureP,
                $"at {skips} skips a player must not lose its slot to an NPC ({playerP} vs {creatureP})");
        }
    }

    /// <summary>Nearer still beats farther — distance falloff must survive the weight change.</summary>
    [TestMethod]
    public void GetUpdatePriority_MovingCreature_NearerStillOutranksFarther()
    {
        var viewer = MakeCharacter(1, 0f);
        var near = MakeCreature(2, 50f, speed: 4f);
        var far = MakeCreature(3, 350f, speed: 4f);

        var nearP = near.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);
        var farP = far.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

        Assert.IsTrue(nearP > farP, $"({nearP} vs {farP})");
    }

    /// <summary>The A/B lever: off restores the legacy weight so a live comparison is possible.</summary>
    [TestMethod]
    public void GetUpdatePriority_LeverOff_RestoresLegacyCreatureWeight()
    {
        var viewer = MakeCharacter(1, 0f);
        var moving = MakeCreature(2, 100f, speed: 4f);

        GhostCreature.EnableCreatureMovingPriority = false;
        var off = moving.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);
        GhostCreature.EnableCreatureMovingPriority = true;
        var on = moving.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

        Assert.IsTrue(on > off, $"the lever must actually change the weight ({on} vs {off})");
    }

    /// <summary>Self/target pins are policy-independent and must be preserved by the override.</summary>
    [TestMethod]
    public void GetUpdatePriority_ScopeObjectAndViewerTarget_StillPinnedAtOne()
    {
        var viewer = MakeCharacter(1, 0f);
        var target = MakeCreature(2, 350f, speed: 4f);

        viewer.SetTargetObject(target);

        Assert.AreEqual(1.0f,
            target.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0), 0.0001f,
            "the viewer's target must stay pinned regardless of distance");
    }

    private static Character MakeCharacter(long coid, float x)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.Position = new Vector3(x, 0f, 0f);
        character.CreateGhost();
        return character;
    }

    private static Creature MakeCreature(long coid, float x, float speed = 0f)
    {
        var creature = new Creature();
        creature.SetCoid(coid, true);
        creature.Position = new Vector3(x, 0f, 0f);
        creature.CreateGhost();
        if (speed > 0f)
        {
            creature.ApplyServerMove(
                creature.Position, Quaternion.Default, new Vector3(speed, 0f, 0f), creature.Position);
        }

        return creature;
    }

    private static Vehicle MakeVehicle(long coid, float x)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.Position = new Vector3(x, 0f, 0f);
        vehicle.CreateGhost();
        vehicle.ApplyServerMove(vehicle.Position, Quaternion.Default, new Vector3(12f, 0f, 0f), dt: 0.1f);
        return vehicle;
    }
}
