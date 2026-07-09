namespace AutoCore.Sector.Network;

using AutoCore.Utils;

/// <summary>
/// Isolates per-connection combat processing during the sector tick so one failure
/// cannot skip combat for other players (SS-02).
/// </summary>
public static class SectorCombatTick
{
    /// <summary>
    /// Runs combat processing for each connection entry.
    /// Failures are isolated per COID: log and continue; never abort the tick for others.
    /// </summary>
    /// <param name="entries">COID + combat action pairs (e.g. ProcessCombatIfFiring).</param>
    /// <param name="onError">Optional error sink; defaults to <see cref="Logger.WriteLog"/>.</param>
    public static void ProcessAll(
        IEnumerable<(long Coid, Action ProcessCombat)> entries,
        Action<long, Exception> onError = null)
    {
        foreach (var (coid, processCombat) in entries)
        {
            try
            {
                processCombat?.Invoke();
            }
            catch (Exception ex)
            {
                if (onError != null)
                    onError(coid, ex);
                else
                    Logger.WriteLog(LogType.Error,
                        "Unhandled exception in sector combat tick for COID {0}; continuing. {1}",
                        coid, ex);
            }
        }
    }
}
