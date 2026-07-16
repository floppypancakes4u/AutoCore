namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Float/int constants verified from <c>autoassault.exe</c> via Ghidra <c>read_memory</c>.
/// Addresses are image-base 0x400000 VA. Re-verify on conflict — binary wins.
/// </summary>
public static class HkPhysicsConstants
{
    // --- universal ---
    public const float Zero = 0f;
    public const float One = 1f;
    public const float Half = 0.5f;                    // DAT_00a0f298
    public const float NegHalf = -0.5f;                // DAT_00aaa6cc

    // --- substep (StepTo 0x4d6c80) ---
    public const float MaxFrameDt = 0.1f;              // 0x00a0f730
    public const float SubstepHzCap = 29.9999998f;     // 0x009cc798 ≈ 30

    // --- steering (applyAction stage-1/2 @ 0x598650; read_memory 2026-07-15) ---
    /// <summary>DAT_009d54e0 — VehicleAction+0x20 ctor seed; stage-1 rate base (≈20/7).</summary>
    public const float SteerStage1RateBase = 20f / 7f;
    /// <summary>DAT_00a10e74 — stage-1 open-band rate factor when command is off-zero.</summary>
    public const float SteerStage1OpenBandFactor = 2f;
    public const float SteerRampPerTick = 0.05f;       // DAT_00a10e78 (stage-2 ± per tick, not ×dt)
    public const float SteerSpeedFactorDivisor = 20f;  // 0x00af3388 (raw confirmed 0x41a00000)
    public const float SteerInputMin = -1f;            // DAT_00aaa668
    public const float SteerInputMax = 1f;

    // --- engine / calcWheelTorque 0x598040 ---
    public const float UprightDotThreshold = 0.8f;     // DAT_00a0f698
    /// <summary>Upright falloff exponent: when |dot| &lt; 0.8, upright = |dot|^4 (double @ 0x9d54e8).</summary>
    public const float UprightPowExponent = 4f;        // 0x009d54e8 (double 4.0)
    public const float LowSpeedTractionCutoff = 15f;   // DAT_00aaa7a4
    public const float LowSpeedTractionSlope = 0.2f;   // DAT_00a0f70c
    public const float TorqueClampMax = 1000f;         // DAT_00a0f520
    public const float HandbrakeRearTorqueScale = 0.5f; // DAT_00a0f298
    public const float RearDriverModScale = 2f;        // DAT_00a10e74 (calcWheelTorque rear path)
    /// <summary>
    /// Fixed wheels-builder payload at <c>wheel+0x84</c> (<c>DAT_00aaa7a4</c> = 15.0).
    /// Used by <c>hkDefaultBrake_update</c> as the wheelsMass-style kill-spin scale
    /// (<c>raw = −spin · r² · wheelsMass · invDt</c>). Same plate value as
    /// <see cref="LowSpeedTractionCutoff"/> / sharp speed gate.
    /// </summary>
    public const float WheelsMassScale = 15f; // DAT_00aaa7a4

    // --- drive controller MoveToTarget3DPoint 0x4fc650 ---
    public const float SteerDeadband = 0.01f;          // DAT_00a0f718
    public const float SteerGain = 2f;                 // DAT_00a10e74 (controller role)
    public const double ReverseAlignGate = -0.4;      // _DAT_009cd238 (double)
    public const float ThrottleSpeedGate = 5f;         // DAT_00aaa688
    public const float NearTargetDistance = 30f;       // DAT_00a0f694
    public const float CruiseScaleMin = 0.1f;          // DAT_00a0f730
    public const float SharpSpeedGate = 15f;           // DAT_00aaa7a4
    public const float SharpLateralThreshold = 0.7f;   // DAT_00a0f710

