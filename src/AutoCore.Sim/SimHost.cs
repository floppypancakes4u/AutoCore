using AutoCore.Game.Chat;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Sim.Clone;
using AutoCore.Utils;
using AutoCore.Utils.Reliability;

namespace AutoCore.Sim;

/// <summary>
/// Facade the Sector server hosts: owns the clone manager (and later the per-map sim worlds).
/// Wiring: SectorServer calls <see cref="InstallCommandHook"/> at startup and
/// <see cref="Tick"/> once per main-loop tick (after TickNpcs, before Interface.Pulse).
/// </summary>
public sealed class SimHost
{
    public static SimHost Instance { get; } = new();

    private readonly Collision.MapCollisionWorlds _collisionWorlds = new();
    private readonly CloneManager _cloneManager;
    private readonly Npc.NpcVehicleSimManager _npcVehicles;

    public SimHost()
    {
        // One hull-world cache shared by clones and NPC vehicles — a map's static collision
        // world is built once no matter who asks first.
        _cloneManager = new CloneManager(_collisionWorlds);
        _npcVehicles = new Npc.NpcVehicleSimManager(_collisionWorlds);
    }

    internal CloneManager CloneManager => _cloneManager;

    internal Npc.NpcVehicleSimManager NpcVehicles => _npcVehicles;

    /// <summary>
    /// NpcTicker hook: adopt (and keep owning) a pathed NPC vehicle. Boundary catch — a bad
    /// vehicle must fall back to the legacy mover, not abort the NPC tick.
    /// </summary>
    public bool TryAdoptNpcVehicle(AutoCore.Game.Entities.Vehicle vehicle)
    {
        try
        {
            return _npcVehicles.TryAdopt(vehicle);
        }
        catch (Exception ex)
        {
            Logger.WriteException(LogType.Error, "SimHost.TryAdoptNpcVehicle", ex);
            return false;
        }
    }

    /// <summary>
    /// Boundary catch (repo exception-safety rules): a spawn failure on live data must degrade
    /// to a chat message, not abort the inbound packet handler (seen live 2026-08-08 with an
    /// unconstructible equipped ornament).
    /// </summary>
    public string ToggleClone(Character character, string args)
    {
        try
        {
            var count = 1;
            float? spacing = null;
            if (!string.IsNullOrWhiteSpace(args))
            {
                var usage = $"Usage: /clone [count 1-{Clone.CloneManager.MaxFleetSize}] [spacing 1-50 m]";
                var argv = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!int.TryParse(argv[0], out count) || count < 1 || count > Clone.CloneManager.MaxFleetSize)
                    return usage;

                if (argv.Length > 1)
                {
                    if (!float.TryParse(argv[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var metres)
                        || metres is < 1f or > 50f)
                    {
                        return usage;
                    }

                    spacing = metres;
                }
            }

            return _cloneManager.Toggle(character, count, spacing);
        }
        catch (Exception ex)
        {
            Logger.WriteException(LogType.Error, "SimHost.ToggleClone", ex);
            return "Clone failed — see server log.";
        }
    }

    public void Tick(long nowMs, float dt)
    {
        // Start hull builds as soon as a player is on a map so turret LOS is ready
        // before the first combat scan (GetOrRequest is otherwise first-hit and
        // returned null → a clear shot through walls).
        Guard.Run("sim: prefetch hull worlds", PrefetchHullWorlds);
        _cloneManager.Tick(nowMs, dt);
        _npcVehicles.Tick(nowMs, dt);
    }

    void PrefetchHullWorlds()
    {
        foreach (var map in MapManager.Instance.AllMaps())
        {
            if (map.PlayerCount > 0)
                _collisionWorlds.GetOrRequest(map);
        }
    }

    /// <summary>
    /// /clonetrim: sets or reports the global publish-height trim (metres). Live tuning knob
    /// for the residual per-map body height (2026-08-09 feedback: "ever so slightly floaty").
    /// </summary>
    public string TrimClone(Character character, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
            return $"Clone height trim is {CloneManager.HeightTrim:+0.00;-0.00;0.00} m. Usage: /clonetrim <metres> (e.g. /clonetrim -0.35)";

        if (!float.TryParse(arg, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var trim)
            || !float.IsFinite(trim) || MathF.Abs(trim) > 5f)
        {
            return "Usage: /clonetrim <metres between -5 and 5>  (e.g. /clonetrim -0.35)";
        }

        CloneManager.HeightTrim = trim;
        return $"Clone height trim set to {trim:+0.00;-0.00;0.00} m.";
    }

