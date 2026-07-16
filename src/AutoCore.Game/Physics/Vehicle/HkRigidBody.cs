namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Mutable chassis rigid-body state for the Havok-style vehicle integrator skeleton.
/// <para>
/// Integration uses <b>semi-implicit Euler</b> (symplectic Euler): velocities are advanced from
/// force/torque (and optional gravity), then position/orientation use the <i>updated</i>
/// velocities. This matches the Havok 2.x rigid-body step ordering used by the retail
/// <c>hkRigidBody</c> world integrator (AA ships Havok 2.3) — not fully explicit Euler.
/// </para>
/// Chassis basis (see <see cref="HkVehicleData"/>): +Z forward, +X right, +Y up.
/// Inertia is stored as a world/body-aligned diagonal inverse for the skeleton phase;
/// full tensor rotation lands in a later module.
/// </summary>
public sealed class HkRigidBody
{
    // --- pose ---
    public float PosX;
    public float PosY;
    public float PosZ;

    /// <summary>Orientation quaternion (x, y, z, w). Identity = (0,0,0,1).</summary>
    public float QuatX;
    public float QuatY;
    public float QuatZ;
    public float QuatW = 1f;

    // --- velocity ---
    public float LinVelX;
    public float LinVelY;
    public float LinVelZ;

    public float AngVelX;
    public float AngVelY;
    public float AngVelZ;

    // --- force / torque accumulators (cleared at end of Integrate) ---
    public float ForceX;
    public float ForceY;
    public float ForceZ;

    public float TorqueX;
    public float TorqueY;
    public float TorqueZ;

    // --- mass properties ---
    public float Mass = HkPhysicsConstants.UnitMass;
    public float InvMass = HkPhysicsConstants.UnitMass;

    /// <summary>Diagonal inverse inertia (Ixx⁻¹, Iyy⁻¹, Izz⁻¹).</summary>
    public float InvInertiaX = 1f;
    public float InvInertiaY = 1f;
    public float InvInertiaZ = 1f;

    /// <summary>
    /// Sets <see cref="Mass"/> and derives <see cref="InvMass"/> (0 when mass ≤ 0).
    /// </summary>
    public void SetMass(float mass)
    {
        Mass = mass;
        InvMass = mass > 0f ? 1f / mass : 0f;
    }

    /// <summary>Accumulates a world-space force for the next <see cref="Integrate"/>.</summary>
    public void ApplyForce(float fx, float fy, float fz)
    {
        ForceX += fx;
        ForceY += fy;
        ForceZ += fz;
    }

    /// <summary>Accumulates a world-space torque for the next <see cref="Integrate"/>.</summary>
    public void ApplyTorque(float tx, float ty, float tz)
    {
        TorqueX += tx;
        TorqueY += ty;
        TorqueZ += tz;
    }

    /// <summary>
    /// Instantaneous impulse at a world-space point (relative to COM = position).
    /// Updates linear and angular velocity immediately (does not touch force/torque accumulators).
    /// <c>Δv = J · invMass</c>, <c>Δω = invI · (r × J)</c> with diagonal invI.
    /// </summary>
    public void ApplyPointImpulse(float jx, float jy, float jz, float pointX, float pointY, float pointZ)
    {
        ApplyPointImpulseCore(jx, jy, jz, pointX, pointY, pointZ, yawTorqueOnly: false);
    }

    /// <summary>
    /// Like <see cref="ApplyPointImpulse"/> but only the <b>world-Y (yaw)</b> component of
    /// <c>r×J</c> is applied. Pitch/roll torques from ground-plane tire forces are dropped —
    /// those were the live NPC tumble path under the reduced friction model. Yaw is kept so
    /// front-axle lateral impulses can steer the chassis.
    /// </summary>
    /// <param name="yawTorqueScale">
    /// Multiplier on the yaw torque (0 = pure COM linear, 1 = full yaw arm). Live friction
    /// scales this by planar speed so stopped cars do not "clock-spin" from tire forces.
    /// </param>
    public void ApplyPointImpulseYawOnly(
        float jx, float jy, float jz,
        float pointX, float pointY, float pointZ,
        float yawTorqueScale = 1f)
    {
        ApplyPointImpulseCore(jx, jy, jz, pointX, pointY, pointZ, yawTorqueOnly: true, yawTorqueScale);
    }

    private void ApplyPointImpulseCore(
        float jx, float jy, float jz,
        float pointX, float pointY, float pointZ,
        bool yawTorqueOnly,
        float yawTorqueScale = 1f)
    {
        LinVelX += jx * InvMass;
        LinVelY += jy * InvMass;
        LinVelZ += jz * InvMass;

        float rx = pointX - PosX;
        float ry = pointY - PosY;
        float rz = pointZ - PosZ;

        // r × J
        float tx = ry * jz - rz * jy;
        float ty = rz * jx - rx * jz;
        float tz = rx * jy - ry * jx;

        if (yawTorqueOnly)
        {
            if (yawTorqueScale != 0f && float.IsFinite(yawTorqueScale))
                AngVelY += ty * InvInertiaY * yawTorqueScale;
            return;
        }

        AngVelX += tx * InvInertiaX;
        AngVelY += ty * InvInertiaY;
        AngVelZ += tz * InvInertiaZ;
    }

