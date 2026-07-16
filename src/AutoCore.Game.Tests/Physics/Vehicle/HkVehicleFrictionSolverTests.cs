using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Reduced friction solver (0x6c4450) + circleProjection (0x6c3f90) —
/// drivePack, μ·N·dt circle clamp, lateral Jv weight.
/// </summary>
[TestClass]
public class HkVehicleFrictionSolverTests
{
    private const float Epsilon = 1e-5f;

    private static AxleFrictionInput GroundedAxle(
        float slipLong,
        float slipLat = 0f,
        float drivePack = 0f,
        float normalLoad = 100f,
        float mu0 = 1f,
        float muSlope = 0f,
        float muMax = 1f,
        bool driveEnabled = true)
        => new()
        {
            InContact = true,
            DriveEnabled = driveEnabled,
            DrivePack = drivePack,
            SlipLongitudinal = slipLong,
            SlipLateral = slipLat,
            NormalLoad = normalLoad,
            Mu0 = mu0,
            MuSlope = muSlope,
            MuMax = muMax,
        };

    // -------------------------------------------------------------------------
    // Zero drive — slip cancel
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Solve_ZeroDrive_WithLongSlip_OpposesSlipAndBoundedByMuNDt()
    {
        const float dt = 1f / 60f;
        const float invMass = 1f; // unit mass → invKeff = 1
        const float mu = 1f;
        const float n = 100f;
        const float slip = 50f; // unconstrained |imp| = 50 >> Fmax

        float fMax = mu * n * dt;
        Assert.IsTrue(fMax > 0f);

        var inputs = new[]
        {
            GroundedAxle(slipLong: slip, drivePack: 0f, normalLoad: n, mu0: mu, muMax: mu),
            GroundedAxle(slipLong: 0f, drivePack: 0f, normalLoad: n, mu0: mu, muMax: mu),
        };
        var outputs = new AxleFrictionImpulse[2];

        HkVehicleFrictionSolver.Solve(dt, inputs, invMass, outputs);

        // Opposes positive longitudinal slip.
        Assert.IsTrue(outputs[0].Longitudinal < 0f,
            $"expected opposing long impulse, got {outputs[0].Longitudinal}");
        Assert.AreEqual(0f, outputs[0].Lateral, Epsilon);

        // Bounded by friction impulse limit μ·|N|·dt.
        Assert.AreEqual(-fMax, outputs[0].Longitudinal, Epsilon);
        Assert.IsTrue(MathF.Abs(outputs[0].Longitudinal) <= fMax + Epsilon);
    }

    [TestMethod]
    public void Solve_ZeroDrive_SmallSlip_UnclampedOpposesExactly()
    {
        const float dt = 0.05f;
        const float invMass = 1f;
        const float slip = 0.2f; // |imp| = 0.2 < Fmax = 1*100*0.05 = 5

        var inputs = new[]
        {
            GroundedAxle(slipLong: slip, drivePack: 0f),
            GroundedAxle(slipLong: 0f),
        };
        var outputs = new AxleFrictionImpulse[2];

        HkVehicleFrictionSolver.Solve(dt, inputs, invMass, outputs);

        Assert.AreEqual(-slip, outputs[0].Longitudinal, Epsilon);
        Assert.AreEqual(0f, outputs[0].Lateral, Epsilon);
    }

    [TestMethod]
    public void Solve_NegativeSlip_OpposesPositiveImpulseBounded()
    {
        const float dt = 0.05f;
        const float n = 40f;
        const float mu = 1f;
        float fMax = mu * n * dt;

        var inputs = new[]
        {
            GroundedAxle(slipLong: -80f, drivePack: 0f, normalLoad: n, mu0: mu, muMax: mu),
            GroundedAxle(0f, normalLoad: n, mu0: mu, muMax: mu),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, inputs, chassisInvMass: 1f, outputs);

        Assert.IsTrue(outputs[0].Longitudinal > 0f);
        Assert.AreEqual(fMax, outputs[0].Longitudinal, Epsilon);
    }

