namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;

/// <summary>Intent for a pending mission-row write (latest-wins per character+mission).</summary>
public enum QuestPersistKind
{
    Upsert,
    Complete,
}

/// <summary>
/// A snapshot of the desired persisted state for one (character, mission). Field values are
/// captured at enqueue time so a later mutation of the live <c>CharacterQuest</c> cannot corrupt an
/// in-flight write.
/// </summary>
public readonly struct QuestPersistOp
{
    public QuestPersistKind Kind { get; }
    public byte ActiveObjectiveSequence { get; }
    public byte State { get; }
    public byte[] ObjectiveProgress { get; }

    private QuestPersistOp(QuestPersistKind kind, byte activeObjectiveSequence, byte state, byte[] objectiveProgress)
    {
        Kind = kind;
        ActiveObjectiveSequence = activeObjectiveSequence;
        State = state;
        ObjectiveProgress = objectiveProgress;
    }

    public static QuestPersistOp Upsert(byte activeObjectiveSequence, byte state, byte[] objectiveProgress)
        => new(QuestPersistKind.Upsert, activeObjectiveSequence, state, objectiveProgress);

    public static QuestPersistOp Complete()
        => new(QuestPersistKind.Complete, 0, 0, null);
}

/// <summary>
/// Latest-wins pending mission DB writes keyed by character COID + mission id. Hot path only
/// enqueues; <see cref="Flush"/> runs off the network/tick thread.
/// </summary>
public sealed class MissionPersistenceQueue
{
    private readonly ConcurrentDictionary<(long Coid, int MissionId), QuestPersistOp> _pending = new();

    public int PendingCount => _pending.Count;

    /// <summary>Record or replace the pending op for (coid, missionId).</summary>
    public void Enqueue(long coid, int missionId, QuestPersistOp op)
    {
        _pending[(coid, missionId)] = op;
    }

    /// <summary>
    /// Atomically drain current pending entries and invoke <paramref name="persist"/> for each.
    /// Entries enqueued during <paramref name="persist"/> remain for a later flush.
    /// Failed writes are restored unless a newer operation for the same key already exists.
    /// </summary>
    /// <returns>Number of entries successfully persisted in this call.</returns>
    public int Flush(Action<long, int, QuestPersistOp> persist)
    {
        ArgumentNullException.ThrowIfNull(persist);

        if (_pending.IsEmpty)
            return 0;

        var keys = _pending.Keys.ToArray();
        var drained = 0;

        foreach (var key in keys)
        {
            if (!_pending.TryRemove(key, out var op))
                continue;

            try
            {
                persist(key.Coid, key.MissionId, op);
                ++drained;
            }
            catch
            {
                // Do not lose a mission mutation on a transient DB failure. Preserve a newer
                // operation if one arrived while this write was in flight.
                _pending.TryAdd(key, op);
            }
        }

        return drained;
    }

    public void Clear() => _pending.Clear();

    /// <summary>Drop pending ops for a single character (used by /clearAllMissions).</summary>
    public void RemoveForCharacter(long coid)
    {
        foreach (var key in _pending.Keys.Where(k => k.Coid == coid).ToArray())
            _pending.TryRemove(key, out _);
    }

    /// <summary>
    /// Drop pending Upsert ops for a character so active rows are not re-created after
    /// /removeCurrentMission. Leaves Complete ops so unflushed completions are not lost.
    /// </summary>
    public void RemoveUpsertsForCharacter(long coid)
    {
        foreach (var key in _pending.Keys.Where(k => k.Coid == coid).ToArray())
        {
            if (_pending.TryGetValue(key, out var op) && op.Kind == QuestPersistKind.Upsert)
                _pending.TryRemove(key, out _);
        }
    }
}
