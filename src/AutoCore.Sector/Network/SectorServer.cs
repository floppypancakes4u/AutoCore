namespace AutoCore.Sector.Network;

using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Managers;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Sector.Dev;
using AutoCore.Sector.Config;
using AutoCore.Utils;
using AutoCore.Utils.Logging;
using AutoCore.Utils.Reliability;
using AutoCore.Utils.Server;
using AutoCore.Utils.Threading;
using AutoCore.Utils.Timer;
using System.Diagnostics;
using System.Net;

public partial class SectorServer : BaseServer, ILoopable
{
    /// <summary>
    /// 50ms: halves path step length and matches TNL ghost send floor so hard pose snaps
    /// are smaller/denser (client capture: median SetDrivingInputs gap was ~480ms when packs starved).
    /// </summary>
    public const int MainLoopTime = 50; // Milliseconds

    public SectorConfig Config { get; private set; } = new();
    public IPAddress PublicAddress { get; private set; }
    public MainLoop Loop { get; }
    public Timer Timer { get; } = new();
    public override bool IsRunning => Loop != null && Loop.Running;
    public TNLInterface Interface { get; private set; }
    private readonly object _interfaceLock = new();
    private DevControlServer _devControlServer;
    private long _lastPathPoseDiagBucket = -1;
    private readonly HealthSummaryReporter _healthSummary;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private long _tickCount;
    private long _tickDurationSumMs;
    private long _tickDurationMaxMs;

    public SectorServer()
        : base("Sector")
    {
        Loop = new MainLoop(this, MainLoopTime);
        _healthSummary = new HealthSummaryReporter(CollectHealthMetrics, intervalMs: 60_000);

        RegisterCommands();
    }

    private IReadOnlyList<(string Key, object Value)> CollectHealthMetrics()
    {
        var avg = _tickCount > 0 ? (double)_tickDurationSumMs / _tickCount : 0;
        var max = _tickDurationMaxMs;
        _tickCount = 0;
        _tickDurationSumMs = 0;
        _tickDurationMaxMs = 0;

        var sessions = 0;
        try
        {
            sessions = Interface?.MapConnections?.Count ?? 0;
        }
        catch
        {
            // Interface may be mid-teardown
        }

        return new (string, object)[]
        {
            ("Sessions", sessions),
            ("TickAvgMs", Math.Round(avg, 2)),
            ("TickMaxMs", max),
            ("MissionPersistPending", MissionPersistence.Instance.PendingPersistCount),
            ("MissionPersistDeadLettered", MissionPersistence.Instance.DeadLetteredCount),
            ("Ss31SelectSkips", CharacterSelectionManager.CorruptIdentitySkipCount),
            ("Ss31OverwriteRefusals", AutoCore.Game.Inventory.InventoryPersistence.Ss31OverwriteRefusedCount),
            ("UptimeSeconds", (long)_uptime.Elapsed.TotalSeconds),
        };
    }

    public void Setup(SectorConfig config)
    {
        Logger.WriteLog(LogType.Initialize, "Setting up the Sector server...");

        if (config != null)
            Config = config;

        Logger.WriteLog(LogType.Initialize, "Initializing the TNL interface...");
        Interface = new TNLInterface(Config.GameConfig.Port, true)
        {
            AllowVersionMismatch = Config.GameConfig.AllowVersionMismatch,
            ExpectedVersion = Config.GameConfig.ExpectedVersion > 0 ? Config.GameConfig.ExpectedVersion : TNLInterface.Version
        };

        Logger.WriteLog(LogType.Initialize, "Initializing the network...");
        PublicAddress = IPAddress.Parse(Config.GameConfig.PublicAddress);

        RegisterSectorLoopControl();

        // /clone routes through AutoCore.Game's CloneCommandControl seam into AutoCore.Sim.
        AutoCore.Sim.SimHost.InstallCommandHook();

        Logger.WriteLog(LogType.Initialize, "The Sector server has been setup!");
    }

