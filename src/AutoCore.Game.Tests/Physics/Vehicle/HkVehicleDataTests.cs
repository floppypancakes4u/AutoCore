using System.Collections.Generic;
using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;

[TestClass]
public class HkVehicleDataTests
{
    [TestCleanup]
    public void TearDown() => HkVehicleDataCache.Clear();

    private static VehicleSpecific SyntheticCar() => new()
    {
        WheelExistance = 0b001111, // 4 wheels
        WheelAxle = 2,
        VehicleFlags = (short)(1 << 2), // front steers
        WheelHardPoints = new[]
        {
            new Vector3(0.8f, -0.2f, 1.2f),
            new Vector3(-0.8f, -0.2f, 1.2f),
            new Vector3(0.8f, -0.2f, -1.2f),
            new Vector3(-0.8f, -0.2f, -1.2f),
            default,
            default,
        },
        WheelRadius = new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0f, 0f },
        WheelWidth = new[] { 0.2f, 0.2f, 0.2f, 0.2f, 0f, 0f },
        SuspensionLength = new FrontRear { Front = 0.3f, Rear = 0.32f },
        SuspensionStrength = new FrontRear { Front = 40f, Rear = 38f },
        SuspensionDampeningCoefficientCompression = new FrontRear { Front = 3f, Rear = 3f },
        SuspensionDampeningCoefficientExtension = new FrontRear { Front = 2f, Rear = 2f },
        BrakesMaxTorque = new FrontRear { Front = 100f, Rear = 80f },
        BrakesPedalInput = new FrontRear { Front = 0.1f, Rear = 0.1f },
        BrakesMinBlockTime = new FrontRear { Front = 0f, Rear = 0f },
        SteeringMaxAngle = 0.6f,
        SteeringFullSpeedLimit = 12f,
        AerodynamicsAirDensity = 1.2f,
        AerodynamicsFrontalArea = 2f,
        AerodynamicsDrag = 0.3f,
        AerodynamicsLift = -0.1f,
        AerodynamicsExtraGravity = new Vector3(0f, 0f, 0f),
        AVDNormalSpinDamping = 1.5f,
        AVDCollisionSpinDamping = 8f,
        AVDCollisionThreshold = 4f,
        RVInertiaRoll = 1.1f,
        RVInertiaPitch = 1.2f,
        RVInertiaYaw = 1.3f,
        RVFrictionEqualizer = 1f,
        WheelTorqueRatios = new FrontRear { Front = 0f, Rear = 1f },
        RearWheelFrictionScalar = 1.05f,
        CenterOfMassModifier = new Vector3(0f, -0.1f, 0.05f),
        NumberOfGears = 5,
        GearRatios = new[] { 3.5f, 2.1f, 1.4f, 1.0f, 0.8f },
        TransmissionRatio = 3.5f,
        ReverseGearRation = 3.2f,
        TorqueMax = 200,
        MinTorqueFactor = 0.2f,
        MaxTorqueFactor = 1.0f,
        SpeedLimiter = 40f,
        AbsoluteTopSpeed = 50f,
    };

    [TestMethod]
    public void FromVehicleSpecific_UnitMassAndInertia()
    {
        var d = HkVehicleData.FromVehicleSpecific(SyntheticCar(), cbid: 1001);
        Assert.AreEqual(1f, d.Mass);
        Assert.AreEqual(1f, d.InvMass);
        Assert.AreEqual(1.1f, d.InertiaRoll, 1e-5f);
        Assert.AreEqual(1.2f, d.InertiaPitch, 1e-5f);
        Assert.AreEqual(1.3f, d.InertiaYaw, 1e-5f);
        Assert.AreEqual(1001, d.Cbid);
    }

    [TestMethod]
    public void FromVehicleSpecific_FourWheels_FrontRearSplit()
    {
        var d = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        Assert.AreEqual(4, d.WheelCount);
        Assert.AreEqual(2, d.FrontWheelCount);
        Assert.IsTrue(d.Wheels[0].DoesSteer);
        Assert.IsTrue(d.Wheels[1].DoesSteer);
        Assert.IsFalse(d.Wheels[2].DoesSteer);
        Assert.IsFalse(d.Wheels[3].DoesSteer);
        Assert.IsFalse(d.Wheels[0].IsRear);
        Assert.IsTrue(d.Wheels[2].IsRear);
        Assert.AreEqual(0.3f, d.Wheels[0].SuspensionRestLength);
        Assert.AreEqual(0.32f, d.Wheels[2].SuspensionRestLength);
    }

    [TestMethod]
    public void FromVehicleSpecific_MapsAeroAndSteering()
    {
        var d = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        Assert.AreEqual(0.6f, d.SteeringMaxAngle);
        Assert.AreEqual(12f, d.SteeringFullSpeedLimit);
        Assert.AreEqual(1.2f, d.AirDensity);
        Assert.AreEqual(2f, d.FrontalArea);
        Assert.AreEqual(0.3f, d.DragCoefficient);
        Assert.AreEqual(-0.1f, d.LiftCoefficient);
        Assert.AreEqual(1.5f, d.AvdNormalSpinDamping);
        Assert.AreEqual(8f, d.AvdCollisionSpinDamping);
    }

    [TestMethod]
    public void FromVehicleSpecific_EngineFactors_Factor0IsMinTorque_TrivialLutOor()
    {
        var d = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        Assert.IsTrue(d.EngineEnabled);
        Assert.AreEqual(0, d.EngineRows);
        Assert.AreEqual(0, d.EngineCols);
        Assert.AreEqual(HkVehicleData.EngineFactorCount, d.EngineFactors.Count);
        Assert.AreEqual(0.2f, d.EngineFactors[0], 1e-5f); // MinTorqueFactor
        Assert.AreEqual(1.0f, d.EngineFactors[7], 1e-5f); // MaxTorqueFactor
        Assert.AreEqual(d.MinTorqueFactor, d.EngineFactors[0], 1e-5f);

        // Trivial 0×0 LUT → Evaluate always OOR → factors[0].
        float f = TorqueCurve2D.Evaluate(
            d.EngineEnabled, d.EngineRows, d.EngineCols, d.EngineRangeScale,
            d.EngineFactorsArray, d.EngineLutArray, 999f, -999f);
        Assert.AreEqual(0.2f, f, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_NullAirDensityOverride_UsesVehSpec()
    {
        var d = HkVehicleData.FromVehicleSpecific(SyntheticCar(), airDensityOverride: null);
        Assert.AreEqual(1.2f, d.AirDensity, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_AirDensityOverride_ReplacesVehSpecValue()
    {
        var d = HkVehicleData.FromVehicleSpecific(SyntheticCar(), airDensityOverride: 0.5f);
        Assert.AreEqual(0.5f, d.AirDensity, 1e-5f);
        // Other aero params remain from VehicleSpecific.
        Assert.AreEqual(2f, d.FrontalArea, 1e-5f);
        Assert.AreEqual(0.3f, d.DragCoefficient, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_AirDensityOverride_ScalesAeroDrag()
    {
        // ActionSim passes data.AirDensity into HkVehicleAerodynamics — override must scale drag.
        const float v = 20f;
        var baseData = HkVehicleData.FromVehicleSpecific(SyntheticCar());
        var halfRho = HkVehicleData.FromVehicleSpecific(SyntheticCar(), airDensityOverride: baseData.AirDensity * 0.5f);

        var (fx0, fy0, fz0) = HkVehicleAerodynamics.ComputeForce(
            baseData.AirDensity, baseData.FrontalArea, baseData.DragCoefficient, liftCoefficient: 0f,
            0f, 0f, 0f,
            0f, 0f, 1f, 0f, 1f, 0f,
            0f, 0f, v, mass: 1f);
        var (fx1, fy1, fz1) = HkVehicleAerodynamics.ComputeForce(
            halfRho.AirDensity, halfRho.FrontalArea, halfRho.DragCoefficient, liftCoefficient: 0f,
            0f, 0f, 0f,
            0f, 0f, 1f, 0f, 1f, 0f,
            0f, 0f, v, mass: 1f);

        Assert.AreEqual(fz0 * 0.5f, fz1, 1e-4f);
        Assert.AreEqual(0f, fx1, 1e-5f);
        Assert.AreEqual(0f, fy1, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_RearFrictionScalar_AppliedToRearOnly()
    {
        var vs = SyntheticCar();
        vs.RearWheelFrictionScalar = 1.05f;
        vs.RVFrictionEqualizer = 1f;
        vs.WheelTorqueRatios = new FrontRear { Front = 0.2f, Rear = 0.8f };

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.AreEqual(1.05f, d.RearWheelFrictionScalar, 1e-5f);
        Assert.AreEqual(1f, d.FrictionEqualizer, 1e-5f);

        // Front: friction = equalizer only; torque ratio unchanged.
        Assert.IsFalse(d.Wheels[0].IsRear);
        Assert.AreEqual(1f, d.Wheels[0].Friction, 1e-5f);
        Assert.AreEqual(0.2f, d.Wheels[0].TorqueRatio, 1e-5f);

        // Rear: friction and torque ratio scaled by RearWheelFrictionScalar.
        Assert.IsTrue(d.Wheels[2].IsRear);
        Assert.AreEqual(1.05f, d.Wheels[2].Friction, 1e-5f);
        Assert.AreEqual(0.8f * 1.05f, d.Wheels[2].TorqueRatio, 1e-5f);
        Assert.AreEqual(d.Wheels[2].Friction, d.Wheels[3].Friction, 1e-5f);
        Assert.AreEqual(d.Wheels[2].TorqueRatio, d.Wheels[3].TorqueRatio, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_TorqueRatio_CarriesFrontRearSplit_NotContactGate()
    {
        // C5: tRatio stays on setup for calcWheelTorque; wheel+0x88 is runtime contact gate
        // (not stored on HkWheelSetup). Rear tRatio still includes RearWheelFrictionScalar fold.
        var vs = SyntheticCar();
        vs.WheelTorqueRatios = new FrontRear { Front = 0.25f, Rear = 0.75f };
        vs.RearWheelFrictionScalar = 1.1f;

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.IsFalse(d.Wheels[0].IsRear);
        Assert.AreEqual(0.25f, d.Wheels[0].TorqueRatio, 1e-5f);
        Assert.AreEqual(d.Wheels[0].TorqueRatio, d.Wheels[1].TorqueRatio, 1e-5f);

        Assert.IsTrue(d.Wheels[2].IsRear);
        Assert.AreEqual(0.75f * 1.1f, d.Wheels[2].TorqueRatio, 1e-5f);
        Assert.AreEqual(d.Wheels[2].TorqueRatio, d.Wheels[3].TorqueRatio, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_TorqueRatio_UndrivenFrontIsZero()
    {
        var vs = SyntheticCar();
        vs.WheelTorqueRatios = new FrontRear { Front = 0f, Rear = 1f };
        vs.RearWheelFrictionScalar = 1.05f;

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.AreEqual(0f, d.Wheels[0].TorqueRatio, 1e-5f);
        Assert.AreEqual(0f, d.Wheels[1].TorqueRatio, 1e-5f);
        Assert.AreEqual(1.05f, d.Wheels[2].TorqueRatio, 1e-5f);
        Assert.AreEqual(1.05f, d.Wheels[3].TorqueRatio, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_MapsCenterOfMassModifier()
    {
        var vs = SyntheticCar();
        vs.CenterOfMassModifier = new Vector3(0.1f, -0.2f, 0.05f);

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.AreEqual(0.1f, d.CenterOfMassModifierX, 1e-5f);
        Assert.AreEqual(-0.2f, d.CenterOfMassModifierY, 1e-5f);
        Assert.AreEqual(0.05f, d.CenterOfMassModifierZ, 1e-5f);
    }

    [TestMethod]
    public void ApplyComOffset_HardpointRelativeToCom()
    {
        // Lever arm r = hardpoint_cs − CenterOfMassModifier (Phase 4 torque arms).
        var vs = SyntheticCar();
        vs.CenterOfMassModifier = new Vector3(0f, -0.1f, 0.05f);
        var d = HkVehicleData.FromVehicleSpecific(vs);

        // Wheel 0 hardpoint (0.8, -0.2, 1.2)
        d.ApplyComOffset(0.8f, -0.2f, 1.2f, out var rx, out var ry, out var rz);
        Assert.AreEqual(0.8f - 0f, rx, 1e-5f);
        Assert.AreEqual(-0.2f - (-0.1f), ry, 1e-5f);
        Assert.AreEqual(1.2f - 0.05f, rz, 1e-5f);
    }

    [TestMethod]
    public void ApplyComOffset_ZeroModifier_Identity()
    {
        var vs = SyntheticCar();
        vs.CenterOfMassModifier = new Vector3(0f, 0f, 0f);
        var d = HkVehicleData.FromVehicleSpecific(vs);

        d.ApplyComOffset(1f, 2f, 3f, out var rx, out var ry, out var rz);
        Assert.AreEqual(1f, rx, 1e-5f);
        Assert.AreEqual(2f, ry, 1e-5f);
        Assert.AreEqual(3f, rz, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_EngineFactors_FromMinMax_TrivialLutOor()
    {
        var vs = SyntheticCar();
        vs.MinTorqueFactor = 0.2f;
        vs.MaxTorqueFactor = 1.0f;

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.IsTrue(d.EngineEnabled);
        Assert.AreEqual(0, d.EngineRows);
        Assert.AreEqual(0, d.EngineCols);
        Assert.AreEqual(8, d.EngineFactorsArray.Length);
        Assert.AreEqual(0.2f, d.EngineFactors[0], 1e-5f);
        Assert.AreEqual(1.0f, d.EngineFactors[7], 1e-5f);
        // Trivial dims → Evaluate always OOR → factors[0]
        float f = TorqueCurve2D.Evaluate(
            d.EngineEnabled, d.EngineRows, d.EngineCols, d.EngineRangeScale,
            d.EngineFactorsArray, d.EngineLutArray, 100f, 50f);
        Assert.AreEqual(0.2f, f, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_RearFrictionScalar_ZeroFallsBackToOne()
    {
        var vs = SyntheticCar();
        vs.RearWheelFrictionScalar = 0f;
        vs.RVFrictionEqualizer = 0.9f;
        vs.WheelTorqueRatios = new FrontRear { Front = 0f, Rear = 1f };

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.AreEqual(1f, d.RearWheelFrictionScalar, 1e-5f);
        Assert.AreEqual(0.9f, d.Wheels[0].Friction, 1e-5f);
        Assert.AreEqual(0.9f, d.Wheels[2].Friction, 1e-5f);
        Assert.AreEqual(1f, d.Wheels[2].TorqueRatio, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_SteerMult_ScalesAngleAndSpeedLimit()
    {
        var vs = SyntheticCar();
        vs.SteeringMaxAngle = 0.6f;
        vs.SteeringFullSpeedLimit = 12f;

        var d = HkVehicleData.FromVehicleSpecific(
            vs,
            steerAngleMult: 1.5f,
            steerSpeedMult: 0.5f);

        Assert.AreEqual(0.6f * 1.5f, d.SteeringMaxAngle, 1e-5f);
        Assert.AreEqual(12f * 0.5f, d.SteeringFullSpeedLimit, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_EmptyWheels_FallsBackToFourSlots()
    {
        var vs = SyntheticCar();
        vs.WheelExistance = 0;
        vs.WheelHardPoints = new Vector3[6]; // all zero → no hardpoints
        vs.WheelRadius = new[] { 0f, 0f, 0f, 0f, 0f, 0f };
        vs.WheelWidth = new[] { 0f, 0f, 0f, 0f, 0f, 0f };
        vs.WheelAxle = 0;

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.AreEqual(4, d.WheelCount);
        // WheelAxle==0 → frontCount = max(1, (count+1)/2) = 2
        Assert.AreEqual(2, d.FrontWheelCount);
        Assert.IsFalse(d.Wheels[0].IsRear);
        Assert.IsFalse(d.Wheels[1].IsRear);
        Assert.IsTrue(d.Wheels[2].IsRear);
        Assert.IsTrue(d.Wheels[3].IsRear);
        // Present array zeros are kept (0.35 default only when radius array missing/short).
        Assert.AreEqual(0f, d.Wheels[0].Radius, 1e-5f);
        Assert.AreEqual(0f, d.Wheels[3].Radius, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_EmptyWheels_NullRadiusUsesDefault()
    {
        var vs = SyntheticCar();
        vs.WheelExistance = 0;
        vs.WheelHardPoints = new Vector3[6];
        vs.WheelRadius = null;
        vs.WheelWidth = null;
        vs.WheelAxle = 0;

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.AreEqual(4, d.WheelCount);
        Assert.AreEqual(0.35f, d.Wheels[0].Radius, 1e-5f);
        Assert.AreEqual(0.2f, d.Wheels[0].Width, 1e-5f);
        Assert.AreEqual(0.35f, d.Wheels[3].Radius, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_WheelMaskZero_UsesHardpointsAndRadius()
    {
        var vs = SyntheticCar();
        vs.WheelExistance = 0; // mask empty → discover from hardpoints/radius
        // SyntheticCar already has 4 hardpoints + radii

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.AreEqual(4, d.WheelCount);
        Assert.AreEqual(0.4f, d.Wheels[0].Radius, 1e-5f);
        Assert.AreEqual(1.2f, d.Wheels[0].HardpointZ, 1e-5f);
        Assert.AreEqual(-1.2f, d.Wheels[2].HardpointZ, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_MapsAllAvdFields()
    {
        var vs = SyntheticCar();
        vs.AVDNormalSpinDamping = 1.5f;
        vs.AVDCollisionSpinDamping = 8f;
        vs.AVDCollisionThreshold = 4f;

        var d = HkVehicleData.FromVehicleSpecific(vs);

        Assert.AreEqual(1.5f, d.AvdNormalSpinDamping, 1e-5f);
        Assert.AreEqual(8f, d.AvdCollisionSpinDamping, 1e-5f);
        Assert.AreEqual(4f, d.AvdCollisionThreshold, 1e-5f);
    }

    [TestMethod]
    public void FromVehicleSpecific_AvdSpinMult_ScalesDampingNotThreshold()
    {
        var vs = SyntheticCar();
        vs.AVDNormalSpinDamping = 1.5f;
        vs.AVDCollisionSpinDamping = 8f;
        vs.AVDCollisionThreshold = 4f;

        var d = HkVehicleData.FromVehicleSpecific(vs, avdSpinMult: 2f);

        Assert.AreEqual(1.5f * 2f, d.AvdNormalSpinDamping, 1e-5f);
        Assert.AreEqual(8f * 2f, d.AvdCollisionSpinDamping, 1e-5f);
        Assert.AreEqual(4f, d.AvdCollisionThreshold, 1e-5f);
    }

    [TestMethod]
    public void Cache_GetOrCompute_ReusesSameInstanceKey()
    {
        var vs = SyntheticCar();
        var a = HkVehicleDataCache.GetOrCompute(42, vs);
        var b = HkVehicleDataCache.GetOrCompute(42, vs);
        Assert.AreSame(a, b);
        Assert.IsTrue(HkVehicleDataCache.TryGet(42, out var c));
        Assert.AreSame(a, c);
    }

    [TestMethod]
    public void Cache_BuildFromCloneBases_IndexesByCbid_UsesGravity()
    {
        var cv = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cv.VehicleSpecific = SyntheticCar();
        cv.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 1001 };

        Assert.AreEqual(1, HkVehicleDataCache.BuildFromCloneBases(
            new Dictionary<int, CloneBase> { [1001] = cv }, gravityY: -12.5f));
        Assert.IsTrue(HkVehicleDataCache.TryGet(1001, out var data));
        Assert.AreEqual(1001, data.Cbid);
        Assert.AreEqual(-12.5f, data.GravityY, 1e-5f);
        Assert.AreEqual(4, data.WheelCount);
    }

    [TestMethod]
    public void Cache_BuildFromCloneBases_UsesAirDensityOverride()
    {
        var cv = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cv.VehicleSpecific = SyntheticCar();
        cv.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 1002 };

        Assert.AreEqual(1, HkVehicleDataCache.BuildFromCloneBases(
            new Dictionary<int, CloneBase> { [1002] = cv },
            gravityY: -9.81f,
            airDensityOverride: 0.8f));
        Assert.IsTrue(HkVehicleDataCache.TryGet(1002, out var data));
        Assert.AreEqual(0.8f, data.AirDensity, 1e-5f);
    }

    [TestMethod]
    public void Cache_BuildFromCloneBases_NullAirDensityOverride_UsesVehSpec()
    {
        var cv = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cv.VehicleSpecific = SyntheticCar();
        cv.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 1003 };

        Assert.AreEqual(1, HkVehicleDataCache.BuildFromCloneBases(
            new Dictionary<int, CloneBase> { [1003] = cv },
            gravityY: -9.81f,
            airDensityOverride: null));
        Assert.IsTrue(HkVehicleDataCache.TryGet(1003, out var data));
        Assert.AreEqual(1.2f, data.AirDensity, 1e-5f);
    }

    [TestMethod]
    public void Cache_GetOrCompute_UsesAirDensityOverride_WhenMiss()
    {
        var vs = SyntheticCar();
        var data = HkVehicleDataCache.GetOrCompute(77, vs, airDensityOverride: 2.4f);
        Assert.AreEqual(2.4f, data.AirDensity, 1e-5f);
        // Cached entry reuses same instance (override only applies on miss).
        var again = HkVehicleDataCache.GetOrCompute(77, vs, airDensityOverride: 0.1f);
        Assert.AreSame(data, again);
        Assert.AreEqual(2.4f, again.AirDensity, 1e-5f);
    }

    [TestMethod]
    public void Cache_BuildFromCloneBases_NullOrEmpty_ReturnsZero()
    {
        Assert.AreEqual(0, HkVehicleDataCache.BuildFromCloneBases(null));
        Assert.AreEqual(0, HkVehicleDataCache.BuildFromCloneBases(new Dictionary<int, CloneBase>()));
        Assert.AreEqual(0, HkVehicleDataCache.Count);
    }
}
