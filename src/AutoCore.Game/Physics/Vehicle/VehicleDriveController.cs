namespace AutoCore.Game.Physics.Vehicle;

using AutoCore.Game.Structures;

/// <summary>
/// Pure port of client <c>CVOGVehicle::MoveToTarget3DPoint</c> @ <c>0x004fc650</c>.
/// Generates throttle / steer / sharp axes from chassis pose and an AI aim point.
/// Does not write chassis position — drive-input generation only.
/// </summary>
/// <remarks>
/// Sign convention (retail, do not "normalize"): normal forward driving uses base = <c>-1</c>,
/// so throttle at entity+0x614 is negative when driving toward the aim. Reverse (aim behind and
/// <c>allowReverse</c>) uses base = <c>+1</c>. Spec:
/// <c>docs/reconstruction/physics/drive-controller-spec.md</c>.
/// Constants verified via Ghidra <c>read_memory</c> (image base 0x400000).
/// </remarks>
public static class VehicleDriveController
{
    /// <summary>
    /// Compute drive axes matching client math.
    /// </summary>
    /// <param name="position">Chassis world position.</param>
    /// <param name="right">Chassis right unit vector.</param>
    /// <param name="forward">Chassis forward unit vector.</param>
    /// <param name="velocity">Chassis linear velocity.</param>
    /// <param name="aim">AI aim / waypoint world point.</param>
    /// <param name="acceptDist">Arrival planar radius (client param_2).</param>
    /// <param name="cruiseScale">Cruise speed multiplier (client param_3).</param>
    /// <param name="allowReverse">Enable reverse when aim is behind (client param_5).</param>
    /// <param name="alwaysDrive">
    /// When true, skip arrival brake even inside <paramref name="acceptDist"/>
    /// (client entity+0x103).
    /// </param>
    /// <returns>
    /// Throttle (+0x614), steer (+0x618), sharp/handbrake-assist (+0x61c as 0/1).
    /// On arrival (inside acceptDist and not <paramref name="alwaysDrive"/>): throttle 0, sharp 1;
    /// steer is returned as 0 (client leaves +0x618 unchanged — callers may ignore).
    /// </returns>
    public static (float Throttle, float Steer, byte Sharp) ComputeAxes(
        Vector3 position,
        Vector3 right,
        Vector3 forward,
        Vector3 velocity,
        Vector3 aim,
        float acceptDist,
        float cruiseScale,
        bool allowReverse,
        bool alwaysDrive = false)
    {
        var dx = aim.X - position.X;
        var dy = aim.Y - position.Y;
        var dz = aim.Z - position.Z;

        // Planar distance (Y ignored) — decompile SQRT(dx²+dz²).
        var distXZ = MathF.Sqrt((dx * dx) + (dz * dz));

        // Arrival gate: drive when acceptDist < distXZ OR alwaysDrive.
        // Else: handbrake (sharp=1), longitudinal input 0, steer left unchanged.
        if (!(acceptDist < distXZ || alwaysDrive))
            return (0f, 0f, 1);

        // Full 3D normalize of aim−pos.
        var m2 = (dx * dx) + (dy * dy) + (dz * dz);
        var inv = m2 != 0f ? HkPhysicsConstants.One / MathF.Sqrt(m2) : 0f;
        var dirX = dx * inv;
        var dirY = dy * inv;
        var dirZ = dz * inv;

        // Projections (right / forward from chassis basis extractors).
        var lateral = (right.X * dirX) + (right.Y * dirY) + (right.Z * dirZ);
        var fAlign = (forward.X * dirX) + (forward.Y * dirY) + (forward.Z * dirZ);
        var speed = MathF.Sqrt(
            (velocity.X * velocity.X) + (velocity.Y * velocity.Y) + (velocity.Z * velocity.Z));
        var fwdSpeed = (velocity.X * forward.X) + (velocity.Y * forward.Y) + (velocity.Z * forward.Z);

        // Base direction: -1 forward (normal), +1 reverse when allowReverse && fAlign < -0.4.
        // Retail compares fAlign against (float)_DAT_009cd238 (-0.4 double).
        var reverseGate = (float)HkPhysicsConstants.ReverseAlignGate;
        float baseDir;
        if (!allowReverse || reverseGate <= fAlign)
            baseDir = -HkPhysicsConstants.One;
        else
            baseDir = HkPhysicsConstants.One;

        // Steering.
        float steer;
        if (MathF.Abs(lateral) >= HkPhysicsConstants.SteerDeadband)
        {
            // steer = clamp(base * lateral * 2.0, -1, +1)
            var raw = baseDir * lateral * HkPhysicsConstants.SteerGain;
            steer = Math.Clamp(raw, -HkPhysicsConstants.One, HkPhysicsConstants.One);
        }
        else if (fAlign >= 0f)
        {
            // Deadband, facing toward aim → straighten.
            steer = 0f;
        }
        else
        {
            // Deadband, facing away → hard spin in lateral sign direction.
            steer = lateral > 0f ? HkPhysicsConstants.One : -HkPhysicsConstants.One;
        }

        // Throttle starts at base; scale only once speed > 5.
        var thr = baseDir;
        if (speed > HkPhysicsConstants.ThrottleSpeedGate)
        {
            // Near-target ease: thr *= distXZ * (1/30) when 0 < distXZ < 30.
            // Decompile uses fVar7==0 after steer write: (0 < distXZ) && (distXZ < 30).
            if (distXZ > 0f && distXZ < HkPhysicsConstants.NearTargetDistance)
                thr *= distXZ * (HkPhysicsConstants.One / HkPhysicsConstants.NearTargetDistance);

            // Cruise scale when cruiseScale > 0.1; negate if currently moving backward.
            if (cruiseScale > HkPhysicsConstants.CruiseScaleMin)
            {
                var cs = fwdSpeed < 0f ? -cruiseScale : cruiseScale;
                thr *= cs;
            }
        }

        // Sharp / handbrake-assist: speed > 15 && |lateral| > 0.7.
        byte sharp = 0;
        if (speed > HkPhysicsConstants.SharpSpeedGate
            && MathF.Abs(lateral) > HkPhysicsConstants.SharpLateralThreshold)
        {
            sharp = 1;
        }

        return (thr, steer, sharp);
    }
}
