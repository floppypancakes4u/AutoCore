namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Immutable per-wheel setup derived from <c>VehicleSpecific</c> (front/rear fan-out).
/// Client wheel struct stride 0xC0 — see 0.4 / 0.8 evidence.
/// </summary>
public readonly struct HkWheelSetup
{
    public HkWheelSetup(
        int index,
        int axleIndex,
        float hardpointX, float hardpointY, float hardpointZ,
        float radius,
        float width,
        float suspensionRestLength,
        float suspensionStrength,
        float dampingCompression,
        float dampingExtension,
        float maxBrakingTorque,
        float minPedalInputToBlock,
        float torqueRatio,
        float driveScale,
        float friction,
        bool doesSteer,
        bool handbrakeConnected,
        bool isRear)
    {
        Index = index;
        AxleIndex = axleIndex;
        HardpointX = hardpointX;
        HardpointY = hardpointY;
        HardpointZ = hardpointZ;
        Radius = radius;
        Width = width;
        SuspensionRestLength = suspensionRestLength;
        SuspensionStrength = suspensionStrength;
        DampingCompression = dampingCompression;
        DampingExtension = dampingExtension;
        MaxBrakingTorque = maxBrakingTorque;
        MinPedalInputToBlock = minPedalInputToBlock;
        TorqueRatio = torqueRatio;
        DriveScale = driveScale;
        Friction = friction;
        DoesSteer = doesSteer;
        HandbrakeConnected = handbrakeConnected;
        IsRear = isRear;
    }

    public int Index { get; }
    /// <summary>0 = front axle, 1 = rear (AA friction solver uses 2 axles).</summary>
    public int AxleIndex { get; }
    public float HardpointX { get; }
    public float HardpointY { get; }
    public float HardpointZ { get; }
    public float Radius { get; }
    public float Width { get; }
    public float SuspensionRestLength { get; }
    public float SuspensionStrength { get; }
    public float DampingCompression { get; }
    public float DampingExtension { get; }
    public float MaxBrakingTorque { get; }
    public float MinPedalInputToBlock { get; }
    public float TorqueRatio { get; }
    /// <summary>
    /// Per-wheel drive-torque scale into the friction solver axle pack
    /// (client <c>wheel+0x88</c>). Setup maps from <see cref="TorqueRatio"/>
    /// (rear already includes <c>RearWheelFrictionScalar</c>); see
    /// <c>fn_wheel_driveScale_0x88.md</c>.
    /// </summary>
    public float DriveScale { get; }
    public float Friction { get; }
    public bool DoesSteer { get; }
    public bool HandbrakeConnected { get; }
    public bool IsRear { get; }
}
