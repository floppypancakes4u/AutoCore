namespace AutoCore.Sector.Network;

using AutoCore.Utils;

/// <summary>
/// Isolates per-connection player pose dead reckoning during the sector tick so one
/// failure cannot skip pose advance for other players (mirrors <see cref="SectorCombatTick"/>).
/// </summary>
public static class SectorPlayerPoseTick
{
    /// <summary>
    /// Runs <c>AdvanceNetworkPose</c> for each connection entry.
    /// Failures are isolated per COID: log and continue; never abort the tick for others.
    /// </summary>
    /// <param name="entries">COID + pose advance action pairs.</param>
    /// <param name="onError">Optional error sink; defaults to <see cref="Logger.WriteLog"/>.</param>
    public static void ProcessAll(
        IEnumerable<(long Coid, Action AdvancePose)> entries,
        Action<long, Exception> onError = null)
    {
        if (entries == null)
            return;

        foreach (var (coid, advancePose) in entries)
        {
            try
            {
                advancePose?.Invoke();
            }
            catch (Exception ex)
            {
                if (onError != null)
                    onError(coid, ex);
                else
                    Logger.WriteLog(LogType.Error,
                        "AdvanceNetworkPose failed coid={0}: {1}",
                        coid, ex.Message);
            }
        }
    }

    /// <summary>
    /// Clamps a main-loop delta (milliseconds) to the pose advance dt window used by
    /// <see cref="SectorServer"/> (1–100 ms → 0.001–0.1 s).
    /// </summary>
    public static float ClampPoseDtSeconds(double deltaMs)
    {
        return (float)Math.Clamp(deltaMs / 1000.0, 0.001, 0.1);
    }
}
