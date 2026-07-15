namespace AutoCore.Game.Npc;

using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Per-vehicle multi-sample ground plane under the chassis for server-authored pitch/roll + Y.
/// </summary>
/// <remarks>
/// Each vehicle is sampled independently every tick at its own XZ. When clonebase
/// <c>WheelHardPoints</c> are available, height and stance use those wheel tracks (not a
/// one-size-fits-all box). Y is never the average of far look-ahead samples alone — that
/// floated chassis on berms. Client local axes: +Z forward, +X right, Y up
/// (<c>FUN_004e8a40</c> / <c>004e8ad0</c>).
/// </remarks>
public static class TerrainContactPlane
{
    public static float DefaultHalfLength { get; set; } = 4.0f;

    public static float DefaultHalfWidth { get; set; } = 1.8f;

    public static float MaxPitchRollRadians { get; set; } = 0.65f;

    /// <summary>Extra Y above contact. Ghost pose is chassis origin — keep ~0.</summary>
    public static float DefaultGroundClearance { get; set; } = 0f;

    public delegate bool HeightSample(float worldX, float worldZ, out float worldY);

    /// <summary>
    /// Align using a generic half-length/width footprint (no per-vehicle hardpoints).
    /// </summary>
    public static bool TryAlign(
        Vector3 position,
        float yawRadians,
        HeightSample sample,
        out Vector3 groundedPosition,
        out Quaternion rotation,
        float halfLength = -1f,
        float halfWidth = -1f,
        float groundClearance = -1f)
        => TryAlign(
            position, yawRadians, sample, out groundedPosition, out rotation,
            halfLength, halfWidth, groundClearance, wheelHardPoints: null);

    /// <summary>
    /// Per-vehicle align. When <paramref name="wheelHardPoints"/> are present, samples terrain
    /// under each wheel track for Y and for pitch/roll extremes.
    /// </summary>
    public static bool TryAlign(
        Vector3 position,
        float yawRadians,
        HeightSample sample,
        out Vector3 groundedPosition,
        out Quaternion rotation,
        float halfLength,
        float halfWidth,
        float groundClearance,
        Vector3[] wheelHardPoints)
    {
        groundedPosition = position;
        rotation = YawOnly(yawRadians);

        if (sample == null)
            return false;

        // Finite non-negative clearance wins; NaN/-1 → default pad.
        var clearance = float.IsFinite(groundClearance) && groundClearance >= 0f
            ? groundClearance
            : DefaultGroundClearance;
        var fwdX = MathF.Sin(yawRadians);
        var fwdZ = MathF.Cos(yawRadians);
        var rightX = MathF.Cos(yawRadians);
        var rightZ = -MathF.Sin(yawRadians);

        float yC, yF, yB, yR, yL;
        float hl;
        float hw;

        if (TryCollectWheelSamples(
                position, fwdX, fwdZ, rightX, rightZ, wheelHardPoints, sample,
                out yC, out yF, out yB, out yR, out yL, out hl, out hw))
        {
            // Per-vehicle wheel tracks succeeded.
        }
        else
        {
            // Generic footprint around this vehicle's position.
            // Stryker disable once equality : half-dim default sentinel
            hl = halfLength > 0f ? halfLength : DefaultHalfLength;
            // Stryker disable once equality : half-dim default sentinel
            // Stryker disable once conditional : ternary covered by halfWidth<=0 tests + slope independence on linear grades
            hw = halfWidth > 0f ? halfWidth : DefaultHalfWidth;
            if (!sample(position.X, position.Z, out yC))
                return false;
            if (!sample(position.X + (fwdX * hl), position.Z + (fwdZ * hl), out yF))
                return false;
            if (!sample(position.X - (fwdX * hl), position.Z - (fwdZ * hl), out yB))
                return false;
            if (!sample(position.X + (rightX * hw), position.Z + (rightZ * hw), out yR))
                return false;
            if (!sample(position.X - (rightX * hw), position.Z - (rightZ * hw), out yL))
                return false;
        }

        // Y = center / mean wheel height at this vehicle (not far look-ahead average alone).
        var groundedY = yC + clearance;
        groundedPosition = new Vector3(position.X, groundedY, position.Z);

        // Pitch / roll from sample height differences (same geometry as a plane fit, fewer ops).
        // span = full track length/width used when sampling ±half*axis.
        var spanF = Math.Max(hl * 2f, 1e-3f);
        var spanR = Math.Max(hw * 2f, 1e-3f);
        // Signs match client +Z forward / +X right: climb (front higher) → +pitch (fwd.Y>0);
        // right side higher → +roll (right.Y>0).
        var pitch = MathF.Atan2(yB - yF, spanF);
        var roll = MathF.Atan2(yR - yL, spanR);

        var maxTilt = Math.Max(0f, MaxPitchRollRadians);
        pitch = Math.Clamp(pitch, -maxTilt, maxTilt);
        roll = Math.Clamp(roll, -maxTilt, maxTilt);

        rotation = FromYawPitchRoll(yawRadians, pitch, roll);
        return true;
    }