    /// <summary>
    /// /clonefollowdist: sets the live follow-distance override (metres; "default" resets to
    /// the tuning default). Added so obstacle-collision testing can trail far enough back to
    /// line the clone up on things (user request 2026-08-09).
    /// </summary>
    public string SetFollowDistance(Character character, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            var current = Ai.CloneAiTuning.FollowDistanceOverride;
            return current.HasValue
                ? $"Clone follow distance override is {current.Value:0.#} m. Usage: /clonefollowdist <metres|default>"
                : $"Clone follow distance is the default ({new Ai.CloneAiTuning().FollowDistance:0.#} m). Usage: /clonefollowdist <metres|default>";
        }

        if (arg.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            Ai.CloneAiTuning.FollowDistanceOverride = null;
            return $"Clone follow distance reset to default ({new Ai.CloneAiTuning().FollowDistance:0.#} m).";
        }

        if (!float.TryParse(arg, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var metres)
            || metres is < 2f or > 200f)
        {
            return "Usage: /clonefollowdist <metres between 2 and 200, or 'default'>";
        }

        Ai.CloneAiTuning.FollowDistanceOverride = metres;
        return $"Clone follow distance set to {metres:0.#} m.";
    }

    /// <summary>/clonestop (hold=true) and /clonefollow (hold=false).</summary>
    public string SetCloneHold(Character character, bool hold) => _cloneManager.SetHold(character, hold);

    /// <summary>/cloneteleport.</summary>
    public string TeleportClone(Character character) => _cloneManager.Teleport(character);

    /// <summary>/clonestartpath.</summary>
    public string StartClonePath(Character character) => _cloneManager.StartPath(character);

    /// <summary>/clonepathspeed: sets or resets the live path cruise speed (m/s).</summary>
    public string SetPathSpeed(Character character, string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            var current = Ai.CloneAiTuning.PathSpeedOverride;
            return current.HasValue
                ? $"Clone path speed override is {current.Value:0.#} m/s. Usage: /clonepathspeed <m/s|default>"
                : $"Clone path speed is the default ({new Ai.CloneAiTuning().PathSpeed:0.#} m/s). Usage: /clonepathspeed <m/s|default>";
        }

        if (arg.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            Ai.CloneAiTuning.PathSpeedOverride = null;
            return $"Clone path speed reset to default ({new Ai.CloneAiTuning().PathSpeed:0.#} m/s).";
        }

        if (!float.TryParse(arg, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var speed)
            || speed is < 2f or > 60f)
        {
            return "Usage: /clonepathspeed <m/s between 2 and 60, or 'default'>";
        }

        Ai.CloneAiTuning.PathSpeedOverride = speed;
        return $"Clone path speed set to {speed:0.#} m/s.";
    }

    /// <summary>Routes the /clone* commands through AutoCore.Game's hook seam to this host.</summary>
    public static void InstallCommandHook()
    {
        CloneCommandControl.TryToggleClone = Instance.ToggleClone;
        CloneCommandControl.TryTrimClone = Instance.TrimClone;
        CloneCommandControl.TrySetFollowDistance = Instance.SetFollowDistance;
        CloneCommandControl.TrySetHold = Instance.SetCloneHold;
        CloneCommandControl.TryTeleportClone = Instance.TeleportClone;
        CloneCommandControl.TryStartPath = Instance.StartClonePath;
        CloneCommandControl.TrySetPathSpeed = Instance.SetPathSpeed;
        AutoCore.Game.Npc.NpcVehicleSimControl.TrySimDrive = Instance.TryAdoptNpcVehicle;
        AutoCore.Game.Npc.NpcTurretLos.TryHasClearLos = Instance.HasTurretClearLos;
    }

    /// <summary>
    /// Hull-world LOS for stationary turrets. Missing/building world degrades to clear so
    /// turrets still shoot until the map's static collision is ready.
    /// </summary>
    internal bool HasTurretClearLos(Game.Map.SectorMap map, Game.Structures.Vector3 from, Game.Structures.Vector3 to)
    {
        return Collision.LineOfSight.TurretMayShoot(_collisionWorlds.GetOrRequest(map), from, to);
    }
}
