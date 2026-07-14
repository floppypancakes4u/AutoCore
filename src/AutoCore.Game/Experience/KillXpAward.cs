namespace AutoCore.Game.Experience;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Utils;

/// <summary>
/// Awards kill XP to the murderer's character (docs/XP.md v1: full credit, no convoy split).
/// </summary>
public static class KillXpAward
{
    /// <summary>
    /// If <paramref name="victim"/> has a murderer character, compute and grant kill XP.
    /// Only combatants (<see cref="Creature"/> / <see cref="Vehicle"/>) award kill XP —
    /// pure map props and inventory <see cref="SimpleObject"/>s do not (docs/XP.md).
    /// Safe to call from any OnDeath path.
    /// </summary>
    public static void TryAward(ClonedObjectBase victim)
    {
        if (victim == null || victim.Murderer.Coid <= 0)
            return;

        // Map scenery / inventory junk: OnDeath still runs for loot + mission kill credit,
        // but kill XP is creature/vehicle only (level table + XPPercent).
        if (victim is not (Creature or Vehicle))
            return;

        Character killer;
        try
        {
            var murdererObj = ObjectManager.Instance.GetObject(victim.Murderer);
            killer = murdererObj?.GetSuperCharacter(false);
        }
        catch
        {
            return;
        }

        if (killer == null)
            return;

        try
        {
            var playerLevel = killer.Level;
            var victimLevel = victim is Creature victimCreature
                ? victimCreature.GetLevel()
                : (byte)1;
            var xpPercent = ResolveXpPercent(victim);
            var amount = ExperienceService.Instance.ComputeKillXp(
                playerLevel,
                victimLevel,
                xpPercent,
                participation: 1f);

            if (amount <= 0)
                return;

            ExperienceService.Instance.GiveXp(killer, amount, XpSource.Kill);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "KillXpAward failed victim={0} killer={1}: {2}",
                victim.ObjectId.Coid,
                killer.ObjectId.Coid,
                ex.Message);
        }
    }

    private static float ResolveXpPercent(ClonedObjectBase victim)
    {
        if (victim is not Creature creature)
            return 1f;

        try
        {
            CloneBaseCreature cb = creature.CloneBaseObject as CloneBaseCreature;
            if (cb == null && creature.CBID > 0)
                cb = AssetManager.Instance.GetCloneBase(creature.CBID) as CloneBaseCreature;
            if (cb?.CreatureSpecific != null)
            {
                var pct = cb.CreatureSpecific.XPPercent;
                return pct > 0f ? pct : 1f;
            }
        }
        catch
        {
            // fall through
        }

        return 1f;
    }
}
