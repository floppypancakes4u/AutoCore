using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

using AutoCore.Database.World.Models;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// SS-36 tripwires: the server had four inconsistent "who may I damage" rules (weapons, splash,
/// skills, ram) — skills had none at all, splash ran with chassis faction −1 and hit everyone,
/// and ram could latently one-hit-kill on-foot players (Character : Creature). CombatEligibility
/// is the single choke point every damage route must consult. Retail-style hostility: effective
/// owner-chain-root faction inequality (client owner vfunc +0x298); cross-race players (0/1/2)
/// are mutually hostile, same faction and self are not.
/// </summary>
[TestClass]
public class CombatEligibilityTests
{
    private const int Human = 0;
    private const int Mutant = 1;
    private const int Biomek = 2;
    private const int HostileNpc = 21;

    #region IsFactionEligible (pure rule shared with weapon acquisition)

    [TestMethod]
    public void IsFactionEligible_CrossRacePlayers_Allowed()
    {
        Assert.IsTrue(CombatEligibility.IsFactionEligible(Human, Mutant, false));
        Assert.IsTrue(CombatEligibility.IsFactionEligible(Mutant, Biomek, false));
        Assert.IsTrue(CombatEligibility.IsFactionEligible(Human, Biomek, false));
        Assert.IsTrue(CombatEligibility.IsFactionEligible(Biomek, Human, false));
    }

    [TestMethod]
    public void IsFactionEligible_SameFaction_Denied()
    {
        Assert.IsFalse(CombatEligibility.IsFactionEligible(Human, Human, false));
        Assert.IsFalse(CombatEligibility.IsFactionEligible(Mutant, Mutant, false));
        Assert.IsFalse(CombatEligibility.IsFactionEligible(HostileNpc, HostileNpc, false));
    }

    /// <summary>
    /// SS-36 splash-class tripwire: player-vehicle chassis Faction is permanently −1 (LoadFromDB
    /// never calls SetupCBFields). The old raw inequality made −1 match nobody, so a mistaken
    /// chassis-faction call site splash-damaged EVERYONE. A negative attacker faction must
    /// fail closed: acquire nothing, damage nothing.
    /// </summary>
    [TestMethod]
    public void IsFactionEligible_NegativeAttackerFaction_DeniedFailClosed()
    {
        foreach (var victim in new[] { Human, Mutant, HostileNpc, CombatEligibility.UnsetFaction })
        {
            Assert.IsFalse(CombatEligibility.IsFactionEligible(CombatEligibility.UnsetFaction, victim, false),
                $"unset (-1) attacker must never damage faction {victim}");
            Assert.IsFalse(CombatEligibility.IsFactionEligible(CombatEligibility.NeutralFaction, victim, false),
                $"neutral (-100) attacker must never damage faction {victim}");
        }
    }

    [TestMethod]
    public void IsFactionEligible_NeutralVictim_Denied()
    {
        Assert.IsFalse(CombatEligibility.IsFactionEligible(Human, CombatEligibility.NeutralFaction, false),
            "neutral (-100) town/quest NPCs are protected from guns (client FindTargetToAttack parity)");
        Assert.IsFalse(CombatEligibility.IsFactionEligible(HostileNpc, CombatEligibility.NeutralFaction, false));
    }

    /// <summary>Unset-faction (−1) NPCs exist live and are shootable today — must stay shootable.</summary>
    [TestMethod]
    public void IsFactionEligible_UnsetVictim_Allowed()
    {
        Assert.IsTrue(CombatEligibility.IsFactionEligible(Human, CombatEligibility.UnsetFaction, false));
        Assert.IsTrue(CombatEligibility.IsFactionEligible(HostileNpc, CombatEligibility.UnsetFaction, false));
    }