    // -------------------------------------------------------------------------
    // Drive pack bias (DAT_00a0f298 = 0.5 twice)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Solve_DrivePackBias_AddsLongitudinalPushBeyondSlipCancel()
    {
        // invK = 1 (unit mass). Zero slip → pure drive bias.
        // driveTarget = invKeff * (pack * 0.5) * 0.5 = pack/4
        // driveMax = mu0 * |N| * dt = 1 * 100 * 0.05 = 5
        // pack=8 → bias=2 < 5 → impLong = 0 - 2 = -2
        const float dt = 0.05f;
        const float pack = 8f;
        const float expectedBias = pack * 0.5f * 0.5f; // 2

        var inputs = new[]
        {
            GroundedAxle(slipLong: 0f, drivePack: pack, normalLoad: 100f, mu0: 1f, muMax: 1f),
            GroundedAxle(0f),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, inputs, chassisInvMass: 1f, outputs);

        Assert.AreEqual(-expectedBias, outputs[0].Longitudinal, Epsilon);
        Assert.AreEqual(0f, outputs[0].Lateral, Epsilon);
    }

    [TestMethod]
    public void Solve_DrivePackBias_ClampedByDriveMax()
    {
        // pack large → driveTarget = pack/4 >> driveMax = mu0*|N|*dt = 1
        const float dt = 0.01f;
        const float n = 100f;
        const float mu = 1f;
        float driveMax = mu * n * dt; // 1
        const float pack = 1000f; // bias raw = 250

        var inputs = new[]
        {
            GroundedAxle(slipLong: 0f, drivePack: pack, normalLoad: n, mu0: mu, muMax: mu),
            GroundedAxle(0f, normalLoad: n, mu0: mu, muMax: mu),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, inputs, chassisInvMass: 1f, outputs);

        // Still inside circle (Fmax = driveMax here with slope 0), so long = -driveMax
        Assert.AreEqual(-driveMax, outputs[0].Longitudinal, Epsilon);
    }

    /// <summary>
    /// C-mass regression: real chassis mass must not explode drive bias via invKeff².
    /// Impulse scales ∝ mass so Δv = bias·invMass stays O(pack/4); driveMax = μ·|N|·dt
    /// (no extra invKeff) keeps the clamp in the same units as the friction circle.
    /// </summary>
    [TestMethod]
    public void Solve_DrivePackBias_RealMass_ImpulseScalesWithMass_DeltaVStable()
    {
        const float dt = 1f / 60f;
        const float mass = 1500f;
        const float invMass = 1f / mass;
        const float pack = 8f;
        // Mass-scaled suspension load (gScale = mass); typical small compression.
        const float normalLoad = mass * 40f * 0.05f; // 3000
        const float mu = 1f;

        float invKeff = 1f / invMass; // pure-linear unit jacobian
        float expectedBias = invKeff * (pack * 0.5f * 0.5f); // mass * pack/4
        float driveMax = mu * normalLoad * dt;
        float expectedImp = -MathF.Min(expectedBias, driveMax);

        var inputs = new[]
        {
            new AxleFrictionInput
            {
                InContact = true,
                DriveEnabled = true,
                DrivePack = pack,
                SlipLongitudinal = 0f,
                SlipLateral = 0f,
                NormalLoad = normalLoad,
                Mu0 = mu,
                MuMax = mu,
                InvKeffLong = invKeff,
                InvKeffLat = invKeff,
            },
            GroundedAxle(0f, normalLoad: normalLoad, mu0: mu, muMax: mu),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, inputs, invMass, outputs);

        Assert.AreEqual(expectedImp, outputs[0].Longitudinal, 1e-3f);

        // Δv must stay O(pack/4), not O(mass·pack) (the old invKeff² explosion).
        float deltaV = MathF.Abs(outputs[0].Longitudinal) * invMass;
        Assert.IsTrue(deltaV < 1f,
            $"drive Δv exploded under real mass: {deltaV} m/s (impulse={outputs[0].Longitudinal})");
        Assert.IsTrue(MathF.Abs(outputs[0].Longitudinal) < mass * 10f,
            $"drive impulse absurd for mass={mass}: {outputs[0].Longitudinal}");
    }

