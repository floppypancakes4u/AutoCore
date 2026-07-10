using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.Entities;

using System;
using AutoCore.Database.World.Models;
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
