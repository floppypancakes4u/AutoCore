namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.TNL;
using AutoCore.Utils;

/// <summary>
/// Fires map-authored <see cref="ObjectTemplate.TriggerEvents"/> when the player UseObjects
/// an NPC/world object. Retail kiosk vendors wire spawn TriggerEvents → Trigger → OpenStore
/// (e.g. backrange kiosk spawn 9837 → trigger 9838 → OpenStore reaction 9839).
/// </summary>
public static class InteractTriggerService
{
    /// <summary>
    /// Resolve the target's spawn owner (or the object itself) and fire positive TriggerEvent
    /// COIDs as map <see cref="Trigger"/>s. Returns true only if at least one reaction actually ran
    /// (so UseObject does not "succeed" silently with no 0x206C).
    /// </summary>
    public static bool TryFire(TNLConnection conn, Character character, long targetCoid)
    {
        if (character?.Map == null || targetCoid <= 0)
            return false;

        var map = character.Map;
        var activator = character.CurrentVehicle ?? (ClonedObjectBase)character;
        var reactionsFired = 0;

        var target = map.GetObjectByCoid(targetCoid);

        // Direct UseObject on a trigger volume/object.
        if (target is Trigger directTrigger)
        {
            reactionsFired += FireTriggerAndCount(map, activator, character.ObjectId.Coid, directTrigger, "direct");
            return reactionsFired > 0;
        }

        // SpawnPoint children: SpawnOwner / LastSpawnedCoid links kiosk NPC → TriggerEvents.
        var spawn = ResolveSpawnPoint(map, target, targetCoid);
        if (spawn != null)
        {
            reactionsFired += FireTemplateTriggerEvents(
                map,
                activator,
                character.ObjectId.Coid,
                spawn.ObjectId.Coid,
                spawn.Template?.TriggerEvents,
                source: "spawn");
        }
        else if (target is Creature or Vehicle)
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: no SpawnOwner for target={0} type={1} (cannot fire spawn TriggerEvents)",
                targetCoid,
                target.GetType().Name);
        }

        // Any map object whose template carries TriggerEvents (props, stores, etc.).
        if (reactionsFired == 0
            && target != null
            && map.MapData?.Templates != null
            && map.MapData.Templates.TryGetValue(target.ObjectId.Coid, out var tpl)
            && tpl.TriggerEvents != null)
        {
            reactionsFired += FireTemplateTriggerEvents(
                map,
                activator,
                character.ObjectId.Coid,
                target.ObjectId.Coid,
                tpl.TriggerEvents,
                source: "target-template");
        }

        if (reactionsFired == 0
            && map.MapData?.Templates != null
            && map.MapData.Templates.TryGetValue(targetCoid, out var targetTpl)
            && targetTpl.TriggerEvents != null)
        {
            reactionsFired += FireTemplateTriggerEvents(
                map,
                activator,
                character.ObjectId.Coid,
                targetCoid,
                targetTpl.TriggerEvents,
                source: "map-template");
        }

        return reactionsFired > 0;
    }

    internal static SpawnPoint ResolveSpawnPoint(SectorMap map, ClonedObjectBase target, long targetCoid)
    {
        if (map == null)
            return null;

        if (target is SpawnPoint sp)
            return sp;

        long spawnCoid = 0;
        if (target is Creature creature && creature.SpawnOwner > 0)
            spawnCoid = creature.SpawnOwner;
        else if (target is Vehicle vehicle && vehicle.SpawnOwnerCoid > 0)
            spawnCoid = vehicle.SpawnOwnerCoid;

        if (spawnCoid > 0 && map.GetObjectByCoid(spawnCoid) is SpawnPoint byOwner)
            return byOwner;

        // Fallback: spawn point that last spawned this COID (if SpawnOwner was not stamped).
        foreach (var obj in map.Objects.Values)
        {
            if (obj is SpawnPoint candidate && candidate.LastSpawnedCoid == targetCoid)
                return candidate;
        }

        return null;
    }

    internal static int FireTemplateTriggerEvents(
        SectorMap map,
        ClonedObjectBase activator,
        long characterCoid,
        long sourceCoid,
        long[] triggerEvents,
        string source)
    {
        if (map == null || triggerEvents == null || triggerEvents.Length == 0)
            return 0;

        var reactionsFired = 0;
        foreach (var te in triggerEvents)
        {
            if (te <= 0)
                continue;

            EnsureTriggerMaterialized(map, te);

            if (map.GetObjectByCoid(te) is not Trigger trigger)
            {
                Logger.WriteLog(LogType.Debug,
                    "UseObject: TriggerEvent coid={0} from {1}={2} not found as Trigger",
                    te,
                    source,
                    sourceCoid);
                continue;
            }

            reactionsFired += FireTriggerAndCount(map, activator, characterCoid, trigger, source);
        }

        return reactionsFired;
    }

    static int FireTriggerAndCount(
        SectorMap map,
        ClonedObjectBase activator,
        long characterCoid,
        Trigger trigger,
        string source)
    {
        if (trigger?.Template == null)
            return 0;

        // Mirror TriggerManager early-outs so we don't claim success when nothing runs.
        if (trigger.Template.ActivationCount == 0)
            return 0;
        if (trigger.Template.ActivationCount > 0 && trigger.FireCount >= trigger.Template.ActivationCount)
            return 0;
        if (trigger.Template.Conditions.Count > 0 && !trigger.ConditionsPass(activator))
            return 0;

        var reactionList = trigger.Template.Reactions;
        if (reactionList == null || reactionList.Count == 0)
        {
            Logger.WriteLog(LogType.Debug,
                "UseObject: trigger coid={0} has no reactions (source={1})",
                trigger.ObjectId.Coid,
                source);
            return 0;
        }

        // Pre-materialize reaction targets so TriggerReactions does not miss them.
        foreach (var rxCoid in reactionList)
            map.GetOrMaterializeReaction(rxCoid);

        Logger.WriteLog(LogType.Debug,
            "UseObject: fire trigger coid={0} reactions=[{1}] source={2} charCoid={3}",
            trigger.ObjectId.Coid,
            string.Join(',', reactionList),
            source,
            characterCoid);

        // Use TriggerManager so ActivationCount / FireCount stay consistent with collision path.
        var before = trigger.FireCount;
        TriggerManager.Instance.FireTriggerReactions(activator, trigger);
        // Count reactions that ran: FireCount only advances when manager accepted the fire.
        if (trigger.FireCount <= before)
            return 0;

        // Re-run count via TriggerReactionsCount is wrong (would double-fire). Approximate success
        // as "manager accepted fire and reaction list non-empty" — reactions were just triggered.
        return reactionList.Count;
    }

    /// <summary>Place a trigger from MapData if it is not live yet.</summary>
    internal static void EnsureTriggerMaterialized(SectorMap map, long triggerCoid)
    {
        if (map == null || triggerCoid <= 0)
            return;
        if (map.GetObjectByCoid(triggerCoid) is Trigger)
            return;
        if (map.MapData?.Templates == null
            || !map.MapData.Templates.TryGetValue(triggerCoid, out var tpl)
            || tpl is not TriggerTemplate tt)
        {
            return;
        }

        var placed = tt.Create() as Trigger;
        if (placed == null)
            return;

        placed.SetCoid(triggerCoid, false);
        placed.SetMap(map);
        Logger.WriteLog(LogType.Debug,
            "UseObject: materialized trigger coid={0} name='{1}'",
            triggerCoid,
            tt.Name);
    }
}
