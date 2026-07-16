using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Mutable per-wheel runtime state (client wheel stride 0xC0 subset).
/// Kept separate from any future <c>VehiclePhysicsInstance</c> container.
/// </summary>
[TestClass]
public class HkWheelRuntimeStateTests
{
    [TestMethod]
    public void Defaults_AirborneStyle_ZerosAndUnitScaling()
    {
        var w = new HkWheelRuntimeState();

        Assert.IsFalse(w.InContact);
        Assert.AreEqual(0f, w.CurrentLength);
        Assert.AreEqual(1f, w.Scaling);
        Assert.AreEqual(0f, w.ClosingSpeed);
        Assert.AreEqual(0f, w.SteerAngle);
        Assert.AreEqual(0f, w.DriveTorque);
        Assert.AreEqual(0f, w.Spin);
        Assert.AreEqual(0f, w.SpinAngle);
        Assert.AreEqual(0f, w.LongContactVel);
        Assert.AreEqual(0f, w.LongImpulse);
        Assert.AreEqual(0f, w.LatImpulse);
        Assert.AreEqual(0f, w.ContactNormalX);
        Assert.AreEqual(0f, w.ContactNormalY);
        Assert.AreEqual(0f, w.ContactNormalZ);
        Assert.AreEqual(0f, w.ContactPointX);
        Assert.AreEqual(0f, w.ContactPointY);
        Assert.AreEqual(0f, w.ContactPointZ);
    }

    [TestMethod]
    public void Fields_AreMutable_RoundTrip()
    {
        var w = new HkWheelRuntimeState
        {
            InContact = true,
            CurrentLength = 0.12f,
            Scaling = 1f / 0.3f,
            ClosingSpeed = -0.5f,
            SteerAngle = 0.25f,
            DriveTorque = 42f,
            Spin = 10.5f,
            SpinAngle = 2.25f,
            LongContactVel = 1.5f,
            LongImpulse = 0.4f,
            LatImpulse = -0.2f,
            ContactNormalX = 0.1f,
            ContactNormalY = 0.98f,
            ContactNormalZ = -0.1f,
            ContactPointX = 1f,
            ContactPointY = 2f,
            ContactPointZ = 3f,
        };

        Assert.IsTrue(w.InContact);
        Assert.AreEqual(0.12f, w.CurrentLength);
        Assert.AreEqual(1f / 0.3f, w.Scaling, 1e-6f);
        Assert.AreEqual(-0.5f, w.ClosingSpeed);
        Assert.AreEqual(0.25f, w.SteerAngle);
        Assert.AreEqual(42f, w.DriveTorque);
        Assert.AreEqual(10.5f, w.Spin);
        Assert.AreEqual(2.25f, w.SpinAngle);
        Assert.AreEqual(1.5f, w.LongContactVel);
        Assert.AreEqual(0.4f, w.LongImpulse);
        Assert.AreEqual(-0.2f, w.LatImpulse);
        Assert.AreEqual(0.1f, w.ContactNormalX);
        Assert.AreEqual(0.98f, w.ContactNormalY);
        Assert.AreEqual(-0.1f, w.ContactNormalZ);
        Assert.AreEqual(1f, w.ContactPointX);
        Assert.AreEqual(2f, w.ContactPointY);
        Assert.AreEqual(3f, w.ContactPointZ);
    }

    [TestMethod]
    public void ClearContact_SetsAirborneDefaults_PreservesSteerDriveSpin()
    {
        var w = new HkWheelRuntimeState
        {
            InContact = true,
            CurrentLength = -0.05f,
            Scaling = 5f,
            ClosingSpeed = -1.2f,
            SteerAngle = 0.4f,
            DriveTorque = 100f,
            Spin = 7f,
            SpinAngle = 0.33f,
            LongContactVel = -1.1f,
            ContactNormalX = 0.2f,
            ContactNormalY = 0.9f,
            ContactNormalZ = 0.1f,
            ContactPointX = 9f,
            ContactPointY = 8f,
            ContactPointZ = 7f,
        };

        const float restLen = 0.3f;
        // Miss path: normal = -downAxis; down = (0,-1,0) → normal (0,1,0)
        w.ClearContact(restLen, downDirX: 0f, downDirY: -1f, downDirZ: 0f);

        Assert.IsFalse(w.InContact);
        Assert.AreEqual(restLen, w.CurrentLength);
        Assert.AreEqual(1f, w.Scaling);
        Assert.AreEqual(0f, w.ClosingSpeed);
        Assert.AreEqual(0f, w.ContactNormalX, 1e-6f);
        Assert.AreEqual(1f, w.ContactNormalY, 1e-6f);
        Assert.AreEqual(0f, w.ContactNormalZ, 1e-6f);

        // Contact point left alone (hardpoint/world write is a separate path).
        Assert.AreEqual(9f, w.ContactPointX);
        Assert.AreEqual(8f, w.ContactPointY);
        Assert.AreEqual(7f, w.ContactPointZ);

        // Steer / drive / spin kinematics are not part of the miss contact block.
        Assert.AreEqual(0.4f, w.SteerAngle);
        Assert.AreEqual(100f, w.DriveTorque);
        Assert.AreEqual(7f, w.Spin);
        Assert.AreEqual(0.33f, w.SpinAngle);
        Assert.AreEqual(-1.1f, w.LongContactVel);
    }

    [TestMethod]
    public void ApplyContact_CopiesWheelContactFields()
    {
        var contact = new WheelContact(
            inContact: true,
            length: -0.05f,
            fraction: 0.5f,
            normalX: 0.05f,
            normalY: 0.99f,
            normalZ: -0.02f,
            closingSpeed: -0.3f);

        var w = new HkWheelRuntimeState { Scaling = 3.5f, SteerAngle = 0.1f };
        w.ApplyContact(contact, scaling: 1f / 0.3f);

        Assert.IsTrue(w.InContact);
        Assert.AreEqual(-0.05f, w.CurrentLength, 1e-6f);
        Assert.AreEqual(1f / 0.3f, w.Scaling, 1e-6f);
        Assert.AreEqual(-0.3f, w.ClosingSpeed, 1e-6f);
        Assert.AreEqual(0.05f, w.ContactNormalX, 1e-6f);
        Assert.AreEqual(0.99f, w.ContactNormalY, 1e-6f);
        Assert.AreEqual(-0.02f, w.ContactNormalZ, 1e-6f);
        Assert.AreEqual(0.1f, w.SteerAngle); // untouched
    }
}