    [TestMethod]
    public void Solve_DriveDisabled_IgnoresDrivePack()
    {
        const float dt = 0.05f;
        var inputs = new[]
        {
            GroundedAxle(slipLong: 0.1f, drivePack: 500f, driveEnabled: false),
            GroundedAxle(0f),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, inputs, chassisInvMass: 1f, outputs);

        Assert.AreEqual(-0.1f, outputs[0].Longitudinal, Epsilon);
    }

    // -------------------------------------------------------------------------
    // Friction circle clamp
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Solve_FrictionCircleClamp_ScalesCombinedImpulseToMuNDt()
    {
        const float dt = 0.1f;
        const float invMass = 1f;
        const float mu = 1f;
        const float n = 10f;
        // Unconstrained (3,4) magnitude 5; Fmax = 1*10*0.1 = 1 → scale by 0.2 → (0.6, 0.8)
        const float slipLong = 3f;
        const float slipLat = 4f;
        float fMax = mu * n * dt;

        var inputs = new[]
        {
            GroundedAxle(slipLong: slipLong, slipLat: slipLat, drivePack: 0f, normalLoad: n, mu0: mu, muMax: mu),
            GroundedAxle(slipLong: 0f, drivePack: 0f, normalLoad: n, mu0: mu, muMax: mu),
        };
        var outputs = new AxleFrictionImpulse[2];

        HkVehicleFrictionSolver.Solve(dt, inputs, invMass, outputs);

        float mag = MathF.Sqrt(
            outputs[0].Longitudinal * outputs[0].Longitudinal +
            outputs[0].Lateral * outputs[0].Lateral);

        Assert.AreEqual(fMax, mag, Epsilon);
        Assert.AreEqual(-0.6f, outputs[0].Longitudinal, Epsilon);
        Assert.AreEqual(-0.8f, outputs[0].Lateral, Epsilon);
    }

    [TestMethod]
    public void ClampFrictionCircle_InsideCircle_Unchanged()
    {
        float lon = 0.3f;
        float lat = 0.4f; // mag 0.5 < 1
        HkVehicleFrictionSolver.ClampFrictionCircle(ref lon, ref lat, maxImpulse: 1f);
        Assert.AreEqual(0.3f, lon, Epsilon);
        Assert.AreEqual(0.4f, lat, Epsilon);
    }

    [TestMethod]
    public void ClampFrictionCircle_Outside_PreservesDirection()
    {
        float lon = 6f;
        float lat = 8f; // mag 10
        HkVehicleFrictionSolver.ClampFrictionCircle(ref lon, ref lat, maxImpulse: 5f);
        Assert.AreEqual(3f, lon, Epsilon);
        Assert.AreEqual(4f, lat, Epsilon);
    }

    [TestMethod]
    public void ClampFrictionCircle_ZeroLimit_ZerosImpulses()
    {
        float lon = 1f;
        float lat = 2f;
        HkVehicleFrictionSolver.ClampFrictionCircle(ref lon, ref lat, maxImpulse: 0f);
        Assert.AreEqual(0f, lon, Epsilon);
        Assert.AreEqual(0f, lat, Epsilon);
    }

    // -------------------------------------------------------------------------
    // circleProjection (0x6c3f90)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CircleProjection_InsideUnitCircle_Unchanged()
    {
        float lon = 0.3f;
        float lat = 0.4f; // |n| = 0.5 < 1 with Fmax=1
        float residual = HkVehicleFrictionSolver.CircleProjection(
            ref lon, ref lat, fMaxLong: 1f, fMaxLat: 1f);

        Assert.AreEqual(0f, residual, Epsilon);
        Assert.AreEqual(0.3f, lon, Epsilon);
        Assert.AreEqual(0.4f, lat, Epsilon);
    }

