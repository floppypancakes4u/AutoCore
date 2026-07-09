using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Sector.Tests.Network;

using AutoCore.Sector.Network;

[TestClass]
public class SectorCombatTickTests
{
    /// <summary>
    /// SS-02 tripwire: one throwing connection must not prevent later connections from processing.
    /// </summary>
    [TestMethod]
    public void ProcessAll_WhenFirstEntryThrows_LaterEntriesStillRun()
    {
        var secondRan = false;
        var entries = new List<(long Coid, Action ProcessCombat)>
        {
            (1001L, () => throw new InvalidOperationException("SS-02 injected combat failure")),
            (1002L, () => { secondRan = true; }),
        };

        SectorCombatTick.ProcessAll(entries, onError: (_, _) => { });

        Assert.IsTrue(
            secondRan,
            "Expected the second connection's combat action to run after the first threw. " +
            "SS-02: per-connection isolation — one bad vehicle must not skip others.");
    }

    /// <summary>
    /// SS-02: failures must be reported with COID (and exception), not swallowed silently.
    /// </summary>
    [TestMethod]
    public void ProcessAll_WhenEntryThrows_OnErrorReceivesCoidAndException()
    {
        const long expectedCoid = 4242L;
        long? reportedCoid = null;
        Exception reportedEx = null;

        var entries = new List<(long Coid, Action ProcessCombat)>
        {
            (expectedCoid, () => throw new InvalidOperationException("SS-02 combat error payload")),
        };

        SectorCombatTick.ProcessAll(entries, onError: (coid, ex) =>
        {
            reportedCoid = coid;
            reportedEx = ex;
        });

        Assert.AreEqual(expectedCoid, reportedCoid, "onError must receive the failing connection COID.");
        Assert.IsNotNull(reportedEx, "onError must receive the exception.");
        Assert.IsInstanceOfType(reportedEx, typeof(InvalidOperationException));
        Assert.AreEqual("SS-02 combat error payload", reportedEx!.Message);
    }

    [TestMethod]
    public void ProcessAll_WhenAllSucceed_AllActionsRun()
    {
        var ran = new List<long>();
        var errors = 0;

        var entries = new List<(long Coid, Action ProcessCombat)>
        {
            (1L, () => ran.Add(1L)),
            (2L, () => ran.Add(2L)),
            (3L, () => ran.Add(3L)),
        };

        SectorCombatTick.ProcessAll(entries, onError: (_, _) => Interlocked.Increment(ref errors));

        CollectionAssert.AreEqual(new long[] { 1L, 2L, 3L }, ran);
        Assert.AreEqual(0, errors, "Successful actions must not invoke onError.");
    }

    [TestMethod]
    public void ProcessAll_NullAction_IsSkippedWithoutError()
    {
        var errors = 0;
        var afterNullRan = false;

        var entries = new List<(long Coid, Action ProcessCombat)>
        {
            (10L, null!),
            (11L, () => { afterNullRan = true; }),
        };

        SectorCombatTick.ProcessAll(entries, onError: (_, _) => Interlocked.Increment(ref errors));

        Assert.IsTrue(afterNullRan, "Null ProcessCombat should be skipped; later entries still run.");
        Assert.AreEqual(0, errors, "Null ProcessCombat must not report an error.");
    }

    [TestMethod]
    public void ProcessAll_DefaultLoggerPath_DoesNotRethrow()
    {
        var entries = new List<(long Coid, Action ProcessCombat)>
        {
            (99L, () => throw new InvalidOperationException("SS-02 default logger path")),
        };

        // No custom onError — uses Logger.WriteLog default. Must not propagate.
        SectorCombatTick.ProcessAll(entries);

        // If we reached here without exception, default path swallowed correctly.
        Assert.IsTrue(true);
    }
}
