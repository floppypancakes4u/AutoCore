namespace AutoCore.Game.Diagnostics;

using System.Collections.Concurrent;
using AutoCore.Game.Structures;

/// <summary>
/// Detects <b>reversals in the transmitted pose stream</b>: consecutive pose packs for one creature
/// whose displacement vectors point in opposing directions.
/// <para>
/// This separates the two candidate causes of visible rubberbanding, which no amount of pose-rate
/// tuning can distinguish:
/// </para>
/// <list type="bullet">
/// <item><description><b>Transport</b> — the wire stream is monotonic and the client's correction
/// (<c>CVOGPhysicsBase::DoPositionUpdate</c> @0053eec0, which hard-snaps rather than blending) is
/// what produces the visible jump. Reversal count stays near zero.</description></item>
/// <item><description><b>Server motion</b> — the server itself walks the creature backwards, so the
/// client is faithfully rendering what it was told and the bug is in the NPC mover, not the
/// network. Reversal count is high.</description></item>
/// </list>
/// <para>
/// Sampled at serialisation time, so it measures exactly what the client receives — not the
/// server's internal tick position, which can differ if anything rewrites pose between ticks.
/// </para>
/// </summary>
public static class CreatureMotionDiag
{
    /// <summary>Default off; enabled via the wire lever board.</summary>
    public static bool Enabled;

    /// <summary>Ignore sub-millimetre jitter — only real movement counts as a direction.</summary>
    private const float MinStepSq = 0.01f * 0.01f;

    private sealed class Track
    {
        public Vector3 LastPos;
        public Vector3 LastDelta;
        public bool HasPos;
        public bool HasDelta;

        /// <summary>
        /// COID this track's history belongs to. A ghost instance can be re-pointed to a different
        /// parent (ghost rebind), and continuing the old history across that would compare two
        /// different creatures' positions — fabricating a large false "reversal". Tracked so a
        /// rebind resets the history and is counted separately instead.
        /// </summary>
        public long Coid;
        public bool HasCoid;
    }

    /// <summary>
    /// Keyed on the ghost <b>instance</b>, never on COID. Local map COIDs are not unique across map
    /// instances (per-player instances of one continent mint identical local COIDs) and the local
    /// space overlaps the global one, so a COID-keyed track can alias two different creatures onto
    /// one history — which fabricates enormous false "reversals" as the two positions alternate.
    /// A weak table also drops entries when the ghost dies, so nothing leaks across map changes.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Track> Tracks = new();

    private static int _reversals;
    private static int _samples;
    private static float _maxBackward;

    /// <summary>Ghost instances observed carrying a new COID (rebind) this window.</summary>
    private static int _rebinds;

    /// <summary>
    /// Record one transmitted pose. <paramref name="ghost"/> is the creature's ghost instance and is
    /// used purely as an identity key — never a COID, which is not unique across map instances.
    /// Call from the PositionMask branch of pack, non-initial only.
    /// </summary>
    public static void RecordPose(object ghost, Vector3 position, long coid)
    {
        if (!Enabled || ghost == null)
            return;

        var track = Tracks.GetValue(ghost, static _ => new Track());

        // Ghost rebind: this instance now carries a different creature. Comparing across that
        // boundary would invent a reversal the wire never contained, so drop the history.
        if (track.HasCoid && track.Coid != coid)
        {
            System.Threading.Interlocked.Increment(ref _rebinds);
            track.HasPos = false;
            track.HasDelta = false;
        }

        track.Coid = coid;
        track.HasCoid = true;

        if (!track.HasPos)
        {
            track.LastPos = position;
            track.HasPos = true;
            return;
        }

        var delta = new Vector3(
            position.X - track.LastPos.X,
            position.Y - track.LastPos.Y,
            position.Z - track.LastPos.Z);
        track.LastPos = position;

        // XZ only: terrain snapping moves Y independently of travel direction and would otherwise
        // register as a reversal on every slope crest.
        var stepSq = (delta.X * delta.X) + (delta.Z * delta.Z);
        if (stepSq < MinStepSq)
            return;

        if (track.HasDelta)
        {
            System.Threading.Interlocked.Increment(ref _samples);

            var dot = (delta.X * track.LastDelta.X) + (delta.Z * track.LastDelta.Z);
            if (dot < 0f)
            {
                System.Threading.Interlocked.Increment(ref _reversals);

                var backward = (float)System.Math.Sqrt(stepSq);
                // Racy max under concurrency; diagnostics only, and pack is single-threaded today.
                if (backward > _maxBackward)
                    _maxBackward = backward;
            }
        }

        track.LastDelta = delta;
        track.HasDelta = true;
    }

    /// <summary>Read and reset the window counters.</summary>
    public static (int Reversals, int Samples, float MaxBackward, int Rebinds) Sample()
    {
        var reversals = System.Threading.Interlocked.Exchange(ref _reversals, 0);
        var samples = System.Threading.Interlocked.Exchange(ref _samples, 0);
        var rebinds = System.Threading.Interlocked.Exchange(ref _rebinds, 0);
        var maxBackward = _maxBackward;
        _maxBackward = 0f;
        return (reversals, samples, maxBackward, rebinds);
    }

    /// <summary>Drop all per-creature history (map change / test isolation).</summary>
    public static void Reset()
    {
        Tracks.Clear();
        System.Threading.Interlocked.Exchange(ref _reversals, 0);
        System.Threading.Interlocked.Exchange(ref _samples, 0);
        System.Threading.Interlocked.Exchange(ref _rebinds, 0);
        _maxBackward = 0f;
    }
}
