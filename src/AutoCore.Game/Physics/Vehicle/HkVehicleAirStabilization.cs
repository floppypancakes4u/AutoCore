namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Port-ready air-stabilization / upright-restore essentials for server sim.
/// </summary>
/// <remarks>
/// <para>
/// Retail map (do not merge these mechanisms):
/// <list type="bullet">
/// <item>
/// <b>Upright-restore</b> — <c>VehicleAction_applyAction @ 0x598650</c> non-mode-0x02 path:
/// when <c>0.1 &lt; bodyUp·worldUp &lt; 0.7</c> and throttle ≠ 0, apply righting angular impulse
/// (see <c>fn_upright_restore.md</c>, plate <c>DAT_00af3380</c>).
/// </item>
/// <item>
/// <b>Continuous AVD</b> — <c>hkAngularVelocityDamper_update @ 0x64d810</c>: already ported in
/// <see cref="HkVehicleVelocityDamper"/>. Do not re-implement spin scaling here.
/// </item>
/// <item>
/// <b>Collision-window airStabilization</b> — <c>VehicleAction_airStabilization @ 0x598320</c>:
/// while <c>nowMs − lastCollisionMs &lt; 6400</c>, set in-collision and (if moving) apply an
/// entity-vtbl corrective impulse; on window expiry zero vel + re-ground. Full path needs
/// entity last-collision stamp, stabilizer slots at entity+0x260, and the entity impulse
/// builder (vtbl+0x3c). <b>DEFERRED</b> — helpers accept timestamps as parameters only.
/// </item>
/// <item>
/// <b>Re-ground Y raise</b> — <c>DAT_00a110d8 = 10.0</c> is <c>pos.y + 10</c> before
/// <c>CVOGMap_CastTerrainHeight</c> on recovery. Client/recovery only; <b>not</b> server hot path.
/// Use <see cref="ComputeReGroundCastStartY"/> when implementing optional recovery.
/// </item>
/// </list>
/// </para>
/// Ghidra: decompile <c>0x598320</c> (window + re-ground), upright gate in applyAction.
/// </remarks>
public static class HkVehicleAirStabilization
{
    /// <summary>
    /// Retail upright-restore gate: <c>UprightRestoreMinDot &lt; upDot &lt; UprightRestoreDot</c>
    /// (<c>0.1 &lt; bodyUp·worldUp &lt; 0.7</c>). Equivalent to requiring
    /// <c>|dot| &lt; 0.7</c> while rejecting near-inverted / inverted chassis (lower guard).
    /// </summary>
    public static bool NeedsUprightRestore(float bodyUpDotWorldUp)
    {
        return bodyUpDotWorldUp < HkPhysicsConstants.UprightRestoreDot
               && bodyUpDotWorldUp > HkPhysicsConstants.UprightRestoreMinDot;
    }

