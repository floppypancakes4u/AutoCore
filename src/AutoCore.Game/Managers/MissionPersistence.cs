namespace AutoCore.Game.Managers;

using System.Diagnostics.CodeAnalysis;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

/// <summary>
/// Server-authoritative mission-state persistence. Mutation hooks enqueue a latest-wins snapshot;
/// a background flush writes <see cref="CharacterQuestData"/> / <see cref="CharacterCompletedMissionData"/>
/// off the network/tick thread. Structurally mirrors <see cref="ExplorationManager"/>.
/// </summary>
public class MissionPersistence : Singleton<MissionPersistence>
{
    private readonly MissionPersistenceQueue _persistQueue = new();
    private int _backgroundFlushScheduled;

    /// <summary>When true (default), enqueue schedules a ThreadPool flush so production never blocks.</summary>
    internal bool AutoFlushOnEnqueue { get; set; } = true;

    /// <summary>Persist one op. Defaults to EF <see cref="CharContext"/> write; replace in tests.</summary>
    internal Action<long, int, QuestPersistOp> PersistQuestRow { get; set; }

    internal int PendingPersistCount => _persistQueue.PendingCount;

    public MissionPersistence()
    {
        PersistQuestRow = PersistQuestRowToDatabase;
        DeleteAllRows = DeleteAllRowsFromDatabase;
        DeleteActiveRows = DeleteActiveRowsFromDatabase;
    }

    /// <summary>Enqueue an upsert of the quest's current active objective + progress.</summary>
    public void OnQuestChanged(Character character, CharacterQuest quest)
    {
        if (character == null || quest == null)
            return;

        _persistQueue.Enqueue(
            character.ObjectId.Coid,
            quest.MissionId,
            QuestPersistOp.Upsert(quest.ActiveObjectiveSequence, quest.State, PackProgress(quest.ObjectiveProgress)));

        MaybeFlush();
    }

    /// <summary>Enqueue completion: delete the active-quest row and insert a completed-mission row.</summary>
    public void OnMissionCompleted(long coid, int missionId)
    {
        _persistQueue.Enqueue(coid, missionId, QuestPersistOp.Complete());
        MaybeFlush();
    }

    /// <summary>Drain pending mission writes (background path / disconnect / tests).</summary>
    public int FlushPending()
    {
        var persist = PersistQuestRow ?? PersistQuestRowToDatabase;
        return _persistQueue.Flush(persist);
    }

    /// <summary>
    /// Delete all persisted mission rows (active + completed) for a character and drop any pending
    /// writes for it. Used by the /clearAllMissions diagnostic command.
    /// </summary>
    public void DeleteAllForCharacter(long coid)
    {
        _persistQueue.RemoveForCharacter(coid);
        DeleteAllRows?.Invoke(coid);
    }

    /// <summary>
    /// Delete active mission rows only for a character and drop pending Upserts that would
    /// re-create them. Completed-mission rows and pending Complete ops are preserved.
    /// Used by the /removeCurrentMission diagnostic command.
    /// </summary>
    public void DeleteActiveForCharacter(long coid)
    {
        _persistQueue.RemoveUpsertsForCharacter(coid);
        DeleteActiveRows?.Invoke(coid);
    }

    /// <summary>DB delete seam (overridable in tests). Defaults to the EF CharContext delete.</summary>
    internal Action<long> DeleteAllRows { get; set; }

    /// <summary>DB active-only delete seam (overridable in tests). Defaults to EF CharacterQuests delete.</summary>
    internal Action<long> DeleteActiveRows { get; set; }

    /// <summary>Reset queue and test hooks (unit tests).</summary>
    internal void ResetPersistenceForTests()
    {
        AutoFlushOnEnqueue = false;
        PersistQuestRow = PersistQuestRowToDatabase;
        DeleteAllRows = DeleteAllRowsFromDatabase;
        DeleteActiveRows = DeleteActiveRowsFromDatabase;
        _persistQueue.Clear();
        Interlocked.Exchange(ref _backgroundFlushScheduled, 0);
    }

