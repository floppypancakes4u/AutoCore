using System.Runtime.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Map;

/// <summary>
/// Tight numeric oracles for coverage + mutation on PathCurvature, TerrainContactPlane,
/// NpcVehicleDriveController, and VehicleGroundMetricsCache.
/// </summary>
[TestClass]
public class NpcDriveMutationOracleTests
{
    [TestCleanup]
    public void TearDown()
    {
        NpcVehicleDriveController.Enabled = false;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = MathF.PI * 1.5f;
        NpcVehicleDriveController.MaxAcceleration = 50f;
        NpcVehicleDriveController.MaxBrake = 60f;
        NpcVehicleDriveController.LookAheadDistance = 28f;
        NpcVehicleDriveController.MaxPathDrift = 6f;
        VehicleGroundMetricsCache.Clear();
        VehicleGroundMetricsCache.RideHeightScale = 1f;
        VehicleGroundMetricsCache.MaxRideHeight = 1.25f;
        TerrainContactPlane.DefaultHalfLength = 4.0f;
        TerrainContactPlane.DefaultHalfWidth = 1.8f;
        TerrainContactPlane.MaxPitchRollRadians = 0.65f;
        TerrainContactPlane.DefaultGroundClearance = 0f;
    }

    // ── PathCurvature ────────────────────────────────────────────────────────

    [TestMethod]
    public void PathCurvature_Radius_AsymmetricOffsetTriangle_ExactCircumradius()
    {
        // 3-4-5 right triangle NOT at origin — kills + vs − on edge vectors.
        var a = new Vector3(10f, 7f, 20f);
        var b = new Vector3(14f, 7f, 20f);
        var c = new Vector3(10f, 7f, 23f);
        Assert.AreEqual(2.5f, PathCurvature.Radius(a, b, c), 0.001f);

        // Different orientation / order
        Assert.AreEqual(2.5f, PathCurvature.Radius(c, a, b), 0.001f);
        Assert.AreEqual(2.5f, PathCurvature.Radius(b, c, a), 0.001f);
    }

    [TestMethod]
    public void PathCurvature_Radius_IgnoresY_UsesXZOnly()
    {
        var rFlat = PathCurvature.Radius(
            new Vector3(0, 0, 0), new Vector3(4, 0, 0), new Vector3(0, 0, 3));
        var rTall = PathCurvature.Radius(
            new Vector3(0, 100, 0), new Vector3(4, -50, 0), new Vector3(0, 999, 3));
        Assert.AreEqual(rFlat, rTall, 0.001f);
        Assert.AreEqual(2.5f, rTall, 0.001f);
    }

    [TestMethod]
    public void PathCurvature_Radius_NearlyCollinear_IsInfinite()
    {
        // Cross product near zero → infinite radius
        var r = PathCurvature.Radius(
            new Vector3(0, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(20, 0, 1e-8f));
        Assert.IsTrue(float.IsPositiveInfinity(r) || r > 1e5f, $"got {r}");
    }

    [TestMethod]
    public void PathCurvature_SpeedScale_ExactLinearRamp()
    {
        // floor
        Assert.AreEqual(0.25f, PathCurvature.SpeedScale(0f), 1e-5f);
        Assert.AreEqual(0.25f, PathCurvature.SpeedScale(1e-4f), 1e-5f);
        // mid: t = 15/30 = 0.5 → 0.25 + 0.75*0.5 = 0.625
        Assert.AreEqual(0.625f, PathCurvature.SpeedScale(15f), 1e-5f);
        // t = 10/30 → 0.25 + 0.25 = 0.5
        Assert.AreEqual(0.5f, PathCurvature.SpeedScale(10f), 1e-5f);
        // t = 20/30 → 0.25 + 0.5 = 0.75
        Assert.AreEqual(0.75f, PathCurvature.SpeedScale(20f), 1e-5f);
        // at and above threshold
        Assert.AreEqual(1f, PathCurvature.SpeedScale(30f), 1e-5f);
        Assert.AreEqual(0.25f + 0.75f * (29.999f / 30f), PathCurvature.SpeedScale(29.999f), 1e-5f);
        Assert.AreEqual(1f, PathCurvature.SpeedScale(float.PositiveInfinity), 1e-5f);
        Assert.AreEqual(1f, PathCurvature.SpeedScale(float.NaN), 1e-5f);
        Assert.AreEqual(1f, PathCurvature.SpeedScale(float.NegativeInfinity), 1e-5f);
    }

    [TestMethod]
    public void PathCurvature_SpeedScale_JustBelowFull_IsBelowOne()
    {
        var s = PathCurvature.SpeedScale(29f);
        Assert.IsTrue(s < 1f && s > 0.9f, $"got {s}");
        // exact: 0.25 + 0.75*(29/30) = 0.25 + 0.725 = 0.975
        Assert.AreEqual(0.25f + 0.75f * (29f / 30f), s, 1e-5f);
    }

    // ── VehicleGroundMetricsCache ────────────────────────────────────────────

    [TestMethod]
    public void Metrics_NullArrays_Defaults()
    {
        var m = VehicleGroundMetricsCache.Compute(new VehicleSpecific
        {
            WheelRadius = null,
            WheelHardPoints = null,
        });
        Assert.AreEqual(0f, m.MeanWheelRadius, 1e-5f);
        Assert.AreEqual(0f, m.MeanHardpointY, 1e-5f);
        Assert.AreEqual(0f, m.ChassisHeightAboveTerrain, 1e-5f);
        Assert.AreEqual(TerrainContactPlane.DefaultHalfLength, m.HalfLength, 1e-5f);
        Assert.AreEqual(TerrainContactPlane.DefaultHalfWidth, m.HalfWidth, 1e-5f);
        Assert.AreEqual(0, m.WheelRadiusCount);
        Assert.AreEqual(0, m.HardpointCount);
    }

    [TestMethod]
    public void Metrics_RadiusFilter_ExcludesOutOfRange()
    {
        var m = VehicleGroundMetricsCache.Compute(new VehicleSpecific
        {
            // 0.05 excluded (not > 0.05), 5 excluded (not < 5), 0.06 and 4.9 kept
            WheelRadius = new[] { 0.05f, 0.06f, 5f, 4.9f, -1f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 2f),
                new Vector3(-1f, 0f, 2f),
                new Vector3(1f, 0f, -2f),
                new Vector3(-1f, 0f, -2f),
            },
        });
        Assert.AreEqual(2, m.WheelRadiusCount);
        Assert.AreEqual((0.06f + 4.9f) / 2f, m.MeanWheelRadius, 1e-4f);
    }