    [TestMethod]
    public void IsFactionEligible_IgnoresHostility_AlwaysAllowed()
    {
        // Map-prop scenery candidates (rails, fences, billboards): no faction hostility.
        Assert.IsTrue(CombatEligibility.IsFactionEligible(Human, Human, true));
        Assert.IsTrue(CombatEligibility.IsFactionEligible(Human, CombatEligibility.NeutralFaction, true));
        Assert.IsTrue(CombatEligibility.IsFactionEligible(CombatEligibility.UnsetFaction, Human, true));
    }

    [TestMethod]
    public void IsFactionEligible_NpcVsNpc_DifferentFactions_Allowed()
    {
        Assert.IsTrue(CombatEligibility.IsFactionEligible(3, HostileNpc, false));
        Assert.IsTrue(CombatEligibility.IsFactionEligible(HostileNpc, 3, false));
    }

    #endregion

    #region GetEffectiveFaction

    [TestMethod]
    public void GetEffectiveFaction_ResolvesOwnerChainRoot()
    {
        var map = CreateTestMap();
        var (vehicle, character) = CreatePlayerVehicle(map, 8101, Mutant);

        Assert.AreEqual(Mutant, CombatEligibility.GetEffectiveFaction(vehicle),
            "vehicle must resolve to its driver Character's faction, not chassis Faction");
        Assert.AreEqual(-1, vehicle.Faction, "player-vehicle chassis faction stays -1 (the RC2 landmine)");
        Assert.AreEqual(Mutant, CombatEligibility.GetEffectiveFaction(character));
    }

    [TestMethod]
    public void GetEffectiveFaction_TeamFactionZero_FallsBackToRootFaction()
    {
        var creature = new Creature();
        creature.SetCoid(8102, false);
        creature.Faction = HostileNpc;

        Assert.AreEqual(HostileNpc, CombatEligibility.GetEffectiveFaction(creature));
    }

    /// <summary>
    /// SS-46: the dormant retail TeamFaction lever is deliberately NOT consulted. Acquisition
    /// keys candidates on GetIDFaction, so a TeamFaction-aware gate could disagree the moment
    /// anything rewrote Faction alone (reaction 22 SetFactionFromVar) — the player would acquire,
    /// roll hits, and see nothing land. Arena revival must change acquisition and the gate together.
    /// </summary>
    [TestMethod]
    public void GetEffectiveFaction_IgnoresDormantTeamFactionLever()
    {
        var creature = new TeamFactionCreature { Team = 5 };
        creature.SetCoid(8103, false);
        creature.Faction = Mutant;

        Assert.AreEqual(Mutant, CombatEligibility.GetEffectiveFaction(creature),
            "effective faction is the owner-chain root's Faction — identical to GetIDFaction");
        Assert.AreEqual(creature.GetIDFaction(), CombatEligibility.GetEffectiveFaction(creature));
    }

    #endregion

    #region CanDamage (entity-level gate)

    [TestMethod]
    public void CanDamage_NullArguments_Denied()
    {
        var map = CreateTestMap();
        var (vehicle, _) = CreatePlayerVehicle(map, 8201, Human);

        Assert.IsFalse(CombatEligibility.CanDamage(null, vehicle, DamageContext.WeaponFire));
        Assert.IsFalse(CombatEligibility.CanDamage(vehicle, null, DamageContext.WeaponFire));
        Assert.IsFalse(CombatEligibility.CanDamage(null, null, DamageContext.WeaponFire));
    }

    [TestMethod]
    public void CanDamage_AdminContext_BypassesHostility()
    {
        var map = CreateTestMap();
        var (attacker, _) = CreatePlayerVehicle(map, 8211, Human);
        var (sameFaction, _) = CreatePlayerVehicle(map, 8213, Human);

        Assert.IsTrue(CombatEligibility.CanDamage(attacker, sameFaction, DamageContext.Admin),
            "/kill is GM-gated upstream; the gate must not second-guess admin damage");
        Assert.IsTrue(CombatEligibility.CanDamage(attacker, attacker, DamageContext.Admin),
            "admin self-kill is allowed");
    }

