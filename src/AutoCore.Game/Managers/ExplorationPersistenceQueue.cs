namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;

/// <summary>
/// Latest-wins pending exploration DB writes keyed by character COID + continent.
/// Hot path only enqueues; <see cref="Flush"/> runs off the network/tick thread.
/// </summary>
public sealed class ExplorationPersistenceQueue
{
    private readonly ConcurrentDictionary<(long Coid, int ContinentId), uint> _pending = new();

    public int PendingCount => _pending.Count;

    /// <summary>
    /// Record or replace the pending explored-bits snapshot for (coid, continentId).
    /// Continent 0 is ignored (matches in-memory reveal rules).
    /// </summary>
    public void Enqueue(long coid, int continentId, uint exploredBits)
    {
        if (continentId == 0)
            return;

        _pending[(coid, continentId)] = exploredBits;
    }

    /// <summary>
    /// Atomically drain current pending entries and invoke <paramref name="persist"/> for each.
    /// Entries enqueued during <paramref name="persist"/> remain for a later flush.
    /// </summary>
    /// <returns>Number of entries drained in this call.</returns>
    public int Flush(Action<long, int, uint> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);

        if (_pending.IsEmpty)
            return 0;

        // Snapshot keys first so concurrent Enqueue during persist is not lost.
        var keys = _pending.Keys.ToArray();
        var drained = 0;

        foreach (var key in keys)
        {
            if (!_pending.TryRemove(key, out var bits))
                continue;

            persist(key.Coid, key.ContinentId, bits);
            ++drained;
        }

        return drained;
    }

    public void Clear() => _pending.Clear();
}
