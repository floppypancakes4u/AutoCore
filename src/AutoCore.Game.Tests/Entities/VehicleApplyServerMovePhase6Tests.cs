using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Phase 6: thr/steer/sharp/angVel from the physics (or kinematic) mover must reach ghost-facing fields.
/// </summary>
[TestClass]
public class VehicleApplyServerMovePhase6Tests
{
    [TestMethod]
    public void ApplyServerMove_SharpTurnNonZero_SetsHandbreakFlag()
    {
        var v = new Vehicle();
        v.VehicleFlags = 0;

        v.ApplyServerMove(
            new Vector3(0, 1, 0),
            Quaternion.Default,
            new Vector3(0, 0, 5),
            dt: 1f / 60f,
            driveThrottle: -0.5f,
            driveSteering: 0.2f,
            sharpTurn: 1);

        Assert.AreEqual(VehicleMovedFlags.Handbreak, v.VehicleFlags & VehicleMovedFlags.Handbreak);
        Assert.AreEqual(-0.5f, v.Acceleration, 1e-5f);
        Assert.AreEqual(0.2f, v.Steering, 1e-5f);
    }

    [TestMethod]
    public void ApplyServerMove_SharpTurnZero_ClearsHandbreakFlag()
    {
        var v = new Vehicle();
        v.VehicleFlags = VehicleMovedFlags.Handbreak | VehicleMovedFlags.Corpse;

        v.ApplyServerMove(
            new Vector3(0, 1, 0),
            Quaternion.Default,
            default,
            dt: 1f / 60f,
            driveThrottle: 0f,
            driveSteering: 0f,
            sharpTurn: 0);

        Assert.AreEqual(0, (int)(v.VehicleFlags & VehicleMovedFlags.Handbreak));
        Assert.AreEqual(VehicleMovedFlags.Corpse, v.VehicleFlags & VehicleMovedFlags.Corpse);
    }

    [TestMethod]
    public void ApplyServerMove_SimAngularVelocity_PreferredOverEstimate()
    {
        var v = new Vehicle();
        v.ApplyServerMove(new Vector3(0, 1, 0), Quaternion.Default, default, 0f);

        var simOmega = new Vector3(0.1f, 0.5f, -0.2f);
        // Large rotation delta would produce a different estimate if used.
        var nextRot = new Quaternion(0f, 0.7071f, 0f, 0.7071f);
        v.ApplyServerMove(
            new Vector3(0, 1, 1),
            nextRot,
            new Vector3(0, 0, 2),
            dt: 1f / 60f,
            driveThrottle: -1f,
            driveSteering: 0f,
            sharpTurn: 0,
            angularVelocity: simOmega);

        Assert.AreEqual(simOmega.X, v.AngularVelocity.X, 1e-5f);
        Assert.AreEqual(simOmega.Y, v.AngularVelocity.Y, 1e-5f);
        Assert.AreEqual(simOmega.Z, v.AngularVelocity.Z, 1e-5f);
        Assert.AreEqual(-1f, v.Acceleration, 1e-5f);
    }

    [TestMethod]
    public void ApplyServerMove_NullSharpTurn_DoesNotTouchFlags()
    {
        var v = new Vehicle();
        v.VehicleFlags = VehicleMovedFlags.Handbreak;

        v.ApplyServerMove(
            new Vector3(1, 2, 3),
            Quaternion.Default,
            default,
            dt: 1f / 60f,
            driveThrottle: 0.1f,
            driveSteering: null,
            sharpTurn: null);

        Assert.AreEqual(VehicleMovedFlags.Handbreak, v.VehicleFlags & VehicleMovedFlags.Handbreak);
    }
}
