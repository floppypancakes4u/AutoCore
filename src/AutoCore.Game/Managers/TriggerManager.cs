namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Utils.Memory;
using System.Collections.Concurrent;

public class TriggerManager : Singleton<TriggerManager>
{
    // Track which triggers have already fired for each object to prevent repeated triggering
    // Key: (ObjectCoid, TriggerCoid), Value: true if currently inside trigger zone and already triggered
    private readonly ConcurrentDictionary<(long ObjectCoid, long TriggerCoid), bool> _activeTriggers = new();

    public void CheckTriggersFor(ClonedObjectBase clonedObject)
    {
        if (clonedObject is null)
            return;

        var map = clonedObject.Map;
        if (map is null)
            return;

        var objectCoid = clonedObject.ObjectId.Coid;

        foreach (var triggerKvp in map.Triggers)
        {
            var trigger = triggerKvp.Value;
            var triggerCoid = trigger.ObjectId.Coid;
            var key = (objectCoid, triggerCoid);

            // Check if the object can trigger this trigger (is within range and meets conditions)
            var canTrigger = trigger.CanTrigger(clonedObject);
            var alreadyTriggered = _activeTriggers.TryGetValue(key, out var isActive) && isActive;

            if (canTrigger)
            {
                if (!alreadyTriggered)
                {
                    // Object just entered the trigger zone - fire the reactions
                    _activeTriggers[key] = true;
                    clonedObject.Map.TriggerReactions(clonedObject, trigger.Template.Reactions);
                }
                // If already triggered, don't fire again (player is still in the zone)
            }
            else
            {
                if (alreadyTriggered)
                {
                    // Object left the trigger zone - reset the triggered state
                    _activeTriggers.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>
    /// Clear all trigger states for an object (e.g., when they leave the map)
    /// </summary>
    public void ClearTriggersFor(long objectCoid)
    {
        var keysToRemove = _activeTriggers.Keys.Where(k => k.ObjectCoid == objectCoid).ToList();
        foreach (var key in keysToRemove)
        {
            _activeTriggers.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Clear trigger states for a specific trigger (e.g., when it's reset)
    /// </summary>
    public void ClearTrigger(long triggerCoid)
    {
        var keysToRemove = _activeTriggers.Keys.Where(k => k.TriggerCoid == triggerCoid).ToList();
        foreach (var key in keysToRemove)
        {
            _activeTriggers.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Reset a specific trigger for a specific object (e.g., from a ResetTrigger reaction)
    /// </summary>
    public void ResetTriggerFor(long objectCoid, long triggerCoid)
    {
        _activeTriggers.TryRemove((objectCoid, triggerCoid), out _);
    }
}
