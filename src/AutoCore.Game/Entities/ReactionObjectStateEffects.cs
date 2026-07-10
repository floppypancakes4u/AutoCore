namespace AutoCore.Game.Entities;

using AutoCore.Game.EntityTemplates;
using AutoCore.Utils;

/// <summary>
/// Server authority for object-state reactions (MakeInvincible / MakeNotInvincible / SetFactionFromVar).
/// Extracted for coverage and reuse; mission-agnostic object list targeting.
/// </summary>
public static class ReactionObjectStateEffects
{
    /// <summary>
    /// MakeInvincible (6) / MakeNotInvincible (7).
    /// </summary>
    public static bool ApplyInvincible(ReactionTemplate template, ClonedObjectBase activator, bool invincible)
    {
        if (template == null)
            return true;

        foreach (var target in EnumerateTargets(template, activator))
        {
            target.SetInvincible(invincible);
            Logger.WriteLog(LogType.Debug,
                "{0} reaction {1}: object {2} IsInvincible={3}",
                invincible ? "MakeInvincible" : "MakeNotInvincible",
                template.COID,
                target.ObjectId.Coid,
                invincible);
        }

        return true;
    }

    /// <summary>
    /// SetFactionFromVar (22): faction = ROUND(logicVar[GenericVar1]).
    /// </summary>
    public static bool ApplyFactionFromVar(ReactionTemplate template, ClonedObjectBase activator)
    {
        if (template == null)
            return true;

        var character = activator?.GetAsCharacter() ?? activator?.GetSuperCharacter(false);
        var store = character?.EnsureLogicVariables();
        if (store == null)
        {
            Logger.WriteLog(LogType.Debug,
                "SetFactionFromVar reaction {0}: no character/logic vars — skip authority (client may still apply via 0x206C)",
                template.COID);
            return true;
        }

        var faction = (int)Math.Round(store.Get(template.GenericVar1));
        foreach (var target in EnumerateTargets(template, activator))
        {
            target.Faction = faction;
            Logger.WriteLog(LogType.Debug,
                "SetFactionFromVar reaction {0}: object {1} Faction={2} (var[{3}])",
                template.COID,
                target.ObjectId.Coid,
                faction,
                template.GenericVar1);
        }

        return true;
    }

    /// <summary>
    /// ActOnActivator → activator; else each Template.Objects COID on the map.
    /// </summary>
    public static IEnumerable<ClonedObjectBase> EnumerateTargets(ReactionTemplate template, ClonedObjectBase activator)
    {
        if (template == null)
            yield break;

        if (template.ActOnActivator)
        {
            if (activator != null)
                yield return activator;
            yield break;
        }

        var map = activator?.Map;
        if (map == null)
            yield break;

        foreach (var objectCoid in template.Objects)
        {
            var obj = map.GetObjectByCoid(objectCoid);
            if (obj != null)
                yield return obj;
            else
                Logger.WriteLog(LogType.Debug,
                    "Reaction {0} ({1}): object {2} not on server (client-side only)",
                    template.COID,
                    template.ReactionType,
                    objectCoid);
        }
    }
}