    [TestMethod]
    public void CanDamage_CorpseOrInvincibleVictim_Denied()
    {
        var map = CreateTestMap();
        var (attacker, _) = CreatePlayerVehicle(map, 8221, Human);
        var corpse = CreateCreature(map, 8223, HostileNpc);
        corpse.OnDeath(DeathType.Silent);
        var invincible = CreateCreature(map, 8224, HostileNpc);
        invincible.SetInvincible(true);

        Assert.IsFalse(CombatEligibility.CanDamage(attacker, corpse, DamageContext.WeaponFire));
        Assert.IsFalse(CombatEligibility.CanDamage(attacker, invincible, DamageContext.WeaponFire));
    }

    /// <summary>
    /// SS-36 / F3 tripwire: Character : Creature, so the old ram guard (`is not Creature`) let a
    /// vehicle one-hit-kill an on-foot player once factions were hostile. Players are always hit
    /// via their vehicle — the gate denies Character victims in every non-Admin context.
    /// </summary>
    [TestMethod]
    public void CanDamage_CharacterVictim_DeniedForAllNonAdminContexts()
    {
        var map = CreateTestMap();
        var (attacker, _) = CreatePlayerVehicle(map, 8231, Human);
        var onFoot = new Character();
        onFoot.SetCoid(8233, true);
        onFoot.Faction = HostileNpc; // worst case: pushed to a genuinely hostile faction
        onFoot.SetMap(map);

        foreach (var context in new[]
                 {
                     DamageContext.WeaponFire, DamageContext.Splash, DamageContext.Skill,
                     DamageContext.Ram, DamageContext.Reaction,
                 })
        {
            Assert.IsFalse(CombatEligibility.CanDamage(attacker, onFoot, context),
                $"on-foot Character must never be damageable via {context}");
        }
    }

    /// <summary>
    /// SS-46 tripwire: the Character refusal sat ABOVE the self/owner rule, so it also blocked
    /// authored self-damage reactions whose activator is the on-foot body. TriggerManager resolves
    /// the Character (not the vehicle) as activator on town maps and whenever the player has no
    /// vehicle — so every damage pain-pad in a town silently stopped working, while repair pads
    /// (positive heal, ungated) kept working.
    /// </summary>
    [TestMethod]
    public void CanDamage_ReactionSelfDamage_OnFootCharacter_Allowed()
    {
        var map = CreateTestMap();
        var onFoot = new Character();
        onFoot.SetCoid(8321, true);
        onFoot.Faction = Human;
        onFoot.SetMap(map);

        Assert.IsTrue(CombatEligibility.CanDamage(onFoot, onFoot, DamageContext.Reaction),
            "an authored pain pad must still damage an on-foot activator (self, Reaction context)");
    }

    /// <summary>
    /// SS-46 tripwire: Admin returned true before the Character refusal, so a GM /kill on a
    /// player's on-foot body reached TakeDamage → OnDeath → SetMap(null) — the exact F3
    /// catastrophe (body off-map, ticks frozen, /warp broken) SS-36 claims to close.
    /// </summary>
    [TestMethod]
    public void CanDamage_AdminContext_StillRefusesCharacterBody()
    {
        var map = CreateTestMap();
        var (gmVehicle, _) = CreatePlayerVehicle(map, 8331, Human);
        var victimBody = new Character();
        victimBody.SetCoid(8333, true);
        victimBody.Faction = Mutant;
        victimBody.SetMap(map);

        Assert.IsFalse(CombatEligibility.CanDamage(gmVehicle, victimBody, DamageContext.Admin),
            "not even Admin may damage a player's Character body — kill the vehicle instead (F3)");
    }