    /// <summary>
    /// Compute upright-restore angular impulse (velocity-space Δω components) toward world-up.
    /// </summary>
    /// <param name="bodyUpX">Chassis body-up world X (unit preferred).</param>
    /// <param name="bodyUpY">Chassis body-up world Y.</param>
    /// <param name="bodyUpZ">Chassis body-up world Z.</param>
    /// <param name="angVelX">Current angular velocity X (damp term).</param>
    /// <param name="angVelY">Current angular velocity Y.</param>
    /// <param name="angVelZ">Current angular velocity Z.</param>
    /// <param name="throttle">Drive throttle (<c>param_2[1]</c>); scales mag and damp. Zero ⇒ no impulse.</param>
    /// <param name="dt">Substep dt (<c>param_2[0]</c>).</param>
    /// <param name="invInertia">Scalar inv-inertia proxy (retail <c>1 / rb+0x2c</c> when nonzero).</param>
    /// <param name="ix">Impulse / Δω X.</param>
    /// <param name="iy">Impulse / Δω Y.</param>
    /// <param name="iz">Impulse / Δω Z.</param>
    /// <returns>True when a finite non-zero righting impulse was produced.</returns>
    /// <remarks>
    /// Formula (port recipe from <c>fn_upright_restore.md</c> §5 / §8, simplified axis):
    /// <c>axis = normalize(bodyUp × worldUp)</c>,
    /// <c>angle = acos(clamp(dot, -1, 1))</c>,
    /// <c>m = invI · dt · 0.8 · angle · throttle</c>,
    /// <c>impulse = axis·m − angVel·(0.1·throttle)</c>.
    /// Retail builds a slightly richer desired basis from a secondary rotated axis; shortest-arc
    /// axis is the server-sim essential for the same 0.7 gate + throttle scaling.
    /// </remarks>
    public static bool TryComputeUprightImpulse(
        float bodyUpX,
        float bodyUpY,
        float bodyUpZ,
        float angVelX,
        float angVelY,
        float angVelZ,
        float throttle,
        float dt,
        float invInertia,
        out float ix,
        out float iy,
        out float iz)
    {
        ix = 0f;
        iy = 0f;
        iz = 0f;

        if (!float.IsFinite(dt) || dt <= 0f)
            return false;
        if (!float.IsFinite(throttle) || throttle == 0f)
            return false;
        if (!float.IsFinite(invInertia))
            return false;

        // worldUp = (0, 1, 0) — DAT_00af3390/94/98
        float upDot = bodyUpY; // bodyUp · (0,1,0)
        if (!NeedsUprightRestore(upDot))
            return false;

        // axis = bodyUp × worldUp  → rotates bodyUp toward worldUp
        // (sx,sy,sz)×(0,1,0) = (sy*0 - sz*1, sz*0 - sx*0, sx*1 - sy*0) = (-sz, 0, sx)
        float ax = -bodyUpZ;
        float ay = 0f;
        float az = bodyUpX;
        float axisLen = PhysicsMath.Length(ax, ay, az);
        if (axisLen <= 1e-8f)
            return false;

        float invLen = 1f / axisLen;
        ax *= invLen;
        ay *= invLen;
        az *= invLen;

        float c = PhysicsMath.Clamp(upDot, -1f, 1f);
        float angle = MathF.Acos(c);

        float m = invInertia * dt * HkPhysicsConstants.UprightRestoreMagScale * angle * throttle;
        float damp = HkPhysicsConstants.UprightRestoreAngDamp * throttle;

        ix = ax * m - angVelX * damp;
        iy = ay * m - angVelY * damp;
        iz = az * m - angVelZ * damp;

        if (!float.IsFinite(ix) || !float.IsFinite(iy) || !float.IsFinite(iz))
        {
            ix = 0f;
            iy = 0f;
            iz = 0f;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Opt-in: if the upright gate fires, add the righting impulse to angular velocity.
    /// Clear params: body-up vector + angVel refs + throttle/dt/invI.
    /// Safe no-op when upright, inverted, zero throttle, or bad dt.
    /// </summary>
    public static bool ApplyUprightRestore(
        float bodyUpX,
        float bodyUpY,
        float bodyUpZ,
        ref float angVelX,
        ref float angVelY,
        ref float angVelZ,
        float throttle,
        float dt,
        float invInertia)
    {
        if (!TryComputeUprightImpulse(
                bodyUpX, bodyUpY, bodyUpZ,
                angVelX, angVelY, angVelZ,
                throttle, dt, invInertia,
                out float ix, out float iy, out float iz))
            return false;

        angVelX += ix;
        angVelY += iy;
        angVelZ += iz;
        return true;
    }

    /// <summary>
    /// Optional recovery API: cast-start Y = <paramref name="posY"/> +
    /// <see cref="HkPhysicsConstants.ReGroundYRaise"/> (10.0).
    /// <b>Not</b> used on the server hot path — client/post-collision re-ground only
    /// (<c>airStabilization</c> recovery at <c>0x5985ad</c>).
    /// </summary>
    public static float ComputeReGroundCastStartY(float posY)
        => posY + HkPhysicsConstants.ReGroundYRaise;

    /// <summary>
    /// Optional recovery API: world Y after terrain cast (retail writes cast result, not startY).
    /// </summary>
    public static float ResolveReGroundPositionY(float terrainHeight)
        => terrainHeight;

    /// <summary>
    /// Param-based collision-window gate: <c>(nowMs − lastCollisionMs) &lt; windowMs</c>.
    /// Entity <c>+0x14</c> stamp wiring is <b>DEFERRED</b> — callers pass times explicitly.
    /// Default window = <see cref="HkPhysicsConstants.CollisionWindowMs"/> (6400).
    /// </summary>
    public static bool IsInCollisionWindow(
        int nowMs,
        int lastCollisionMs,
        int windowMs = HkPhysicsConstants.CollisionWindowMs)
    {
        // Match unsigned-ish retail compare: delta = now - last; active while delta &lt; 0x1900.
        int delta = nowMs - lastCollisionMs;
        return delta >= 0 && delta < windowMs;
    }

    /// <summary>
    /// True on the post-window recovery edge (was in-collision flag set, window now expired).
    /// Full recovery side-effects (zero vel, SetDriveAxes, stabilizer reset, re-ground) are
    /// <b>DEFERRED</b> — they need entity/physics object ownership outside this pure module.
    /// </summary>
    public static bool ShouldRunPostCollisionRecovery(
        int nowMs,
        int lastCollisionMs,
        bool wasInCollision,
        int windowMs = HkPhysicsConstants.CollisionWindowMs)
    {
        if (!wasInCollision)
            return false;
        return !IsInCollisionWindow(nowMs, lastCollisionMs, windowMs);
    }

    /// <summary>
    /// In-window airStab speed gate: impulse only when |v| &gt; <see cref="HkPhysicsConstants.AirStabMovingEpsilon"/>.
    /// Corrective impulse <i>construction</i> (entity vtbl+0x3c) remains deferred.
    /// </summary>
    public static bool IsChassisMovingForAirStab(float linVelX, float linVelY, float linVelZ)
    {
        float speed = PhysicsMath.Length(linVelX, linVelY, linVelZ);
        return speed > HkPhysicsConstants.AirStabMovingEpsilon;
    }
}