    [TestMethod]
    public void Metrics_SkipsZeroHardpoints_UsesMeanOfRest()
    {
        var m = VehicleGroundMetricsCache.Compute(new VehicleSpecific
        {
            WheelRadius = new[] { 0.5f, 0.5f },
            WheelHardPoints = new[]
            {
                default, // skip
                new Vector3(0f, 0f, 0f), // skip (all near zero)
                new Vector3(1.5f, -0.2f, 3f),
                new Vector3(-1.5f, -0.2f, -3f),
            },
        });
        Assert.AreEqual(2, m.HardpointCount);
        Assert.AreEqual(-0.2f, m.MeanHardpointY, 1e-4f);
        // ride = 0.5 - (-0.2) = 0.7
        Assert.AreEqual(0.7f, m.ChassisHeightAboveTerrain, 1e-4f);
        Assert.AreEqual(3f, m.HalfLength, 1e-3f);
        Assert.AreEqual(1.5f, m.HalfWidth, 1e-3f);
    }

    [TestMethod]
    public void Metrics_RawNegative_ClampedToZero()
    {
        // hardpoint Y above radius → raw negative → 0
        var m = VehicleGroundMetricsCache.Compute(new VehicleSpecific
        {
            WheelRadius = new[] { 0.2f, 0.2f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0.5f, 1f),
                new Vector3(-1f, 0.5f, -1f),
            },
        });
        Assert.AreEqual(0f, m.ChassisHeightAboveTerrain, 1e-5f);
    }

    [TestMethod]
    public void Metrics_SmallFootprint_UsesDefaults()
    {
        // maxAbsZ <= 0.5 and maxAbsX <= 0.3 → defaults
        var m = VehicleGroundMetricsCache.Compute(new VehicleSpecific
        {
            WheelRadius = new[] { 0.4f },
            WheelHardPoints = new[]
            {
                new Vector3(0.2f, 0f, 0.4f),
                new Vector3(-0.2f, 0f, -0.4f),
            },
        });
        Assert.AreEqual(TerrainContactPlane.DefaultHalfLength, m.HalfLength, 1e-4f);
        Assert.AreEqual(TerrainContactPlane.DefaultHalfWidth, m.HalfWidth, 1e-4f);
    }

    [TestMethod]
    public void Metrics_LargeFootprint_Clamped()
    {
        var m = VehicleGroundMetricsCache.Compute(new VehicleSpecific
        {
            WheelRadius = new[] { 0.5f },
            WheelHardPoints = new[]
            {
                new Vector3(10f, 0f, 20f),
                new Vector3(-10f, 0f, -20f),
            },
        });
        Assert.AreEqual(8f, m.HalfLength, 1e-4f); // clamp max 8
        Assert.AreEqual(4f, m.HalfWidth, 1e-4f);  // clamp max 4
    }

    [TestMethod]
    public void Metrics_Build_SkipsNonVehicle_AndAveragesRide()
    {
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.5f, 0.5f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 2f), new Vector3(-1f, 0f, 2f),
                new Vector3(1f, 0f, -2f), new Vector3(-1f, 0f, -2f),
            },
        };
        var cv = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cv.VehicleSpecific = vs;
        cv.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 11 };

        // Non-vehicle entry must be skipped (not throw)
        var plain = (CloneBaseObject)FormatterServices.GetUninitializedObject(typeof(CloneBaseObject));
        plain.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 22 };

        var n = VehicleGroundMetricsCache.BuildFromCloneBases(new Dictionary<int, CloneBase>
        {
            [11] = cv,
            [22] = plain,
        });
        Assert.AreEqual(1, n);
        Assert.AreEqual(1, VehicleGroundMetricsCache.Count);
        Assert.IsFalse(VehicleGroundMetricsCache.TryGet(22, out _));
        Assert.IsTrue(VehicleGroundMetricsCache.TryGet(11, out var m));
        Assert.AreEqual(0.5f, m.ChassisHeightAboveTerrain, 1e-4f);
    }

    // ── TerrainContactPlane ──────────────────────────────────────────────────

    [TestMethod]
    public void Terrain_NullSample_ReturnsFalse()
    {
        var ok = TerrainContactPlane.TryAlign(
            new Vector3(1, 2, 3), 0.25f, sample: null,
            out var g, out var r);
        Assert.IsFalse(ok);
        Assert.AreEqual(2f, g.Y, 1e-5f);
        Assert.AreEqual(TerrainContactPlane.YawOnly(0.25f).Y, r.Y, 1e-4f);
    }

    [TestMethod]
    public void Terrain_DefaultClearance_WhenNegativeArg()
    {
        TerrainContactPlane.DefaultGroundClearance = 0.33f;
        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) => { y = 10f; return true; },
            out var g, out _,
            halfLength: 2f, halfWidth: 1f, groundClearance: -1f));
        Assert.AreEqual(10.33f, g.Y, 1e-4f);
    }

    [TestMethod]
    public void Terrain_ExplicitClearance_OverridesDefault()
    {
        TerrainContactPlane.DefaultGroundClearance = 9f;
        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) => { y = 1f; return true; },
            out var g, out _,
            halfLength: 2f, halfWidth: 1f, groundClearance: 0.25f));
        Assert.AreEqual(1.25f, g.Y, 1e-4f);
    }

    [TestMethod]
    public void Terrain_UsesDefaultHalfDims_WhenNonPositive()
    {
        TerrainContactPlane.DefaultHalfLength = 3f;
        TerrainContactPlane.DefaultHalfWidth = 1.1f;
        float? frontZ = null;
        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f, // +Z forward
            sample: (float x, float z, out float y) =>
            {
                if (z > 0.1f) frontZ = z;
                y = 0f;
                return true;
            },
            out _, out _,
            halfLength: 0f, halfWidth: -2f, groundClearance: 0f));
        Assert.IsNotNull(frontZ);
        Assert.AreEqual(3f, frontZ.Value, 0.05f); // DefaultHalfLength sample
    }

    [TestMethod]
    public void Terrain_KnownSlope_ExactPitchOracle()
    {
        // Y = 0.25 * z, yaw 0 (+Z), hl=4 → yF-yB = 0.25*8 = 2 over span 8 → grade 0.25
        const float grade = 0.25f;
        const float hl = 4f;
        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 10f), 0f,
            sample: (float x, float z, out float y) => { y = z * grade; return true; },
            out var g, out var rot,
            halfLength: hl, halfWidth: 1.5f, groundClearance: 0f));

        Assert.AreEqual(10f * grade, g.Y, 1e-4f); // center Y only
        var fwd = TerrainContactPlane.ForwardFromQuaternion(rot);
        // forward projected: (0, grade*span, span) normalized ≈ (0, 0.2425, 0.970)
        var expectedFy = grade / MathF.Sqrt(1f + grade * grade);
        Assert.AreEqual(expectedFy, fwd.Y, 0.03f);
        Assert.IsTrue(fwd.Z > 0.9f, $"fwd.Z={fwd.Z}");
        Assert.IsTrue(MathF.Abs(fwd.X) < 0.05f, $"fwd.X={fwd.X}");
    }

    [TestMethod]
    public void Terrain_CrossSlope_ExactRollOracle()
    {
        const float grade = 0.3f;
        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) => { y = x * grade; return true; },
            out var g, out var rot,
            halfLength: 3f, halfWidth: 2f, groundClearance: 0f));
        Assert.AreEqual(0f, g.Y, 1e-4f);
        // right vector Y should track grade
        var q = rot;
        var rightY = 2f * ((q.X * q.Y) + (q.Z * q.W));
        var expectedRy = grade / MathF.Sqrt(1f + grade * grade);
        Assert.AreEqual(expectedRy, rightY, 0.05f);
    }

    [TestMethod]
    public void Terrain_Yaw90_SamplesAlongPlusX()
    {
        float? frontX = null;
        var yaw = MathF.PI * 0.5f; // +X forward
        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), yaw,
            sample: (float x, float z, out float y) =>
            {
                if (x > 0.5f) frontX = x;
                y = 0f;
                return true;
            },
            out _, out _,
            halfLength: 5f, halfWidth: 1f, groundClearance: 0f));
        Assert.IsNotNull(frontX);
        Assert.AreEqual(5f, frontX.Value, 0.1f);
    }

    [TestMethod]
    public void Terrain_WheelHardpoints_SkipZeros_AndHalfDims()
    {
        var wheels = new[]
        {
            default,
            new Vector3(1.1f, 0f, 2.2f),
            new Vector3(-1.1f, 0f, 2.2f),
            new Vector3(1.1f, 0f, -2.2f),
            new Vector3(-1.1f, 0f, -2.2f),
        };
        Assert.IsTrue(TerrainContactPlane.TryCollectWheelSamples(
            new Vector3(0, 0, 0), 0f, 1f, 1f, 0f, wheels,
            sample: (float x, float z, out float y) => { y = 4f + z * 0.1f; return true; },
            out var yC, out var yF, out var yB, out var yR, out var yL,
            out var hl, out var hw));
        Assert.AreEqual(4f, yC, 0.05f);
        Assert.IsTrue(yF > yB, $"front {yF} should be above back {yB}");
        Assert.AreEqual(2.2f, hl, 0.01f);
        Assert.AreEqual(1.1f, hw, 0.01f);
    }

    [TestMethod]
    public void Terrain_WheelSamples_OnlyFrontHalf_FallsBackYForBack()
    {
        // All hardpoints Z >= 0 → nB=0 → yB = yC
        var wheels = new[]
        {
            new Vector3(1f, 0f, 1f),
            new Vector3(-1f, 0f, 1f),
            new Vector3(1f, 0f, 2f),
            new Vector3(-1f, 0f, 2f),
        };
        Assert.IsTrue(TerrainContactPlane.TryCollectWheelSamples(
            new Vector3(0, 0, 0), 0f, 1f, 1f, 0f, wheels,
            sample: (float x, float z, out float y) => { y = z; return true; },
            out var yC, out var yF, out var yB, out _, out _, out _, out _));
        Assert.AreEqual(yC, yB, 1e-4f);
        Assert.AreEqual(yF, yC, 1e-3f); // all front, means match
    }

    [TestMethod]
    public void Terrain_FromYawPitchRoll_ExactComponents()
    {
        // yaw only
        var y = 0.8f;
        var qy = TerrainContactPlane.FromYawPitchRoll(y, 0f, 0f);
        Assert.AreEqual(0f, qy.X, 1e-5f);
        Assert.AreEqual(MathF.Sin(y * 0.5f), qy.Y, 1e-5f);
        Assert.AreEqual(0f, qy.Z, 1e-5f);
        Assert.AreEqual(MathF.Cos(y * 0.5f), qy.W, 1e-5f);

        // Convention: negative pitch = nose up (matches Atan2(yB-yF) climb).
        var p = -0.3f;
        var qp = TerrainContactPlane.FromYawPitchRoll(0f, p, 0f);
        var fp = TerrainContactPlane.ForwardFromQuaternion(qp);
        Assert.AreEqual(MathF.Sin(-p), fp.Y, 0.02f); // +0.295
        Assert.AreEqual(MathF.Cos(p), fp.Z, 0.02f);

        // roll only
        var r = 0.25f;
        var qr = TerrainContactPlane.FromYawPitchRoll(0f, 0f, r);
        var rightY = 2f * ((qr.X * qr.Y) + (qr.Z * qr.W));
        Assert.AreEqual(MathF.Sin(r), rightY, 0.05f);

        // combined: component formula oracles (yaw*pitch*roll)
        var yaw = 0.4f;
        var pitch = -0.2f;
        var roll = 0.15f;
        var qc = TerrainContactPlane.FromYawPitchRoll(yaw, pitch, roll);
        var hy = yaw * 0.5f; var hp = pitch * 0.5f; var hr = roll * 0.5f;
        var sy = MathF.Sin(hy); var cy = MathF.Cos(hy);
        var sp = MathF.Sin(hp); var cp = MathF.Cos(hp);
        var sr = MathF.Sin(hr); var cr = MathF.Cos(hr);
        Assert.AreEqual((cy * sp * cr) + (sy * cp * sr), qc.X, 1e-5f);
        Assert.AreEqual((sy * cp * cr) - (cy * sp * sr), qc.Y, 1e-5f);
        Assert.AreEqual((cy * cp * sr) - (sy * sp * cr), qc.Z, 1e-5f);
        Assert.AreEqual((cy * cp * cr) + (sy * sp * sr), qc.W, 1e-5f);
    }

    [TestMethod]
    public void Terrain_FromBasisColumns_RoundTripsYawPitch()
    {
        var q = TerrainContactPlane.FromBasisColumns(1, 0, 0, 0, 1, 0, 0, 0, 1);
        Assert.AreEqual(0f, q.X, 0.05f);
        Assert.AreEqual(0f, q.Y, 0.05f);
        Assert.AreEqual(1f, MathF.Abs(q.W), 0.05f);

        // pitched forward
        var pitch = 0.3f;
        var fwdY = -MathF.Sin(pitch);
        var fwdZ = MathF.Cos(pitch);
        var q2 = TerrainContactPlane.FromBasisColumns(1, 0, 0, 0, MathF.Cos(pitch), MathF.Sin(pitch), 0, fwdY, fwdZ);
        var f2 = TerrainContactPlane.ForwardFromQuaternion(q2);
        Assert.AreEqual(fwdY, f2.Y, 0.08f);
    }

    [TestMethod]
    public void Terrain_PitchRoll_ExactAtan2FromSamples()
    {
        // yF-yB over span → pitch magnitude |atan2(diff,span)|
        const float grade = 0.2f;
        const float hl = 5f;
        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) => { y = z * grade; return true; },
            out var g, out var rot, halfLength: hl, halfWidth: 2f, groundClearance: 0.05f));
        Assert.AreEqual(0.05f, g.Y, 1e-4f);
        var expectedPitch = MathF.Atan(grade); // |atan2(±grade*span, span)|
        var fwd = TerrainContactPlane.ForwardFromQuaternion(rot);
        Assert.AreEqual(MathF.Sin(expectedPitch), fwd.Y, 0.02f);
        Assert.AreEqual(MathF.Cos(expectedPitch), fwd.Z, 0.02f);
    }

    [TestMethod]
    public void Terrain_YawOnly_MatchesHalfAngleFormula()
    {
        var yaw = 1.2f;
        var q = TerrainContactPlane.YawOnly(yaw);
        Assert.AreEqual(0f, q.X, 1e-5f);
        Assert.AreEqual(MathF.Sin(yaw * 0.5f), q.Y, 1e-5f);
        Assert.AreEqual(0f, q.Z, 1e-5f);
        Assert.AreEqual(MathF.Cos(yaw * 0.5f), q.W, 1e-5f);
    }

    [TestMethod]
    public void Terrain_ForwardFromQuaternion_Yaw0And90()
    {
        var f0 = TerrainContactPlane.ForwardFromQuaternion(TerrainContactPlane.YawOnly(0f));
        Assert.AreEqual(0f, f0.X, 0.02f);
        Assert.AreEqual(0f, f0.Y, 0.02f);
        Assert.AreEqual(1f, f0.Z, 0.02f);

        var f90 = TerrainContactPlane.ForwardFromQuaternion(TerrainContactPlane.YawOnly(MathF.PI * 0.5f));
        Assert.AreEqual(1f, f90.X, 0.05f);
        Assert.AreEqual(0f, f90.Y, 0.05f);
        Assert.AreEqual(0f, f90.Z, 0.05f);
    }

    [TestMethod]
    public void Terrain_ResolveFootprint_ClampsAndDefaults()
    {
        TerrainContactPlane.ResolveVehicleFootprint(
            new[] { new Vector3(0.1f, 0f, 0.2f) }, null, 0f,
            out var hl0, out var hw0, out _);
        Assert.AreEqual(TerrainContactPlane.DefaultHalfLength, hl0, 1e-4f);
        Assert.AreEqual(TerrainContactPlane.DefaultHalfWidth, hw0, 1e-4f);

        TerrainContactPlane.ResolveVehicleFootprint(
            new[] { new Vector3(9f, 0f, 12f), new Vector3(-9f, 0f, -12f) }, null, 0f,
            out var hl1, out var hw1, out _);
        Assert.AreEqual(8f, hl1, 1e-4f);
        Assert.AreEqual(4f, hw1, 1e-4f);
    }

    [TestMethod]
    public void Terrain_SampleFailsAtBack_ReturnsFalse()
    {
        Assert.IsFalse(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) =>
            {
                y = 0f;
                return z >= -0.01f; // back sample fails
            },
            out _, out _, halfLength: 2f, halfWidth: 1f));
    }

    // ── NpcVehicleDriveController ────────────────────────────────────────────

    [TestMethod]
    public void Drive_Accel_ExactStep()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 30f;
        var hard = Hard(new Vector3(20, 0, 0), new Vector3(12, 0, 0));
        var r = NpcVehicleDriveController.Apply(
            hard, new Vector3(0, 5, 0), Yaw(MathF.PI * 0.5f), cruiseSpeed: 12f, dt: 0.1f,
            StraightPath(), 1000, previousVelocity: default);
        // from 0 → min(12, 30*0.1)=3
        Assert.AreEqual(3f, Xz(r.Velocity), 0.05f);
        // no heightfield → keep previous Y
        Assert.AreEqual(5f, r.NewPosition.Y, 0.01f);
    }

    [TestMethod]
    public void Drive_IntegrateFacing_ExactStepAlongYaw()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        NpcVehicleDriveController.MaxBrake = 1000f;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 100f;
        NpcVehicleDriveController.MaxPathDrift = 100f; // no pull
        NpcVehicleDriveController.LookAheadDistance = 28f;
        // Face +X, speed 10, dt 0.2 → step 2 along +X, Y preserved
        var path = StraightPath();
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(100, 0, 0), new Vector3(10, 0, 0)),
            new Vector3(0, 2, 0), Yaw(MathF.PI * 0.5f), cruiseSpeed: 10f, dt: 0.2f,
            path, 1000, previousVelocity: new Vector3(10, 0, 0));
        Assert.AreEqual(2f, r.NewPosition.X, 0.05f);
        Assert.AreEqual(2f, r.NewPosition.Y, 0.01f);
        Assert.AreEqual(0f, r.NewPosition.Z, 0.05f);
        Assert.AreEqual(10f, r.Velocity.X, 0.1f);
        Assert.AreEqual(0f, r.Velocity.Z, 0.1f);
    }

    [TestMethod]
    public void Drive_LookAhead_ExactAimOnStraightSegment()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 100f;
        NpcVehicleDriveController.LookAheadDistance = 20f;
        NpcVehicleDriveController.MaxPathDrift = 100f;
        // On +X path at x=10, look 20 → aim at x=30
        var path = StraightPath();
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(15, 0, 0), new Vector3(12, 0, 0), newIndex: 1),
            new Vector3(10, 0, 0), Yaw(MathF.PI * 0.5f), 12f, 0.05f, path, 1000,
            previousVelocity: new Vector3(12, 0, 0), laneOffset: 0f);
        // Facing aim along +X → thr high, steer ~0
        Assert.IsTrue(r.Throttle >= 0.95f, $"thr={r.Throttle}");
        Assert.AreEqual(0f, r.Steering, 0.15f);
        // Integrated ~12*0.05=0.6 along +X
        Assert.AreEqual(10.6f, r.NewPosition.X, 0.15f);
    }

    [TestMethod]
    public void Drive_Brake_ExactStep()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxBrake = 40f;
        var hard = Hard(new Vector3(0, 0, 0), default); // stopped hard
        var r = NpcVehicleDriveController.Apply(
            hard, new Vector3(0, 0, 0), Yaw(0f), cruiseSpeed: 12f, dt: 0.1f,
            StraightPath(), 1000, previousVelocity: new Vector3(0, 0, 10f));
        // desired 0 (hard speed 0, not rolling through), brake 40*0.1=4 → 6
        Assert.AreEqual(6f, Xz(r.Velocity), 0.1f);
    }

    [TestMethod]
    public void Drive_CornerScale_ExactMultiplierOnCruise()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        NpcVehicleDriveController.MaxBrake = 1000f; // reach desired in one tick
        // 3-4-5 triangle R=2.5 → scale = 0.25 + 0.75*(2.5/30) = 0.3125
        var tight = new MapPathTemplate();
        tight.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        tight.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(4, 0, 0), AcceptDistance = 2f });
        tight.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 3), AcceptDistance = 2f });

        var expectedScale = PathCurvature.SpeedScale(PathCurvature.Radius(
            tight.Points[0].Position, tight.Points[1].Position, tight.Points[2].Position));
        Assert.AreEqual(0.3125f, expectedScale, 0.01f);

        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(2, 0, 0), new Vector3(12, 0, 0), newIndex: 1),
            new Vector3(0, 0, 0), Yaw(MathF.PI * 0.5f), cruiseSpeed: 20f, dt: 0.2f,
            tight, 1000, previousVelocity: new Vector3(20, 0, 0));
        Assert.AreEqual(20f * expectedScale, Xz(r.Velocity), 0.3f);
    }

    [TestMethod]
    public void Drive_TwoPointPath_NoCornerSlowdown()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(50, 0, 0), AcceptDistance = 2f });
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(10, 0, 0), new Vector3(12, 0, 0), newIndex: 1),
            new Vector3(0, 0, 0), Yaw(MathF.PI * 0.5f), cruiseSpeed: 12f, dt: 0.1f,
            path, 1000, previousVelocity: new Vector3(12, 0, 0));
        Assert.AreEqual(12f, Xz(r.Velocity), 0.2f);
    }

    [TestMethod]
    public void Drive_ThrottleFloor_RaisesLowThrWhenMoving()
    {
        NpcVehicleDriveController.Enabled = true;
        // Far aim along facing + long look → thr should floor to 0.95 when speed > 0.5
        var path = StraightPath();
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(50, 0, 0), new Vector3(12, 0, 0)),
            new Vector3(0, 0, 0), Yaw(MathF.PI * 0.5f), cruiseSpeed: 12f, dt: 0.1f,
            path, 1000, previousVelocity: new Vector3(12, 0, 0));
        Assert.IsTrue(r.Throttle >= 0.9f, $"thr={r.Throttle}");
    }

    [TestMethod]
    public void Drive_ThrottleFloor_UsesMaxNotMin_WhenCruiseThrIsOne()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        // Well aligned along +X: Compute thr ≈ 1; Max(1,0.95)=1, Min would yield 0.95
        var path = StraightPath();
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(40, 0, 0), new Vector3(12, 0, 0)),
            new Vector3(0, 0, 0), Yaw(MathF.PI * 0.5f), cruiseSpeed: 12f, dt: 0.1f,
            path, 1000, previousVelocity: new Vector3(12, 0, 0));
        Assert.AreEqual(1f, r.Throttle, 0.02f);
    }

    [TestMethod]
    public void Drive_DefaultFootprintMinusOne_UsesTerrainDefaults()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        // No vehicle → footprint half dims = -1 → Terrain defaults (4 / 1.8)
        // Pitch grade on Z: default hl=4 must match exact pitch oracle
        TerrainContactPlane.DefaultHalfLength = 4f;
        TerrainContactPlane.DefaultHalfWidth = 1.8f;
        using var tga = MapTerrainHeightfieldTests.BuildHeightTga(2, 2, new ushort[] { 256, 256, 256, 256 });
        // Use sample-based path via flat field still exercises -1 → default path
        var path = StraightPath();
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(5, 0, 0), new Vector3(10, 0, 0)),
            new Vector3(0, 1, 0), Yaw(MathF.PI * 0.5f), 10f, 0.1f, path, 1000,
            previousVelocity: new Vector3(10, 0, 0), vehicle: null);
        Assert.IsTrue(r.HasDriveInputs);
        // Without heightfield Y stays previous
        Assert.AreEqual(1f, r.NewPosition.Y, 0.01f);
    }

    [TestMethod]
    public void Drive_ThrottleFloor_NotAppliedWhenStopped()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxBrake = 1000f;
        // Collapsed path: aim on vehicle → Compute thr=0; speed 0 → floor not applied
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 1f });
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(0, 0, 0), default, newIndex: 0),
            new Vector3(0, 0, 0), Yaw(0f), cruiseSpeed: 0f, dt: 0.1f, path, 1000,
            previousVelocity: default);
        Assert.AreEqual(0f, r.Throttle, 0.05f);
    }

    [TestMethod]
    public void Drive_ThrottleFloor_ZeroThrWhileMoving_RaisesToFloor()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        NpcVehicleDriveController.MaxBrake = 1000f;
        // Aim on vehicle → Compute thr=0; still rolling → thr floored to 0.95 (kills thr>0 mutant)
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0.0001f, 0, 0), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0.0002f, 0, 0), AcceptDistance = 1f });
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(0, 0, 0), new Vector3(12, 0, 0), newIndex: 0),
            new Vector3(0, 0, 0), Yaw(MathF.PI * 0.5f), cruiseSpeed: 12f, dt: 0.1f, path, 1000,
            previousVelocity: new Vector3(12, 0, 0));
        Assert.AreEqual(0.95f, r.Throttle, 0.02f);
    }

    [TestMethod]
    public void Drive_WaitHold_PreservesPoseAndFlags()
    {
        NpcVehicleDriveController.Enabled = true;
        var path = StraightPath();
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(99, 0, 99),
            Arrived = true,
            WaitUntilMs = 5000,
            NewIndex = 2,
            NewDirection = -1,
            FireReactionCoid = 42,
            NowReversing = true,
        };
        var prev = new Vector3(3, 4, 5);
        var prevRot = Yaw(0.7f);
        var r = NpcVehicleDriveController.Apply(hard, prev, prevRot, 12f, 0.1f, path, nowMs: 1000);
        Assert.AreEqual(prev, r.NewPosition);
        Assert.AreEqual(prevRot, r.Rotation);
        Assert.AreEqual(0f, r.Throttle, 1e-5f);
        Assert.AreEqual(0f, r.Steering, 1e-5f);
        Assert.AreEqual(default(Vector3), r.Velocity);
        Assert.AreEqual(2, r.NewIndex);
        Assert.AreEqual(42, r.FireReactionCoid);
        Assert.IsTrue(r.NowReversing);
        Assert.IsTrue(r.HasDriveInputs);
        Assert.IsTrue(r.Arrived);
    }

    [TestMethod]
    public void Drive_RollingThrough_BoostsPrevSpeedTowardDesired()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        var path = StraightPath();
        var hard = new PathStepResult
        {
            NewPosition = path.Points[0].Position,
            Velocity = default,
            Arrived = true,
            WaitUntilMs = 100, // already past
            NewIndex = 1,
        };
        // prev slow → rollingThrough boosts to desired before approach
        var r = NpcVehicleDriveController.Apply(
            hard, path.Points[0].Position, Yaw(MathF.PI * 0.5f), cruiseSpeed: 12f, dt: 0.1f,
            path, nowMs: 1000, previousVelocity: new Vector3(1, 0, 0));
        Assert.IsTrue(Xz(r.Velocity) > 10f, $"expected near cruise after boost, spd={Xz(r.Velocity)}");
    }

    [TestMethod]
    public void Drive_LaneOffset_ShiftsAimLaterally()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 10f;
        var path = StraightPath(); // along +X
        var face = Yaw(MathF.PI * 0.5f);
        var r0 = NpcVehicleDriveController.Apply(
            Hard(new Vector3(10, 0, 0), new Vector3(12, 0, 0)),
            new Vector3(0, 0, 0), face, 12f, 0.05f, path, 1000,
            previousVelocity: new Vector3(12, 0, 0), laneOffset: 0f);
        var r1 = NpcVehicleDriveController.Apply(
            Hard(new Vector3(10, 0, 0), new Vector3(12, 0, 0)),
            new Vector3(0, 0, 0), face, 12f, 0.05f, path, 1000,
            previousVelocity: new Vector3(12, 0, 0), laneOffset: 3f);
        // lateral lane should induce nonzero steer difference or path aim change
        Assert.IsTrue(
            MathF.Abs(r1.Steering - r0.Steering) > 0.01f ||
            MathF.Abs(r1.Rotation.Y - r0.Rotation.Y) > 1e-4f,
            $"lane should change steer/yaw: s0={r0.Steering} s1={r1.Steering}");
    }

    [TestMethod]
    public void Drive_NegativeCruise_TreatedAsZero()
    {
        NpcVehicleDriveController.Enabled = true;
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(10, 0, 0), new Vector3(12, 0, 0)),
            new Vector3(0, 0, 0), Yaw(0f), cruiseSpeed: -5f, dt: 0.1f,
            StraightPath(), 1000, previousVelocity: default);
        Assert.AreEqual(0f, Xz(r.Velocity), 0.05f);
        Assert.IsTrue(r.HasDriveInputs);
    }

    [TestMethod]
    public void Drive_MaxPathDrift_ExactPullTowardHard()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxPathDrift = 2f;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 0.01f; // barely turn
        NpcVehicleDriveController.MaxAcceleration = 1000f;
        // Face +Z, hard target far on +X → integrated goes +Z, drift pulls to hard
        var path = StraightPath();
        var hard = Hard(new Vector3(40, 0, 0), new Vector3(12, 0, 0));
        var r = NpcVehicleDriveController.Apply(
            hard, new Vector3(0, 0, 0), Yaw(0f), cruiseSpeed: 20f, dt: 0.5f,
            path, 1000, previousVelocity: new Vector3(0, 0, 20f));
        // After pull, XZ distance from hard should be ~ MaxPathDrift
        var dx = hard.NewPosition.X - r.NewPosition.X;
        var dz = hard.NewPosition.Z - r.NewPosition.Z;
        var drift = MathF.Sqrt(dx * dx + dz * dz);
        Assert.IsTrue(drift <= NpcVehicleDriveController.MaxPathDrift + 0.5f,
            $"drift {drift} should be pulled near MaxPathDrift");
        Assert.IsTrue(r.NewPosition.X > 5f, $"should pull toward hard X, got {r.NewPosition}");
    }

    [TestMethod]
    public void Drive_HeightfieldAlignFail_FallsBackToCenterSample()
    {
        NpcVehicleDriveController.Enabled = true;
        // 2x2 tiny field: center sample works, far footprint samples fail → TryAlign false
        using var tga = MapTerrainHeightfieldTests.BuildHeightTga(2, 2, new ushort[] { 512, 512, 512, 512 });
        Assert.IsTrue(MapTerrainHeightfield.TryLoad(tga, 2, 2, 5f, out var field, out var err), err);

        var path = StraightPath();
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(2, 0, 2), new Vector3(5, 0, 0)),
            new Vector3(1, 99, 1), Yaw(MathF.PI * 0.5f), cruiseSpeed: 5f, dt: 0.05f,
            path, 1000, previousVelocity: new Vector3(5, 0, 0),
            heightfield: field);
        Assert.IsTrue(r.HasDriveInputs);
        // Y should not stay at 99 if sample succeeds
        Assert.IsTrue(r.NewPosition.Y < 50f, $"expected height sample, Y={r.NewPosition.Y}");
    }

    [TestMethod]
    public void Drive_VehicleCacheMiss_ComputesMetricsInline()
    {
        NpcVehicleDriveController.Enabled = true;
        VehicleGroundMetricsCache.Clear();
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.6f, 0.6f, 0.6f, 0.6f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 2f), new Vector3(-1f, 0f, 2f),
                new Vector3(1f, 0f, -2f), new Vector3(-1f, 0f, -2f),
            },
        };
        var cv = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cv.VehicleSpecific = vs;
        cv.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 5555 }; // not in cache
        var vehicle = new Vehicle();
        vehicle.AssignCloneBaseForTests(cv);

        using var tga = MapTerrainHeightfieldTests.BuildHeightTga(2, 2, new ushort[] { 256, 256, 256, 256 });
        Assert.IsTrue(MapTerrainHeightfield.TryLoad(tga, 2, 2, 10f, out var field, out var err), err);

        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(5, 0, 0), new Vector3(12, 0, 0)),
            new Vector3(0, 0, 0), Yaw(MathF.PI * 0.5f), 12f, 0.1f, StraightPath(), 1000,
            previousVelocity: new Vector3(12, 0, 0), heightfield: field, vehicle: vehicle);
        Assert.IsTrue(r.HasDriveInputs);
        // terrain ~4 + ride 0.6
        Assert.IsTrue(r.NewPosition.Y > 4.3f, $"Y={r.NewPosition.Y}");
    }

    [TestMethod]
    public void Drive_VehicleCbidZero_StillUsesCompute()
    {
        NpcVehicleDriveController.Enabled = true;
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.4f, 0.4f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 1.5f), new Vector3(-1f, 0f, 1.5f),
                new Vector3(1f, 0f, -1.5f), new Vector3(-1f, 0f, -1.5f),
            },
        };
        var cv = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cv.VehicleSpecific = vs;
        cv.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 0 };
        var vehicle = new Vehicle();
        vehicle.AssignCloneBaseForTests(cv);

        using var tga = MapTerrainHeightfieldTests.BuildHeightTga(2, 2, new ushort[] { 256, 256, 256, 256 });
        Assert.IsTrue(MapTerrainHeightfield.TryLoad(tga, 2, 2, 10f, out var field, out _));

        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(3, 0, 0), new Vector3(8, 0, 0)),
            new Vector3(0, 0, 0), Yaw(MathF.PI * 0.5f), 8f, 0.1f, StraightPath(), 1000,
            previousVelocity: new Vector3(8, 0, 0), heightfield: field, vehicle: vehicle);
        Assert.IsTrue(r.HasDriveInputs);
        Assert.IsTrue(r.NewPosition.Y > 4.1f, $"Y={r.NewPosition.Y}");
    }

    [TestMethod]
    public void Drive_LookAhead_WrapsAroundClosedPath()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.LookAheadDistance = 80f;
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(10, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(10, 0, 10), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 10), AcceptDistance = 2f });

        // Near end of path; look-ahead should wrap
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(1, 0, 10), new Vector3(0, 0, -5), newIndex: 3),
            new Vector3(2, 0, 10), Yaw(MathF.PI), 10f, 0.1f, path, 1000,
            previousVelocity: new Vector3(-5, 0, 0));
        Assert.IsTrue(r.HasDriveInputs);
        Assert.IsTrue(float.IsFinite(r.Steering));
    }

    [TestMethod]
    public void Drive_AimOnTopOfVehicle_UsesHardVelocityYaw()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.LookAheadDistance = 0.0001f; // force tiny look (clamped to 16 actually)
        // Put vehicle ON hard target with hard velocity along +X; desired yaw from hard vel when aim collinear
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0.01f, 0, 0), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0.02f, 0, 0), AcceptDistance = 1f });

        var pos = new Vector3(0, 0, 0);
        var r = NpcVehicleDriveController.Apply(
            Hard(pos, new Vector3(10, 0, 0), newIndex: 0),
            pos, Yaw(0f), 10f, 0.05f, path, 1000,
            previousVelocity: new Vector3(10, 0, 0));
        Assert.IsTrue(r.HasDriveInputs);
        // Should turn toward +X (hard vel) rather than stay pure +Z forever after rate limit steps
        var yaw = VehicleDriveInputs.YawFromQuaternion(r.Rotation);
        Assert.IsTrue(MathF.Abs(yaw) > 0.01f || Xz(r.Velocity) > 0f);
    }

    [TestMethod]
    public void Drive_ZeroSpeed_ZeroVelocityVector()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxBrake = 1000f;
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(0, 0, 0), default),
            new Vector3(0, 0, 0), Yaw(0f), cruiseSpeed: 0f, dt: 0.1f,
            StraightPath(), 1000, previousVelocity: default);
        Assert.AreEqual(0f, r.Velocity.X, 1e-5f);
        Assert.AreEqual(0f, r.Velocity.Y, 1e-5f);
        Assert.AreEqual(0f, r.Velocity.Z, 1e-5f);
    }

    [TestMethod]
    public void Drive_YawRateLimit_CapsTurn()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 1f; // rad/s
        // Face +Z, hard target on +X → want ~π/2 turn, dt=0.1 → max 0.1 rad
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(50, 0, 0), new Vector3(12, 0, 0)),
            new Vector3(0, 0, 0), Yaw(0f), 12f, 0.1f, StraightPath(), 1000,
            previousVelocity: new Vector3(0, 0, 12f));
        var yaw = VehicleDriveInputs.YawFromQuaternion(r.Rotation);
        Assert.AreEqual(0.1f, MathF.Abs(yaw), 0.02f);
    }

    [TestMethod]
    public void Drive_NormalizeYaw_CrossesPiBoundary()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 10f;
        // Face nearly -π, aim toward +π side
        var face = Yaw(MathF.PI - 0.05f);
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(-10, 0, 0), new Vector3(-12, 0, 0)),
            new Vector3(0, 0, 0), face, 12f, 0.1f, StraightPath(), 1000,
            previousVelocity: new Vector3(-12, 0, 0));
        Assert.IsTrue(r.HasDriveInputs);
        Assert.IsTrue(float.IsFinite(r.Rotation.Y));
    }

    [TestMethod]
    public void Metrics_NonFiniteRaw_ClampedToZero()
    {
        var m = VehicleGroundMetricsCache.Compute(new VehicleSpecific
        {
            WheelRadius = new[] { 0.5f },
            WheelHardPoints = new[] { new Vector3(1f, float.NaN, 1f), new Vector3(-1f, float.NaN, -1f) },
        });
        Assert.AreEqual(0f, m.ChassisHeightAboveTerrain, 1e-5f);
    }

    [TestMethod]
    public void Drive_AimAndHardStill_UsesFallbackYaw()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 100f;
        // Collapsed path: all points on vehicle → aim ≈ position; hard vel zero → fallback yaw kept
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 1f });
        var faceYaw = 0.77f;
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(0, 0, 0), default, newIndex: 0),
            new Vector3(0, 0, 0), Yaw(faceYaw), cruiseSpeed: 0f, dt: 0.1f, path, 1000,
            previousVelocity: default);
        Assert.IsTrue(r.HasDriveInputs);
        var yaw = VehicleDriveInputs.YawFromQuaternion(r.Rotation);
        Assert.AreEqual(faceYaw, yaw, 0.05f);
    }

    [TestMethod]
    public void Drive_LaneOffset_ZeroSegment_ReturnsPoint()
    {
        NpcVehicleDriveController.Enabled = true;
        var path = new MapPathTemplate();
        // Identical points → segment length 0 in ApplyLaneOffset
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(5, 0, 5), AcceptDistance = 1f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(5, 0, 5), AcceptDistance = 1f });
        var r = NpcVehicleDriveController.Apply(
            Hard(new Vector3(5, 0, 5), new Vector3(1, 0, 0), newIndex: 0),
            new Vector3(5, 0, 5), Yaw(0f), 5f, 0.1f, path, 1000,
            previousVelocity: new Vector3(1, 0, 0), laneOffset: 2f);
        Assert.IsTrue(r.HasDriveInputs);
    }

    [TestMethod]
    public void Terrain_WheelSampleMiss_SkipsWheelContinues()
    {
        var wheels = new[]
        {
            new Vector3(1f, 0f, 1f),
            new Vector3(-1f, 0f, 1f),
            new Vector3(1f, 0f, -1f),
            new Vector3(-1f, 0f, -1f),
        };
        // Fail sample only on +X side — still get nAll>=2 from other wheels
        Assert.IsTrue(TerrainContactPlane.TryCollectWheelSamples(
            new Vector3(0, 0, 0), 0f, 1f, 1f, 0f, wheels,
            sample: (float x, float z, out float y) =>
            {
                y = 2f;
                return x < 0.5f; // skip right wheels
            },
            out var yC, out _, out _, out _, out _, out _, out _));
        Assert.AreEqual(2f, yC, 1e-4f);
    }

    [TestMethod]
    public void Terrain_SampleFailsRight_ReturnsFalse()
    {
        Assert.IsFalse(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) =>
            {
                y = 0f;
                return x < 0.5f; // right sample fails
            },
            out _, out _, halfLength: 2f, halfWidth: 1f));
    }

    [TestMethod]
    public void Terrain_SampleFailsLeft_ReturnsFalse()
    {
        Assert.IsFalse(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) =>
            {
                y = 0f;
                return x > -0.5f; // left sample fails
            },
            out _, out _, halfLength: 2f, halfWidth: 1f));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static PathStepResult Hard(Vector3 pos, Vector3 vel, int newIndex = 1)
        => new()
        {
            NewPosition = pos,
            Velocity = vel,
            Rotation = Quaternion.Default,
            NewIndex = newIndex,
            NewDirection = 1,
        };

    private static MapPathTemplate StraightPath()
    {
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(100, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(200, 0, 0), AcceptDistance = 2f });
        return path;
    }

    private static Quaternion Yaw(float yaw)
    {
        var h = yaw * 0.5f;
        return new Quaternion(0f, MathF.Sin(h), 0f, MathF.Cos(h));
    }

    private static float Xz(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