    /// <summary>
    /// SS-46: weapon acquisition keys on the candidate faction while the gate keys on the
    /// effective faction. If those two ever disagree the player fires, the client predicts hits,
    /// and nothing lands. They must be the same function for every entity shape.
    /// </summary>
    [TestMethod]
    public void GetEffectiveFaction_AgreesWithGetIDFaction_WhenTeamFactionIsStale()
    {
        var map = CreateTestMap();
        // Retail lever shape: TeamFaction was seeded at load, then a reaction rewrote Faction.
        var creature = new TeamFactionCreature { Team = Mutant };
        creature.SetCoid(8341, false);
        creature.Faction = 3;
        creature.SetMap(map);

        Assert.AreEqual(
            creature.GetIDFaction(),
            CombatEligibility.GetEffectiveFaction(creature),
            "acquisition (GetIDFaction) and the gate (GetEffectiveFaction) must never disagree — " +
            "a divergence makes shots acquire but never land");
    }

    [TestMethod]
    public void GetEffectiveFaction_NullEntity_ReturnsUnset()
    {
        Assert.AreEqual(CombatEligibility.UnsetFaction, CombatEligibility.GetEffectiveFaction(null));
    }

    [TestMethod]
    public void CanDamage_AttackerWithNoMap_Denied()
    {
        var map = CreateTestMap();
        var victim = CreateCreature(map, 8353, HostileNpc);
        var orphan = new Vehicle();
        orphan.SetCoid(8351, false);
        orphan.InitializeHealthForTests(100);

        Assert.IsFalse(CombatEligibility.CanDamage(orphan, victim, DamageContext.WeaponFire),
            "an attacker that is not on a map must fail closed");
    }

    /// <summary>Negative pin: Vehicle/Creature subclass GraphicsObject but must NOT take the scenery path.</summary>
    [TestMethod]
    public void CanDamage_VehicleVictim_DoesNotTakeSceneryAllowance()
    {
        var map = CreateTestMap();
        var (a, _) = CreatePlayerVehicle(map, 8361, Human);
        var (b, _) = CreatePlayerVehicle(map, 8363, Human);

        Assert.IsFalse(CombatEligibility.CanDamage(a, b, DamageContext.WeaponFire),
            "scenery uses exact-type GraphicsObject; a Vehicle must still face the faction rules");
    }

    [TestMethod]
    public void CanDamage_SelfOrOwnedEntity_DeniedExceptReaction()
    {
        var map = CreateTestMap();
        var (vehicle, _) = CreatePlayerVehicle(map, 8241, Human);

        Assert.IsFalse(CombatEligibility.CanDamage(vehicle, vehicle, DamageContext.WeaponFire));
        Assert.IsFalse(CombatEligibility.CanDamage(vehicle, vehicle, DamageContext.Skill),
            "self-cast damage skills (the skill-2567 self-nuke) must be denied");
        Assert.IsFalse(CombatEligibility.CanDamage(vehicle, vehicle, DamageContext.Ram));
        Assert.IsTrue(CombatEligibility.CanDamage(vehicle, vehicle, DamageContext.Reaction),
            "authored pain pads apply self-damage via reactions — allowed under Reaction");
    }

    [TestMethod]
    public void CanDamage_ReactionContext_DifferentRoot_Denied()
    {
        var map = CreateTestMap();
        var (padVictim, _) = CreatePlayerVehicle(map, 8251, Human);
        var (other, _) = CreatePlayerVehicle(map, 8253, Mutant);

        Assert.IsFalse(CombatEligibility.CanDamage(padVictim, other, DamageContext.Reaction),
            "reactions only self-damage by construction; keep that structural");
    }

    [TestMethod]
    public void CanDamage_CrossMapVictim_Denied()
    {
        var mapA = CreateTestMap();
        var mapB = CreateTestMap();
        var (attacker, _) = CreatePlayerVehicle(mapA, 8261, Human);
        var (victim, _) = CreatePlayerVehicle(mapB, 8263, Mutant);

        Assert.IsFalse(CombatEligibility.CanDamage(attacker, victim, DamageContext.Skill),
            "cross-map/cross-instance damage (global-registry skill reach) must be denied");
    }

