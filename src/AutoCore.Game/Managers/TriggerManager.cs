namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils;
using AutoCore.Utils.Memory;
using System.Collections.Concurrent;

/// <summary>
/// Volume + condition-driven trigger dispatch.
/// Re-evaluates when the activator moves and when mission/logic state changes
/// (client: computed vars type 9/11/12 + StepTriggers / variable watchers).
///
/// Cascade cycles (Activate→self, VariableSet loops, mission re-eval re-entry) are
/// bounded by depth + per-trigger stack guards — map data often has self-reactivating triggers.
/// </summary>
public class TriggerManager : Singleton<TriggerManager>
{
    /// <summary>Hard cap on nested FireTriggerReactions / Activate cascades.</summary>
    public const int MaxCascadeDepth = 16;

    // Physical enter latch: (ObjectCoid, TriggerCoid) currently inside and already fired.
    private readonly ConcurrentDictionary<(long ObjectCoid, long TriggerCoid), bool> _activeTriggers = new();

    // Per-collider repair cadence. Multiple vehicles on one pad must never share a deadline.
    private readonly ConcurrentDictionary<(long ObjectCoid, long TriggerCoid, long ReactionCoid), long> _nextSkillPulseMs = new();
    internal const long SkillPulseIntervalMs = 1000;

    // One-shot for remote/condition-driven fires (mission change, variable set).
    private readonly ConcurrentDictionary<(long ActorCoid, long TriggerCoid), bool> _firedConditionalTriggers = new();

    // Re-entrancy: cascade stack (not concurrent — game logic is single-threaded per sector).
    private int _cascadeDepth;
    private readonly HashSet<long> _firingTriggerCoids = new();
    private bool _missionReevalActive;
    private bool _missionReevalPending;
    private bool _variableReevalActive;

    /// <summary>
    /// Fires a trigger reaction list once ActivationCount allows.
    /// Shared by collision, mission re-eval, variable watchers, and Activate cascades.
    /// </summary>
    public void FireTriggerReactions(ClonedObjectBase activator, Trigger trigger)
    {
        if (activator?.Map == null || trigger == null)
            return;

        if (_cascadeDepth >= MaxCascadeDepth)
        {
            Logger.WriteLog(LogType.Error,
                "TriggerManager: cascade depth {0} exceeded (trigger={1}) — cycle guard",
                MaxCascadeDepth,
                trigger.ObjectId.Coid);
            return;
        }

        var triggerCoid = trigger.ObjectId.Coid;
        if (!_firingTriggerCoids.Add(triggerCoid))
        {
            // Same trigger already on the call stack (e.g. Activate self-target / pulse loop).
            Logger.WriteLog(LogType.Debug,
                "TriggerManager: skip re-entrant fire trigger={0}",
                triggerCoid);
            return;
        }

        _cascadeDepth++;
        try
        {
            if (trigger.Template.ActivationCount == 0)
                return;

            if (trigger.Template.ActivationCount > 0 && trigger.FireCount >= trigger.Template.ActivationCount)
                return;

            if (trigger.Template.Conditions.Count > 0 && !trigger.ConditionsPass(activator))
                return;

            trigger.FireCount++;
            LogPlayerTrigger(activator, trigger);
            activator.Map.TriggerReactions(activator, trigger.Template.Reactions);
        }
        finally
        {
            _cascadeDepth--;
            _firingTriggerCoids.Remove(triggerCoid);
        }
    }

    public void CheckTriggersFor(ClonedObjectBase clonedObject)
        => CheckTriggersFor(clonedObject, Environment.TickCount64);

    internal void CheckTriggersFor(ClonedObjectBase clonedObject, long nowMs)
        => CheckTriggersFor(clonedObject, nowMs, pulseSkills: true);

    /// <summary>
    /// Collision trigger scan for a logged-in player.
    /// Town continents put the avatar on foot (<c>UsingVehicle = false</c>); field/highway maps
    /// keep the vehicle as the moving body. Always checking only the vehicle left town pads
    /// (e.g. Upside → Back Range) dead because the vehicle sits at entry while the human walks.
    /// </summary>
    public void CheckTriggersForPlayer(Character character)
        => CheckTriggersForPlayer(character, Environment.TickCount64);

