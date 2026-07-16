namespace AutoCore.Game.Physics.Vehicle;

using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Structures;

/// <summary>
/// Immutable per-clonebase Havok vehicle setup built from <see cref="VehicleSpecific"/>.
/// Mass model: unit mass (1.0) + diagonal unit-inertia from RVInertia* — client normalizes
/// per unit mass (see 0.2-mass-inertia.md / FUN_005fc620). Chassis basis: +Z fwd, +X right, +Y up.
/// Setup path: Vehicle_buildHavokVehicleFramework 0x5fd390.
/// </summary>
public sealed class HkVehicleData
{
    public const int MaxWheels = 6;
    public const int AxleCount = 2;
    /// <summary>Retail engine factor table length (+0x344, eight discrete levels).</summary>
    public const int EngineFactorCount = 8;

    private readonly float[] _engineFactors;
    private readonly byte[] _engineLut;

    private HkVehicleData(
        int cbid,
        float mass,
        float invMass,
        float inertiaRoll,
        float inertiaPitch,
        float inertiaYaw,
        float comModX, float comModY, float comModZ,
        float gravityY,
        float steeringMaxAngle,
        float steeringFullSpeedLimit,
        float airDensity,
        float frontalArea,
        float dragCoefficient,
        float liftCoefficient,
        float extraGravityX, float extraGravityY, float extraGravityZ,
        float avdNormalSpinDamping,
        float avdCollisionSpinDamping,
        float avdCollisionThreshold,
        float rearWheelFrictionScalar,
        float frictionEqualizer,
        float speedLimiter,
        float absoluteTopSpeed,
        int frontWheelCount,
        byte numberOfGears,
        float transmissionRatio,
        float reverseGearRatio,
        float clutchDelayTime,
        short downshiftRpm,
        short upshiftRpm,
        float[] gearRatios,
        short torqueMax,
        float minTorqueFactor,
        float maxTorqueFactor,
        bool engineEnabled,
        int engineRows,
        int engineCols,
        float engineRangeScale,
        float[] engineFactors,
        byte[] engineLut,
        HkWheelSetup[] wheels)
    {
        Cbid = cbid;
        Mass = mass;
        InvMass = invMass;
        InertiaRoll = inertiaRoll;
        InertiaPitch = inertiaPitch;
        InertiaYaw = inertiaYaw;
        CenterOfMassModifierX = comModX;
        CenterOfMassModifierY = comModY;
        CenterOfMassModifierZ = comModZ;
        GravityY = gravityY;
        SteeringMaxAngle = steeringMaxAngle;
        SteeringFullSpeedLimit = steeringFullSpeedLimit;
        AirDensity = airDensity;
        FrontalArea = frontalArea;
        DragCoefficient = dragCoefficient;
        LiftCoefficient = liftCoefficient;
        ExtraGravityX = extraGravityX;
        ExtraGravityY = extraGravityY;
        ExtraGravityZ = extraGravityZ;
        AvdNormalSpinDamping = avdNormalSpinDamping;
        AvdCollisionSpinDamping = avdCollisionSpinDamping;
        AvdCollisionThreshold = avdCollisionThreshold;
        RearWheelFrictionScalar = rearWheelFrictionScalar;
        FrictionEqualizer = frictionEqualizer;
        SpeedLimiter = speedLimiter;
        AbsoluteTopSpeed = absoluteTopSpeed;
        FrontWheelCount = frontWheelCount;
        NumberOfGears = numberOfGears;
        TransmissionRatio = transmissionRatio;
        ReverseGearRatio = reverseGearRatio;
        ClutchDelayTime = clutchDelayTime;
        DownshiftRpm = downshiftRpm;
        UpshiftRpm = upshiftRpm;
        GearRatios = gearRatios;
        TorqueMax = torqueMax;
        MinTorqueFactor = minTorqueFactor;
        MaxTorqueFactor = maxTorqueFactor;
        EngineEnabled = engineEnabled;
        EngineRows = engineRows;
        EngineCols = engineCols;
        EngineRangeScale = engineRangeScale;
        _engineFactors = engineFactors;
        _engineLut = engineLut;
        Wheels = wheels;
    }