    /// <summary>Exposes live main-loop period to chat/console via <see cref="SectorLoopControl"/>.</summary>
    private void RegisterSectorLoopControl()
    {
        SectorLoopControl.GetLoopMilliseconds = () => Loop?.LoopTime;
        SectorLoopControl.TrySetLoopMilliseconds = ms =>
        {
            if (Loop == null)
                return "Sector main loop is not running.";

            var before = Loop.LoopTime;
            Loop.LoopTime = ms;
            var after = Loop.LoopTime;
            Logger.WriteLog(LogType.Command, "Sector main loop period {0}ms → {1}ms (requested {2}ms)", before, after, ms);
            return $"Sector tick set to {after}ms (requested {ms}ms; clamp {Utils.Threading.MainLoop.MinLoopTimeMs}-{Utils.Threading.MainLoop.MaxLoopTimeMs}).";
        };
    }

    public void MainLoop(long delta)
    {
        var tickSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Timer.Update(delta);

            if (Interface == null)
                return;

            lock (_interfaceLock)
            {
                if (Interface == null)
                    return;

                // SS-12: each tick stage is isolated. These calls were unguarded, so a failure in any
                // one of them aborted the whole remaining tick — including Interface.Pulse(), which
                // is what actually ships state to clients. The tick thread survived (SS-01) but that
                // tick produced no network output at all.

                // Refresh spatial-grid cells for entities that moved since the last tick before any
                // scope queries run inside Pulse(), so interest management sees current positions.
                Guard.Run("sector tick: RebucketAllGrids", MapManager.Instance.RebucketAllGrids);

                // NPC AI before Pulse so ApplyServerMove dirties PositionMask on the same tick that
                // CollapseDirtyList + ghost WritePacket run. Previously TickNpcs ran after Pulse, so
                // pose dirties waited a full MainLoopTime (100ms) and often looked like sparse snaps.
                Guard.Run("sector tick: TickNpcs",
                    () => MapManager.Instance.TickNpcs(Environment.TickCount64, delta / 1000f));

                // Hard guarantee: every pathing NPC re-enters the TNL dirty queue every tick.
                // Live WireDiag after rate floor still showed only ~4 pose packs per Gunny then silence.
                var pathPoseDirty = 0;
                Guard.Run("sector tick: ForcePathVehiclePoseDirty",
                    () => pathPoseDirty = MapManager.Instance.ForcePathVehiclePoseDirty());

                // Simulated clone vehicles (AutoCore.Sim): lifecycle checks and, in later phases,
                // physics-driven movement. Must run before Interface.Pulse for the same
                // dirty-mask-then-pack ordering reason as TickNpcs.
                Guard.Run("sector tick: SimHost.Tick",
                    () => AutoCore.Sim.SimHost.Instance.Tick(Environment.TickCount64, delta / 1000f));

                // Player pose dead reckoning between C2S VehicleMoved: keep-dirty rebroadcasts an
                // advancing pose so remote observers do not hard-snap to a frozen server position
                // every TNL period (choppy remote vehicles). Must run before Pulse for the same reason
                // as TickNpcs. NPC path pose is advanced by TickNpcs above.
                var poseDt = SectorPlayerPoseTick.ClampPoseDtSeconds(delta);
                var poseEntries = new List<(long Coid, Action AdvancePose)>(Interface.MapConnections.Count);
                foreach (var kvp in Interface.MapConnections)
                {
                    var conn = kvp.Value;
                    var coid = conn != null ? conn.GetPlayerCOID() : kvp.Key;
                    poseEntries.Add((coid, () => conn?.CurrentCharacter?.CurrentVehicle?.AdvanceNetworkPose(poseDt)));
                }
                SectorPlayerPoseTick.ProcessAll(poseEntries);

                // Ghost pack/send. Several GhostObject.PackUpdate overrides throw by design (for
                // example "PackUpdate for GhostObject without parent!"), so one malformed ghost must
                // not take down the rest of the tick.
                Guard.Run("sector tick: Interface.Pulse", Interface.Pulse);

                if ((Environment.TickCount64 / 2000) != _lastPathPoseDiagBucket)
                {
                    _lastPathPoseDiagBucket = Environment.TickCount64 / 2000;
                    var rates = "";
                    foreach (var kvp in Interface.MapConnections)
                    {
                        var c = kvp.Value;
                        if (c == null)
                            continue;
                        rates =
                            $" period={c.NegotiatedPacketSendPeriodMs}ms pkt={c.NegotiatedPacketSendSizeBytes}B ghosting={c.IsGhosting()}";
                        break;
                    }

                    var packs = System.Threading.Interlocked.Exchange(ref GhostVehicle.PosePacksSinceDiag, 0);
                    if (LogFilters.PathPoseForce)
                    {
                        Logger.WriteLog(LogType.Network,
                            "PathPoseForce dirtyGhosted={0} posePacks2s={1}{2}",
                            pathPoseDirty, packs, rates);
                    }
                }

                // Server-side combat tick: decouple firing from VehicleMoved packet arrival rate.
                // This fixes "clicking fires faster than holding" when the client sends fewer movement packets while stationary.
                // SS-02: isolate per connection so one bad vehicle cannot skip others and failures are logged.
                var combatEntries = new List<(long Coid, Action ProcessCombat)>(Interface.MapConnections.Count);
                foreach (var kvp in Interface.MapConnections)
                {
                    var conn = kvp.Value;
                    var coid = conn != null ? conn.GetPlayerCOID() : kvp.Key;
                    combatEntries.Add((coid, () => conn?.CurrentCharacter?.CurrentVehicle?.ProcessCombatIfFiring()));
                }
                SectorCombatTick.ProcessAll(combatEntries);

                // Log-only watchdog for a map-transfer handshake that stopped advancing. The client
                // has no local player between MapInfo and the Stage3 ack, so a stalled handshake
                // shows up in-game as a frozen full loading bar with nothing in the server log.
                Guard.Run("sector tick: map-transfer stall watchdog", () =>
                {
                    var nowMs = Environment.TickCount64;
                    foreach (var kvp in Interface.MapConnections)
                    {
                        var conn = kvp.Value;
                        if (conn == null)
                            continue;

                        conn.ReportMapTransferHandshakeStall(nowMs);
                        // Creates landing is only half of world entry — ghosting still has to start.
                        conn.ReportGhostingNeverStarted(nowMs);
                    }
                });

                // Delayed map-prop corpse despawn (ram wrecks stay ~12.5s then DestroyObject).
                MapPropCorpseDespawn.Tick();

                // Combat pools (heat cool / shield / power) — CVOGHBRegeneration @ 3000 ms. HP does not regen.
                // Accumulate MainLoop delta into discrete 3000 ms pulses per player vehicle.
                var poolDeltaMs = (int)Math.Clamp(delta, 1, 250);
                foreach (var kvp in Interface.MapConnections)
                {
                    var character = kvp.Value?.CurrentCharacter;
                    var vehicle = character?.CurrentVehicle;
                    if (vehicle == null || vehicle.GetIsCorpse())
                        continue;

                    var weaponsFiring = vehicle.Firing != 0;
                    try
                    {
                        AutoCore.Game.Combat.VehicleCombatPool.Advance(
                            vehicle, character, poolDeltaMs, weaponsFiring);
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLog(LogType.Error,
                            "VehicleCombatPool.Advance failed coid={0}: {1}",
                            vehicle.ObjectId?.Coid ?? 0, ex.Message);
                    }
                }
            }
        }
        finally
        {
            tickSw.Stop();
            var tickMs = tickSw.ElapsedMilliseconds;
            _tickCount++;
            _tickDurationSumMs += tickMs;
            if (tickMs > _tickDurationMaxMs)
                _tickDurationMaxMs = tickMs;

            if (tickMs > MainLoopTime)
            {
                GameLog.Warn("TickOverrun", "SRV-001",
                    ("DurationMs", tickMs),
                    ("BudgetMs", MainLoopTime));
            }

            _healthSummary.Tick(delta);
        }
    }

    public bool Start()
    {
        // If no config file has been found, these values are 0 by default
        if (Config.GameConfig.Port == 0)
        {
            Logger.WriteLog(LogType.Error, "Invalid config values!");
            return false;
        }

        Loop.Start();

        Logger.WriteLog(LogType.Network, "*** Listening for clients on port {0}", Config.GameConfig.Port);

        if (Config.GameConfig.EnableDevControl)
        {
            _devControlServer = new DevControlServer(() => Interface);
            _devControlServer.Start(Config.GameConfig.DevControlPort);
        }

        return true;
    }

    public void Shutdown()
    {
        Logger.WriteLog(LogType.None, "Shutting down the server...");

        _devControlServer?.Stop();
        _devControlServer = null;

        lock (_interfaceLock)
        {
            if (Interface != null)
            {
                Interface.Close();
                Interface.Socket?.Stop();
                Interface = null;
            }
        }

        if (Loop != null && Loop.Running)
            Loop.Stop();

        Logger.WriteLog(LogType.None, "The server was shut down!");
    }
}
