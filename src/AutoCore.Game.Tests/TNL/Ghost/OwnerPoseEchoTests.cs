using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// The owning client is authoritative for its own vehicle pose (C2S VehicleMoved). Echoing
/// PositionMask deltas back to the owner makes the client hard-snap to the server's
/// dead-reckoned pose (Vehicle_setDrivingInputs), which fights local physics at exactly the
/// moments client velocity changes abruptly — a ram impact reads as a split-second movement
/// freeze. Owner deltas must strip pose; foreign viewers must keep streaming it.
/// </summary>
[TestClass]
public class OwnerPoseEchoTests
{
    [TestCleanup]
    public void TearDown()
    {
        NetObject.PIsInitialUpdate = false;
    }

    [TestMethod]
    public void PackUpdate_OwnerDelta_StripsPositionMask_AndDoesNotKeepPoseDirty()
    {
        var connection = CreateOwnerScopedConnection(out var vehicle, 92_001);

        // Moving fast: ShouldStreamPose is true, so without the owner exclusion the pose
        // would both pack and re-arm keep-dirty every send period.
        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), Quaternion.Default, new Vector3(15f, 0f, 0f));
        Assert.IsTrue(GhostVehicle.ShouldStreamPose(vehicle));

        NetObject.PIsInitialUpdate = false;
        var posePacksBefore = GhostVehicle.PosePacksSinceDiag;

        var stream = new BitStream(new byte[8192], 8192);
        var ret = vehicle.Ghost.PackUpdate(connection, GhostObject.PositionMask | GhostVehicle.HealthMask, stream);

        Assert.AreEqual(posePacksBefore, GhostVehicle.PosePacksSinceDiag,
            "Owner delta must not write the pose block back to the owning client.");
        Assert.AreEqual(0UL, ret & GhostObject.PositionMask,
            "Owner connection must not keep PositionMask dirty (would resend empty pose updates forever).");
    }

    [TestMethod]
    public void PackUpdate_ForeignDelta_StillPacksPose_AndKeepsPoseDirtyWhileMoving()
    {
        CreateOwnerScopedConnection(out var vehicle, 92_002);

        vehicle.ApplyServerMove(new Vector3(0f, 0f, 0f), Quaternion.Default, new Vector3(15f, 0f, 0f));

        NetObject.PIsInitialUpdate = false;
        var posePacksBefore = GhostVehicle.PosePacksSinceDiag;

        // Null connection = not the owner's control connection (foreign viewer path).
        var stream = new BitStream(new byte[8192], 8192);
        var ret = vehicle.Ghost.PackUpdate(null, GhostObject.PositionMask, stream);

        Assert.AreEqual(posePacksBefore + 1, GhostVehicle.PosePacksSinceDiag,
            "Foreign viewers must still receive pose deltas.");
        Assert.AreNotEqual(0UL, ret & GhostObject.PositionMask,
            "Foreign keep-dirty streaming must be unaffected by the owner exclusion.");
    }

    /// <summary>
    /// Connection whose CurrentCharacter.CurrentVehicle is the ghosted vehicle — the same
    /// owner-control shape PackUpdate detects for OwnerCombatInitialMask on initials.
    /// </summary>
    private static TNLConnection CreateOwnerScopedConnection(out Vehicle vehicle, long coid)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.CreateGhost();

        var character = new Character();
        character.SetCoid(coid + 1, true);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetOwner(character);
        connection.CurrentCharacter = character;

        connection.ActivateGhosting();
        connection.ObjectLocalScopeAlways(vehicle.Ghost);

        var ghostInfo = vehicle.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo);
        ghostInfo.UpdateMask = 0;
        return connection;
    }
}
