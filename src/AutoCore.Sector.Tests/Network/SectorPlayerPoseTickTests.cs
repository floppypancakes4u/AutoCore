using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Sector.Tests.Network;

using AutoCore.Sector.Network;

/// <summary>
/// Regression for the sector-tick isolation wrapper around player pose dead reckoning.
/// One throwing connection must not skip pose advance for others (same SS-02 pattern as combat).
/// </summary>
[TestClass]
public class SectorPlayerPoseTickTests
{
    [TestMethod]
    public void ProcessAll_WhenFirstEntryThrows_LaterEntriesStillRun()
    {
        var secondRan = false;
        var entries = new List<(long Coid, Action AdvancePose)>
        {
            (1001L, () => throw new InvalidOperationException("injected pose failure")),
            (1002L, () => { secondRan = true; }),
        };

        SectorPlayerPoseTick.ProcessAll(entries, onError: (_, _) => { });

        Assert.IsTrue(secondRan,
            "Second connection pose advance must run after the first threw.");
    }

    [TestMethod]
    public void ProcessAll_WhenEntryThrows_OnErrorReceivesCoidAndException()
    {
        const long expectedCoid = 7777L;
        long? reportedCoid = null;
        Exception reportedEx = null;

        var entries = new List<(long Coid, Action AdvancePose)>
        {
            (expectedCoid, () => throw new InvalidOperationException("pose error payload")),
        };

        SectorPlayerPoseTick.ProcessAll(entries, onError: (coid, ex) =>
        {
            reportedCoid = coid;
            reportedEx = ex;
        });

        Assert.AreEqual(expectedCoid, reportedCoid);
        Assert.IsNotNull(reportedEx);
        Assert.IsInstanceOfType(reportedEx, typeof(InvalidOperationException));
        Assert.AreEqual("pose error payload", reportedEx!.Message);
    }

    [TestMethod]
    public void ProcessAll_WhenAllSucceed_AllActionsRun()
    {
        var ran = new List<long>();
        var errors = 0;

        var entries = new List<(long Coid, Action AdvancePose)>
        {
            (1L, () => ran.Add(1L)),
            (2L, () => ran.Add(2L)),
            (3L, () => ran.Add(3L)),
        };

        SectorPlayerPoseTick.ProcessAll(entries, onError: (_, _) => Interlocked.Increment(ref errors));

        CollectionAssert.AreEqual(new long[] { 1L, 2L, 3L }, ran);
        Assert.AreEqual(0, errors);
    }

    [TestMethod]
    public void ProcessAll_NullAction_IsSkippedWithoutError()
    {
        var errors = 0;
        var ran = false;

        var entries = new List<(long Coid, Action AdvancePose)>
        {
            (1L, null),
            (2L, () => { ran = true; }),
        };

        SectorPlayerPoseTick.ProcessAll(entries, onError: (_, _) => Interlocked.Increment(ref errors));

        Assert.IsTrue(ran);
        Assert.AreEqual(0, errors);
    }

    [TestMethod]
    public void ProcessAll_NullEntries_NoThrow()
    {
        SectorPlayerPoseTick.ProcessAll(null, onError: (_, _) => Assert.Fail("must not report error"));
    }

    [TestMethod]
    public void ProcessAll_DefaultOnError_DoesNotRethrow()
    {
        // Uses Logger path (onError null); must not escape the tick.
        SectorPlayerPoseTick.ProcessAll(new List<(long, Action)>
        {
            (9L, () => throw new InvalidOperationException("logged only")),
        });
    }

    [TestMethod]
    public void ProcessAll_Empty_NoOp()
    {
        var errors = 0;
        SectorPlayerPoseTick.ProcessAll(
            Array.Empty<(long, Action)>(),
            onError: (_, _) => Interlocked.Increment(ref errors));
        Assert.AreEqual(0, errors);
    }

    [TestMethod]
    public void ClampPoseDtSeconds_50ms_Is005()
    {
        Assert.AreEqual(0.05f, SectorPlayerPoseTick.ClampPoseDtSeconds(50), 1e-6f);
    }

    [TestMethod]
    public void ClampPoseDtSeconds_FloorsTinyDelta()
    {
        Assert.AreEqual(0.001f, SectorPlayerPoseTick.ClampPoseDtSeconds(0.1), 1e-6f);
        Assert.AreEqual(0.001f, SectorPlayerPoseTick.ClampPoseDtSeconds(0), 1e-6f);
    }

    [TestMethod]
    public void ClampPoseDtSeconds_CapsHugeDelta()
    {
        Assert.AreEqual(0.1f, SectorPlayerPoseTick.ClampPoseDtSeconds(500), 1e-6f);
        Assert.AreEqual(0.1f, SectorPlayerPoseTick.ClampPoseDtSeconds(5000), 1e-6f);
    }

    [TestMethod]
    public void ClampPoseDtSeconds_100ms_Is01()
    {
        Assert.AreEqual(0.1f, SectorPlayerPoseTick.ClampPoseDtSeconds(100), 1e-6f);
    }

    [TestMethod]
    public void ClampPoseDtSeconds_1ms_Is0001()
    {
        Assert.AreEqual(0.001f, SectorPlayerPoseTick.ClampPoseDtSeconds(1), 1e-6f);
    }
}
