namespace AutoCore.Game.Combat;

/// <summary>
/// Max soft/hard fire targets per shot from weapon flags (client <c>FUN_0056ac50</c>).
/// Runtime flag byte on weapon mirrors clonebase <c>WeaponSpecific.Flags</c>.
/// </summary>
public static class WeaponFireTargetLimits
{
    public const byte SprayModeBit = 0x01;
    public const byte MaxTargetsHundredBit = 0x40;
    public const int AbsoluteCap = 100;

    /// <summary>
    /// bit0 set → max(1, SprayTargets); bit6 without bit0 → 100; else 1. Cap 100.
    /// </summary>
    public static int GetMaxTargets(byte flags, byte sprayTargets)
    {
        int max;
        if ((flags & SprayModeBit) != 0)
            max = sprayTargets == 0 ? 1 : sprayTargets;
        else if ((flags & MaxTargetsHundredBit) != 0)
            max = AbsoluteCap;
        else
            max = 1;

        return Math.Clamp(max, 1, AbsoluteCap);
    }
}
