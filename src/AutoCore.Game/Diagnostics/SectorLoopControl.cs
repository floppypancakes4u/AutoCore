namespace AutoCore.Game.Diagnostics;

/// <summary>
/// Cross-layer hook so chat/console can tune the sector main-loop period without a hard Game→Sector reference.
/// SectorServer registers handlers at startup.
/// </summary>
public static class SectorLoopControl
{
    /// <summary>Returns current sector loop period in ms, or null if not registered.</summary>
    public static Func<int?> GetLoopMilliseconds { get; set; }

    /// <summary>
    /// Sets sector loop period in ms. Returns a user-visible status line, or null if not registered.
    /// </summary>
    public static Func<int, string> TrySetLoopMilliseconds { get; set; }

    public static int? CurrentMilliseconds => GetLoopMilliseconds?.Invoke();

    public static bool TrySet(int milliseconds, out string message)
    {
        if (TrySetLoopMilliseconds == null)
        {
            message = "Sector loop control is not available (sector server not running).";
            return false;
        }

        message = TrySetLoopMilliseconds(milliseconds);
        return true;
    }
}
