namespace AutoCore.Game.Combat;

using AutoCore.Game.Entities;

/// <summary>Which damage route is asking (SS-36). Admin bypasses hostility; Reaction permits self.</summary>
public enum DamageContext
{
    WeaponFire,
    Splash,
    Skill,
    Ram,
    Reaction,
    Admin,
}

/// <summary>
/// The single combat-eligibility choke point (fixes.md §6.A, SS-36). Every damage route —
/// weapons, splash, skills, ram, reactions, admin — must consult <see cref="CanDamage"/> before
/// the TakeDamage sink. Retail-style hostility (client owner vfunc +0x298): effective-faction
/// inequality via the owner-chain root, with self/owner exclusion and the −1/−100 carve-outs.
/// Cross-race players (0/1/2) are mutually hostile; same faction and self are not.
/// </summary>
public static class CombatEligibility
{
    /// <summary>Town/quest NPCs the client's FindTargetToAttack skips — never a valid victim.</summary>
    public const int NeutralFaction = -100;

    /// <summary>
    /// ClonedObjectBase ctor default. Player-vehicle chassis keep it forever (LoadFromDB never
    /// calls SetupCBFields) — a negative ATTACKER faction therefore fails closed, while −1
    /// victims (unset-faction live NPCs) stay damageable.
    /// </summary>
    public const int UnsetFaction = -1;

    /// <summary>Entity-level gate. Consult before every TakeDamage(damage, attacker) sink.</summary>
    public static bool CanDamage(ClonedObjectBase attacker, ClonedObjectBase victim, DamageContext context)
    {
        if (attacker == null || victim == null)
            return false;

        // On-foot player bodies are never third-party combat victims — SS-46: this sits ABOVE the
        // Admin bypass because killing a Character runs OnDeath → SetMap(null), which strands the
        // body off-map, freezes its ticks and breaks /warp (F3). Even a GM must kill the vehicle.
        // The one exception is authored self-damage: TriggerManager resolves the Character (not the
        // vehicle) as the activator on town maps, so pain pads must still reach their own body.
        if (victim is Character)
            return context == DamageContext.Reaction && ReferenceEquals(GetRoot(attacker), GetRoot(victim));

        // /kill and friends: GM-gated upstream (SS-28); the victim's own TakeDamage guards still run.
        if (context == DamageContext.Admin)
            return true;

        if (victim.IsCorpse || victim.IsInvincible)
            return false;

        // Self/owner exclusion by owner-chain root: a character, their vehicle, and anything the
        // chain owns are all "self". Authored pain pads self-damage via reactions — only there.
        if (ReferenceEquals(GetRoot(attacker), GetRoot(victim)))
            return context == DamageContext.Reaction;

        if (context == DamageContext.Reaction)
            return false; // reactions only ever damage the activator by construction

        // Same live map instance required — kills cross-map/cross-instance skill reach.
        // Reference compare: per-player instances share ContinentId (SS-30 invariant).
        if (attacker.Map == null || !ReferenceEquals(attacker.Map, victim.Map))
            return false;

        // Scenery map props (rails, fences, billboards): no faction hostility. Must precede the
        // faction rules so owner-less Sim/clone vehicles (root −1) keep destroying fences.
        if (victim.GetType() == typeof(GraphicsObject))
            return true;

        return IsFactionEligible(GetEffectiveFaction(attacker), GetEffectiveFaction(victim), false);
    }

    /// <summary>
    /// Effective combat faction: the owner-chain root's faction, i.e. exactly
    /// <see cref="ClonedObjectBase.GetIDFaction"/>.
    /// <para>
    /// SS-46: this deliberately does NOT consult the dormant retail <c>TeamFaction</c> lever.
    /// Weapon acquisition keys candidates on <c>GetIDFaction</c>; if the gate keyed on a
    /// TeamFaction-aware value the two could disagree the moment anything rewrote
    /// <c>Faction</c> without <c>TeamFaction</c> (reaction 22 <c>SetFactionFromVar</c> does
    /// exactly that), and the player would acquire a target, roll hits, and see nothing land.
    /// When the Arena/PvP side lever is revived it must be introduced in acquisition and here in
    /// the same change — pinned by
    /// <c>CombatEligibilityTests.GetEffectiveFaction_AgreesWithGetIDFaction_WhenTeamFactionIsStale</c>.
    /// </para>
    /// </summary>
    public static int GetEffectiveFaction(ClonedObjectBase entity)
    {
        if (entity == null)
            return UnsetFaction;

        return GetRoot(entity).Faction;
    }

    /// <summary>
    /// Pure faction rule shared with <see cref="WeaponFireTargetAcquisition.IsEligible"/> so the
    /// candidate pipeline and the entity routes cannot drift. A negative attacker faction fails
    /// closed (the chassis-faction −1 splash bug class acquires nothing instead of everyone).
    /// </summary>
    public static bool IsFactionEligible(int attackerEffectiveFaction, int victimEffectiveFaction, bool victimIgnoresHostility)
    {
        if (victimIgnoresHostility)
            return true;

        if (attackerEffectiveFaction < 0)
            return false;

        if (victimEffectiveFaction == NeutralFaction)
            return false;

        if (victimEffectiveFaction == UnsetFaction)
            return true;

        return attackerEffectiveFaction != victimEffectiveFaction;
    }

    private static ClonedObjectBase GetRoot(ClonedObjectBase entity)
    {
        var obj = entity;
        for (var owner = obj.Owner; owner != null; owner = owner.Owner)
            obj = owner;
        return obj;
    }
}
