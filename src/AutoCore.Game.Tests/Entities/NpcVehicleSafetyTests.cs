using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.Entities;

using System;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Stage 2: NPC vehicles/creatures have no DBData-backed row (no player owns them), so ghost
/// packing and entity accessors must not dereference the DB-only Vehicle fields unconditionally
/// (GhostVehicle.PackUpdate initial block + Vehicle.WriteToPacket used to NPE for NPC vehicles).
/// </summary>
[TestClass]
public class NpcVehicleSafetyTests
{
    [TestCleanup]
    public void TearDown()
    {
        NetObject.PIsInitialUpdate = false;
    }

    [TestMethod]
    public void GhostVehicle_PackInitial_NoDbData_DoesNotThrow()
    {
        var map = CreateTestMap(4801);

        var vehicle = new Vehicle();
        vehicle.SetCoid(9001, true);
        vehicle.SetMap(map);
        vehicle.CreateGhost();

        var buffer = new byte[4096];
        var stream = new BitStream(buffer, (uint)buffer.Length);

        NetObject.PIsInitialUpdate = true;

        var ret = vehicle.Ghost.PackUpdate(null, GhostObject.InitialMask, stream);

        Assert.AreEqual(0x80ul, ret);
        Assert.IsTrue(stream.GetBitPosition() > 0);
    }

    [TestMethod]
    public void Vehicle_NameAndColors_FallBackWithoutDbData()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(9002, true);