    /// <summary>
    /// Semi-implicit Euler step of length <paramref name="dt"/> seconds.
    /// When <paramref name="applyGravity"/> is true, adds <c>F += m · g</c> with
    /// <c>g = (0, gravityY, 0)</c> before integrating.
    /// Non-finite or non-positive <paramref name="dt"/> is a no-op.
    /// </summary>
    /// <param name="gravityY">
    /// World gravity Y (default <see cref="HkPhysicsConstants.DefaultGravityY"/>).
    /// Callers with per-vehicle gravity (e.g. from server config) pass it here.
    /// </param>
    public void Integrate(float dt, bool applyGravity = true, float gravityY = HkPhysicsConstants.DefaultGravityY)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
            return;

        if (applyGravity && InvMass > 0f)
        {
            // F_g = m * g  →  a = invMass * F_g = g
            ForceY += Mass * gravityY;
        }

        // 1) velocities from accumulators (semi-implicit / symplectic Euler)
        LinVelX += ForceX * InvMass * dt;
        LinVelY += ForceY * InvMass * dt;
        LinVelZ += ForceZ * InvMass * dt;

        AngVelX += TorqueX * InvInertiaX * dt;
        AngVelY += TorqueY * InvInertiaY * dt;
        AngVelZ += TorqueZ * InvInertiaZ * dt;

        // Clear accumulators after they have been consumed
        ForceX = ForceY = ForceZ = 0f;
        TorqueX = TorqueY = TorqueZ = 0f;

        // Clamp velocities before pose integrate (server stability — reduced model can explode).
        ClampVelocity(
            HkPhysicsConstants.MaxLinearSpeed,
            HkPhysicsConstants.MaxAngularSpeed);

        // 2) pose from *updated* velocities
        PosX += LinVelX * dt;
        PosY += LinVelY * dt;
        PosZ += LinVelZ * dt;

        IntegrateOrientation(dt);
    }

    /// <summary>Clamp linear and angular speeds to finite caps (no-op if non-finite).</summary>
    public void ClampVelocity(float maxLinSpeed, float maxAngSpeed)
    {
        if (maxLinSpeed > 0f)
        {
            float ls = LinVelX * LinVelX + LinVelY * LinVelY + LinVelZ * LinVelZ;
            if (!float.IsFinite(ls))
            {
                LinVelX = LinVelY = LinVelZ = 0f;
            }
            else if (ls > maxLinSpeed * maxLinSpeed)
            {
                float s = maxLinSpeed / MathF.Sqrt(ls);
                LinVelX *= s;
                LinVelY *= s;
                LinVelZ *= s;
            }
        }

        if (maxAngSpeed > 0f)
        {
            float asq = AngVelX * AngVelX + AngVelY * AngVelY + AngVelZ * AngVelZ;
            if (!float.IsFinite(asq))
            {
                AngVelX = AngVelY = AngVelZ = 0f;
            }
            else if (asq > maxAngSpeed * maxAngSpeed)
            {
                float s = maxAngSpeed / MathF.Sqrt(asq);
                AngVelX *= s;
                AngVelY *= s;
                AngVelZ *= s;
            }
        }
    }

    /// <summary>
    /// Quaternion rate from angular velocity: <c>q̇ = ½ · ω_q · q</c> with pure-vector
    /// <c>ω_q = (ωx, ωy, ωz, 0)</c>, then <c>q ← normalize(q + q̇ · dt)</c>.
    /// </summary>
    private void IntegrateOrientation(float dt)
    {
        float wx = AngVelX;
        float wy = AngVelY;
        float wz = AngVelZ;

        float qx = QuatX;
        float qy = QuatY;
        float qz = QuatZ;
        float qw = QuatW;

        // 0.5 * ω_q ⊗ q  (ω_w = 0)
        float halfDt = 0.5f * dt;
        float dqx = halfDt * (wx * qw + wy * qz - wz * qy);
        float dqy = halfDt * (-wx * qz + wy * qw + wz * qx);
        float dqz = halfDt * (wx * qy - wy * qx + wz * qw);
        float dqw = halfDt * (-wx * qx - wy * qy - wz * qz);

        qx += dqx;
        qy += dqy;
        qz += dqz;
        qw += dqw;

        float lenSq = qx * qx + qy * qy + qz * qz + qw * qw;
        if (lenSq > 0f && float.IsFinite(lenSq))
        {
            float invLen = 1f / MathF.Sqrt(lenSq);
            QuatX = qx * invLen;
            QuatY = qy * invLen;
            QuatZ = qz * invLen;
            QuatW = qw * invLen;
        }
        else
        {
            // Degenerate — reset to identity
            QuatX = 0f;
            QuatY = 0f;
            QuatZ = 0f;
            QuatW = 1f;
        }
    }
}