    public static bool TryAlign(
        Vector3 position,
        float yawRadians,
        MapTerrainHeightfield heightfield,
        out Vector3 groundedPosition,
        out Quaternion rotation,
        float halfLength = -1f,
        float halfWidth = -1f,
        float groundClearance = -1f,
        Vector3[] wheelHardPoints = null)
    {
        if (heightfield == null)
        {
            groundedPosition = position;
            rotation = YawOnly(yawRadians);
            return false;
        }

        return TryAlign(
            position,
            yawRadians,
            (float x, float z, out float y) => heightfield.TrySample(x, z, out y),
            out groundedPosition,
            out rotation,
            halfLength,
            halfWidth,
            groundClearance,
            wheelHardPoints);
    }

    /// <summary>
    /// Sample under this vehicle's wheel hardpoints. yC = mean of all wheel heights (per-vehicle).
    /// yF/yB/yR/yL = mean of wheels in the front/back/right/left half for stance.
    /// </summary>
    internal static bool TryCollectWheelSamples(
        Vector3 position,
        float fwdX, float fwdZ, float rightX, float rightZ,
        Vector3[] wheelHardPoints,
        HeightSample sample,
        out float yC,
        out float yF,
        out float yB,
        out float yR,
        out float yL,
        out float halfLength,
        out float halfWidth)
    {
        yC = yF = yB = yR = yL = 0f;
        halfLength = DefaultHalfLength;
        halfWidth = DefaultHalfWidth;

        if (wheelHardPoints == null || sample == null)
            return false;

        var sumAll = 0f;
        var nAll = 0;
        var sumF = 0f;
        var nF = 0;
        var sumB = 0f;
        var nB = 0;
        var sumR = 0f;
        var nR = 0;
        var sumL = 0f;
        var nL = 0;
        var maxAbsZ = 0f;
        var maxAbsX = 0f;

        // Stryker disable equality
        // Stryker disable logical
        foreach (var hp in wheelHardPoints)
        {
            if (MathF.Abs(hp.X) < 1e-4f && MathF.Abs(hp.Z) < 1e-4f && MathF.Abs(hp.Y) < 1e-4f)
                continue;

            maxAbsZ = Math.Max(maxAbsZ, MathF.Abs(hp.Z));
            maxAbsX = Math.Max(maxAbsX, MathF.Abs(hp.X));

            // world = pos + right*localX + forward*localZ
            var wx = position.X + (rightX * hp.X) + (fwdX * hp.Z);
            var wz = position.Z + (rightZ * hp.X) + (fwdZ * hp.Z);
            if (!sample(wx, wz, out var hy))
                continue;

            sumAll += hy;
            nAll++;

            if (hp.Z >= 0f)
            {
                sumF += hy;
                nF++;
            }
            else
            {
                sumB += hy;
                nB++;
            }

            if (hp.X >= 0f)
            {
                sumR += hy;
                nR++;
            }
            else
            {
                sumL += hy;
                nL++;
            }
        }

        if (nAll < 2)
            return false;

        yC = sumAll / nAll;
        yF = nF > 0 ? sumF / nF : yC;
        yB = nB > 0 ? sumB / nB : yC;
        yR = nR > 0 ? sumR / nR : yC;
        yL = nL > 0 ? sumL / nL : yC;

        if (maxAbsZ > 0.5f)
            halfLength = Math.Clamp(maxAbsZ, 1.5f, 8f);
        if (maxAbsX > 0.3f)
            halfWidth = Math.Clamp(maxAbsX, 0.8f, 4f);
        // Stryker restore all

        return true;
    }