    public int Cbid { get; }
    public float Mass { get; }
    public float InvMass { get; }
    /// <summary>Diagonal unit-inertia components (mass·RVInertia). Axis pairing: Roll/Pitch/Yaw from DB.</summary>
    public float InertiaRoll { get; }
    public float InertiaPitch { get; }
    public float InertiaYaw { get; }
    public float CenterOfMassModifierX { get; }
    public float CenterOfMassModifierY { get; }
    public float CenterOfMassModifierZ { get; }
    public float GravityY { get; }
    public float SteeringMaxAngle { get; }
    public float SteeringFullSpeedLimit { get; }
    public float AirDensity { get; }
    public float FrontalArea { get; }
    public float DragCoefficient { get; }
    public float LiftCoefficient { get; }
    public float ExtraGravityX { get; }
    public float ExtraGravityY { get; }
    public float ExtraGravityZ { get; }
    public float AvdNormalSpinDamping { get; }
    public float AvdCollisionSpinDamping { get; }
    public float AvdCollisionThreshold { get; }
    public float RearWheelFrictionScalar { get; }
    public float FrictionEqualizer { get; }
    public float SpeedLimiter { get; }
    public float AbsoluteTopSpeed { get; }
    /// <summary>Front-axle wheel count (split index). Wheels with index &gt;= this are rear.</summary>
    public int FrontWheelCount { get; }
    public byte NumberOfGears { get; }
    public float TransmissionRatio { get; }
    public float ReverseGearRatio { get; }
    public float ClutchDelayTime { get; }
    public short DownshiftRpm { get; }
    public short UpshiftRpm { get; }
    public IReadOnlyList<float> GearRatios { get; }
    public short TorqueMax { get; }
    public float MinTorqueFactor { get; }
    public float MaxTorqueFactor { get; }

    /// <summary>
    /// Engine enabled flag (+0x0c). False → <see cref="TorqueCurve2D.Evaluate"/> returns 1.0.
    /// </summary>
    public bool EngineEnabled { get; }

    /// <summary>X (contact) bin count (+0x10). 0 → always OOR → factors[0].</summary>
    public int EngineRows { get; }

    /// <summary>Y (contact) bin count (+0x14) and LUT row stride.</summary>
    public int EngineCols { get; }

    /// <summary>Bin width (+0x18). Must be nonzero (used as 1/scale even on OOR path).</summary>
    public float EngineRangeScale { get; }

    /// <summary>
    /// Eight discrete torque-factor levels (+0x344). Index 0 is the OOR default
    /// (populated as <see cref="MinTorqueFactor"/>).
    /// </summary>
    public IReadOnlyList<float> EngineFactors => _engineFactors;

    /// <summary>Mutable array view for <see cref="TorqueCurve2D.Evaluate"/> (do not mutate).</summary>
    public float[] EngineFactorsArray => _engineFactors;

    /// <summary>Byte LUT (+0x3dc); length rows*cols. Empty when trivial constant-factor setup.</summary>
    public IReadOnlyList<byte> EngineLut => _engineLut;

    /// <summary>Mutable array view for <see cref="TorqueCurve2D.Evaluate"/> (do not mutate).</summary>
    public byte[] EngineLutArray => _engineLut;

    public IReadOnlyList<HkWheelSetup> Wheels { get; }
    public int WheelCount => Wheels.Count;

    /// <summary>
    /// Hardpoint lever arm relative to effective COM for Phase 4 impulse/torque application.
    /// Server has no asset COM: local COM = body origin + <see cref="CenterOfMassModifierX"/>/Y/Z,
    /// so <c>r = hardpoint_cs − CenterOfMassModifier</c> (see <c>fn_com_modifier.md</c>).
    /// </summary>
    public void ApplyComOffset(
        float hardpointX, float hardpointY, float hardpointZ,
        out float relativeX, out float relativeY, out float relativeZ)
    {
        relativeX = hardpointX - CenterOfMassModifierX;
        relativeY = hardpointY - CenterOfMassModifierY;
        relativeZ = hardpointZ - CenterOfMassModifierZ;
    }