    // --- AVD / air stab / upright-restore (0x598650 gate + 0x598320 recovery) ---
    /// <summary>Upper gate: righting when bodyUp·worldUp &lt; 0.7 (DAT_00af3380).</summary>
    public const float UprightRestoreDot = 0.7f;       // DAT_00af3380
    /// <summary>Lower gate: skip when upDot ≤ 0.1 (near-inverted). Shared float pool @ 0xa0f730.</summary>
    public const float UprightRestoreMinDot = 0.1f;    // g_flMultiKillCountBlend @ 0xa0f730
    /// <summary>Righting impulse magnitude scale (DAT_00af3378).</summary>
    public const float UprightRestoreMagScale = 0.8f;  // DAT_00af3378
    /// <summary>AngVel damp term × throttle (DAT_00af337c).</summary>
    public const float UprightRestoreAngDamp = 0.1f;   // DAT_00af337c
    /// <summary>Re-ground cast raise (DAT_00a110d8). Client recovery only — not server hot path.</summary>
    public const float ReGroundYRaise = 10f;           // DAT_00a110d8
    /// <summary>
    /// Downward ray length for <see cref="VehiclePhysicsInstance.ReGround"/>. Retail uses an
    /// unbounded heightfield lookup (<c>CVOGMap_CastTerrainHeight 0x4cfe60</c>); the server ray
    /// API needs a finite max, so this is large enough to reach terrain from the raised start.
    /// </summary>
    public const float ReGroundCastMaxDistance = 100000f;
    /// <summary>Collision-window length in ms (imm 0x1900). Entity stamp wiring deferred.</summary>
    public const int CollisionWindowMs = 6400;         // 0x1900
    /// <summary>airStabilization “is moving” speed epsilon (DAT_009d54a8 ≈ 2^-23).</summary>
    public const float AirStabMovingEpsilon = 1.1920929e-7f; // DAT_009d54a8

    // --- mass model (server: unit mass; Havok normalizes per unit mass) ---
    public const float UnitMass = 1f;
    public const float DefaultGravityY = -9.81f;

    // --- friction solver ---
    public const float InvDenomEpsilon = 1.1920929e-7f; // _DAT_00a0d2f4 ≈ 2^-23
    public const float LateralAngWeight = 0.25f;        // DAT_00a0f704
    /// <summary>
    /// Per-wheel μ slope / viscosity friction written by wheels builder
    /// <c>FUN_005fcce0</c> (<c>desc+0x34[i] = g_flMsToSeconds_Inferred</c> @ <c>0xa0f72c</c>).
    /// </summary>
    public const float WheelsViscosityFriction = 0.001f; // DAT_00a0f72c
    /// <summary>
    /// μmax = μ0 × this (wheels builder <c>DAT_00aaa68c</c>).
    /// </summary>
    public const float WheelsMuMaxScale = 1.5f; // DAT_00aaa68c

    // --- server stability (not retail constants; prevent explode from reduced model) ---
    /// <summary>
    /// Max |suspension force| magnitude after mass scale (per wheel). Retail is unclamped —
    /// used only when <c>ServerConfig.SuspensionForceClampEnabled</c> is set (default OFF, C2).
    /// </summary>
    public const float MaxSuspensionForce = 80f;
    /// <summary>Max |linear velocity| after each substep (world units/s).</summary>
    public const float MaxLinearSpeed = 80f;
    /// <summary>Max |angular velocity| after each substep (rad/s).</summary>
    public const float MaxAngularSpeed = 8f;
    /// <summary>Path soft-pull: max planar drift from hard navigator before clamp (world units).</summary>
    public const float PathSoftPullMaxDrift = 6f;
    /// <summary>Path soft-pull: max |Y − supportY| before vertical clamp (world units).</summary>
    public const float PathSoftPullMaxVerticalDrift = 2.5f;
    /// <summary>Path soft-pull: max planar |v − hard.v| excess before clamp (world units/s).</summary>
    public const float PathSoftPullMaxPlanarVelDrift = 10f;
    /// <summary>
    /// Default chassis height above terrain when no per-template metrics exist.
    /// Must stay near 0 — ghost pose is chassis origin; large values float NPCs.
    /// </summary>
    public const float PathFallbackRideHeight = 0f;
    /// <summary>
    /// Clearance above terrain sample used for airborne hysteresis (world units).
    /// </summary>
    public const float PathAirborneClearance = 0.12f;
    /// <summary>
    /// Max surface drop per tick while still treated as continuous contact (world units).
    /// Larger drops (cliff / ramp lip) force ballistic free-flight.
    /// </summary>
    public const float PathMaxStickSurfaceDrop = 0.45f;
    /// <summary>
    /// Front-vs-rear terrain delta (world units) beyond which grounded pitch is disabled
    /// (avoids bridging a crest with a false plane). Contact/airborne is center-sample only.
    /// </summary>
    public const float PathRampLipFrontDrop = 0.22f;
    /// <summary>Half-length for front/rear terrain probes (short — full chassis span bridges lips).</summary>
    public const float PathProbeHalfLength = 1.0f;
    /// <summary>When bodyUp·worldUp is above this, cast wheels with world-down (not body-down).</summary>
    public const float TerrainCastWorldDownDot = 0.35f;
}
