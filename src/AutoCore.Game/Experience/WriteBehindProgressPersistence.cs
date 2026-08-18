namespace AutoCore.Game.Experience;

using AutoCore.Utils;
using AutoCore.Utils.Reliability;

/// <summary>
/// Latest-wins write-behind decorator for <see cref="ICharacterProgressPersistence"/>.
/// <para>
/// <c>GiveXp</c> runs on the sector tick thread inside <c>OnDeath</c>; a synchronous MySQL
/// write per kill stalls the tick during ram-kill bursts (several kills within ~500ms) and
/// bunches ghost/loot sends into a client-visible hitch. Enqueue here is O(1) on the calling
/// thread; a ThreadPool flush (mirroring <c>MissionPersistence</c>) performs the actual EF
/// write. Multiple grants for one character coalesce into a single write of the newest
/// snapshot — safe because <see cref="ICharacterProgressPersistence.SaveProgress"/> is an
/// absolute overwrite.
/// </para>
/// </summary>
public sealed class WriteBehindProgressPersistence : ICharacterProgressPersistence
{
    /// <summary>Flush attempts per entry before it dead-letters (bounded retry; no spin).</summary>
    public const int MaxFlushAttempts = 5;

    private readonly ICharacterProgressPersistence _inner;
    private readonly object _lock = new();
    private readonly Dictionary<long, PendingEntry> _pending = new();
    private int _backgroundFlushScheduled;
    private int _deadLettered;

    /// <summary>When true (default), enqueue schedules a ThreadPool flush so production never blocks.</summary>
    internal bool AutoFlushOnEnqueue { get; set; } = true;

    /// <summary>Pending progress writes (health metric; also asserted in tests).</summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
                return _pending.Count;
        }
    }

    /// <summary>Entries dropped after <see cref="MaxFlushAttempts"/> failed writes.</summary>
    public int DeadLetteredCount => Volatile.Read(ref _deadLettered);

    public WriteBehindProgressPersistence(ICharacterProgressPersistence inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>Read-your-writes: a pending snapshot is newer than any committed row.</summary>
    public CharacterProgressSnapshot LoadProgress(long characterCoid)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(characterCoid, out var entry))
                return entry.Snapshot;
        }

        return _inner.LoadProgress(characterCoid);
    }

    public void SaveProgress(long characterCoid, CharacterProgressSnapshot progress)
    {
        lock (_lock)
        {
            // Latest wins; a fresh snapshot restarts the retry budget (recovery after outage).
            _pending[characterCoid] = new PendingEntry(progress);
        }

        if (AutoFlushOnEnqueue)
            ScheduleBackgroundFlush();
    }

    /// <summary>
    /// Drain pending writes on the calling thread. Returns the number persisted. Failed entries
    /// stay pending (retry budget permitting) so the next mutation or flush retries them.
    /// </summary>
    public int FlushPending()
    {
        List<KeyValuePair<long, PendingEntry>> batch;
        lock (_lock)
            batch = new List<KeyValuePair<long, PendingEntry>>(_pending);

        var persisted = 0;

        foreach (var (coid, entry) in batch)
        {
            try
            {
                _inner.SaveProgress(coid, entry.Snapshot);
                persisted++;

                lock (_lock)
                {
                    // Only clear if no newer snapshot arrived while we were writing.
                    if (_pending.TryGetValue(coid, out var current) && ReferenceEquals(current, entry))
                        _pending.Remove(coid);
                }
            }
            catch (Exception ex)
            {
                var attempts = entry.RecordFailedAttempt();
                if (attempts >= MaxFlushAttempts)
                {
                    lock (_lock)
                    {
                        if (_pending.TryGetValue(coid, out var current) && ReferenceEquals(current, entry))
                            _pending.Remove(coid);
                    }

                    Interlocked.Increment(ref _deadLettered);
                    Logger.WriteException(LogType.Error,
                        $"progress persist dead-lettered after {attempts} attempts coid={coid} " +
                        $"level={entry.Snapshot.Level} xp={entry.Snapshot.Experience}", ex);
                }
                else
                {
                    Logger.WriteException(LogType.Warning,
                        $"progress persist attempt {attempts}/{MaxFlushAttempts} failed coid={coid}; will retry", ex);
                }
            }
        }

        return persisted;
    }

    private void ScheduleBackgroundFlush()
    {
        if (Interlocked.CompareExchange(ref _backgroundFlushScheduled, 1, 0) != 0)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            var persisted = 0;

            // SS-19 pattern: an exception escaping a ThreadPool callback terminates the
            // process — Guard the flush, always clear the scheduling flag.
            try
            {
                Guard.Run("character progress background flush", () => persisted = FlushPending());
            }
            finally
            {
                Interlocked.Exchange(ref _backgroundFlushScheduled, 0);

                // Race: items enqueued after drain but before flag clear. A zero count with
                // pending entries means persistence failed; wait for the next mutation
                // instead of spinning an unbounded retry loop.
                if (persisted > 0 && PendingCount > 0)
                    ScheduleBackgroundFlush();
            }
        });
    }

    /// <summary>Pending snapshot plus its failed-attempt count (identity marks staleness).</summary>
    private sealed class PendingEntry
    {
        public CharacterProgressSnapshot Snapshot { get; }
        private int _failedAttempts;

        public PendingEntry(CharacterProgressSnapshot snapshot) => Snapshot = snapshot;

        public int RecordFailedAttempt() => Interlocked.Increment(ref _failedAttempts);
    }
}