        Assert.AreEqual(string.Empty, vehicle.Name);
        Assert.AreEqual(0u, vehicle.PrimaryColor);
        Assert.AreEqual(0u, vehicle.SecondaryColor);
        Assert.AreEqual((byte)0, vehicle.Trim);
    }

    [TestMethod]
    public void Vehicle_ApplyServerMove_SetsPoseAndPositionMask()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var vehicle = new Vehicle();
        vehicle.SetCoid(9003, true);
        vehicle.CreateGhost();

        // Establish a real GhostInfo ref so SetMaskBits has somewhere to deliver to
        // (mirrors TNLConnection.EnsureGhostsAndScopeAfterMapTransfer).
        connection.ActivateGhosting();
        connection.ObjectLocalScopeAlways(vehicle.Ghost);

        var ghostInfo = vehicle.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo, "Expected vehicle ghost to have a scoped GhostInfo ref.");
        ghostInfo.UpdateMask = 0; // clear the "everything dirty" state ObjectInScope seeds

        var position = new Vector3(1f, 2f, 3f);
        var rotation = new Quaternion(0f, 0.5f, 0f, 0.8660254f);
        var velocity = new Vector3(4f, 5f, 6f);

        vehicle.ApplyServerMove(position, rotation, velocity);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(position, vehicle.Position);
        Assert.AreEqual(rotation, vehicle.Rotation);
        Assert.AreEqual(velocity, vehicle.Velocity);
        Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask,
            "ApplyServerMove must dirty the ghost's PositionMask so the pose change is delivered.");
    }

    [TestMethod]
    public void Vehicle_ApplyServerMove_WithDt_FillsAngularAndSteeringFromYawChange()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(9005, true);
        // Face +Z then turn toward +X (about +90° yaw).
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), new Quaternion(0f, 0f, 0f, 1f), new Vector3(0f, 0f, 10f));
        var turned = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f);
        vehicle.ApplyServerMove(new Vector3(1f, 0f, 1f), turned, new Vector3(10f, 0f, 0f), dt: 0.1f);

        Assert.AreNotEqual(0f, vehicle.AngularVelocity.Y, "Yaw change over dt must set angular velocity Y.");
        Assert.AreNotEqual(0f, vehicle.Steering, "Yaw change over dt must set steering for pose pack.");
    }

    [TestMethod]
    public void Vehicle_ApplyServerMove_WithConstantSpeed_SetsCruiseThrottleNotZero()
    {
        // Ghost "Acceleration" is client throttle (+0x614), WriteSignedFloat 6-bit ∈ ~[-1,1].
        // d(speed)/dt is ~0 at cruise and freezes Havok between pose snaps.
        var vehicle = new Vehicle();
        vehicle.SetCoid(9008, true);
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), Quaternion.Default, new Vector3(0f, 0f, 12f), dt: 0.1f);
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 1.2f), Quaternion.Default, new Vector3(0f, 0f, 12f), dt: 0.1f);

        Assert.IsTrue(vehicle.Acceleration > 0.5f,
            $"Constant-speed path move must send cruise throttle, got {vehicle.Acceleration}");
    }

    [TestMethod]
    public void Vehicle_ApplyServerMove_WithZeroVelocity_ClearsThrottle()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(9009, true);
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), Quaternion.Default, new Vector3(0f, 0f, 12f), dt: 0.1f);
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), Quaternion.Default, new Vector3(0f, 0f, 0f), dt: 0.1f);

        Assert.AreEqual(0f, vehicle.Acceleration, 1e-3f, "Stopped vehicles must not leave cruise throttle on.");
    }

    [TestMethod]
    public void Vehicle_ApplyServerMove_ClientSidePathVisual_SkipsPositionMaskWhileIdlePatrol()
    {
        GhostVehicle.EnableClientSidePathVisual = true;
        try
        {
            var connection = new TNLConnection();
            connection.SetGhostFrom(true);
            connection.SetGhostTo(false);

            var vehicle = new Vehicle();
            vehicle.SetCoid(9006, true);
            vehicle.CoidCurrentPath = 42;
            vehicle.NpcAi = new NpcAiState { CombatState = HBAICombatState.IdlePatrol };
            vehicle.CreateGhost();
            connection.ActivateGhosting();
            connection.ObjectLocalScopeAlways(vehicle.Ghost);

            var ghostInfo = vehicle.Ghost.GetFirstObjectRef();
            Assert.IsNotNull(ghostInfo);
            ghostInfo.UpdateMask = 0;

            vehicle.ApplyServerMove(new Vector3(5f, 0f, 0f), Quaternion.Default, new Vector3(12f, 0f, 0f));
            NetObject.CollapseDirtyList();

            Assert.AreEqual(5f, vehicle.Position.X, "Server pose must still advance for combat authority.");
            Assert.AreEqual(0UL, ghostInfo.UpdateMask & GhostObject.PositionMask,
                "Idle path patrol must not ghost pose when client-side path visual is enabled.");
        }
        finally
        {
            GhostVehicle.EnableClientSidePathVisual = false;
        }
    }

    [TestMethod]
    public void Vehicle_ApplyServerMove_ClientSidePathVisual_DirtiesPoseWhenEngaged()
    {
        GhostVehicle.EnableClientSidePathVisual = true;
        try
        {
            var connection = new TNLConnection();
            connection.SetGhostFrom(true);
            connection.SetGhostTo(false);

            var vehicle = new Vehicle();
            vehicle.SetCoid(9007, true);
            vehicle.CoidCurrentPath = 42;
            vehicle.NpcAi = new NpcAiState { CombatState = HBAICombatState.Engage };
            vehicle.CreateGhost();
            connection.ActivateGhosting();
            connection.ObjectLocalScopeAlways(vehicle.Ghost);

            var ghostInfo = vehicle.Ghost.GetFirstObjectRef();
            Assert.IsNotNull(ghostInfo);
            ghostInfo.UpdateMask = 0;

            vehicle.ApplyServerMove(new Vector3(9f, 0f, 0f), Quaternion.Default, new Vector3(12f, 0f, 0f));
            NetObject.CollapseDirtyList();

            Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask,
                "Combat motion must still ghost pose even with client-side path visual.");
        }
        finally
        {
            GhostVehicle.EnableClientSidePathVisual = false;
        }
    }

    [TestMethod]
    public void Creature_ApplyServerMove_SetsTargetPosition()
    {
        var creature = new Creature();
        creature.SetCoid(9004, true);

        var position = new Vector3(10f, 0f, 5f);
        var rotation = new Quaternion(0f, 0f, 0f, 1f);
        var velocity = new Vector3(1f, 0f, 0f);
        var targetPosition = new Vector3(20f, 0f, 5f);

        // No ghost attached: must stay null-safe (NPCs may move before their ghost exists).
        creature.ApplyServerMove(position, rotation, velocity, targetPosition);

        Assert.AreEqual(position, creature.Position);
        Assert.AreEqual(rotation, creature.Rotation);
        Assert.AreEqual(velocity, creature.Velocity);
        Assert.AreEqual(targetPosition, creature.TargetPosition);
    }

    [TestMethod]
    public void Creature_NpcAi_AssignsOnPlainCreature()
    {
        var creature = new Creature();

        var state = new NpcAiState();
        creature.NpcAi = state;

        Assert.AreSame(state, creature.NpcAi);
    }

    [TestMethod]
    public void Character_NpcAi_SetNonNull_Throws()
    {
        var character = new Character();

        Assert.ThrowsException<InvalidOperationException>(() => character.NpcAi = new NpcAiState());
    }

    [TestMethod]
    public void Character_NpcAi_RemainsNullByDefault()
    {
        var character = new Character();

        Assert.IsNull(character.NpcAi);
    }

    private static SectorMap CreateTestMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_npc_safety_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };

        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}
