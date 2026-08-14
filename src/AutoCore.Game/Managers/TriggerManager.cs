namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
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

    // ALL latches are prefixed with SectorMap.InstanceSerial: per-player instances of the same
    // continent mint identical local COIDs, so bare-COID keys would collide across instances
    // (A's NPC latching a volume suppresses B's identical NPC; clears wipe every instance).

    // Physical enter latch: (Serial, ObjectCoid, TriggerCoid) currently inside and already fired.
    private readonly ConcurrentDictionary<(int Serial, long ObjectCoid, long TriggerCoid), bool> _activeTriggers = new();

    // Per-collider repair cadence. Multiple vehicles on one pad must never share a deadline.
    private readonly ConcurrentDictionary<(int Serial, long ObjectCoid, long TriggerCoid, long ReactionCoid), long> _nextSkillPulseMs = new();
    internal const long SkillPulseIntervalMs = 1000;

    // One-shot for remote/condition-driven fires (mission change, variable set).
    private readonly ConcurrentDictionary<(int Serial, long ActorCoid, long TriggerCoid), bool> _firedConditionalTriggers = new();

    // Re-entrancy: cascade stack (not concurrent — game logic is single-threaded per sector).
    private int _cascadeDepth;
    private readonly HashSet<(int Serial, long TriggerCoid)> _firingTriggerCoids = new();
    private bool _missionReevalActive;
    private bool _missionReevalPending;
    private bool _variableReevalActive;

    // SS-51: characters whose mission re-eval was deferred because their client was still
    // loading in. Coalesced to one flush per character by FlushDeferredEntryReeval.
    private readonly HashSet<long> _entryDeferredReeval = new();

    // SS-51: true only while running that entry flush — collision gates must contain the player.
    private bool _entryScopedReeval;

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

        var firingKey = (activator.Map.InstanceSerial, trigger.ObjectId.Coid);
        if (!_firingTriggerCoids.Add(firingKey))
        {
            // Same trigger already on the call stack (e.g. Activate self-target / pulse loop).
            // Per-serial: the same trigger coid firing in a sibling instance is legal.
            Logger.WriteLog(LogType.Debug,
                "TriggerManager: skip re-entrant fire trigger={0}",
                trigger.ObjectId.Coid);
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
            _firingTriggerCoids.Remove(firingKey);
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

        var serial = map.InstanceSerial;
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
            var key = (serial, objectCoid, triggerCoid);

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
                ClearSkillPulses(serial, objectCoid, triggerCoid);
            }
        }
    }

    private void ScheduleSkillPulses(ClonedObjectBase activator, Trigger trigger, long nowMs)
    {
        foreach (var reactionCoid in trigger.Template.Reactions)
        {
            if (activator.Map?.GetObjectByCoid(reactionCoid) is Reaction { Template.ReactionType: ReactionType.SkillCast })
                _nextSkillPulseMs[(activator.Map.InstanceSerial, activator.ObjectId.Coid, trigger.ObjectId.Coid, reactionCoid)] = nowMs + SkillPulseIntervalMs;
        }
    }

    private void PulseSkillsIfDue(ClonedObjectBase activator, Trigger trigger, long nowMs)
    {
        foreach (var reactionCoid in trigger.Template.Reactions)
        {
            if (activator.Map?.GetObjectByCoid(reactionCoid) is not Reaction { Template.ReactionType: ReactionType.SkillCast })
                continue;

            var key = (activator.Map.InstanceSerial, activator.ObjectId.Coid, trigger.ObjectId.Coid, reactionCoid);
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

    private void ClearSkillPulses(int serial, long objectCoid, long triggerCoid)
    {
        foreach (var key in _nextSkillPulseMs.Keys
                     .Where(key => key.Serial == serial && key.ObjectCoid == objectCoid && key.TriggerCoid == triggerCoid)
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

        if (TryDeferForWorldEntry(character, "OnMissionStateChanged"))
            return;

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
        // Shared with world-entry: journal-true graphics/gates apply remotely;
        // collision SpawnPoint Creates and Activate cascades wait for the volume.
        ReplayPersistedMissionWorldReactions(activator);
    }

    /// <summary>
    /// Fire condition-passing triggers for mission state (types 9/10/11/12).
    /// Collision-authored triggers only fire here when the activator is already inside
    /// the volume (retail <c>DoCollisionTrigger</c> + <c>DoPhantomCollisions</c>).
    /// Persistent graphics outside volume are applied by
    /// <see cref="ReplayPersistedMissionWorldReactions"/>, not by consuming FireCount.
    /// </summary>
    public void FireMissionConditionTriggers(ClonedObjectBase activator)
        => ReevaluateConditionalTriggers(activator, watchVarId: null);

    /// <summary>
    /// SS-51: true when the character's client has not received its create stream yet (login /
    /// map transfer), in which case the caller must not fire anything — the work is remembered
    /// and replayed once by <see cref="FlushDeferredEntryReeval"/>.
    /// <para>
    /// Every entry into mission-phase work must consult this, not just
    /// <see cref="OnMissionStateChanged"/>: <c>SectorMap.ApplyMissionPhaseWorldState</c> also
    /// runs <c>ReplayMissionWorldSetup</c>, which reaches the same out-of-volume firing through
    /// <see cref="FireMissionConditionTriggers"/>. Gating only one half still stormed the client
    /// (live 2026-08-10 16:30: 116 gates fired between the deferral and the flush).
    /// </para>
    /// </summary>
    internal bool TryDeferForWorldEntry(Character character, string source)
    {
        if (character == null || character.WorldEntryComplete)
            return false;

        _entryDeferredReeval.Add(character.ObjectId.Coid);
        MissionFlowDiag.Log(
            "{0} DEFER world-entry char={1} map={2}",
            source,
            character.ObjectId.Coid,
            character.Map?.ContinentId ?? -1);
        return true;
    }

    /// <summary>
    /// SS-51: run the single coalesced mission re-eval deferred while the character was entering
    /// the world. Called by <see cref="Character.CompleteWorldEntry"/> after the create stream.
    /// The flush is <em>entry-scoped</em>: collision gates must contain the player (see
    /// <see cref="ReevaluateConditionalTriggers"/>), so entering a map cannot mass-open gates
    /// across the whole continent.
    /// </summary>
    public void FlushDeferredEntryReeval(Character character)
    {
        if (character == null)
            return;

        if (!_entryDeferredReeval.Remove(character.ObjectId.Coid))
            return;

        var activator = character.CurrentVehicle?.Map != null
            ? (ClonedObjectBase)character.CurrentVehicle
            : character;
        if (activator.Map == null)
            return;

        MissionFlowDiag.Log(
            "FlushDeferredEntryReeval char={0} map={1} {2}",
            character.ObjectId.Coid,
            activator.Map.ContinentId,
            MissionFlowDiag.QuestSummary(character));

        var wasEntryFlush = _entryScopedReeval;
        _entryScopedReeval = true;
        try
        {
            OnMissionStateChanged(activator);
            character.Map?.ReplayMissionWorldSetup(activator);
        }
        finally
        {
            _entryScopedReeval = wasEntryFlush;
        }
    }

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

        // Mission re-eval (watchVarId null):
        //   collision + in volume  → full FireTriggerReactions (retail phantom overlap)
        //   collision + out of volume → skip here; persist replay applies graphics only
        //   non-collision scale<=2 → full fire (condition-only watchers)
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
                    // Retail CVOGTrigger::DoCollisionTrigger requires an actual overlap.
                    // Out-of-volume mission changes must not consume FireCount or spawn
                    // encounters. Persistent graphics are restored separately.
                    if (activator.Position.DistSq(trigger.Position) > trigger.Scale * trigger.Scale)
                    {
                        LogDeferredCollisionEncounter(map, trigger, character);
                        continue;
                    }
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

            var key = (map.InstanceSerial, actorCoid, kvp.Key.Coid);
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
    /// Debug-only: a collision encounter was eligible by journal but is waiting for volume.
    /// Graphics-only gates are silent here — persist replay applies them.
    /// </summary>
    private static void LogDeferredCollisionEncounter(
        Map.SectorMap map, Trigger trigger, Character character)
    {
        if (map == null || trigger?.Template?.Reactions == null)
            return;

        if (!HasCollisionEncounterSideEffects(map, trigger))
            return;

        Logger.WriteLog(LogType.Debug,
            "TriggerManager: defer collision encounter map={0} trigger={1} reactions={2} reason=awaiting-volume {3}",
            map.ContinentId,
            trigger.ObjectId.Coid,
            trigger.Template.Reactions.Count,
            character != null ? MissionFlowDiag.QuestSummary(character) : "mission=-");
        MissionFlowDiag.Log(
            "DEFER-VOLUME trigger={0} name='{1}' map={2} reactions={3} reason=awaiting-volume",
            trigger.ObjectId.Coid,
            trigger.Template.Name ?? string.Empty,
            map.ContinentId,
            trigger.Template.Reactions.Count);
    }

    /// <summary>
    /// True when firing the trigger would spawn or wake an encounter (SpawnPoint Create
    /// or Activate cascade). Persistent graphics Create/Delete/Death are not encounters.
    /// </summary>
    private static bool HasCollisionEncounterSideEffects(Map.SectorMap map, Trigger trigger)
    {
        if (map == null || trigger?.Template?.Reactions == null)
            return false;

        foreach (var coid in trigger.Template.Reactions)
        {
            if (map.GetObjectByCoid(coid) is not Reaction reaction)
                continue;

            var type = reaction.Template.ReactionType;
            if (type == ReactionType.Activate || type == ReactionType.Deactivate)
                return true;

            if (type != ReactionType.Create)
                continue;

            if (reaction.Template.Objects == null)
                continue;

            foreach (var targetCoid in reaction.Template.Objects)
            {
                var live = map.GetObjectByCoid(targetCoid);
                map.MapData.Templates.TryGetValue(targetCoid, out var template);
                if (live is SpawnPoint || template is SpawnPointTemplate)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replay only deterministic FAM graphics/gate reactions whose condition is already
    /// true from persisted journal state (type 9/10/11/12). Used on world-entry flush so
    /// closed doors / mission barriers restore without re-entering the original volume.
    /// Does not fire Activate cascades or Create targeting SpawnPoints (ambushes).
    /// </summary>
    internal int ReplayPersistedMissionWorldReactions(ClonedObjectBase activator)
    {
        var map = activator?.Map;
        if (map == null)
            return 0;

        var character = activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
        if (character == null)
            return 0;

        character.EnsureLogicVariables();
        var actorCoid = character.ObjectId.Coid;
        var fired = 0;

        foreach (var kvp in map.Triggers.ToList())
        {
            var trigger = kvp.Value;
            if (trigger?.Template == null || trigger.Template.Conditions.Count == 0)
                continue;

            if (trigger.Template.DoOnActivate && !trigger.Template.DoConditionals)
                continue;

            if (!trigger.Template.DoConditionals)
                continue;

            var key = (map.InstanceSerial, actorCoid, kvp.Key.Coid);
            if (_firedConditionalTriggers.ContainsKey(key))
                continue;

            if (!HasPersistedMissionProgressCondition(map, trigger, character))
                continue;

            if (!trigger.ConditionsPass(activator))
                continue;

            var safe = CollectPersistedGateReactions(map, trigger, activator, character);
            if (safe.Count == 0)
                continue;

            _firedConditionalTriggers[key] = true;
            MissionFlowDiag.Log(
                "PERSIST-GATE FIRE trigger={0} name='{1}' actor={2} reactions=[{3}]",
                kvp.Key.Coid,
                trigger.Template.Name ?? string.Empty,
                actorCoid,
                string.Join(',', safe));
            map.TriggerReactions(activator, safe);
            fired += safe.Count;
        }

        return fired;
    }

    /// <summary>
    /// True when the trigger has a type 9/10/11/12 condition that is already true from
    /// the character's persisted journal (completed / active mission or objective).
    /// Default-true type-0 latches and "has NOT completed" comparisons are excluded so
    /// SS-51 still blocks the map-661 mass-open storm.
    /// </summary>
    private static bool HasPersistedMissionProgressCondition(
        Map.SectorMap map, Trigger trigger, Character character)
    {
        var store = character.EnsureLogicVariables();
        if (store == null)
            return false;

        foreach (var condition in trigger.Template.Conditions)
        {
            if (!map.MapData.Variables.TryGetValue(condition.LeftId, out var def) || def == null)
                continue;

            if (def.Type != LogicVariableStore.TypeHasCompletedMission
                && def.Type != LogicVariableStore.TypeHasCompletedObjective
                && def.Type != LogicVariableStore.TypeHasActiveMission
                && def.Type != LogicVariableStore.TypeHasActiveObjective)
            {
                continue;
            }

            if (store.Get(condition.LeftId) == 1.0f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Create/Delete/Death reactions whose targets are FAM graphics (or missing client-only
    /// statics). SpawnPoint Creates are ambushes — never selected here.
    /// </summary>
    private static List<long> CollectPersistedGateReactions(
        Map.SectorMap map,
        Trigger trigger,
        ClonedObjectBase activator,
        Character character)
    {
        var selected = new List<long>();
        var encounter = HasCollisionEncounterSideEffects(map, trigger);
        foreach (var rxCoid in trigger.Template.Reactions)
        {
            if (map.GetObjectByCoid(rxCoid) is not Reaction reaction)
                continue;

            var type = reaction.Template.ReactionType;
            if (type is not (ReactionType.Create or ReactionType.Delete or ReactionType.Death))
                continue;

            // Mixed ambush graphs (Wastes 18585) Create encounter FX next to SpawnPoint
            // Creates. Those Creates wait for the volume; only Delete/Death restore gates.
            if (type == ReactionType.Create && encounter)
                continue;

            if (!ReactionTargetsPersistentGraphics(map, reaction, activator, character, type))
                continue;

            selected.Add(rxCoid);
        }

        return selected;
    }

    private static bool ReactionTargetsPersistentGraphics(
        Map.SectorMap map,
        Reaction reaction,
        ClonedObjectBase activator,
        Character character,
        ReactionType type)
    {
        if (reaction.Template.Objects == null || reaction.Template.Objects.Count == 0)
            return false;

        var anyGraphics = false;
        foreach (var targetCoid in reaction.Template.Objects)
        {
            var live = map.GetObjectByCoid(targetCoid);
            map.MapData.Templates.TryGetValue(targetCoid, out var template);

            if (live is SpawnPoint || template is SpawnPointTemplate)
                return false;

            if (live is Trigger || template is TriggerTemplate)
                continue;

            if (live is GraphicsObject || template is GraphicsObjectTemplate)
            {
                anyGraphics = true;
                continue;
            }

            if (live == null && template == null)
            {
                MissionWorldStateLog.WarnMissingTarget(
                    character,
                    map.ContinentId,
                    targetCoid,
                    type.ToString(),
                    $"reaction={reaction.Template.COID} trigger-persist");
                if (type is ReactionType.Delete or ReactionType.Death)
                    anyGraphics = true;
            }
        }

        return anyGraphics;
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

    /// <summary>Clears every latch this object holds on <paramref name="map"/> (LeaveMap path).</summary>
    public void ClearTriggersFor(Map.SectorMap map, long objectCoid)
    {
        if (map == null)
            return;

        var serial = map.InstanceSerial;
        foreach (var key in _activeTriggers.Keys.Where(k => k.Serial == serial && k.ObjectCoid == objectCoid).ToList())
            _activeTriggers.TryRemove(key, out _);

        foreach (var key in _firedConditionalTriggers.Keys.Where(k => k.Serial == serial && k.ActorCoid == objectCoid).ToList())
            _firedConditionalTriggers.TryRemove(key, out _);

        foreach (var key in _nextSkillPulseMs.Keys.Where(k => k.Serial == serial && k.ObjectCoid == objectCoid).ToList())
            _nextSkillPulseMs.TryRemove(key, out _);
    }

    /// <summary>Clears every latch on one trigger of <paramref name="map"/> — never siblings'.</summary>
    public void ClearTrigger(Map.SectorMap map, long triggerCoid)
    {
        if (map == null)
            return;

        var serial = map.InstanceSerial;
        foreach (var key in _activeTriggers.Keys.Where(k => k.Serial == serial && k.TriggerCoid == triggerCoid).ToList())
            _activeTriggers.TryRemove(key, out _);

        foreach (var key in _firedConditionalTriggers.Keys.Where(k => k.Serial == serial && k.TriggerCoid == triggerCoid).ToList())
            _firedConditionalTriggers.TryRemove(key, out _);

        foreach (var key in _nextSkillPulseMs.Keys.Where(k => k.Serial == serial && k.TriggerCoid == triggerCoid).ToList())
            _nextSkillPulseMs.TryRemove(key, out _);
    }

    public void ResetTriggerFor(Map.SectorMap map, long objectCoid, long triggerCoid)
    {
        if (map == null)
            return;

        var serial = map.InstanceSerial;
        _activeTriggers.TryRemove((serial, objectCoid, triggerCoid), out _);
        _firedConditionalTriggers.TryRemove((serial, objectCoid, triggerCoid), out _);
        ClearSkillPulses(serial, objectCoid, triggerCoid);
    }

    /// <summary>Wipes every latch belonging to one map instance. Called from instance disposal.</summary>
    public void ClearInstance(int instanceSerial)
    {
        foreach (var key in _activeTriggers.Keys.Where(k => k.Serial == instanceSerial).ToList())
            _activeTriggers.TryRemove(key, out _);

        foreach (var key in _firedConditionalTriggers.Keys.Where(k => k.Serial == instanceSerial).ToList())
            _firedConditionalTriggers.TryRemove(key, out _);

        foreach (var key in _nextSkillPulseMs.Keys.Where(k => k.Serial == instanceSerial).ToList())
            _nextSkillPulseMs.TryRemove(key, out _);
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
        _entryDeferredReeval.Clear();
        _entryScopedReeval = false;
    }
}
