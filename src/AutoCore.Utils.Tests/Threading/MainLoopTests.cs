using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Utils.Tests.Threading;

using AutoCore.Utils.Threading;

[TestClass]
public class MainLoopTests
{
    /// <summary>
    /// SS-01: An unhandled exception from ILoopable.MainLoop must not kill the loop thread.
    /// The loop should catch, log, and continue ticking.
    /// </summary>
    [TestMethod]
    public void MainLoop_ContinuesAfterUnhandledExceptionInTick()
    {
        var callCount = 0;
        var loopable = new CallbackLoopable(_ =>
        {
            var n = Interlocked.Increment(ref callCount);
            if (n == 1)
                throw new InvalidOperationException("SS-01 injected tick failure");
        });

        var mainLoop = new MainLoop(loopable, loopTime: 15);
        mainLoop.Start();

        try
        {
            Assert.IsTrue(mainLoop.Running, "Loop should report Running after Start.");

            // Wait for at least two ticks: first throws, second must still run if SS-01 is fixed.
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (Volatile.Read(ref callCount) < 2 && DateTime.UtcNow < deadline)
                Thread.Sleep(10);

            var finalCount = Volatile.Read(ref callCount);
            Assert.IsTrue(
                finalCount >= 2,
                $"Expected MainLoop to continue after an exception in a tick (callCount >= 2), but callCount was {finalCount}. " +
                "SS-01: unhandled exceptions must not kill the main loop thread.");

            Assert.IsTrue(
                mainLoop.Running,
                "MainLoop.Running must stay true after a recovered tick exception (until Stop).");
        }
        finally
        {
            StopAndJoin(mainLoop);
        }
    }

    [TestMethod]
    public void Constructor_SetsObjectAndLoopTime()
    {
        var loopable = new CallbackLoopable(_ => { });
        var mainLoop = new MainLoop(loopable, loopTime: 42);

        Assert.AreSame(loopable, mainLoop.Object);
        Assert.AreEqual(42, mainLoop.LoopTime);
        Assert.IsFalse(mainLoop.Running);
        Assert.IsNull(mainLoop.LoopThread);
    }

    [TestMethod]
    public void LoopTime_Set_ClampsAndIsReadable()
    {
        var mainLoop = new MainLoop(new CallbackLoopable(_ => { }), loopTime: 100);
        mainLoop.LoopTime = 50;
        Assert.AreEqual(50, mainLoop.LoopTime);

        mainLoop.LoopTime = 0;
        Assert.AreEqual(MainLoop.MinLoopTimeMs, mainLoop.LoopTime);

        mainLoop.LoopTime = 99999;
        Assert.AreEqual(MainLoop.MaxLoopTimeMs, mainLoop.LoopTime);
    }

    [TestMethod]
    public void Start_WhenAlreadyRunning_Throws()
    {
        var callCount = 0;
        var mainLoop = new MainLoop(new CallbackLoopable(_ => Interlocked.Increment(ref callCount)), loopTime: 20);
        mainLoop.Start();

        try
        {
            var ex = Assert.ThrowsException<Exception>(() => mainLoop.Start());
            Assert.AreEqual("Unable to start a running MainLoop!", ex.Message);
            Assert.IsTrue(mainLoop.Running);
        }
        finally
        {
            StopAndJoin(mainLoop);
        }
    }

    [TestMethod]
    public void Stop_WhenNotRunning_Throws()
    {
        var mainLoop = new MainLoop(new CallbackLoopable(_ => { }), loopTime: 20);

        var ex = Assert.ThrowsException<Exception>(() => mainLoop.Stop());
        Assert.AreEqual("Unable to stop a not running MainLoop!", ex.Message);
        Assert.IsFalse(mainLoop.Running);
    }

    [TestMethod]
    public void MainLoop_SuccessfulTicks_ThenStop_ExitsCleanly()
    {
        var callCount = 0;
        var mainLoop = new MainLoop(
            new CallbackLoopable(_ => Interlocked.Increment(ref callCount)),
            loopTime: 15);

        mainLoop.Start();

        try
        {
            Assert.IsTrue(mainLoop.Running);
            Assert.IsNotNull(mainLoop.LoopThread);

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (Volatile.Read(ref callCount) < 3 && DateTime.UtcNow < deadline)
                Thread.Sleep(10);

            Assert.IsTrue(
                Volatile.Read(ref callCount) >= 3,
                "Expected several successful ticks before Stop.");
        }
        finally
        {
            StopAndJoin(mainLoop);
        }

        Assert.IsFalse(mainLoop.Running);

        var countAfterStop = Volatile.Read(ref callCount);
        Thread.Sleep(50);
        Assert.AreEqual(
            countAfterStop,
            Volatile.Read(ref callCount),
            "No further ticks should run after Stop completes.");
    }

    /// <summary>
    /// Exercises the timing branches in Loop (fast tick sleep clamp and slow-tick path)
    /// by varying work duration relative to loopTime.
    /// </summary>
    [TestMethod]
    public void MainLoop_SleepTimingBranches_AreExercised()
    {
        var callCount = 0;
        var mainLoop = new MainLoop(new CallbackLoopable(delta =>
        {
            var n = Interlocked.Increment(ref callCount);
            // First ticks: spend enough wall time that delta can exceed a small loopTime (slow path).
            // Later ticks: return quickly so the fast-path sleep clamp (prevSleepTime < 10) can run.
            if (n <= 2)
                Thread.Sleep(40);
        }), loopTime: 15);

        mainLoop.Start();

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (Volatile.Read(ref callCount) < 6 && DateTime.UtcNow < deadline)
                Thread.Sleep(10);

            Assert.IsTrue(
                Volatile.Read(ref callCount) >= 6,
                "Expected enough ticks to exercise both slow and fast sleep branches.");
        }
        finally
        {
            StopAndJoin(mainLoop);
        }
    }

    [TestMethod]
    public void MainLoop_PassesPositiveDeltaToLoopable()
    {
        long lastDelta = -1;
        var sawTick = new ManualResetEventSlim(false);
        var mainLoop = new MainLoop(new CallbackLoopable(delta =>
        {
            Interlocked.Exchange(ref lastDelta, delta);
            sawTick.Set();
        }), loopTime: 15);

        mainLoop.Start();

        try
        {
            Assert.IsTrue(sawTick.Wait(TimeSpan.FromSeconds(2)), "Expected at least one tick.");
            // Second tick should have a non-negative delta from wall clock.
            sawTick.Reset();
            Assert.IsTrue(sawTick.Wait(TimeSpan.FromSeconds(2)), "Expected a second tick.");
            Assert.IsTrue(
                Volatile.Read(ref lastDelta) >= 0,
                $"Expected non-negative delta, got {lastDelta}.");
        }
        finally
        {
            StopAndJoin(mainLoop);
        }
    }

    private static void StopAndJoin(MainLoop mainLoop)
    {
        if (mainLoop.Running)
            mainLoop.Stop();

        mainLoop.LoopThread?.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class CallbackLoopable : ILoopable
    {
        private readonly Action<long> _onTick;

        public CallbackLoopable(Action<long> onTick) => _onTick = onTick;

        public void MainLoop(long delta) => _onTick(delta);
    }
}
