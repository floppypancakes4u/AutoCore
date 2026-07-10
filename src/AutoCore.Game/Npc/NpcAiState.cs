namespace AutoCore.Game.Npc;

using AutoCore.Game.Constants;
using AutoCore.Game.Structures;

/// <summary>
/// Server-side runtime AI state for a single NPC <see cref="Entities.Creature"/> or
/// <see cref="Entities.Vehicle"/> (NPC.md). Not persisted; rebuilt from <see cref="Profile"/>
/// on spawn. Must stay null for player-controlled <c>Character</c> instances — call sites that
/// create/assign it are expected to guard with `is not Character`.
/// </summary>
public sealed class NpcAiState
{
    /// <summary>wad.xml tCreatureAI row driving this NPC's behavior, or null if unassigned.</summary>
    public CreatureAiProfile Profile { get; set; }

    /// <summary>Current combat phase sent to clients under the creature/vehicle StateMask.</summary>
    public HBAICombatState CombatState { get; set; }

    /// <summary>Spawn/anchor position used for patrol loops and return-to-home behavior.</summary>
    public Vector3 HomePosition { get; set; }

    /// <summary>Index into the active path's waypoint list; -1 when not following a path.</summary>
    public int PathIndex { get; set; } = -1;

    /// <summary>+1 walking the path forward, -1 walking it backward (ping-pong patrols).</summary>
    public int PathDirection { get; set; } = 1;

    /// <summary>Environment.TickCount64 timestamp the NPC is idling/waiting until.</summary>
    public long WaitUntilMs { get; set; }

    /// <summary>Environment.TickCount64 timestamp combat engagement started.</summary>
    public long EngageStartedMs { get; set; }

    /// <summary>Environment.TickCount64 timestamp a flee state should end.</summary>
    public long FleeUntilMs { get; set; }

    /// <summary>Environment.TickCount64 timestamp of the last nearby-hostile aggro scan.</summary>
    public long LastAggroScanMs { get; set; }

    /// <summary>Whether this NPC has already called nearby allies for help this engagement.</summary>
    public bool HelpCalled { get; set; }

    /// <summary>Whether the NPC is currently walking back to <see cref="HomePosition"/>.</summary>
    public bool ReturningHome { get; set; }
}
