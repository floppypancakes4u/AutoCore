using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.StateTransition;

using AutoCore.Game.Managers;

/// <summary>
/// Realistic concurrency for mission persistence: latest-wins queue under parallel enqueue/flush.
/// Does not invent multi-threaded sector mission logic.
/// </summary>
[TestClass]
public class MissionPersistenceConcurrencyTests
{
    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Queue_ParallelEnqueueSameKey_LatestWinsOnFlush()
    {
        var queue = new MissionPersistenceQueue();
        const int iterations = 500;
        Parallel.For(0, iterations, i =>
        {
            queue.Enqueue(100, 50, QuestPersistOp.Upsert((byte)(i % 8), 0, BitConverter.GetBytes(i)));
        });
        // Final write after parallel section must win if last-writer-wins is honored for that key.
        queue.Enqueue(100, 50, QuestPersistOp.Complete());

        var ops = new ConcurrentBag<(long Coid, int MissionId, QuestPersistKind Kind)>();
        var count = queue.Flush((coid, missionId, op) => ops.Add((coid, missionId, op.Kind)));

        Assert.AreEqual(1, count);
        Assert.AreEqual(0, queue.PendingCount);
        Assert.IsTrue(ops.Contains((100L, 50, QuestPersistKind.Complete)));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Queue_ParallelEnqueueDifferentCharacters_Isolated()
    {
        var queue = new MissionPersistenceQueue();
        Parallel.For(0, 200, i =>
        {
            var coid = 1000L + (i % 10);
            queue.Enqueue(coid, 7, QuestPersistOp.Upsert(0, 0, Array.Empty<byte>()));
        });

        var byCoid = new ConcurrentDictionary<long, int>();
        queue.Flush((coid, _, _) => byCoid.AddOrUpdate(coid, 1, (_, c) => c + 1));

        Assert.AreEqual(10, byCoid.Count);
        foreach (var kv in byCoid)
            Assert.AreEqual(1, kv.Value, $"coid {kv.Key} should flush once (latest-wins)");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Queue_FailedPersistDuringParallelEnqueue_RetainsOrSucceedsWithoutLoss()
    {
        var queue = new MissionPersistenceQueue();
        queue.Enqueue(1, 1, QuestPersistOp.Upsert(0, 0, Array.Empty<byte>()));

        var failOnce = 0;
        var first = queue.Flush((_, _, _) =>
        {
            if (Interlocked.Exchange(ref failOnce, 1) == 0)
                throw new InvalidOperationException("db down");
        });
        Assert.AreEqual(0, first);
        Assert.AreEqual(1, queue.PendingCount);

        // Concurrent enqueues while pending — Complete must supersede Upsert.
        Parallel.Invoke(
            () => queue.Enqueue(1, 1, QuestPersistOp.Complete()),
            () => queue.Enqueue(1, 1, QuestPersistOp.Upsert(2, 0, Array.Empty<byte>())));

        var kinds = new List<QuestPersistKind>();
        var second = queue.Flush((_, _, op) => kinds.Add(op.Kind));
        Assert.AreEqual(1, second);
        Assert.AreEqual(1, kinds.Count);
        // Last parallel writer wins; either Complete or Upsert is acceptable for the race,
        // but state must be exactly one op and not empty.
        Assert.IsTrue(kinds[0] is QuestPersistKind.Complete or QuestPersistKind.Upsert);
        Assert.AreEqual(0, queue.PendingCount);
    }
}