    internal void CheckTriggersForPlayer(Character character, long nowMs)
    {
        if (character is null)
            return;

        var activator = ResolvePlayerTriggerActivator(character);
        if (activator?.Map is null)
            return;

        CheckTriggersFor(activator, nowMs, pulseSkills: true);
    }

    /// <summary>
    /// Town → character body; non-town with vehicle on a map → vehicle; else character.
    /// Matches <see cref="Character"/> create-packet <c>UsingVehicle = !IsTown</c>.
    /// </summary>
    internal static ClonedObjectBase ResolvePlayerTriggerActivator(Character character)
    {
        if (character is null)
            return null;

        var isTown = character.Map?.MapData?.ContinentObject?.IsTown == true;
        if (isTown)
            return character;

        var vehicle = character.CurrentVehicle;
        if (vehicle?.Map != null)
            return vehicle;

        return character;
    }

    /// <summary>
    /// After player HP changes (heal pad, skills, admin set HP), re-evaluate collision
    /// volume conditions without advancing skill pulse cadence. Type-7 health% gates
    /// (e.g. full-HP complete objectives) open while standing still; pad heal timing
    /// stays owned by movement/tick <see cref="CheckTriggersFor"/>.
    /// </summary>
    public void OnPlayerHealthChanged(ClonedObjectBase activator)
    {
        if (activator == null)
            return;

        CheckTriggersFor(activator, Environment.TickCount64, pulseSkills: false);
    }

    internal void CheckTriggersFor(ClonedObjectBase clonedObject, long nowMs, bool pulseSkills)
    {
        if (clonedObject is null)
            return;

        var map = clonedObject.Map;
        if (map is null)
            return;

        var objectCoid = clonedObject.ObjectId.Coid;

        // Flush deferred SpawnPoint TriggerEvents when the player approaches Create targets
        // (air-drop / pad setup created by combat-spawn TE, etc.).
        FlushDeferredSpawnTriggerEvents(clonedObject);

        // Snapshot keys — a reaction may mutate Triggers during fire.
        var triggers = map.Triggers.Values.ToList();
        foreach (var trigger in triggers)
        {
            // Movement / volume path only. DoOnActivate remotes (e.g. l1_rem_gunnysioux_initiator)
            // must not fire when the player merely stands near them — only via Activate cascade.
            if (!trigger.Template.DoCollision)
                continue;

            var triggerCoid = trigger.ObjectId.Coid;
            var key = (objectCoid, triggerCoid);

            var canTrigger = trigger.CanTrigger(clonedObject);
            var alreadyTriggered = _activeTriggers.TryGetValue(key, out var isActive) && isActive;

            if (canTrigger)
            {
                if (!alreadyTriggered)
                {
                    _activeTriggers[key] = true;
                    FireTriggerReactions(clonedObject, trigger);
                    if (pulseSkills)
                        ScheduleSkillPulses(clonedObject, trigger, nowMs);
                }
                else if (pulseSkills)
                    PulseSkillsIfDue(clonedObject, trigger, nowMs);
            }
            else if (alreadyTriggered)
            {
                _activeTriggers.TryRemove(key, out _);
                ClearSkillPulses(objectCoid, triggerCoid);
            }
        }
    }

    private void ScheduleSkillPulses(ClonedObjectBase activator, Trigger trigger, long nowMs)
    {
        foreach (var reactionCoid in trigger.Template.Reactions)
        {
            if (activator.Map?.GetObjectByCoid(reactionCoid) is Reaction { Template.ReactionType: ReactionType.SkillCast })
                _nextSkillPulseMs[(activator.ObjectId.Coid, trigger.ObjectId.Coid, reactionCoid)] = nowMs + SkillPulseIntervalMs;
        }
    }

    private void PulseSkillsIfDue(ClonedObjectBase activator, Trigger trigger, long nowMs)
    {
        foreach (var reactionCoid in trigger.Template.Reactions)
        {
            if (activator.Map?.GetObjectByCoid(reactionCoid) is not Reaction { Template.ReactionType: ReactionType.SkillCast })
                continue;

            var key = (activator.ObjectId.Coid, trigger.ObjectId.Coid, reactionCoid);
            if (!_nextSkillPulseMs.TryGetValue(key, out var nextPulseMs))
            {
                _nextSkillPulseMs[key] = nowMs + SkillPulseIntervalMs;
                continue;
            }

            if (nowMs < nextPulseMs)
                continue;

            _nextSkillPulseMs[key] = nowMs + SkillPulseIntervalMs;

            // Keep the deadline alive so a vehicle damaged while remaining on the pad resumes
            // within one second, but emit no skill/effect traffic while already full.
            if (activator.GetMaximumHP() > 0 && activator.GetCurrentHP() >= activator.GetMaximumHP())
                continue;

            activator.Map.TriggerReactions(activator, new List<long> { reactionCoid });
        }
    }

