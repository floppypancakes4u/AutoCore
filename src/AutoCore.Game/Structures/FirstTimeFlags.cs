namespace AutoCore.Game.Structures;

/// <summary>
/// Helpers for the client first-time tips/hints bitmap (4×uint32).
/// Tip index N uses bit (N % 32) of dword (N / 32). Hide-tips is bit 31 of dword 0.
/// Client: FUN_00801760 (show tip), FUN_0040ff80 (set bit), FUN_0092c6d0 (send 0x20B1).
/// </summary>
public static class FirstTimeFlags
{
    public const int MaxTipId = 0x31; // 49 — client rejects higher tip ids
    public const int DwordCount = 4;
    public const uint HideTipsBit = 1u << 31;

    public static bool IsTipSeen(uint[] flags, int tipId)
    {
        if (!IsValidFlagsArray(flags) || tipId < 0 || tipId > 127)
            return false;

        return (flags[tipId >> 5] & (1u << (tipId & 0x1F))) != 0;
    }

    public static void SetTipSeen(uint[] flags, int tipId)
    {
        if (!IsValidFlagsArray(flags) || tipId < 0 || tipId > 127)
            return;

        flags[tipId >> 5] |= 1u << (tipId & 0x1F);
    }

    public static bool GetHideTips(uint flags1) => (flags1 & HideTipsBit) != 0;

    public static uint SetHideTips(uint flags1, bool hide) =>
        hide ? flags1 | HideTipsBit : flags1 & ~HideTipsBit;

    /// <summary>
    /// Copies account-scoped flags into a CreateCharacterExtended FirstTimeFlags buffer.
    /// When <paramref name="source"/> is null, leaves buffer zeros (or clears if non-null buffer).
    /// </summary>
    public static void CopyToBuffer(uint[] destination, uint f1, uint f2, uint f3, uint f4)
    {
        if (!IsValidFlagsArray(destination))
            return;

        destination[0] = f1;
        destination[1] = f2;
        destination[2] = f3;
        destination[3] = f4;
    }

    /// <summary>
    /// Full replace of four dwords (matches client UpdateFirstTimeFlags payload).
    /// </summary>
    public static void Assign(out uint f1, out uint f2, out uint f3, out uint f4,
        uint new1, uint new2, uint new3, uint new4)
    {
        f1 = new1;
        f2 = new2;
        f3 = new3;
        f4 = new4;
    }

    public static bool IsValidFlagsArray(uint[] flags) =>
        flags != null && flags.Length >= DwordCount;
}
