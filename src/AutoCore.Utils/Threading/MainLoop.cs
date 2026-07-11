namespace AutoCore.Utils.Threading;

using AutoCore.Utils;

public class MainLoop
{
    /// <summary>Target tick period in ms. Mutable for live tuning (e.g. /sectorTick).</summary>
    public int LoopTime
    {
        get => Volatile.Read(ref _loopTime);
        set => Volatile.Write(ref _loopTime, Math.Clamp(value, MinLoopTimeMs, MaxLoopTimeMs));
    }

    public const int MinLoopTimeMs = 1;
    public const int MaxLoopTimeMs = 5000;

    private int _loopTime;

    public bool Running { get; private set; }
    public ILoopable Object { get; }
    public Thread LoopThread { get; private set; }

    private static long CurrentMs()
    {
        return (DateTime.UtcNow - new DateTime(1970, 1, 1)).Ticks / TimeSpan.TicksPerMillisecond;
    }

    public MainLoop(ILoopable obj, int loopTime)
    {
        Object = obj;
        _loopTime = Math.Clamp(loopTime, MinLoopTimeMs, MaxLoopTimeMs);
    }

    public void Start()
    {
        if (Running)
            throw new Exception("Unable to start a running MainLoop!");

        Running = true;

        LoopThread = new Thread(Loop)
        {
            Priority = ThreadPriority.Highest
        };
        LoopThread.Start();
    }

    public void Stop()
    {
        if (!Running)
            throw new Exception("Unable to stop a not running MainLoop!");

        // No need to join the thread, setting Running to false will eventually stop the thread
        Running = false;
    }

    private void Loop()
    {
        var prevTime = CurrentMs();
        var prevSleepTime = 0;

        while (Running)
        {
            var realTime = CurrentMs();

            var delta = realTime - prevTime;

            // SS-01: never let a tick exception kill the main loop thread.
            try
            {
                Object.MainLoop(delta);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error,
                    $"Unhandled exception in MainLoop tick; continuing. {ex}");
            }

            prevTime = realTime;

            // Floor sleep with LoopTime so short ticks (e.g. 10ms) are not forced to 10ms+ overhead
            // when LoopTime itself is small; never sleep less than 1ms.
            var loopTime = LoopTime;
            var minSleep = Math.Max(1, Math.Min(10, loopTime));

            if (delta <= loopTime + prevSleepTime)
            {
                prevSleepTime = loopTime + prevSleepTime - (int)delta;
                if (prevSleepTime < minSleep)
                    prevSleepTime = minSleep;
            }
            else
                prevSleepTime = minSleep;

            Thread.Sleep(prevSleepTime);
        }
    }
}