    /// <summary>
    /// Build setup from clonebase vehicle data. Runtime prefix multipliers default to 1.0
    /// (entity+0x1fc etc.); pass overrides when porting upgrade paths.
    /// </summary>
    public static HkVehicleData FromVehicleSpecific(
        VehicleSpecific vs,
        int cbid = 0,
        float gravityY = HkPhysicsConstants.DefaultGravityY,
        float? airDensityOverride = null,
        float gearWheelDimMult = 1f,
        float brakeFrontMult = 1f,
        float brakeRearMult = 1f,
        float steerAngleMult = 1f,
        float steerSpeedMult = 1f,
        float avdSpinMult = 1f)
    {
        if (vs.WheelHardPoints == null)
            throw new ArgumentException("VehicleSpecific.WheelHardPoints is required", nameof(vs));

        var mass = HkPhysicsConstants.UnitMass;
        var invMass = mass > 0f ? 1f / mass : 0f;

        // Unit inertia from RVInertia*; absolute inertia = mass * unit (mass=1 → same).
        var iRoll = mass * (vs.RVInertiaRoll > 0f ? vs.RVInertiaRoll : 1f);
        var iPitch = mass * (vs.RVInertiaPitch > 0f ? vs.RVInertiaPitch : 1f);
        var iYaw = mass * (vs.RVInertiaYaw > 0f ? vs.RVInertiaYaw : 1f);

        var wheelMask = vs.WheelExistance;
        var present = new List<int>(MaxWheels);
        for (var i = 0; i < MaxWheels; i++)
        {
            var existsBit = (wheelMask & (1 << i)) != 0;
            var hasHp = i < vs.WheelHardPoints.Length && !IsNearZeroHardpoint(vs.WheelHardPoints[i]);
            var hasR = vs.WheelRadius != null && i < vs.WheelRadius.Length && vs.WheelRadius[i] > 0.05f;
            if (wheelMask == 0)
            {
                if (hasHp || hasR)
                    present.Add(i);
            }
            else if (existsBit)
            {
                present.Add(i);
            }
        }

        if (present.Count == 0)
        {
            // Fallback: first 4 slots with any radius/hardpoint data.
            for (var i = 0; i < Math.Min(4, MaxWheels); i++)
                present.Add(i);
        }

        // Front axle count: WheelAxle is the binary front-count byte (setup +0x4cc analogue).
        var frontCount = vs.WheelAxle > 0
            ? Math.Clamp((int)vs.WheelAxle, 1, present.Count)
            : Math.Max(1, (present.Count + 1) / 2);

        // VehicleFlags bits (setup-field-mapping): bit0/1 handbrake F/R, bit2/3 steers F/R.
        var flags = vs.VehicleFlags;
        var frontSteers = (flags & (1 << 2)) != 0 || (flags & (1 << 2 | 1 << 3)) == 0; // default front if unset
        var rearSteers = (flags & (1 << 3)) != 0;
        if ((flags & (1 << 2 | 1 << 3)) == 0)
        {
            frontSteers = true;
            rearSteers = false;
        }
        var frontHb = (flags & (1 << 0)) != 0;
        var rearHb = (flags & (1 << 1)) != 0 || true; // retail handbrake is rear-biased; default rear connected

        var rearMu = vs.RearWheelFrictionScalar > 0f ? vs.RearWheelFrictionScalar : 1f;
        var wheels = new HkWheelSetup[present.Count];
        for (var wi = 0; wi < present.Count; wi++)
        {
            var i = present[wi];
            var isRear = wi >= frontCount;
            var axle = isRear ? 1 : 0;
            var hp = i < vs.WheelHardPoints.Length ? vs.WheelHardPoints[i] : default;
            var radius = vs.WheelRadius != null && i < vs.WheelRadius.Length
                ? vs.WheelRadius[i] * gearWheelDimMult
                : 0.35f * gearWheelDimMult;
            var width = vs.WheelWidth != null && i < vs.WheelWidth.Length
                ? vs.WheelWidth[i] * gearWheelDimMult
                : 0.2f * gearWheelDimMult;

            var rest = isRear ? vs.SuspensionLength.Rear : vs.SuspensionLength.Front;
            var strength = isRear ? vs.SuspensionStrength.Rear : vs.SuspensionStrength.Front;
            var dampC = isRear
                ? vs.SuspensionDampeningCoefficientCompression.Rear
                : vs.SuspensionDampeningCoefficientCompression.Front;
            var dampE = isRear
                ? vs.SuspensionDampeningCoefficientExtension.Rear
                : vs.SuspensionDampeningCoefficientExtension.Front;
            var brake = (isRear ? vs.BrakesMaxTorque.Rear : vs.BrakesMaxTorque.Front)
                        * (isRear ? brakeRearMult : brakeFrontMult);
            var pedal = isRear ? vs.BrakesPedalInput.Rear : vs.BrakesPedalInput.Front;
            var tRatio = isRear ? vs.WheelTorqueRatios.Rear : vs.WheelTorqueRatios.Front;
            if (isRear)
                tRatio *= rearMu; // setup applies RearWheelFrictionScalar to rear torque/friction table

            // wheel+0x88: RE writer open — map from TorqueRatio (already rear-scaled).
            // See docs/reconstruction/physics/verified/fn_wheel_driveScale_0x88.md.
            var driveScale = tRatio;

            // Base friction: use equalizer as neutral μ if no per-wheel table in C# blob.
            var friction = vs.RVFrictionEqualizer > 0f ? vs.RVFrictionEqualizer : 1f;
            if (isRear)
                friction *= rearMu;

            wheels[wi] = new HkWheelSetup(
                index: wi,
                axleIndex: axle,
                hardpointX: hp.X,
                hardpointY: hp.Y,
                hardpointZ: hp.Z,
                radius: radius,
                width: width,
                suspensionRestLength: rest,
                suspensionStrength: strength,
                dampingCompression: dampC,
                dampingExtension: dampE,
                maxBrakingTorque: brake,
                minPedalInputToBlock: pedal,
                torqueRatio: tRatio,
                driveScale: driveScale,
                friction: friction,
                doesSteer: isRear ? rearSteers : frontSteers,
                handbrakeConnected: isRear ? rearHb : frontHb,
                isRear: isRear);
        }

        var gears = vs.GearRatios != null
            ? (float[])vs.GearRatios.Clone()
            : Array.Empty<float>();

        // Engine torqueCurve2D setup (0x4a9750). Authored byte LUT is not in VehicleSpecific
        // yet — use a trivial 0×0 table so Evaluate always takes the OOR path → factors[0].
        // factors[0] = MinTorqueFactor; remaining levels LERP Min→Max for future LUT loads.
        var engineFactors = BuildEngineFactors(vs.MinTorqueFactor, vs.MaxTorqueFactor);
        var engineLut = Array.Empty<byte>();

        return new HkVehicleData(
            cbid,
            mass,
            invMass,
            iRoll,
            iPitch,
            iYaw,
            vs.CenterOfMassModifier.X,
            vs.CenterOfMassModifier.Y,
            vs.CenterOfMassModifier.Z,
            gravityY,
            vs.SteeringMaxAngle * steerAngleMult,
            vs.SteeringFullSpeedLimit * steerSpeedMult,
            airDensityOverride ?? vs.AerodynamicsAirDensity,
            vs.AerodynamicsFrontalArea,
            vs.AerodynamicsDrag,
            vs.AerodynamicsLift,
            vs.AerodynamicsExtraGravity.X,
            vs.AerodynamicsExtraGravity.Y,
            vs.AerodynamicsExtraGravity.Z,
            vs.AVDNormalSpinDamping * avdSpinMult,
            vs.AVDCollisionSpinDamping * avdSpinMult,
            vs.AVDCollisionThreshold,
            rearMu,
            vs.RVFrictionEqualizer,
            vs.SpeedLimiter,
            vs.AbsoluteTopSpeed,
            frontCount,
            vs.NumberOfGears,
            vs.TransmissionRatio,
            vs.ReverseGearRation,
            vs.ClutchDelayTime,
            vs.DownshiftRPM,
            vs.UpshiftRPM,
            gears,
            vs.TorqueMax,
            vs.MinTorqueFactor,
            vs.MaxTorqueFactor,
            engineEnabled: true,
            engineRows: 0,
            engineCols: 0,
            engineRangeScale: HkPhysicsConstants.One,
            engineFactors,
            engineLut,
            wheels);
    }

    /// <summary>
    /// Eight factor levels for torqueCurve2D: index 0 = <paramref name="minTorqueFactor"/>
    /// (OOR default); index 7 = <paramref name="maxTorqueFactor"/>; intermediates LERP.
    /// Delegates to <see cref="TorqueCurve2D.BuildFactorsFromMinMax"/>.
    /// </summary>
    public static float[] BuildEngineFactors(float minTorqueFactor, float maxTorqueFactor)
        => TorqueCurve2D.BuildFactorsFromMinMax(minTorqueFactor, maxTorqueFactor);

    private static bool IsNearZeroHardpoint(Vector3 hp)
        => MathF.Abs(hp.X) < 1e-4f && MathF.Abs(hp.Y) < 1e-4f && MathF.Abs(hp.Z) < 1e-4f;
}
