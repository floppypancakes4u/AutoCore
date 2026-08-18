using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Experience;

using AutoCore.Game.Experience;

/// <summary>
/// XP persistence must never block the sector tick thread: ram-kill bursts (several kills
/// within ~500ms) used to run one synchronous MySQL write per kill inside OnDeath, stalling
/// the tick and bunching ghost/loot sends into a client-visible hitch. The write-behind
/// decorator coalesces latest-wins per character and flushes off-thread.
/// </summary>
[TestClass]
public class WriteBehindProgressPersistenceTests
{
    private sealed class RecordingInner : ICharacterProgressPersistence
    {
        public readonly List<(long Coid, CharacterProgressSnapshot Snapshot)> Saves = new();
        public readonly Dictionary<long, CharacterProgressSnapshot> LoadResults = new();
        public Exception ThrowOnSave;
        public ManualResetEventSlim SaveHappened { get; } = new(false);

        public CharacterProgressSnapshot LoadProgress(long characterCoid)
            => LoadResults.TryGetValue(characterCoid, out var s) ? s : new CharacterProgressSnapshot(1, 0);

        public void SaveProgress(long characterCoid, CharacterProgressSnapshot progress)
        {
            if (ThrowOnSave != null)
                throw ThrowOnSave;

            lock (Saves)
                Saves.Add((characterCoid, progress));
            SaveHappened.Set();
        }
    }

    private static WriteBehindProgressPersistence CreateSut(RecordingInner inner, bool autoFlush = false)
        => new(inner) { AutoFlushOnEnqueue = autoFlush };

    [TestMethod]
    public void SaveProgress_DoesNotTouchInner_UntilFlush()
    {
        var inner = new RecordingInner();
        var sut = CreateSut(inner);

        sut.SaveProgress(100, new CharacterProgressSnapshot(5, 1234));

        Assert.AreEqual(0, inner.Saves.Count, "enqueue must not write on the calling thread");
        Assert.AreEqual(1, sut.PendingCount);

        var flushed = sut.FlushPending();

        Assert.AreEqual(1, flushed);
        Assert.AreEqual(1, inner.Saves.Count);
        Assert.AreEqual(100L, inner.Saves[0].Coid);
        Assert.AreEqual(1234, inner.Saves[0].Snapshot.Experience);
        Assert.AreEqual(0, sut.PendingCount);
    }

    [TestMethod]
    public void SaveProgress_CoalescesLatestWins_PerCharacter()
    {
        var inner = new RecordingInner();
        var sut = CreateSut(inner);

        sut.SaveProgress(100, new CharacterProgressSnapshot(5, 1000));
        sut.SaveProgress(100, new CharacterProgressSnapshot(5, 2000));
        sut.SaveProgress(100, new CharacterProgressSnapshot(6, 3000));

        Assert.AreEqual(1, sut.PendingCount, "same character coalesces to one pending write");

        sut.FlushPending();

        Assert.AreEqual(1, inner.Saves.Count, "burst of grants must produce a single DB write");
        Assert.AreEqual(3000, inner.Saves[0].Snapshot.Experience);
        Assert.AreEqual(6, inner.Saves[0].Snapshot.Level);
    }

    [TestMethod]
    public void FlushPending_WritesEachPendingCharacter()
    {
        var inner = new RecordingInner();
        var sut = CreateSut(inner);

        sut.SaveProgress(1, new CharacterProgressSnapshot(2, 10));
        sut.SaveProgress(2, new CharacterProgressSnapshot(3, 20));
        sut.SaveProgress(3, new CharacterProgressSnapshot(4, 30));

        Assert.AreEqual(3, sut.FlushPending());
        Assert.AreEqual(3, inner.Saves.Count);
    }

    [TestMethod]
    public void LoadProgress_ReturnsPendingSnapshot_ReadYourWrites()
    {
        var inner = new RecordingInner();
        inner.LoadResults[100] = new CharacterProgressSnapshot(5, 1000); // stale DB row
        var sut = CreateSut(inner);

        sut.SaveProgress(100, new CharacterProgressSnapshot(6, 9999));

        var loaded = sut.LoadProgress(100);

        Assert.AreEqual(9999, loaded.Experience, "pending snapshot is newer than the DB row");
        Assert.AreEqual(6, loaded.Level);
    }

    [TestMethod]
    public void LoadProgress_NoPending_FallsThroughToInner()
    {
        var inner = new RecordingInner();
        inner.LoadResults[100] = new CharacterProgressSnapshot(7, 4242);
        var sut = CreateSut(inner);

        var loaded = sut.LoadProgress(100);

        Assert.AreEqual(4242, loaded.Experience);
    }

    [TestMethod]
    public void Flush_InnerKeepsFailing_DeadLettersAfterBoundedAttempts()
    {
        var inner = new RecordingInner { ThrowOnSave = new InvalidOperationException("db down") };
        var sut = CreateSut(inner);

        sut.SaveProgress(100, new CharacterProgressSnapshot(5, 1000));

        for (var attempt = 0; attempt < WriteBehindProgressPersistence.MaxFlushAttempts; attempt++)
        {
            Assert.AreEqual(0, sut.FlushPending(), $"attempt {attempt} must fail without writing");
        }

        Assert.AreEqual(0, sut.PendingCount, "entry must dead-letter after the attempt budget");
        Assert.AreEqual(1, sut.DeadLetteredCount);

        // Recovery: later saves for the same character start a fresh attempt budget.
        inner.ThrowOnSave = null;
        sut.SaveProgress(100, new CharacterProgressSnapshot(5, 2000));
        Assert.AreEqual(1, sut.FlushPending());
    }

    [TestMethod]
    public void Flush_NewerSnapshotArrivesDuringRetry_IsNotLost()
    {
        var inner = new RecordingInner { ThrowOnSave = new InvalidOperationException("transient") };
        var sut = CreateSut(inner);

        sut.SaveProgress(100, new CharacterProgressSnapshot(5, 1000));
        Assert.AreEqual(0, sut.FlushPending());

        inner.ThrowOnSave = null;
        sut.SaveProgress(100, new CharacterProgressSnapshot(5, 2000));

        Assert.AreEqual(1, sut.FlushPending());
        Assert.AreEqual(2000, inner.Saves[^1].Snapshot.Experience, "latest snapshot must win after a failed attempt");
    }

    [TestMethod]
    public void AutoFlush_WritesInBackground_WithoutBlockingCaller()
    {
        var inner = new RecordingInner();
        var sut = CreateSut(inner, autoFlush: true);

        sut.SaveProgress(100, new CharacterProgressSnapshot(5, 1000));

        Assert.IsTrue(inner.SaveHappened.Wait(5000), "background flush must run shortly after enqueue");
        Assert.AreEqual(1, inner.Saves.Count);
    }

    [TestMethod]
    public void ExperienceService_DefaultPersistence_IsWriteBehind()
    {
        // Tripwire: GiveXp runs on the sector tick thread inside OnDeath. Its default
        // persistence must be the write-behind decorator, never the raw EF store.
        Assert.IsInstanceOfType(
            AutoCore.Game.Experience.ExperienceService.Instance.Persistence,
            typeof(WriteBehindProgressPersistence));
    }
}