    /// <summary>
    /// Rule-ordering pin: scenery is allowed BEFORE the attacker-faction fail-closed rule, so
    /// owner-less Sim/clone vehicles (root faction −1) keep destroying fences via map-prop ram.
    /// </summary>
    [TestMethod]
    public void CanDamage_SceneryProp_AllowedEvenForOwnerlessAttacker()
    {
        var map = CreateTestMap();
        var ownerless = new Vehicle();
        ownerless.SetCoid(8271, false);
        ownerless.SetMap(map);
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.SetCoid(8273, false);
        prop.SetMap(map);

        Assert.AreEqual(-1, ownerless.GetIDFaction(), "precondition: owner-less vehicle root faction is -1");
        Assert.IsTrue(CombatEligibility.CanDamage(ownerless, prop, DamageContext.Ram));
        var (player, _) = CreatePlayerVehicle(map, 8275, Human);
        Assert.IsTrue(CombatEligibility.CanDamage(player, prop, DamageContext.WeaponFire),
            "scenery has no faction hostility regardless of context");
    }

    [TestMethod]
    public void CanDamage_CrossRacePlayerVehicles_Allowed()
    {
        var map = CreateTestMap();
        var (human, _) = CreatePlayerVehicle(map, 8281, Human);
        var (mutant, _) = CreatePlayerVehicle(map, 8283, Mutant);

        Assert.IsTrue(CombatEligibility.CanDamage(human, mutant, DamageContext.WeaponFire),
            "retail policy: cross-race players are mutually hostile");
        Assert.IsTrue(CombatEligibility.CanDamage(mutant, human, DamageContext.Skill));
    }

    [TestMethod]
    public void CanDamage_SameRacePlayerVehicles_Denied()
    {
        var map = CreateTestMap();
        var (a, _) = CreatePlayerVehicle(map, 8291, Human);
        var (b, _) = CreatePlayerVehicle(map, 8293, Human);

        Assert.IsFalse(CombatEligibility.CanDamage(a, b, DamageContext.WeaponFire));
        Assert.IsFalse(CombatEligibility.CanDamage(a, b, DamageContext.Splash),
            "same-faction splash (the RC2 symptom) must be denied at the entity level too");
        Assert.IsFalse(CombatEligibility.CanDamage(a, b, DamageContext.Skill));
        Assert.IsFalse(CombatEligibility.CanDamage(a, b, DamageContext.Ram));
    }

    [TestMethod]
    public void CanDamage_PlayerVsHostileNpc_Allowed()
    {
        var map = CreateTestMap();
        var (player, _) = CreatePlayerVehicle(map, 8301, Human);
        var creature = CreateCreature(map, 8303, HostileNpc);

        Assert.IsTrue(CombatEligibility.CanDamage(player, creature, DamageContext.WeaponFire));
        Assert.IsTrue(CombatEligibility.CanDamage(player, creature, DamageContext.Ram));
    }

    #endregion

    #region Helpers

    private static int _nextContinentId = 9601;

    private static SectorMap CreateTestMap()
    {
        var id = _nextContinentId++;
        return SectorMap.CreateForTests(
            new ContinentObject
            {
                Id = id,
                MapFileName = $"tm_combat_eligibility_{id}",
                DisplayName = "test",
            },
            new Vector4(0, 0, 0, 0));
    }

    private static (Vehicle Vehicle, Character Character) CreatePlayerVehicle(SectorMap map, long coid, int faction)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        character.Faction = faction;

        var vehicle = new Vehicle();
        vehicle.SetCoid(coid + 1, true);
        vehicle.InitializeHealthForTests(500);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (vehicle, character);
    }

    private static Creature CreateCreature(SectorMap map, long coid, int faction)
    {
        var creature = new Creature();
        creature.SetCoid(coid, false);
        creature.InitializeHealthForTests(100);
        creature.Faction = faction;
        creature.SetMap(map);
        return creature;
    }

    /// <summary>Test-only lever: exposes the dormant retail TeamFaction override.</summary>
    private sealed class TeamFactionCreature : Creature
    {
        public int Team;
        public override int GetBareTeamFaction() => Team;
    }

    #endregion
}
