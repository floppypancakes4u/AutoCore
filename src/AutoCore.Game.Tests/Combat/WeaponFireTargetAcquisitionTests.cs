using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class WeaponFireTargetAcquisitionTests
{
    private static WeaponSpecific NarrowWeapon(byte flags = 0, byte spray = 0, float rangeMax = 100f) =>
        new()
        {
            ValidArc = 0.987f,
            RangeMin = 0f,
            RangeMax = rangeMax,
            Flags = flags,
            SprayTargets = spray,
            ExplosionRadius = 0f,
        };

    private static WeaponFireTargetAcquisition.Candidate Cand(
        long coid, float x, float z, int faction = 2, bool corpse = false, bool inv = false, bool dmg = true,
        bool combatant = true, bool ignoresHostility = false) =>
        new(coid, new Vector3(x, 0f, z), faction, corpse, inv, dmg, combatant, ignoresHostility);

    [TestMethod]
    public void Acquire_HardTargetFirst_WhenInArc()
    {
        var shooter = new Vector3(0, 0, 0);
        var aim = TacArcGeometry.AimFromYaw(0f);
        var hard = Cand(10, 0, 20);
        var closerSoft = Cand(11, 0, 5);
        var hits = WeaponFireTargetAcquisition.Acquire(
            shooter, aim, shooterFaction: 1, shooterCoid: 1, ownerCoid: null,
            NarrowWeapon(flags: 0x01, spray: 3),
            new[] { hard, closerSoft },
            hardTargetCoid: 10,
            includeHardTarget: true);

        Assert.AreEqual(2, hits.Count);
        Assert.AreEqual(10, hits[0].Coid);
        Assert.IsTrue(hits[0].IsPrimary);
        Assert.AreEqual(11, hits[1].Coid);
    }

    [TestMethod]
    public void Acquire_SoftSortedByAscendingDistance()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var far = Cand(2, 0, 50);
        var near = Cand(3, 0, 10);
        var mid = Cand(4, 0, 25);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(0x01, 3),
            new[] { far, near, mid },
            hardTargetCoid: null,
            includeHardTarget: false);

        Assert.AreEqual(3, hits.Count);
        Assert.AreEqual(3, hits[0].Coid);
        Assert.AreEqual(4, hits[1].Coid);
        Assert.AreEqual(2, hits[2].Coid);
        Assert.IsTrue(hits[0].IsPrimary);
        Assert.IsFalse(hits[1].IsPrimary);
    }

    [TestMethod]
    public void Acquire_ExcludesSameFactionCorpseInvincibleSelf()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var ok = Cand(5, 0, 10, faction: 2);
        var sameFac = Cand(6, 0, 12, faction: 1);
        var corpse = Cand(7, 0, 8, corpse: true);
        var inv = Cand(8, 0, 9, inv: true);
        var self = Cand(1, 0, 7, faction: 2);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(0x01, 5),
            new[] { ok, sameFac, corpse, inv, self },
            null, false);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(5, hits[0].Coid);
    }

    [TestMethod]
    public void Acquire_RespectsSprayCap()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var list = new List<WeaponFireTargetAcquisition.Candidate>();
        for (var i = 0; i < 10; i++)
            list.Add(Cand(100 + i, 0, 5 + i));

        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(0x01, spray: 2),
            list, null, false);

        Assert.AreEqual(2, hits.Count);
    }

    [TestMethod]
    public void Acquire_SideTargetOutsideNarrowArc_Excluded()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var side = Cand(9, 30, 1); // nearly 90° off narrow cone
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(),
            new[] { side }, null, false);
        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public void AcquireExplosion_OmnidirectionalExcludesAlreadyHit()
    {
        var primary = Cand(1, 0, 10);
        var splashNear = Cand(2, 3, 10, faction: 2);
        var splashFar = Cand(3, 50, 10, faction: 2);
        var already = new HashSet<long> { 1 };
        var hits = WeaponFireTargetAcquisition.AcquireExplosion(
            impact: primary.Position,
            explosionRadius: 8f,
            shooterFaction: 1,
            shooterCoid: 99,
            ownerCoid: null,
            new[] { primary, splashNear, splashFar },
            already);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(2, hits[0].Coid);
    }

    [TestMethod]
    public void AcquireExplosion_ZeroRadius_Empty()
    {
        var hits = WeaponFireTargetAcquisition.AcquireExplosion(
            new Vector3(0, 0, 0), 0f, 1, 1, null,
            new[] { Cand(1, 0, 1) }, new HashSet<long>());
        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public void Acquire_HardTargetAloneFillsCap_SkipsSoft()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var hard = Cand(10, 0, 20);
        var soft = Cand(11, 0, 5);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(flags: 0, spray: 0), // maxTargets = 1
            new[] { hard, soft },
            hardTargetCoid: 10,
            includeHardTarget: true);
        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(10, hits[0].Coid);
    }

    [TestMethod]
    public void Acquire_HardTargetMissingFromCandidates_NoHardSeed()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var soft = Cand(11, 0, 5);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(),
            new[] { soft },
            hardTargetCoid: 999,
            includeHardTarget: true);
        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(11, hits[0].Coid);
    }

    [TestMethod]
    public void IsEligible_OwnerCoidAndExcludeCoid_Rejected()
    {
        var owner = Cand(50, 0, 10, faction: 2);
        Assert.IsFalse(WeaponFireTargetAcquisition.IsEligible(owner, 1, 1, ownerCoid: 50, excludeCoid: null));
        Assert.IsFalse(WeaponFireTargetAcquisition.IsEligible(owner, 1, 1, ownerCoid: null, excludeCoid: 50));
    }

    [TestMethod]
    public void Acquire_BelowRangeMin_Excluded()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var close = Cand(12, 0, 2);
        var weapon = NarrowWeapon();
        weapon.RangeMin = 5f;
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null, weapon, new[] { close }, null, false);
        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public void Candidate_IsCombatantProperty_Exposed()
    {
        var c = Cand(1, 0, 1, combatant: true);
        Assert.IsTrue(c.IsCombatant);
        Assert.AreEqual(0f, c.Position.Y);
    }

    [TestMethod]
    public void AcquireExplosion_IneligibleFaction_Excluded()
    {
        var friend = Cand(2, 1, 10, faction: 1); // same faction as shooter
        var hits = WeaponFireTargetAcquisition.AcquireExplosion(
            new Vector3(0, 0, 10), 5f, shooterFaction: 1, shooterCoid: 99, ownerCoid: null,
            new[] { friend }, new HashSet<long>());
        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public void AcquireExplosion_SortsByDistanceFromImpact()
    {
        var far = Cand(2, 6, 10, faction: 2);
        var near = Cand(3, 1, 10, faction: 2);
        var hits = WeaponFireTargetAcquisition.AcquireExplosion(
            new Vector3(0, 0, 10), 10f, 1, 99, null,
            new[] { far, near }, new HashSet<long>());
        Assert.AreEqual(2, hits.Count);
        Assert.AreEqual(3, hits[0].Coid);
        Assert.IsTrue(hits[0].DistanceFromShooter < hits[1].DistanceFromShooter);
        Assert.IsFalse(hits[0].IsPrimary);
    }

    [TestMethod]
    public void IsEligible_NotDamageable_False()
    {
        var c = Cand(1, 0, 10, dmg: false);
        Assert.IsFalse(WeaponFireTargetAcquisition.IsEligible(c, 1, 99, null, null));
    }

    /// <summary>
    /// Mission combat NPCs (e.g. Final Exam Gunny faction 22) must be hittable by Human (0).
    /// Wrong FactionDirty wiring left NPC at faction 0 → same-faction skip looked like invulnerability.
    /// </summary>
    [TestMethod]
    public void IsEligible_HumanShooter_VsMissionNpcFaction_True()
    {
        var gunny = Cand(14138, 0, 10, faction: 22);
        Assert.IsTrue(WeaponFireTargetAcquisition.IsEligible(
            gunny, shooterFaction: 0, shooterCoid: 1, ownerCoid: null, excludeCoid: null));
    }

    [TestMethod]
    public void IsEligible_HumanShooter_VsWronglyHumanFactionNpc_False()
    {
        var brokenGunny = Cand(14138, 0, 10, faction: 0);
        Assert.IsFalse(WeaponFireTargetAcquisition.IsEligible(
            brokenGunny, shooterFaction: 0, shooterCoid: 1, ownerCoid: null, excludeCoid: null),
            "FactionDirty bug: NPC left at Human 0 is rejected as same-faction");
    }

    [TestMethod]
    public void Acquire_ClosestSoftTargetWins_EvenIfNonCombatant_WhenCapIsOne()
    {
        // Nearest fence under the gun beats a distant vehicle (distance sort, not combatant tier).
        var aim = TacArcGeometry.AimFromYaw(0f);
        var nearProp = Cand(20, 0, 5, combatant: false, ignoresHostility: true);
        var farVehicle = Cand(21, 0, 40, combatant: true);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(flags: 0, spray: 0), // maxTargets = 1
            new[] { nearProp, farVehicle },
            hardTargetCoid: null,
            includeHardTarget: false);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(20, hits[0].Coid, "Closest soft target must win when maxTargets is 1");
    }

    [TestMethod]
    public void Acquire_SoftFillByAscendingDistance_MixedCombatantAndProp()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var veh = Cand(30, 0, 30, combatant: true);
        var propNear = Cand(31, 0, 5, combatant: false, ignoresHostility: true);
        var propMid = Cand(32, 0, 15, combatant: false, ignoresHostility: true);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(flags: 0x01, spray: 2),
            new[] { propNear, propMid, veh },
            null, false);

        Assert.AreEqual(2, hits.Count);
        Assert.AreEqual(31, hits[0].Coid);
        Assert.AreEqual(32, hits[1].Coid);
    }

    [TestMethod]
    public void Acquire_NonCombatantOnlyWhenNoCombatantInArc()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var prop = Cand(40, 0, 10, combatant: false);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(),
            new[] { prop },
            null, false);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(40, hits[0].Coid);
    }

    [TestMethod]
    public void Acquire_HardTargetBeatsCloserCombatant()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var closerVeh = Cand(50, 0, 5, combatant: true);
        var hard = Cand(51, 0, 40, combatant: true);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, 1, 1, null,
            NarrowWeapon(flags: 0x01, spray: 2),
            new[] { closerVeh, hard },
            hardTargetCoid: 51,
            includeHardTarget: true);

        Assert.AreEqual(2, hits.Count);
        Assert.AreEqual(51, hits[0].Coid);
        Assert.AreEqual(50, hits[1].Coid);
    }

    [TestMethod]
    public void Acquire_UsesHostilityFaction_NotChassisWhenDifferentFromDriver()
    {
        // Player race faction 1 vs NPC vehicle whose chassis Faction was also 1 but driver/root
        // hostility faction is 5 (must be supplied via Candidate.Faction = GetIDFaction()).
        var aim = TacArcGeometry.AimFromYaw(0f);
        var npcVehicle = Cand(60, 0, 20, faction: 5, combatant: true);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, shooterFaction: 1, shooterCoid: 1, ownerCoid: null,
            NarrowWeapon(),
            new[] { npcVehicle },
            null, false);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(60, hits[0].Coid);
    }

    [TestMethod]
    public void Acquire_SameHostilityFaction_ExcludedEvenIfCombatant()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var allyChassis = Cand(61, 0, 20, faction: 1, combatant: true);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, shooterFaction: 1, shooterCoid: 1, ownerCoid: null,
            NarrowWeapon(),
            new[] { allyChassis },
            null, false);

        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public void Acquire_MapPropSameFaction_EligibleWhenIgnoresHostility()
    {
        // Ram-eligible scenery often shares map/player faction; still destroyable by TacArc.
        var aim = TacArcGeometry.AimFromYaw(0f);
        var rail = Cand(70, 0, 15, faction: 1, combatant: false, ignoresHostility: true);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, shooterFaction: 1, shooterCoid: 1, ownerCoid: null,
            NarrowWeapon(),
            new[] { rail },
            null, false);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(70, hits[0].Coid);
    }

    [TestMethod]
    public void Acquire_MapPropWithoutIgnoresHostility_SameFactionExcluded()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var junk = Cand(71, 0, 15, faction: 1, combatant: false, ignoresHostility: false);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, shooterFaction: 1, shooterCoid: 1, ownerCoid: null,
            NarrowWeapon(),
            new[] { junk },
            null, false);

        Assert.AreEqual(0, hits.Count);
    }

    [TestMethod]
    public void Acquire_CombatantPreferred_ButMapPropFilledWhenNoCombatant()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var fence = Cand(72, 0, 8, faction: 1, combatant: false, ignoresHostility: true);
        var hits = WeaponFireTargetAcquisition.Acquire(
            new Vector3(0, 0, 0), aim, shooterFaction: 1, shooterCoid: 99, ownerCoid: null,
            NarrowWeapon(),
            new[] { fence },
            null, false);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(72, hits[0].Coid);
        Assert.IsTrue(hits[0].IsPrimary);
    }

    [TestMethod]
    public void AcquireExplosion_MapPropIgnoresHostility()
    {
        var impact = new Vector3(0, 0, 10);
        var fence = Cand(73, 1, 10, faction: 1, combatant: false, ignoresHostility: true);
        var hits = WeaponFireTargetAcquisition.AcquireExplosion(
            impact, explosionRadius: 5f, shooterFaction: 1, shooterCoid: 1, ownerCoid: null,
            new[] { fence }, alreadyHit: new HashSet<long>());

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(73, hits[0].Coid);
    }
}
