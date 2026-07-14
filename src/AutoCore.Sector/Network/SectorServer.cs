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
using AutoCore.Utils.Server;
using AutoCore.Utils.Threading;
using AutoCore.Utils.Timer;
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

    public SectorServer()
        : base("Sector")
    {
        Loop = new MainLoop(this, MainLoopTime);

        RegisterCommands();
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
        Timer.Update(delta);

        if (Interface == null)
            return;

        lock (_interfaceLock)
        {
            if (Interface == null)
                return;

            // Refresh spatial-grid cells for entities that moved since the last tick before any
            // scope queries run inside Pulse(), so interest management sees current positions.
            MapManager.Instance.RebucketAllGrids();

            // NPC AI before Pulse so ApplyServerMove dirties PositionMask on the same tick that
            // CollapseDirtyList + ghost WritePacket run. Previously TickNpcs ran after Pulse, so
            // pose dirties waited a full MainLoopTime (100ms) and often looked like sparse snaps.
            MapManager.Instance.TickNpcs(Environment.TickCount64, delta / 1000f);

            // Hard guarantee: every pathing NPC re-enters the TNL dirty queue every tick.
            // Live WireDiag after rate floor still showed only ~4 pose packs per Gunny then silence.
            var pathPoseDirty = MapManager.Instance.ForcePathVehiclePoseDirty();

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

            Interface.Pulse();

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

            // Delayed map-prop corpse despawn (ram wrecks stay ~12.5s then DestroyObject).
            MapPropCorpseDespawn.Tick();

            // Combat pools (heat cool / shield / power / HP regen) — CVOGHBRegeneration @ 3000 ms.
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
            Interface.Close();
            Interface = null;
        }

        Loop.Stop();

        Logger.WriteLog(LogType.None, "The server was shut down!");
    }
}
