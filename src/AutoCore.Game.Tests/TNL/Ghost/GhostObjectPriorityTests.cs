using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Stage 6: distance/type based <see cref="GhostObject.GetUpdatePriority"/>.
/// </summary>
[TestClass]
public class GhostObjectPriorityTests
{
    [TestMethod]
    public void GetUpdatePriority_NearerIsHigher()
    {
        var viewer = MakeCharacter(1, 0f);
        var near = MakeCreatureGhost(2, 100f);
        var far = MakeCreatureGhost(3, 300f);

        var nearPriority = near.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);
        var farPriority = far.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

        Assert.IsTrue(nearPriority > farPriority,
            $"Nearer object must outrank farther ({nearPriority} vs {farPriority}).");
    }

    [TestMethod]
    public void GetUpdatePriority_MissionGiverOutranksPlainNpc()
    {
        var viewer = MakeCharacter(1, 0f);
        var giver = MakeCreatureGhost(2, 100f, missionGiver: true);
        var plain = MakeCreatureGhost(3, 100f);

        var giverPriority = giver.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);
        var plainPriority = plain.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

        Assert.IsTrue(giverPriority > plainPriority,
            $"Mission giver must outrank a plain NPC at the same distance ({giverPriority} vs {plainPriority}).");
    }

    [TestMethod]
    public void GetUpdatePriority_ForeignVehicleOutranksPlainCreature_WhenBoostEnabled()
    {
        GhostVehicle.EnableForeignVehiclePosePriorityBoost = true;
        try
        {
            var viewer = MakeCharacter(1, 0f);
            var vehicle = MakeVehicleGhost(2, 100f);
            var plain = MakeCreatureGhost(3, 100f);

            var vehiclePriority = vehicle.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);
            var plainPriority = plain.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

            Assert.IsTrue(vehiclePriority > plainPriority,
                $"Moving foreign vehicle priority must exceed plain NPC ({vehiclePriority} vs {plainPriority}).");
        }
        finally
        {
            GhostVehicle.EnableForeignVehiclePosePriorityBoost = true;
        }
    }

    [TestMethod]
    public void GetUpdatePriority_PlayerStillOutranksForeignVehicle()
    {
        GhostVehicle.EnableForeignVehiclePosePriorityBoost = true;
        try
        {
            var viewer = MakeCharacter(1, 0f);
            var otherPlayer = MakeCharacter(2, 100f);
            var vehicle = MakeVehicleGhost(3, 100f);

            var playerPriority = otherPlayer.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);
            var vehiclePriority = vehicle.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

            Assert.IsTrue(playerPriority >= vehiclePriority,
                $"Player vehicles/characters must not lose to foreign NPC cars ({playerPriority} vs {vehiclePriority}).");
        }
        finally
        {
            GhostVehicle.EnableForeignVehiclePosePriorityBoost = true;
        }
    }

    [TestMethod]
    public void GetUpdatePriority_MovingVehicleOutranksIdleVehicle()
    {
        GhostVehicle.EnableForeignVehiclePosePriorityBoost = true;
        try
        {
            var viewer = MakeCharacter(1, 0f);
            var idle = MakeVehicleGhost(2, 50f);
            var moving = MakeVehicleGhost(3, 50f);
            // Non-zero linear velocity arms IsMovingForPoseStream.
            moving.ApplyServerMove(moving.Position, Quaternion.Default, new Vector3(12f, 0f, 0f), dt: 0.1f);

            var idleP = idle.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);
            var movingP = moving.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0);

            Assert.IsTrue(movingP > idleP,
                $"Moving vehicle must outrank idle ({movingP} vs {idleP}).");
        }
        finally
        {
            GhostVehicle.EnableForeignVehiclePosePriorityBoost = true;
        }
    }

    [TestMethod]
    public void GetUpdatePriority_SelfAndTargetPinnedAtOne()
    {
        var viewer = MakeCharacter(1, 0f);
        var target = MakeCreatureGhost(2, 300f);

        // Self: the scope object prioritised against itself.
        Assert.AreEqual(1.0f, viewer.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0), 0.0001f,
            "The scope object must be pinned at priority 1.");

        // Target: the viewer's current target, even far away.
        viewer.SetTargetObject(target);
        Assert.AreEqual(1.0f, target.Ghost.GetUpdatePriority(viewer.Ghost, GhostObject.PositionMask, 0), 0.0001f,
            "The viewer's target must be pinned at priority 1 regardless of distance.");
    }

    private static Character MakeCharacter(long coid, float x)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.Position = new Vector3(x, 0f, 0f);
        character.CreateGhost();
        return character;
    }

    private static Creature MakeCreatureGhost(long coid, float x, bool missionGiver = false)
    {
        var creature = new Creature();
        creature.SetCoid(coid, true);
        creature.Position = new Vector3(x, 0f, 0f);
        creature.IsMissionGiver = missionGiver;
        creature.CreateGhost();
        return creature;
    }

    private static Vehicle MakeVehicleGhost(long coid, float x)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.Position = new Vector3(x, 0f, 0f);
        vehicle.CreateGhost();
        return vehicle;
    }
}