    [TestMethod]
    public void CircleProjection_OutsideIsotropic_RadialProject()
    {
        // Fmax = 5; free (6,8) → |n| = | (6/5, 8/5) | = 2 → project to radius 1 in n-space
        // → imp' = (6,8)/2 *? wait: n = invLim*imp = imp/5, |n|=2, proj n = n/|n| = (0.6, 0.8)
        // imp' = proj_n * Fmax = (3, 4)
        float lon = 6f;
        float lat = 8f;
        float residual = HkVehicleFrictionSolver.CircleProjection(
            ref lon, ref lat, fMaxLong: 5f, fMaxLat: 5f);

        Assert.AreEqual(3f, lon, Epsilon);
        Assert.AreEqual(4f, lat, Epsilon);
        // residual = oldLat - newLat = 8 - 4 = 4
        Assert.AreEqual(4f, residual, Epsilon);
    }

    [TestMethod]
    public void CircleProjection_WithScaleTable_ProjectsOutside()
    {
        // Build real ESI table; free impulse far outside should land on/near unit circle.
        Span<float> scales = stackalloc float[HkVehicleFrictionSolver.CircleProjectionScaleCount];
        HkVehicleFrictionSolver.BuildCircleProjectionScales(product: 4f, scales);

        float lon = 10f;
        float lat = 0f;
        const float fMax = 2f;
        HkVehicleFrictionSolver.CircleProjection(ref lon, ref lat, fMax, fMax, scales);

        float nLong = lon / fMax;
        float nLat = lat / fMax;
        float mag = MathF.Sqrt(nLong * nLong + nLat * nLat);
        Assert.AreEqual(1f, mag, 1e-3f);
    }

    [TestMethod]
    public void CircleProjection_ZeroFmax_ZerosNormalizedAndStaysInside()
    {
        // invLim=0 when Fmax<=0 → n=0 → inside → residual 0, impulses unchanged by projection math
        // (caller should zero via ClampFrictionCircle when Fmax<=0; helper early-outs inside)
        float lon = 1f;
        float lat = 2f;
        float residual = HkVehicleFrictionSolver.CircleProjection(
            ref lon, ref lat, fMaxLong: 0f, fMaxLat: 0f);
        Assert.AreEqual(0f, residual, Epsilon);
        Assert.AreEqual(1f, lon, Epsilon);
        Assert.AreEqual(2f, lat, Epsilon);
    }

    // -------------------------------------------------------------------------
    // Lateral Jv weight (DAT_00a0f704 = 0.25)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void WeightLateralJv_AppliesQuarterWeightOnPriorResidual()
    {
        // Jv_lat = prior * 0.25 + sideRow
        float jv = HkVehicleFrictionSolver.WeightLateralJv(priorResidual: 8f, sideRowJv: 1f);
        Assert.AreEqual(8f * 0.25f + 1f, jv, Epsilon);
        Assert.AreEqual(HkPhysicsConstants.LateralAngWeight, 0.25f, Epsilon);
    }

    [TestMethod]
    public void Solve_UsesCallerSuppliedSlipLateral_WithoutReweighting()
    {
        // If caller already applied 0.25, Solve must not scale again.
        const float dt = 0.05f;
        float weighted = HkVehicleFrictionSolver.WeightLateralJv(4f, 0f); // 1.0
        var inputs = new[]
        {
            GroundedAxle(slipLong: 0f, slipLat: weighted, drivePack: 0f),
            GroundedAxle(0f),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, inputs, chassisInvMass: 1f, outputs);

        Assert.AreEqual(-weighted, outputs[0].Lateral, Epsilon);
    }

