namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Setup + auto gear-select for the Havok transmission component.
/// <para>
/// Setup: <c>Vehicle_BuildTransmissionDescriptor</c> @ <c>0x5fc840</c> →
/// <c>hkDefaultTransmission_ctor</c> @ <c>0x64f610</c> (gear-ratio array, primary/reverse
/// ratios, up/downshift RPM, clutch delay).
/// </para>
/// <para>
/// Runtime gear step: <c>hkDefaultTransmission_update</c> @ <c>0x64f510</c> —
/// downshift when <c>engineRpm &lt; downshiftRPM</c> and gear &gt; 0; upshift when
/// <c>engineRpm &gt; upshiftRPM</c> and gear+1 &lt; numGears. Strict inequalities
/// (equality at the threshold holds gear). Reverse input suppresses auto-shift.
/// </para>
/// <para>
/// <b>Vestigial for drive torque.</b> AA replaced Havok's <c>hkDefaultEngine</c> with
/// <c>VehicleEngine::torqueCurve2D</c> @ <c>0x4a9750</c>, whose args are contact hardpoint
/// X/Z (not RPM/throttle). Those world-space samples are almost always out-of-range, so the
/// LUT falls through to <c>factors[0]</c> — a near-constant torque factor. Transmission
/// output at component <c>+0x1c</c> / per-wheel axle array is <b>not</b> consumed by
/// <c>calcWheelTorque</c> (<c>0x598040</c>) or <c>postTickApplyForces</c> (<c>0x64bc70</c>)
/// (see <c>docs/reconstruction/physics/0.7-transmission.md</c>). Gear state is kept for
/// setup parity, top-speed precompute, and any residual HUD/RPM path.
/// </para>
/// </summary>
public sealed class HkVehicleTransmission
{
    private readonly float[] _gearRatios;

    public HkVehicleTransmission(
        IReadOnlyList<float> gearRatios,
        float primaryTransmissionRatio,
        float reverseGearRatio,
        float upshiftRpm,
        float downshiftRpm,
        float clutchDelayTime = 0f,
        int numberOfGears = 0)
    {
        ArgumentNullException.ThrowIfNull(gearRatios);

        _gearRatios = new float[gearRatios.Count];
        for (var i = 0; i < gearRatios.Count; i++)
            _gearRatios[i] = gearRatios[i];

        PrimaryTransmissionRatio = primaryTransmissionRatio;
        ReverseGearRatio = reverseGearRatio;
        UpshiftRpm = upshiftRpm;
        DownshiftRpm = downshiftRpm;
        ClutchDelayTime = clutchDelayTime;

        // Prefer explicit numGears (VehSpec+0x699); fall back to ratio array length.
        NumberOfGears = numberOfGears > 0
            ? numberOfGears
            : _gearRatios.Length;
    }

    public IReadOnlyList<float> GearRatios => _gearRatios;
    public float PrimaryTransmissionRatio { get; }
    public float ReverseGearRatio { get; }
    public float UpshiftRpm { get; }
    public float DownshiftRpm { get; }
    public float ClutchDelayTime { get; }
    public int NumberOfGears { get; }

    /// <summary>
    /// Build from immutable per-CBID setup (<see cref="HkVehicleData"/>).
    /// Up/Downshift are stored as float RPM (DB shorts already widened at load).
    /// </summary>
    public static HkVehicleTransmission FromVehicleData(HkVehicleData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new HkVehicleTransmission(
            gearRatios: data.GearRatios,
            primaryTransmissionRatio: data.TransmissionRatio,
            reverseGearRatio: data.ReverseGearRatio,
            upshiftRpm: data.UpshiftRpm,
            downshiftRpm: data.DownshiftRpm,
            clutchDelayTime: data.ClutchDelayTime,
            numberOfGears: data.NumberOfGears);
    }

    /// <summary>
    /// Auto gear selection from <c>hkDefaultTransmission_update</c> @ <c>0x64f510</c>
    /// (shift portion only; clutch timer is owned by the sim instance).
    /// </summary>
    /// <param name="currentGear">Current forward gear index (0-based). Clamped to range.</param>
    /// <param name="engineRpm">
    /// Engine RPM from <c>hkDefaultTransmission_calcRPM</c> @ <c>0x64efb0</c>
    /// (component <c>+0x18</c>).
    /// </param>
    /// <param name="isReverse">
    /// Transmission reverse flag (component <c>+0x14</c>). When true, gear is held.
    /// </param>
    /// <returns>Selected gear index in <c>[0, NumberOfGears-1]</c> (or 0 if no gears).</returns>
    public int SelectGear(int currentGear, float engineRpm, bool isReverse = false)
    {
        if (NumberOfGears <= 0)
            return 0;

        var gear = ClampGear(currentGear);

        // Reverse path in update: no up/down auto-shift.
        if (isReverse)
            return gear;

        // Downshift first (retail order): rpm < downshiftRPM && gear > 0
        if (engineRpm < DownshiftRpm && gear > 0)
            gear--;

        // Upshift: rpm > upshiftRPM && gear+1 < numGears
        if (engineRpm > UpshiftRpm && gear + 1 < NumberOfGears)
            gear++;

        return gear;
    }

    /// <summary>
    /// Gear ratio for the active gear. Reverse returns <c>-ReverseGearRatio</c>
    /// (matches update: <c>0.0 - reverseGearRatio</c>).
    /// </summary>
    public float GetGearRatio(int gear, bool isReverse = false)
    {
        if (isReverse)
            return -ReverseGearRatio;

        if (_gearRatios.Length == 0)
            return 0f;

        var i = ClampGear(gear);
        if (i >= _gearRatios.Length)
            i = _gearRatios.Length - 1;
        return _gearRatios[i];
    }

    private int ClampGear(int gear)
    {
        if (NumberOfGears <= 0)
            return 0;
        if (gear < 0)
            return 0;
        if (gear >= NumberOfGears)
            return NumberOfGears - 1;
        return gear;
    }
}