    /// <summary>
    /// Resolve lateral footprint from clonebase hardpoints (and default clearance).
    /// </summary>
    public static void ResolveVehicleFootprint(
        Vector3[] wheelHardPoints,
        float[] wheelRadii,
        float suspensionLengthFront,
        out float halfLength,
        out float halfWidth,
        out float clearance)
    {
        halfLength = DefaultHalfLength;
        halfWidth = DefaultHalfWidth;
        _ = wheelRadii;
        _ = suspensionLengthFront;
        clearance = DefaultGroundClearance;

        var maxAbsZ = 0f;
        var maxAbsX = 0f;
        var any = false;
        if (wheelHardPoints != null)
        {
            // Stryker disable equality
            foreach (var hp in wheelHardPoints)
            {
                if (MathF.Abs(hp.X) < 1e-4f && MathF.Abs(hp.Z) < 1e-4f && MathF.Abs(hp.Y) < 1e-4f)
                    continue;
                any = true;
                maxAbsZ = Math.Max(maxAbsZ, MathF.Abs(hp.Z));
                maxAbsX = Math.Max(maxAbsX, MathF.Abs(hp.X));
            }
            // Stryker restore equality
        }

        if (!any)
            return;

        // Stryker disable equality
        if (maxAbsZ > 0.5f)
            halfLength = Math.Clamp(maxAbsZ, 1.5f, 8f);
        if (maxAbsX > 0.3f)
            halfWidth = Math.Clamp(maxAbsX, 0.8f, 4f);
        // Stryker restore equality
    }

    public static Vector3 ForwardFromQuaternion(Quaternion q)
    {
        var x = q.X;
        var y = q.Y;
        var z = q.Z;
        var w = q.W;
        return new Vector3(
            2f * ((z * x) + (y * w)),
            2f * ((z * y) - (x * w)),
            1f - (2f * ((x * x) + (y * y))));
    }

    internal static Quaternion YawOnly(float yaw)
        => FromYawPitchRoll(yaw, pitch: 0f, roll: 0f);

    /// <summary>
    /// Client axes: +Z forward, +X right, +Y up. Composition qYaw * qPitch * qRoll.
    /// </summary>
    internal static Quaternion FromYawPitchRoll(float yaw, float pitch, float roll)
    {
        var hy = yaw * 0.5f;
        var hp = pitch * 0.5f;
        var hr = roll * 0.5f;
        var sy = MathF.Sin(hy);
        var cy = MathF.Cos(hy);
        var sp = MathF.Sin(hp);
        var cp = MathF.Cos(hp);
        var sr = MathF.Sin(hr);
        var cr = MathF.Cos(hr);

        // yaw(Y) * pitch(X) * roll(Z)
        return new Quaternion(
            (cy * sp * cr) + (sy * cp * sr),
            (sy * cp * cr) - (cy * sp * sr),
            (cy * cp * sr) - (sy * sp * cr),
            (cy * cp * cr) + (sy * sp * sr));
    }

    /// <summary>Legacy basis→quat (tests / tools). Prefer <see cref="FromYawPitchRoll"/>.</summary>
    internal static Quaternion FromBasisColumns(
        float rightX, float rightY, float rightZ,
        float upX, float upY, float upZ,
        float fwdX, float fwdY, float fwdZ)
    {
        // Orthonormal assumed; build yaw/pitch/roll from axes when possible.
        var yaw = MathF.Atan2(fwdX, fwdZ);
        var pitch = MathF.Atan2(-fwdY, MathF.Sqrt((fwdX * fwdX) + (fwdZ * fwdZ)));
        var roll = MathF.Atan2(rightY, upY);
        _ = rightX;
        _ = rightZ;
        _ = upX;
        _ = upZ;
        return FromYawPitchRoll(yaw, pitch, roll);
    }
}