    /// <summary>Pack objective progress int slots into a little-endian blob for storage.</summary>
    internal static byte[] PackProgress(int[] progress)
    {
        if (progress == null || progress.Length == 0)
            return Array.Empty<byte>();

        var bytes = new byte[progress.Length * sizeof(int)];
        Buffer.BlockCopy(progress, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Unpack a stored progress blob back into int slots (inverse of <see cref="PackProgress"/>).</summary>
    internal static int[] UnpackProgress(byte[] bytes)
    {
        if (bytes == null || bytes.Length < sizeof(int))
            return Array.Empty<int>();

        var count = bytes.Length / sizeof(int);
        var progress = new int[count];
        Buffer.BlockCopy(bytes, 0, progress, 0, count * sizeof(int));
        return progress;
    }

    private void MaybeFlush()
    {
        if (AutoFlushOnEnqueue)
            ScheduleBackgroundFlush();
    }

    private void ScheduleBackgroundFlush()
    {
        if (Interlocked.CompareExchange(ref _backgroundFlushScheduled, 1, 0) != 0)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            var persisted = 0;
            try
            {
                persisted = FlushPending();
            }
            finally
            {
                Interlocked.Exchange(ref _backgroundFlushScheduled, 0);

                // Race: items enqueued after drain but before flag clear.
                // A zero count with pending entries means persistence failed; wait for the next
                // mutation or disconnect flush instead of spinning an unbounded retry loop.
                if (persisted > 0 && _persistQueue.PendingCount > 0)
                    ScheduleBackgroundFlush();
            }
        });
    }

    /// <summary>
    /// EF write used by the background flush. Must never run on the network/tick hot path.
    /// Unit tests inject <see cref="PersistQuestRow"/>; the live path needs a CharContext connection.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "EF CharContext I/O; unit tests inject PersistQuestRow.")]
    private static void PersistQuestRowToDatabase(long coid, int missionId, QuestPersistOp op)
    {
        // No DB configured (unit tests / offline) — nothing to persist.
        if (string.IsNullOrEmpty(CharContext.ConnectionString))
            return;

        try
        {
            using var context = new CharContext();

            switch (op.Kind)
            {
                case QuestPersistKind.Upsert:
                {
                    var row = context.CharacterQuests
                        .FirstOrDefault(q => q.CharacterCoid == coid && q.MissionId == missionId);

                    if (row == null)
                    {
                        context.CharacterQuests.Add(new CharacterQuestData
                        {
                            CharacterCoid = coid,
                            MissionId = missionId,
                            ActiveObjectiveSequence = op.ActiveObjectiveSequence,
                            State = op.State,
                            ObjectiveProgress = op.ObjectiveProgress,
                        });
                    }
                    else
                    {
                        row.ActiveObjectiveSequence = op.ActiveObjectiveSequence;
                        row.State = op.State;
                        row.ObjectiveProgress = op.ObjectiveProgress;
                    }

                    break;
                }

                case QuestPersistKind.Complete:
                {
                    var row = context.CharacterQuests
                        .FirstOrDefault(q => q.CharacterCoid == coid && q.MissionId == missionId);
                    if (row != null)
                        context.CharacterQuests.Remove(row);

                    var done = context.CharacterCompletedMissions
                        .FirstOrDefault(c => c.CharacterCoid == coid && c.MissionId == missionId);
                    if (done == null)
                    {
                        context.CharacterCompletedMissions.Add(new CharacterCompletedMissionData
                        {
                            CharacterCoid = coid,
                            MissionId = missionId,
                        });
                    }

                    break;
                }
            }

            context.SaveChanges();

            //Logger.WriteLog(LogType.Debug,
            //    "Mission: persisted {0} coid={1} mission={2}", op.Kind, coid, missionId);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "Mission: failed to persist coid={0} mission={1}: {2}",
                coid, missionId, ex.Message);
            throw;
        }
    }

    [ExcludeFromCodeCoverage(Justification = "EF CharContext I/O; unit tests inject DeleteAllRows.")]
    private static void DeleteAllRowsFromDatabase(long coid)
    {
        if (string.IsNullOrEmpty(CharContext.ConnectionString))
            return;

        try
        {
            using var context = new CharContext();
            context.CharacterQuests.RemoveRange(context.CharacterQuests.Where(q => q.CharacterCoid == coid));
            context.CharacterCompletedMissions.RemoveRange(
                context.CharacterCompletedMissions.Where(c => c.CharacterCoid == coid));
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "Mission: failed to clear coid={0}: {1}", coid, ex.Message);
        }
    }

    [ExcludeFromCodeCoverage(Justification = "EF CharContext I/O; unit tests inject DeleteActiveRows.")]
    private static void DeleteActiveRowsFromDatabase(long coid)
    {
        if (string.IsNullOrEmpty(CharContext.ConnectionString))
            return;

        try
        {
            using var context = new CharContext();
            context.CharacterQuests.RemoveRange(context.CharacterQuests.Where(q => q.CharacterCoid == coid));
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "Mission: failed to clear active missions coid={0}: {1}", coid, ex.Message);
        }
    }
}
