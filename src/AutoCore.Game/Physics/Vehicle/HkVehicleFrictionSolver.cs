namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Per-axle inputs for the friction solve (aggregated in
/// <c>hkVehicleFramework_postTickApplyForces</c> @ 0x64bc70 before
/// <c>hkVehicleFrictionSolver_solve</c> @ 0x6c4450).
/// </summary>
public struct AxleFrictionInput
{
    /// <summary>Any wheel on this axle is grounded (contact flag).</summary>
    public bool InContact;

    /// <summary>
    /// Drive path enabled (solver gate at chassis body friction row; decomp
    /// <c>*(char*)(pfVar8+1)</c>). When false, longitudinal drive pack is ignored
    /// and long impulse comes only from slip cancel.
    /// </summary>
    public bool DriveEnabled;

    /// <summary>
    /// Axle-averaged drive pack:
    /// <c>Σ (wheelTorque_i · wheelScale_i) / axleWheelCount</c>
    /// (postTick aggregation; torque already clamped [0,1000] upstream).
    /// </summary>
    public float DrivePack;

    /// <summary>
    /// Longitudinal slip velocity Jv_long at the axle contact.
    /// Caller builds this as <b>wheel-relative</b> slip
    /// (contact long vel − spin·radius), not absolute chassis speed.
    /// </summary>
    public float SlipLongitudinal;

    /// <summary>
    /// Lateral slip velocity Jv_lat at the axle contact.
    /// <para>
    /// <b>Caller contract for DAT_00a0f704 (0.25):</b> retail builds the second-row
    /// relative velocity as <c>Jv_lat = priorResidual * 0.25 + J_side · v</c>
    /// (see <see cref="HkVehicleFrictionSolver.WeightLateralJv"/>).
    /// When the caller already supplies a fully-formed Jv_lat, pass it here unchanged —
    /// the solver does <b>not</b> re-apply the 0.25 weight.
    /// </para>
    /// </summary>
    public float SlipLateral;

    /// <summary>
    /// Normal / suspension load magnitude |N| for this axle (aggregated susp force).
    /// Friction impulse limit is μ · |N| · dt. Retail uses |susp impulse| only — no gravity floor.
    /// </summary>
    public float NormalLoad;

    /// <summary>Base friction coefficient μ0 (rear already × RearWheelFrictionScalar at setup).</summary>
    public float Mu0;

    /// <summary>Slip-dependent μ slope; 0 disables the linear curve.</summary>
    public float MuSlope;

    /// <summary>μ ceiling.</summary>
    public float MuMax;

    /// <summary>
    /// Optional inverse effective mass for the longitudinal row (1/Keff_long).
    /// When ≤ 0, derived as <c>1 / chassisInvMass</c> (unit-jacobian linear approx).
    /// </summary>
    public float InvKeffLong;

    /// <summary>
    /// Optional inverse effective mass for the lateral row (1/Keff_lat).
    /// When ≤ 0, derived as <c>1 / chassisInvMass</c>.
    /// </summary>
    public float InvKeffLat;

    /// <summary>
    /// Off-diagonal long/lat effective-mass coupling term <c>b</c> in
    /// <c>Keff = [a b; b d]</c> (from <c>J_long · M⁻¹ · J_lat</c>, lin+ang).
    /// Zero yields the diagonal (uncoupled) solve.
    /// </summary>
    public float Coupling;

    /// <summary>
    /// Optional CFM / regularization added to longitudinal Keff before forming
    /// the drive-path <c>invKeff_reg</c> (setup softness, framework+0x1fc+0x54).
    /// </summary>
    public float Softness;

    /// <summary>
    /// Optional product for anisotropic <see cref="HkVehicleFrictionSolver.BuildCircleProjectionScales"/>
    /// (setup-time axle product). When ≤ 0, isotropic <see cref="HkVehicleFrictionSolver.CircleProjection"/>.
    /// </summary>
    public float CircleProduct;
}

/// <summary>Per-axle longitudinal / lateral friction impulses (solver output).</summary>
public struct AxleFrictionImpulse
{
    /// <summary>Longitudinal (drive / brake) impulse — maps toward WHEEL+0x94.</summary>
    public float Longitudinal;

    /// <summary>Lateral (cornering) impulse — maps toward WHEEL+0xa0.</summary>
    public float Lateral;
}

/// <summary>
/// Port of Havok 2.3 <c>hkVehicleFrictionSolver_solve</c> (0x6c4450) as used by
/// AutoAssault over <b>2 axles</b> (not per-wheel), plus leaf helpers.
/// <para>
/// <b>Ported (C4):</b>
/// </para>
/// <list type="bullet">
/// <item>DrivePack aggregation (postTick 0x64bc70)</item>
/// <item>Diagonal long/lat cancel and coupled 2×2 when <see cref="AxleFrictionInput.Coupling"/> ≠ 0</item>
/// <item><see cref="ComputeInvKeffFromContact"/> / <see cref="ComputeKeffCoupling"/> for J·M⁻¹·Jᵀ (lin+ang, chassis)</item>
/// <item>Drive-pack longitudinal bias with DAT_00a0f298 = 0.5 twice and driveMax = μ0·invK_lat·|N|·dt</item>
/// <item>Slip-dependent μ (μ0 + slip·slope clamped [0, μMax]) → Fmax = μ·|N|·dt</item>
/// <item>Friction-circle via <see cref="CircleProjection"/> (+ optional scale table from <see cref="BuildCircleProjectionScales"/>)</item>
/// <item><see cref="WeightLateralJv"/> documents DAT_00a0f704 = 0.25 caller-side Jv_lat build</item>
/// </list>
/// <para>
/// <b>Residual RE gaps (not bit-exact vs live <c>cb</c>/<c>out</c> goldens):</b>
/// </para>
/// <list type="bullet">
/// <item>Full dual-body (chassis + contact RB) Phase A assembly and Phase C/D body-accumulator writeback</item>
/// <item>Cross-axle coupled 2×2 (ax0 vs ax1 primary rows) as decompiled — port couples long/lat per axle</item>
/// <item>Caller 1/mag² pre-scale + dual-axle ordered circleProjection with couple feedback</item>
/// <item>Full RE driveTarget always folds Jv into the 0.5×0.5 blend when gate open; reduced model only biases when DrivePack ≠ 0</item>
/// <item><c>FUN_006c4150</c> product closed form for scale-table build (API present; product source residual)</item>
/// <item>cb+0xa0 max-slip / airborne lateral zeroing</item>
/// <item>Setup mix0/mix1 drive-pack gains at fw+0x1fc</item>
/// </list>
/// </summary>
public static class HkVehicleFrictionSolver
{
    /// <summary>AA friction solve always runs over exactly 2 axles (front/rear).</summary>
    public const int AxleCount = 2;

    /// <summary>
    /// Length of the ESI projection scale table used by <see cref="CircleProjection"/>
    /// (15 lat samples + terminal 1.0 + ratio + x0).
    /// </summary>
    public const int CircleProjectionScaleCount = 18;

    /// <summary>Max geometric search iterations in circleProjection (CMP EDX, 0x10).</summary>
    public const int CircleProjectionMaxIters = 16;

    /// <summary>
    /// Aggregate per-wheel drive torque into one axle's drive pack
    /// (postTickApplyForces @ 0x64bc70):
    /// <c>drivePack = Σ (torque_i · wheelScale_i) / N</c> where N = axle wheel count.
    /// </summary>
    /// <param name="torques">Per-wheel drive torques on this axle (wheels+0x28[i]).</param>
    /// <param name="wheelScales">Per-wheel scale (WHEEL+0x88).</param>
    public static float AggregateDrivePack(ReadOnlySpan<float> torques, ReadOnlySpan<float> wheelScales)
    {
        int n = torques.Length;
        if (n == 0)
            return 0f;
        if (wheelScales.Length != n)
            throw new ArgumentException("wheelScales length must match torques length.", nameof(wheelScales));

        float sum = 0f;
        for (int i = 0; i < n; i++)
            sum += torques[i] * wheelScales[i];
        return sum / n;
    }

    /// <summary>
    /// Retail second-row relative velocity weight (DAT_00a0f704 = 0.25).
    /// Use when building Jv_lat from a prior residual plus the side-row free velocity:
    /// <c>Jv_lat = priorResidual * 0.25 + sideRowJv</c>.
    /// </summary>
    /// <remarks>
    /// Pass the result as <see cref="AxleFrictionInput.SlipLateral"/>. Do not call this
    /// again inside <see cref="Solve"/> — the solver treats SlipLateral as final Jv.
    /// </remarks>
    public static float WeightLateralJv(float priorResidual, float sideRowJv)
        => priorResidual * HkPhysicsConstants.LateralAngWeight + sideRowJv;

    /// <summary>
    /// Inverse effective mass for one constraint row at a contact point on the chassis
    /// (unit linear jacobian along <paramref name="dir"/>, angular arm <c>r × dir</c>).
    /// <c>Keff = invMass + (r×dir)·diag(I⁻¹)·(r×dir) + softness + ε</c>,
    /// <c>invKeff = 1/Keff</c>. Body-B / dual-body terms are residual.
    /// </summary>
    public static float ComputeInvKeffFromContact(
        float chassisInvMass,
        float invInertiaX,
        float invInertiaY,
        float invInertiaZ,
        float rx,
        float ry,
        float rz,
        float dirX,
        float dirY,
        float dirZ,
        float softness = 0f)
    {
        // jAng = r × dir
        float jx = ry * dirZ - rz * dirY;
        float jy = rz * dirX - rx * dirZ;
        float jz = rx * dirY - ry * dirX;

        float keff = chassisInvMass
                     + jx * jx * invInertiaX
                     + jy * jy * invInertiaY
                     + jz * jz * invInertiaZ
                     + softness
                     + HkPhysicsConstants.InvDenomEpsilon;
        if (keff <= 0f)
            return 0f;
        return HkPhysicsConstants.One / keff;
    }

    /// <summary>
    /// Off-diagonal effective-mass coupling <c>b = J_long · M⁻¹ · J_lat</c> (chassis lin+ang).
    /// </summary>
    public static float ComputeKeffCoupling(
        float chassisInvMass,
        float invInertiaX,
        float invInertiaY,
        float invInertiaZ,
        float rx,
        float ry,
        float rz,
        float longX,
        float longY,
        float longZ,
        float latX,
        float latY,
        float latZ)
    {
        float j0x = ry * longZ - rz * longY;
        float j0y = rz * longX - rx * longZ;
        float j0z = rx * longY - ry * longX;

        float j1x = ry * latZ - rz * latY;
        float j1y = rz * latX - rx * latZ;
        float j1z = rx * latY - ry * latX;

        float linDot = longX * latX + longY * latY + longZ * latZ;
        return chassisInvMass * linDot
               + j0x * j1x * invInertiaX
               + j0y * j1y * invInertiaY
               + j0z * j1z * invInertiaZ;
    }

    /// <summary>
    /// Friction impulse limit: μ(slip) · |N| · dt.
    /// μ = clamp(μ0 + slipSpeed·slope, 0, μMax) when slope ≠ 0; else μ = μ0.
    /// </summary>
    public static float ComputeFrictionLimit(
        float mu0,
        float muSlope,
        float muMax,
        float slipSpeed,
        float normalLoad,
        float dt)
    {
        float mu = mu0;
        if (muSlope != 0f)
        {
            mu = mu0 + slipSpeed * muSlope;
            if (mu > muMax)
                mu = muMax;
            if (mu < 0f)
                mu = 0f;
        }

        return mu * MathF.Abs(normalLoad) * dt;
    }

    /// <summary>
    /// Project (long, lat) onto the friction disk of radius <paramref name="maxImpulse"/>.
    /// Direction preserved; zero/negative limit zeroes both components.
    /// Kept as a pure radial helper; the solve path uses <see cref="CircleProjection"/>.
    /// </summary>
    public static void ClampFrictionCircle(ref float longitudinal, ref float lateral, float maxImpulse)
    {
        if (maxImpulse <= 0f)
        {
            longitudinal = 0f;
            lateral = 0f;
            return;
        }

        float magSq = longitudinal * longitudinal + lateral * lateral;
        float maxSq = maxImpulse * maxImpulse;
        if (magSq <= maxSq || magSq <= 0f)
            return;

        float scale = maxImpulse / MathF.Sqrt(magSq);
        longitudinal *= scale;
        lateral *= scale;
    }

    /// <summary>
    /// Build the 18-float ESI scale table used by <c>circleProjection</c> @ 0x6c3f90
    /// (same fill as <c>FUN_006c4150</c> given a positive <paramref name="product"/>).
    /// Layout: [0..14]=table, [15]=1.0, [16]=ratio, [17]=x0.
    /// </summary>
    /// <param name="product">
    /// Effective-mass × wheel term from setup (fw+0x1fc axle product). Must be &gt; 0.
    /// Exact product formula is a residual RE gap — callers that do not have it should
    /// omit the table and use isotropic <see cref="CircleProjection"/>.
    /// </param>
    /// <param name="scales">Destination of length ≥ <see cref="CircleProjectionScaleCount"/>.</param>
    public static void BuildCircleProjectionScales(float product, Span<float> scales)
    {
        if (scales.Length < CircleProjectionScaleCount)
            throw new ArgumentException(
                $"Expected at least {CircleProjectionScaleCount} scale slots.", nameof(scales));
        if (!(product > 0f) || !float.IsFinite(product))
            throw new ArgumentOutOfRangeException(nameof(product), product, "product must be finite and > 0.");

        // x0 = product * 0.0625  (DAT_00a14000)
        const float inv16 = 0.0625f;
        // exponent 1/15 from double @ 0xa0d300
        const double inv15 = 1.0 / 15.0;

        float x0 = product * inv16;
        if (!(x0 > 0f) || !float.IsFinite(x0))
            throw new ArgumentOutOfRangeException(nameof(product), product, "product*0.0625 must be finite and > 0.");

        float ratio = (float)Math.Pow(1.0 / x0, inv15);
        float x = x0;
        double invProduct = 1.0 / product;

        for (int i = 0; i < 15; i++)
        {
            // table[i] = 1 - (1 - x)^(1/product)
            float oneMinusX = 1f - x;
            float warped = oneMinusX <= 0f
                ? 0f
                : (float)Math.Pow(oneMinusX, invProduct);
            scales[i] = 1f - warped;
            x *= ratio;
        }

        scales[15] = HkPhysicsConstants.One;
        scales[16] = ratio;
        scales[17] = x0;
    }

    /// <summary>
    /// Port of <c>hkVehicleFrictionSolver_circleProjection</c> @ 0x6c3f90.
    /// Projects free long/lat impulses onto the unit disk in invLim-normalized space.
    /// </summary>
    /// <param name="impLong">Longitudinal impulse candidate (work+0x84).</param>
    /// <param name="impLat">Lateral impulse candidate (work+0x80).</param>
    /// <param name="fMaxLong">Longitudinal friction limit Fmax (work+0x8c).</param>
    /// <param name="fMaxLat">Lateral friction limit Fmax (work+0x88).</param>
    /// <param name="scales">
    /// Optional ESI scale table (18 floats from <see cref="BuildCircleProjectionScales"/>).
    /// When empty, uses isotropic radial projection (equivalent to
    /// <see cref="ClampFrictionCircle"/> with radius min(FmaxLong,FmaxLat) when equal).
    /// </param>
    /// <returns>
    /// Lateral residual <c>oldLat − projectedLat</c> (work+0x98); 0 when inside circle.
    /// </returns>
    /// <remarks>
    /// Residual metric write to outAxle[+8] (jacobian-weighted Δimp magnitude) is not
    /// exposed here — reduced consumers only need the projected impulses.
    /// </remarks>
    public static float CircleProjection(
        ref float impLong,
        ref float impLat,
        float fMaxLong,
        float fMaxLat,
        ReadOnlySpan<float> scales = default)
    {
        // invLim = 1 / (Fmax + eps)
        float invLimLong = InverseLimit(fMaxLong);
        float invLimLat = InverseLimit(fMaxLat);

        // Normalize free impulse into unit-limit space
        float nLong = invLimLong * impLong;
        float nLat = invLimLat * impLat;
        float mag2 = nLong * nLong + nLat * nLat;

        // Early out — already inside the unit circle
        if (mag2 < HkPhysicsConstants.One)
            return 0f;

        float sLong;
        float sLat;
        float prevLong;
        float prevLat;
        float prevMag2;

        if (scales.Length >= CircleProjectionScaleCount)
        {
            // Geometric search along anisotropic path (≤16 samples)
            sLong = nLong * scales[17]; // * x0
            sLat = nLat * scales[0];    // * table[0]
            mag2 = sLong * sLong + sLat * sLat;
            prevLong = 0f;
            prevLat = 0f;
            prevMag2 = 0f;
            int iter = 0;

            while (mag2 <= HkPhysicsConstants.One && ++iter < CircleProjectionMaxIters)
            {
                prevLong = sLong;
                prevLat = sLat;
                prevMag2 = mag2;
                sLong *= scales[16]; // *= ratio
                // table[iter] for iter 1..14, scales[15]=1.0 at iter==15
                sLat = nLat * scales[iter];
                mag2 = sLong * sLong + sLat * sLat;
            }
        }
        else
        {
            // Isotropic: first sample is the free normalized impulse itself;
            // prev at origin so bracket lerp = pure radial project.
            sLong = nLong;
            sLat = nLat;
            prevLong = 0f;
            prevLat = 0f;
            prevMag2 = 0f;
        }

        // Bracket interpolation onto the unit circle using (sqrt(mag²) − 1) residuals
        float rCur = MathF.Sqrt(mag2) - HkPhysicsConstants.One;
        float rPrev = MathF.Sqrt(prevMag2) - HkPhysicsConstants.One;
        // Special case prevMag2 == 0: r_prev = -1 → pure radial of current sample
        if (prevMag2 <= 0f)
            rPrev = -HkPhysicsConstants.One;

        float denom = rCur - rPrev;
        float t = denom != 0f ? HkPhysicsConstants.One / denom : 0f;
        float wCur = -t * rPrev;
        float wPrev = t * rCur;

        float projNLong = wCur * sLong + wPrev * prevLong;
        float projNLat = wCur * sLat + wPrev * prevLat;

        // Un-normalize by Fmax
        float oldLat = impLat;
        impLong = projNLong * fMaxLong;
        impLat = projNLat * fMaxLat;
        return oldLat - impLat;
    }

    /// <summary>
    /// 2-axle friction solve (coupled long/lat when <see cref="AxleFrictionInput.Coupling"/> ≠ 0).
    /// </summary>
    /// <param name="dt">Substep timestep (param_1[0]/param_1[1] in retail).</param>
    /// <param name="axleInputs">Exactly 2 axle inputs (front, rear).</param>
    /// <param name="chassisInvMass">
    /// Chassis linear inverse mass (RB Minv). Used to form invKeff = 1/Keff when
    /// per-axle <see cref="AxleFrictionInput.InvKeffLong"/> / Lat are unset (≤ 0).
    /// Unit-mass AA defaults this to 1.
    /// </param>
    /// <param name="axleImpulses">Length ≥ 2; filled with long/lat impulses per axle.</param>
    public static void Solve(
        float dt,
        ReadOnlySpan<AxleFrictionInput> axleInputs,
        float chassisInvMass,
        Span<AxleFrictionImpulse> axleImpulses)
    {
        if (axleInputs.Length < AxleCount)
            throw new ArgumentException($"Expected at least {AxleCount} axle inputs.", nameof(axleInputs));
        if (axleImpulses.Length < AxleCount)
            throw new ArgumentException($"Expected at least {AxleCount} axle impulse slots.", nameof(axleImpulses));

        Span<float> scaleScratch = stackalloc float[CircleProjectionScaleCount];

        for (int ax = 0; ax < AxleCount; ax++)
        {
            ref readonly AxleFrictionInput input = ref axleInputs[ax];
            if (!input.InContact || dt <= 0f)
            {
                axleImpulses[ax] = default;
                continue;
            }

            float invKeffLong = ResolveInvKeff(input.InvKeffLong, chassisInvMass);
            float invKeffLat = ResolveInvKeff(input.InvKeffLat, chassisInvMass);

            // Softness → regularized longitudinal invK for the drive path (Keff + setup soft).
            float invKeffLongReg = invKeffLong;
            if (input.Softness > 0f && invKeffLong > 0f)
            {
                float keffLong = HkPhysicsConstants.One / invKeffLong;
                float keffReg = keffLong + input.Softness + HkPhysicsConstants.InvDenomEpsilon;
                invKeffLongReg = HkPhysicsConstants.One / keffReg;
            }

            // --- Phase B/D: (impLong, impLat) = −Minv2 · (Jv_long, Jv_lat) ---
            // Coupled when |Coupling| is meaningful; else diagonal cancel.
            float jvLong = input.SlipLongitudinal;
            float jvLat = input.SlipLateral;
            float impLong;
            float impLat;
            SolveCoupledImpulses(invKeffLong, invKeffLat, input.Coupling, jvLong, jvLat,
                out impLong, out impLat);

            // --- Drive pack bias (Phase D when drive gate enabled) ---
            // Retail (decomp 0x6c4450):
            //   driveTarget = invKeff_reg * invKeff_lat * (driveSlot*0.5 + Jv) * 0.5
            //   driveMax    = mu0 * invKeff_lat * |N| * dt
            //   lambdaLong  = -Jv - clamp_signed(driveTarget, ±driveMax)
            // Reduced deviation: when DrivePack == 0, skip blend so zero-drive slip cancel
            // stays exact (full RE always folds Jv into driveTarget when gate is open).
            if (input.DriveEnabled && input.DrivePack != 0f && invKeffLongReg != 0f && invKeffLat != 0f)
            {
                float half = HkPhysicsConstants.Half;
                // Pack-only term of the 0.5×0.5 blend (DAT_00a0f298 twice).
                float driveTarget = invKeffLongReg * invKeffLat * (input.DrivePack * half) * half;
                float driveMax = input.Mu0 * invKeffLat * MathF.Abs(input.NormalLoad) * dt;
                if (driveMax < 0f)
                    driveMax = 0f;
                float driveBias = ClampSigned(driveTarget, driveMax);
                // RE: lambda = -Jv - driveClamped; with impLong = -invK*Jv this is an
                // additional longitudinal push (engine) in the reduced model.
                impLong -= driveBias;
            }

            // --- Slip-dependent μ and friction-circle projection ---
            // Retail μ slip uses sqrt((-Jv0)² + Jv1²) ≈ free slip speeds (local_184 = -Jv).
            float slipSpeed = MathF.Sqrt(jvLong * jvLong + jvLat * jvLat);

            float fMax = ComputeFrictionLimit(
                input.Mu0, input.MuSlope, input.MuMax,
                slipSpeed, input.NormalLoad, dt);

            if (fMax <= 0f)
            {
                impLong = 0f;
                impLat = 0f;
            }
            else if (input.CircleProduct > 0f && float.IsFinite(input.CircleProduct))
            {
                BuildCircleProjectionScales(input.CircleProduct, scaleScratch);
                // Retail leaf 0x6c3f90 with anisotropic scale table.
                CircleProjection(ref impLong, ref impLat, fMax, fMax, scaleScratch);
            }
            else
            {
                // Isotropic circleProjection (no setup product table).
                CircleProjection(ref impLong, ref impLat, fMax, fMax);
            }

            axleImpulses[ax] = new AxleFrictionImpulse
            {
                Longitudinal = impLong,
                Lateral = impLat,
            };
        }
    }

    /// <summary>
    /// Solve <c>λ = −Keff⁻¹ · Jv</c> for the long/lat block.
    /// <paramref name="invKeffLong"/> / Lat are diagonals of Keff⁻¹ when uncoupled;
    /// <paramref name="coupling"/> is the Keff off-diagonal <c>b</c>.
    /// </summary>
    public static void SolveCoupledImpulses(
        float invKeffLong,
        float invKeffLat,
        float coupling,
        float jvLong,
        float jvLat,
        out float impLong,
        out float impLat)
    {
        // Diagonal fast path (orthogonal unit rows, no ang coupling).
        if (coupling == 0f || !float.IsFinite(coupling))
        {
            impLong = -invKeffLong * jvLong;
            impLat = -invKeffLat * jvLat;
            return;
        }

        // a = Keff_long, d = Keff_lat, b = coupling
        if (invKeffLong <= 0f || invKeffLat <= 0f)
        {
            impLong = 0f;
            impLat = 0f;
            return;
        }

        float a = HkPhysicsConstants.One / invKeffLong;
        float d = HkPhysicsConstants.One / invKeffLat;
        float b = coupling;
        float det = a * d - b * b;
        // Guard mirrors retail det*det check; fall back to diagonal if singular.
        if (!(det * det > 0f) || !float.IsFinite(det))
        {
            impLong = -invKeffLong * jvLong;
            impLat = -invKeffLat * jvLat;
            return;
        }

        // Standard inverse of [a b; b d]: (1/det)[d -b; -b a]
        float invDet = HkPhysicsConstants.One / det;
        impLong = -(d * jvLong - b * jvLat) * invDet;
        impLat = -(-b * jvLong + a * jvLat) * invDet;
    }

    /// <summary>
    /// invKeff = 1/Keff. With unit jacobian, Keff ≈ chassisInvMass, so invKeff = 1/chassisInvMass.
    /// Explicit per-axle invKeff overrides when &gt; 0.
    /// </summary>
    private static float ResolveInvKeff(float explicitInvKeff, float chassisInvMass)
    {
        if (explicitInvKeff > 0f)
            return explicitInvKeff;
        if (chassisInvMass <= 0f)
            return 0f;
        // Guard matches retail _DAT_00a0d2f4 usage on 1/x paths.
        return HkPhysicsConstants.One / (chassisInvMass + HkPhysicsConstants.InvDenomEpsilon);
    }

    private static float InverseLimit(float fMax)
    {
        if (fMax <= 0f)
            return 0f;
        return HkPhysicsConstants.One / (fMax + HkPhysicsConstants.InvDenomEpsilon);
    }

    private static float ClampSigned(float value, float limit)
    {
        if (limit <= 0f)
            return 0f;
        if (value > limit)
            return limit;
        if (value < -limit)
            return -limit;
        return value;
    }
}