    private void ClearSkillPulses(long objectCoid, long triggerCoid)
    {
        foreach (var key in _nextSkillPulseMs.Keys
                     .Where(key => key.ObjectCoid == objectCoid && key.TriggerCoid == triggerCoid)
                     .ToList())
        {
            _nextSkillPulseMs.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// SpawnPoints may defer TriggerEvents when Create targets are out of proximity.
    /// Flush when a player/vehicle activator moves into range.
    /// </summary>
    void FlushDeferredSpawnTriggerEvents(ClonedObjectBase activator)
    {
        if (activator?.Map == null)
            return;

        // Snapshot — flush may Create objects and mutate map collections.
        var objects = activator.Map.Objects.Values.ToList();
        foreach (var obj in objects)
        {
            if (obj is SpawnPoint spawn && spawn.HasDeferredAuthoredTriggerEvents)
                spawn.TryFlushDeferredAuthoredTriggerEvents(activator);
        }
    }

    /// <summary>
    /// Call after grant/complete/set-active so mission-computed vars (types 9/10/11/12) can open
    /// gates, dialogues, etc. without requiring a new movement packet.
    /// Nested calls (GiveMission reaction during re-eval) coalesce to a single follow-up pass.
    /// </summary>
    public void OnMissionStateChanged(ClonedObjectBase activator)
    {
        // Callers often pass CurrentVehicle; vehicle.Map can be null while character.Map is set.
        var character = activator?.GetAsCharacter() ?? activator?.GetSuperCharacter(false);
        var map = activator?.Map ?? character?.Map;
        if (activator == null || map == null)
        {
            MissionFlowDiag.Log(
                "OnMissionStateChanged SKIP no-map activator={0} char={1}",
                activator?.ObjectId.Coid ?? -1,
                character?.ObjectId.Coid ?? -1);
            return;
        }

        // Prefer character vehicle/body that has the map for trigger volume checks.
        var reevalActivator = activator;
        if (reevalActivator.Map == null && character != null)
        {
            reevalActivator = character.CurrentVehicle?.Map != null
                ? (ClonedObjectBase)character.CurrentVehicle
                : character.Map != null
                    ? character
                    : activator;
            if (reevalActivator.Map == null)
            {
                MissionFlowDiag.Log(
                    "OnMissionStateChanged SKIP unresolved map char={0} vehicle={1}",
                    character.ObjectId.Coid,
                    character.CurrentVehicle?.ObjectId.Coid ?? -1);
                return;
            }

            MissionFlowDiag.Log(
                "OnMissionStateChanged remap activator {0} -> {1} (map was null)",
                activator.ObjectId.Coid,
                reevalActivator.ObjectId.Coid);
        }

        if (_missionReevalActive)
        {
            _missionReevalPending = true;
            MissionFlowDiag.Log(
                "OnMissionStateChanged COALESCE nested pending map={0} activator={1}",
                reevalActivator.Map.ContinentId,
                reevalActivator.ObjectId.Coid);
            return;
        }

        _missionReevalActive = true;
        try
        {
            var pass = 0;
            do
            {
                _missionReevalPending = false;
                pass++;
                MissionFlowDiag.Log(
                    "OnMissionStateChanged PASS={0} map={1} activator={2} depth={3} {4}",
                    pass,
                    reevalActivator.Map.ContinentId,
                    reevalActivator.ObjectId.Coid,
                    _cascadeDepth,
                    character != null ? MissionFlowDiag.QuestSummary(character) : "quests=?");
                RunMissionStateReevalPass(reevalActivator);
            }
            while (_missionReevalPending && _cascadeDepth < MaxCascadeDepth);
        }
        finally
        {
            _missionReevalActive = false;
            _missionReevalPending = false;
        }
    }

    private void RunMissionStateReevalPass(ClonedObjectBase activator)
    {
        var character = activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
        if (character != null)
            character.EnsureLogicVariables();

        if (character != null)
        {
            MissionFlowDiag.Log(
                "MissionReeval CheckTriggersForPlayer char={0} {1}",
                character.ObjectId.Coid,
                MissionFlowDiag.QuestSummary(character));
            CheckTriggersForPlayer(character);
        }
        else
            CheckTriggersFor(activator);

        FireMissionConditionTriggers(activator);
    }

    /// <summary>
    /// Fire latched condition-passing triggers for mission state (types 9/10/11/12), including
    /// collision+conditional gate openers outside volume. Used by OnMissionStateChanged and
    /// login/world-phase replay so Create+Delete reaction lists run once per actor.
    /// </summary>
    public void FireMissionConditionTriggers(ClonedObjectBase activator)
        => ReevaluateConditionalTriggers(activator, watchVarId: null);

    /// <summary>
    /// After a VariableSet (etc.) writes a Type-0 flag, fire remote triggers watching that variable.
    /// </summary>
    public void OnVariableChanged(ClonedObjectBase activator, int varId)
    {
        if (activator?.Map == null)
            return;

        // Nested VariableSet during cascade: still evaluate, but depth-guarded via FireTriggerReactions.
        if (_variableReevalActive && _cascadeDepth >= MaxCascadeDepth)
            return;

        var wasActive = _variableReevalActive;
        _variableReevalActive = true;
        try
        {
            ReevaluateConditionalTriggers(activator, watchVarId: varId);
        }
        finally
        {
            _variableReevalActive = wasActive;
        }
    }

    private void ReevaluateConditionalTriggers(ClonedObjectBase activator, int? watchVarId)
    {
        var map = activator.Map;
        if (map == null)
            return;

        var character = activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
        var actorCoid = character?.ObjectId.Coid ?? activator.ObjectId.Coid;

        // Mission re-eval (watchVarId null): also open collision+conditional *gate* triggers
        // (Create/Delete/Death) outside volume — Biomek Dunlap type-9 complete, Human door
        // type-11 accept. Pure Activate cascade volumes (Gunny initiate) stay movement-only.
        // Variable-watch path stays remote-only so large volumes are not mass-fired.
        var missionStateReeval = !watchVarId.HasValue;

        foreach (var kvp in map.Triggers.ToList())
        {
            var trigger = kvp.Value;
            if (trigger.Template.Conditions.Count == 0)
                continue;

            // Pure Activate targets (DoOnActivate, no conditionals) are only fired by
            // ReactionType.Activate cascades — not by objective progress. Ark Bay 14134
            // (l1_rem_gunnysioux_initiator, scale=2, doColl=0, doCond=0) was wrongly
            // classified as a "remote logic watcher" and deleted standing Gunny + created
            // combat pathing car whenever any mission objective advanced.
            if (trigger.Template.DoOnActivate && !trigger.Template.DoConditionals)
                continue;

            // Prefer triggers that actually evaluate conditionals (mission/var watchers).
            if (!trigger.Template.DoConditionals)
                continue;

            if (missionStateReeval)
            {
                if (trigger.Template.DoCollision)
                {
                    // Outside-volume mission fire only for gate openers (Create/Delete/Death).
                    if (!HasWorldMutatingGateReactions(map, trigger))
                        continue;
                }
                else if (trigger.Scale > 2.0f)
                {
                    // Non-collision remotes stay small (existing remote-watcher contract).
                    continue;
                }
            }
            else
            {
                // Variable-watch: remote logic watchers only (small, non-collision).
                if (trigger.Template.DoCollision)
                    continue;
                if (trigger.Scale > 2.0f)
                    continue;
            }

            if (watchVarId.HasValue
                && !trigger.Template.Conditions.Any(c => c.LeftId == watchVarId.Value || c.RightId == watchVarId.Value))
            {
                continue;
            }

            var key = (actorCoid, kvp.Key.Coid);
            if (_firedConditionalTriggers.ContainsKey(key))
                continue;

            if (!trigger.ConditionsPass(activator))
                continue;

            _firedConditionalTriggers[key] = true;
            MissionFlowDiag.Log(
                "COND-TRIGGER FIRE trigger={0} name='{1}' actor={2} watchVar={3} coll={4} scale={5} reactions=[{6}]",
                kvp.Key.Coid,
                trigger.Template.Name ?? string.Empty,
                actorCoid,
                watchVarId?.ToString() ?? "mission",
                trigger.Template.DoCollision ? 1 : 0,
                trigger.Scale,
                string.Join(',', trigger.Template.Reactions));
            Logger.WriteLog(LogType.Debug,
                "TriggerManager: condition fire trigger={0} actor={1} watchVar={2} coll={3}",
                kvp.Key.Coid,
                actorCoid,
                watchVarId?.ToString() ?? "mission",
                trigger.Template.DoCollision);
            FireTriggerReactions(activator, trigger);
        }
    }

    /// <summary>
    /// True when the trigger's reaction list includes Create/Delete/Death (map gate openers).
    /// Pure Activate cascade volumes (e.g. Gunny initiate → rem initiator) are excluded so
    /// mission re-eval does not start combat from outside the initiate sphere.
    /// </summary>
    private static bool HasWorldMutatingGateReactions(Map.SectorMap map, Trigger trigger)
    {
        if (map == null || trigger?.Template?.Reactions == null)
            return false;

        foreach (var coid in trigger.Template.Reactions)
        {
            if (map.GetObjectByCoid(coid) is not Reaction reaction)
                continue;

            switch (reaction.Template.ReactionType)
            {
                case ReactionType.Create:
                case ReactionType.Delete:
                case ReactionType.Death:
                    return true;
            }
        }

        return false;
    }

    private static void LogPlayerTrigger(ClonedObjectBase activator, Trigger trigger)
    {
        var character = activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
        if (character == null)
            return;

        MissionFlowDiag.Log(
            "PLAYER-TRIGGER trigger={0} name='{1}' player={2} activator={3} reactions=[{4}]",
            trigger.ObjectId.Coid,
            trigger.Template.Name ?? string.Empty,
            character.ObjectId.Coid,
            activator.ObjectId.Coid,
            string.Join(',', trigger.Template.Reactions));
        Logger.WriteLog(LogType.Debug,
            "Player trigger occurred: playerCoid={0} activatorCoid={1} trigger={2} name='{3}' reactions=[{4}]",
            character.ObjectId.Coid,
            activator.ObjectId.Coid,
            trigger.ObjectId.Coid,
            trigger.Template.Name ?? string.Empty,
            string.Join(',', trigger.Template.Reactions));
    }

    public void ClearTriggersFor(long objectCoid)
    {
        foreach (var key in _activeTriggers.Keys.Where(k => k.ObjectCoid == objectCoid).ToList())
            _activeTriggers.TryRemove(key, out _);

        foreach (var key in _firedConditionalTriggers.Keys.Where(k => k.ActorCoid == objectCoid).ToList())
            _firedConditionalTriggers.TryRemove(key, out _);

        foreach (var key in _nextSkillPulseMs.Keys.Where(k => k.ObjectCoid == objectCoid).ToList())
            _nextSkillPulseMs.TryRemove(key, out _);
    }

    public void ClearTrigger(long triggerCoid)
    {
        foreach (var key in _activeTriggers.Keys.Where(k => k.TriggerCoid == triggerCoid).ToList())
            _activeTriggers.TryRemove(key, out _);

        foreach (var key in _firedConditionalTriggers.Keys.Where(k => k.TriggerCoid == triggerCoid).ToList())
            _firedConditionalTriggers.TryRemove(key, out _);

        foreach (var key in _nextSkillPulseMs.Keys.Where(k => k.TriggerCoid == triggerCoid).ToList())
            _nextSkillPulseMs.TryRemove(key, out _);
    }

    public void ResetTriggerFor(long objectCoid, long triggerCoid)
    {
        _activeTriggers.TryRemove((objectCoid, triggerCoid), out _);
        _firedConditionalTriggers.TryRemove((objectCoid, triggerCoid), out _);
        ClearSkillPulses(objectCoid, triggerCoid);
    }

    /// <summary>Unit-test helper: wipe all latches (process-wide singleton).</summary>
    internal void ClearAllForTests()
    {
        _activeTriggers.Clear();
        _nextSkillPulseMs.Clear();
        _firedConditionalTriggers.Clear();
        _firingTriggerCoids.Clear();
        _cascadeDepth = 0;
        _missionReevalActive = false;
        _missionReevalPending = false;
        _variableReevalActive = false;
    }
}
