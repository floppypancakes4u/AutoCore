namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Brake-related torque helpers for the vehicle physics port.
/// <para>
/// Retail AA path (<c>VehicleAction::calcWheelTorque</c> @ <c>0x598040</c>): the only
/// brake-adjacent term is a <b>rear drive-torque cut</b> when the handbrake byte
/// (entity <c>+0x61c</c>) is set — rear wheels multiply by
/// <see cref="HkPhysicsConstants.HandbrakeRearTorqueScale"/> (<c>DAT_00a0f298</c> = 0.5).
/// Service brake is <b>not</b> applied in that custom path; deceleration comes from the
/// friction solver when drive pack drops (see <c>docs/reconstruction/physics/brake-spec.md</c>).
/// </para>
/// </summary>
public static class HkVehicleBrake
{
    /// <summary>
    /// Applies the calcWheelTorque handbrake rear-traction cut.
    /// When handbrake is active and the wheel is rear, multiplies <paramref name="torque"/>
    /// by <see cref="HkPhysicsConstants.HandbrakeRearTorqueScale"/> (0.5); otherwise returns
    /// torque unchanged. Front wheels are never scaled.
    /// </summary>
    /// <param name="torque">Per-wheel drive torque before the handbrake cut.</param>
    /// <param name="isRear">True if the wheel is on the rear axle (index &gt; vehicleData+0x4cc).</param>
    /// <param name="handbrakeActive">Entity handbrake / sharp-turn byte (<c>+0x61c</c>) nonzero.</param>
    public static float ApplyHandbrakeDriveTorqueScale(float torque, bool isRear, bool handbrakeActive)
    {
        if (isRear && handbrakeActive)
            return torque * HkPhysicsConstants.HandbrakeRearTorqueScale;
        return torque;
    }

    /// <summary>
    /// Vestigial Havok <c>hkpVehicleDefaultBrake</c> service-brake formula:
    /// <c>brakeTorque = m_maxBreakingTorque * pedalInput</c> (pedal ∈ [0,1]).
    /// <para>
    /// Documented as <b>unused</b> in the AA custom driver: neither <c>applyAction</c>
    /// (<c>0x598650</c>) nor <c>calcWheelTorque</c> (<c>0x598040</c>) applies per-wheel
    /// service brake torque. Kept for completeness if a framework brake component is
    /// later confirmed on the postTick path.
    /// </para>
    /// </summary>
    /// <param name="maxBreakingTorque">Wheel <c>m_maxBreakingTorque</c> (front/rear from DB).</param>
    /// <param name="pedalInput">Brake pedal in [0,1]; values outside are not clamped here.</param>
    public static float ComputeServiceBrakeTorque(float maxBreakingTorque, float pedalInput)
        => maxBreakingTorque * pedalInput;
}