    // -------------------------------------------------------------------------
    // Both axles / out of contact
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Solve_BothAxles_IndependentCircleLimits()
    {
        const float dt = 0.1f;
        var inputs = new[]
        {
            GroundedAxle(slipLong: 100f, slipLat: 0f, normalLoad: 10f, mu0: 1f, muMax: 1f),
            GroundedAxle(slipLong: 0f, slipLat: 100f, normalLoad: 20f, mu0: 0.5f, muMax: 0.5f),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, inputs, chassisInvMass: 1f, outputs);

        Assert.AreEqual(-(1f * 10f * dt), outputs[0].Longitudinal, Epsilon);
        Assert.AreEqual(0f, outputs[0].Lateral, Epsilon);
        Assert.AreEqual(0f, outputs[1].Longitudinal, Epsilon);
        Assert.AreEqual(-(0.5f * 20f * dt), outputs[1].Lateral, Epsilon);
    }

    [TestMethod]
    public void Solve_AirborneAxle_ZeroImpulse()
    {
        var inputs = new[]
        {
            new AxleFrictionInput
            {
                InContact = false,
                DriveEnabled = true,
                DrivePack = 500f,
                SlipLongitudinal = 10f,
                SlipLateral = 5f,
                NormalLoad = 100f,
                Mu0 = 1f,
                MuMax = 1f,
            },
            GroundedAxle(0f),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(1f / 60f, inputs, chassisInvMass: 1f, outputs);
        Assert.AreEqual(0f, outputs[0].Longitudinal, Epsilon);
        Assert.AreEqual(0f, outputs[0].Lateral, Epsilon);
    }

    [TestMethod]
    public void Solve_BothOutOfContact_ZerosAllOutputs()
    {
        var inputs = new[]
        {
            new AxleFrictionInput { InContact = false, DrivePack = 1f, SlipLongitudinal = 1f, Mu0 = 1f, MuMax = 1f, NormalLoad = 10f },
            new AxleFrictionInput { InContact = false, DrivePack = 1f, SlipLateral = 1f, Mu0 = 1f, MuMax = 1f, NormalLoad = 10f },
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(0.016f, inputs, chassisInvMass: 1f, outputs);
        Assert.AreEqual(0f, outputs[0].Longitudinal, Epsilon);
        Assert.AreEqual(0f, outputs[0].Lateral, Epsilon);
        Assert.AreEqual(0f, outputs[1].Longitudinal, Epsilon);
        Assert.AreEqual(0f, outputs[1].Lateral, Epsilon);
    }

    [TestMethod]
    public void Solve_NonPositiveDt_ZerosOutputs()
    {
        var inputs = new[]
        {
            GroundedAxle(slipLong: 5f, slipLat: 5f, drivePack: 10f),
            GroundedAxle(slipLong: 5f),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(0f, inputs, chassisInvMass: 1f, outputs);
        Assert.AreEqual(0f, outputs[0].Longitudinal, Epsilon);
        Assert.AreEqual(0f, outputs[1].Lateral, Epsilon);
    }

    // -------------------------------------------------------------------------
    // Slip-dependent μ
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ComputeFrictionLimit_SlipDependentMu_ClampedToMuMax()
    {
        // mu = min(muMax, max(0, mu0 + slip*slope))
        float fMax = HkVehicleFrictionSolver.ComputeFrictionLimit(
            mu0: 0.5f, muSlope: 0.1f, muMax: 0.8f,
            slipSpeed: 10f, // 0.5 + 1.0 = 1.5 → clamp 0.8
            normalLoad: 100f, dt: 0.1f);
        Assert.AreEqual(0.8f * 100f * 0.1f, fMax, Epsilon);
    }

    [TestMethod]
    public void Solve_SlipDependentMu_RaisesFrictionCeiling()
    {
        // With slope 0: Fmax = 0.5 * 10 * 0.1 = 0.5 → long clamped to -0.5
        // With slope that hits muMax 2: Fmax = 2 * 10 * 0.1 = 2 → long = -2 (slip=5 unclamped would be 5)
        const float dt = 0.1f;
        const float n = 10f;
        const float slip = 5f;

        var low = new[]
        {
            GroundedAxle(slipLong: slip, normalLoad: n, mu0: 0.5f, muSlope: 0f, muMax: 0.5f),
            GroundedAxle(0f, normalLoad: n, mu0: 0.5f, muMax: 0.5f),
        };
        var high = new[]
        {
            GroundedAxle(slipLong: slip, normalLoad: n, mu0: 0.5f, muSlope: 1f, muMax: 2f),
            GroundedAxle(0f, normalLoad: n, mu0: 0.5f, muMax: 2f),
        };
        var outLow = new AxleFrictionImpulse[2];
        var outHigh = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, low, 1f, outLow);
        HkVehicleFrictionSolver.Solve(dt, high, 1f, outHigh);

        Assert.AreEqual(-0.5f, outLow[0].Longitudinal, Epsilon);
        Assert.AreEqual(-2f, outHigh[0].Longitudinal, Epsilon);
        Assert.IsTrue(MathF.Abs(outHigh[0].Longitudinal) > MathF.Abs(outLow[0].Longitudinal));
    }

    // -------------------------------------------------------------------------
    // Drive pack aggregation
    // -------------------------------------------------------------------------

    [TestMethod]
    public void AggregateDrivePack_AveragesTorqueTimesWheelScale()
    {
        // drivePack = Σ(torque_i * scale_i) / axleWheelCount  (postTick 0x64bc70)
        float pack = HkVehicleFrictionSolver.AggregateDrivePack(
            torques: new[] { 100f, 200f },
            wheelScales: new[] { 1f, 0.5f });
        // (100*1 + 200*0.5) / 2 = 100
        Assert.AreEqual(100f, pack, Epsilon);
    }

    [TestMethod]
    public void AggregateDrivePack_Empty_ReturnsZero()
    {
        Assert.AreEqual(0f, HkVehicleFrictionSolver.AggregateDrivePack(
            Array.Empty<float>(), Array.Empty<float>()), Epsilon);
    }

    // -------------------------------------------------------------------------
    // Coupled 2×2 long/lat + contact Keff helpers (C4)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void SolveCoupledImpulses_ZeroCoupling_MatchesDiagonal()
    {
        HkVehicleFrictionSolver.SolveCoupledImpulses(
            invKeffLong: 2f, invKeffLat: 0.5f, coupling: 0f,
            jvLong: 3f, jvLat: -4f,
            out float lon, out float lat);
        Assert.AreEqual(-2f * 3f, lon, Epsilon);
        Assert.AreEqual(-0.5f * -4f, lat, Epsilon);
    }

    [TestMethod]
    public void SolveCoupledImpulses_NonzeroCoupling_UsesStandardInverse()
    {
        // a=2, d=3, b=1 → det=5; Jv=(1,0)
        // λ = −(1/det)[d -b; -b a] · Jv = −(1/5)(3, -1) = (−0.6, 0.2)
        const float a = 2f, d = 3f, b = 1f;
        float invKLong = 1f / a;
        float invKLat = 1f / d;
        HkVehicleFrictionSolver.SolveCoupledImpulses(
            invKLong, invKLat, b, jvLong: 1f, jvLat: 0f,
            out float lon, out float lat);
        Assert.AreEqual(-0.6f, lon, Epsilon);
        Assert.AreEqual(0.2f, lat, Epsilon);
    }

    [TestMethod]
    public void Solve_WithCoupling_DiffersFromDiagonal()
    {
        const float dt = 0.05f;
        var coupled = new[]
        {
            new AxleFrictionInput
            {
                InContact = true,
                DriveEnabled = false,
                SlipLongitudinal = 1f,
                SlipLateral = 1f,
                NormalLoad = 1000f,
                Mu0 = 10f,
                MuMax = 10f,
                InvKeffLong = 0.5f, // Keff_long = 2
                InvKeffLat = 1f / 3f, // Keff_lat = 3
                Coupling = 1f,
            },
            GroundedAxle(0f),
        };
        var diagonal = new[]
        {
            new AxleFrictionInput
            {
                InContact = true,
                DriveEnabled = false,
                SlipLongitudinal = 1f,
                SlipLateral = 1f,
                NormalLoad = 1000f,
                Mu0 = 10f,
                MuMax = 10f,
                InvKeffLong = 0.5f,
                InvKeffLat = 1f / 3f,
                Coupling = 0f,
            },
            GroundedAxle(0f),
        };
        var outC = new AxleFrictionImpulse[2];
        var outD = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, coupled, chassisInvMass: 1f, outC);
        HkVehicleFrictionSolver.Solve(dt, diagonal, chassisInvMass: 1f, outD);

        Assert.AreNotEqual(outC[0].Longitudinal, outD[0].Longitudinal, 1e-4f);
        // Coupled: λ = −(1/5)[3 -1; -1 2]·(1,1) = −(1/5)(2,1) = (−0.4, −0.2)
        Assert.AreEqual(-0.4f, outC[0].Longitudinal, Epsilon);
        Assert.AreEqual(-0.2f, outC[0].Lateral, Epsilon);
    }

    [TestMethod]
    public void ComputeInvKeffFromContact_PureLinear_IsInvMass()
    {
        // r=0 → no angular contribution → Keff = invMass + eps
        float invK = HkVehicleFrictionSolver.ComputeInvKeffFromContact(
            chassisInvMass: 2f,
            invInertiaX: 1f, invInertiaY: 1f, invInertiaZ: 1f,
            rx: 0f, ry: 0f, rz: 0f,
            dirX: 0f, dirY: 0f, dirZ: 1f);
        float expected = 1f / (2f + HkPhysicsConstants.InvDenomEpsilon);
        Assert.AreEqual(expected, invK, Epsilon);
    }

    [TestMethod]
    public void ComputeKeffCoupling_OrthogonalDirs_ZeroLinearTerm()
    {
        // r=0, long ⊥ lat → coupling = 0
        float b = HkVehicleFrictionSolver.ComputeKeffCoupling(
            chassisInvMass: 1f,
            invInertiaX: 1f, invInertiaY: 1f, invInertiaZ: 1f,
            rx: 0f, ry: 0f, rz: 0f,
            longX: 0f, longY: 0f, longZ: 1f,
            latX: 1f, latY: 0f, latZ: 0f);
        Assert.AreEqual(0f, b, Epsilon);
    }

    [TestMethod]
    public void Solve_UsesCircleProjection_NotOnlyClampHelper()
    {
        // Same outside-circle case as Solve_FrictionCircleClamp: isotropic CircleProjection
        // must land on the unit circle in Fmax-space (same as ClampFrictionCircle for equal Fmax).
        const float dt = 0.1f;
        const float mu = 1f;
        const float n = 10f;
        float fMax = mu * n * dt;
        var inputs = new[]
        {
            GroundedAxle(slipLong: 3f, slipLat: 4f, drivePack: 0f, normalLoad: n, mu0: mu, muMax: mu),
            GroundedAxle(0f, normalLoad: n, mu0: mu, muMax: mu),
        };
        var outputs = new AxleFrictionImpulse[2];
        HkVehicleFrictionSolver.Solve(dt, inputs, chassisInvMass: 1f, outputs);

        float mag = MathF.Sqrt(
            outputs[0].Longitudinal * outputs[0].Longitudinal +
            outputs[0].Lateral * outputs[0].Lateral);
        Assert.AreEqual(fMax, mag, Epsilon);
    }
}
