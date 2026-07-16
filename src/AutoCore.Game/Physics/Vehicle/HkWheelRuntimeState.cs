namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Mutable per-wheel runtime state (subset of client wheel struct, stride 0xC0).
/// Written by preUpdate / wheel-collide / steering / engine; read by suspension and friction.
/// </summary>
/// <remarks>
/// Offset map (base = wheelsDesc+0x80 + i·0xC0) — see 0.4-suspension.md / 0.8-struct-offsets.md:
/// <list type="bullet">
/// <item><description>+0x20 contact point (world)</description></item>
/// <item><description>+0x30 contact normal (world)</description></item>
/// <item><description>+0x80 in-contact flag</description></item>
/// <item><description>+0x8C spin angular velocity (rad/s)</description></item>
/// <item><description>+0x90 integrated spin angle (rad)</description></item>
/// <item><description>+0x94 longitudinal friction impulse (solver writeback)</description></item>
/// <item><description>+0x9C longitudinal contact velocity (friction writeback → next spin)</description></item>
/// <item><description>+0xa0 lateral friction impulse (solver writeback)</description></item>
/// <item><description>+0xAC suspension scaling factor</description></item>
/// <item><description>+0xB0 current suspension length</description></item>
/// <item><description>+0xB4 closing speed (&lt;0 compress, ≥0 extend)</description></item>
/// </list>
/// Drive torque lives on the separate wheelsDesc+0x28[i] array on the client; stored here for
/// server aggregation convenience. Steer angle is the physical per-wheel angle from steering.
/// <para>
/// This type is only per-wheel state — not the vehicle container. A future
/// <c>VehiclePhysicsInstance</c> should own an array of these without merging chassis fields here.
/// </para>
/// </remarks>
public sealed class HkWheelRuntimeState
{
    /// <summary>wheel+0x80 — grounded when true.</summary>
    public bool InContact;

    /// <summary>wheel+0xB0 — suspension current length (compression from cast).</summary>
    public float CurrentLength;

    /// <summary>wheel+0xAC — spring scale (often 1/maxSuspLen; miss path uses 1).</summary>
    public float Scaling = 1f;

    /// <summary>wheel+0xB4 — damper velocity; &lt;0 compressing, ≥0 extending.</summary>
    public float ClosingSpeed;

    /// <summary>Physical steer angle for this wheel (radians; 0 if non-steering).</summary>
    public float SteerAngle;

    /// <summary>
    /// Per-wheel engine drive torque (client: wheelsDesc+0x28[i], clamped [0,1000] upstream).
    /// </summary>
    public float DriveTorque;

    /// <summary>
    /// Per-wheel service-brake torque from <c>hkDefaultBrake_update</c> (brake+0x10[i]).
    /// Signed opposing-spin torque; folded into the friction-solver input path as
    /// <c>brakeTorque / radius</c> (postTick <c>local_3ec</c>).
    /// </summary>
    public float BrakeTorque;

    /// <summary>
    /// Per-wheel lock flag from <c>hkDefaultBrake_update</c> (brake+0x1c[i]).
    /// When true, preUpdate forces wheel spin (wheel+0x8c) to zero.
    /// </summary>
    public bool IsBlocked;

    /// <summary>wheel+0x8C — spin angular velocity (rad/s).</summary>
    public float Spin;

    /// <summary>wheel+0x90 — integrated spin angle (radians; visual / kinematics).</summary>
    public float SpinAngle;

    /// <summary>
    /// wheel+0x9C — longitudinal contact velocity from previous friction writeback.
    /// Input to preUpdate spin: <c>ω = (LongContactVel + chassisLongVel) / radius</c>.
    /// </summary>
    public float LongContactVel;

    /// <summary>
    /// wheel+0x94 — longitudinal (drive/brake) friction impulse from
    /// <c>hkVehicleFrictionSolver_solve</c> writeback (axle output copied per wheel).
    /// </summary>
    public float LongImpulse;

    /// <summary>
    /// wheel+0xa0 — lateral (cornering) friction impulse from friction solver writeback.
    /// </summary>
    public float LatImpulse;

    /// <summary>wheel+0x30 — contact normal (world); suspension force direction.</summary>
    public float ContactNormalX;

    public float ContactNormalY;
    public float ContactNormalZ;

    /// <summary>wheel+0x20 — contact point (world); impulse application point.</summary>
    public float ContactPointX;

    public float ContactPointY;
    public float ContactPointZ;

    /// <summary>
    /// Apply airborne miss defaults from preUpdate 0x64cf20:
    /// inContact=0, length=restLen, scaling=1, closingSpeed=0, normal=−downAxis.
    /// Leaves steer/drive/spin/spinAngle/longContactVel and contact point unchanged.
    /// </summary>
    public void ClearContact(float restLength, float downDirX, float downDirY, float downDirZ)
    {
        InContact = false;
        CurrentLength = restLength;
        Scaling = 1f;
        ClosingSpeed = 0f;
        ContactNormalX = -downDirX;
        ContactNormalY = -downDirY;
        ContactNormalZ = -downDirZ;
    }

    /// <summary>
    /// Copy grounded/airborne fields from a cast result and set suspension scaling.
    /// Does not write contact point (caller fills world point from hardpoint/hit).
    /// </summary>
    public void ApplyContact(in WheelContact contact, float scaling)
    {
        InContact = contact.InContact;
        CurrentLength = contact.Length;
        ClosingSpeed = contact.ClosingSpeed;
        ContactNormalX = contact.NormalX;
        ContactNormalY = contact.NormalY;
        ContactNormalZ = contact.NormalZ;
        Scaling = scaling;
    }
}
