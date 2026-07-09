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
            activator.Map.TriggerReactions(activator, trigger.Template.Reactions);
        }
        finally
        {
            _cascadeDepth--;
            _firingTriggerCoids.Remove(triggerCoid);
        }
    }

    public void CheckTriggersFor(ClonedObjectBase clonedObject)
    {
        if (clonedObject is null)
            return;

        var map = clonedObject.Map;
        if (map is null)
            return;

        var objectCoid = clonedObject.ObjectId.Coid;

        // Snapshot keys — a reaction may mutate Triggers during fire.
        var triggers = map.Triggers.Values.ToList();
        foreach (var trigger in triggers)
        {
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
                }
            }
            else if (alreadyTriggered)
            {
                _activeTriggers.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Call after grant/complete/set-active so mission-computed vars (types 9/11/12) can open
    /// gates, dialogues, etc. without requiring a new movement packet.
    /// Nested calls (GiveMission reaction during re-eval) coalesce to a single follow-up pass.
    /// </summary>
    public void OnMissionStateChanged(ClonedObjectBase activator)
    {
        if (activator?.Map == null)
            return;

        if (_missionReevalActive)
        {
            _missionReevalPending = true;
            return;
        }

        _missionReevalActive = true;
        try
        {
            do
            {
                _missionReevalPending = false;
                RunMissionStateReevalPass(activator);
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

        var vehicle = character?.CurrentVehicle;
        if (vehicle?.Map != null)
            CheckTriggersFor(vehicle);
        else
            CheckTriggersFor(activator);

        ReevaluateConditionalTriggers(activator, watchVarId: null);
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

        foreach (var kvp in map.Triggers.ToList())
        {
            var trigger = kvp.Value;
            if (trigger.Template.Conditions.Count == 0)
                continue;

            // Remote logic watchers are small; large spheres are pure collision volumes.
            if (trigger.Scale > 2.0f)
                continue;

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
            Logger.WriteLog(LogType.Debug,
                "TriggerManager: remote condition fire trigger={0} actor={1} watchVar={2}",
                kvp.Key.Coid,
                actorCoid,
                watchVarId?.ToString() ?? "mission");
            FireTriggerReactions(activator, trigger);
        }
    }

    public void ClearTriggersFor(long objectCoid)
    {
        foreach (var key in _activeTriggers.Keys.Where(k => k.ObjectCoid == objectCoid).ToList())
            _activeTriggers.TryRemove(key, out _);

        foreach (var key in _firedConditionalTriggers.Keys.Where(k => k.ActorCoid == objectCoid).ToList())
            _firedConditionalTriggers.TryRemove(key, out _);
    }

    public void ClearTrigger(long triggerCoid)
    {
        foreach (var key in _activeTriggers.Keys.Where(k => k.TriggerCoid == triggerCoid).ToList())
            _activeTriggers.TryRemove(key, out _);

        foreach (var key in _firedConditionalTriggers.Keys.Where(k => k.TriggerCoid == triggerCoid).ToList())
            _firedConditionalTriggers.TryRemove(key, out _);
    }

    public void ResetTriggerFor(long objectCoid, long triggerCoid)
    {
        _activeTriggers.TryRemove((objectCoid, triggerCoid), out _);
        _firedConditionalTriggers.TryRemove((objectCoid, triggerCoid), out _);
    }

    /// <summary>Unit-test helper: wipe all latches (process-wide singleton).</summary>
    internal void ClearAllForTests()
    {
        _activeTriggers.Clear();
        _firedConditionalTriggers.Clear();
        _firingTriggerCoids.Clear();
        _cascadeDepth = 0;
        _missionReevalActive = false;
        _missionReevalPending = false;
        _variableReevalActive = false;
    }
}
