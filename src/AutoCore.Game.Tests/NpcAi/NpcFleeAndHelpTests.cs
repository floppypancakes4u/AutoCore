using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using System;
using System.Runtime.CompilerServices;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Stage 11: flee (val1–val4), call-for-help (val5–val7), and damage-driven aggro.
/// Fleeing NPCs run home with the wire state pinned to Engage (client circling visual);
/// help spreads server-authoritative aggro to same-faction idle allies; and taking damage
/// while idle acquires the attacker immediately (aggro-list parity, bypassing the vision scan).
/// </summary>
[TestClass]
public class NpcFleeAndHelpTests
{
    private const int DriverCbid = 87_100;

    [TestInitialize]
    public void SetUp()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        Vehicle.ClearCombatThrottleForTests();
        NpcCombatAi.ResetRngForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NpcCombatAi.ResetRngForTests();
    }

    // ----- flee ---------------------------------------------------------------------------

    [TestMethod]
    public void Flee_TriggersAtVal3HpBand_AfterVal1Timer()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        SetFleeVals(npc, timerMs: 5000f, fleeHp: 0.3f, reengage: 1f);
        SetHp(npc, maxHp: 100, currentHp: 20); // ratio 0.2 <= 0.3

        var (player, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);
        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;
        npc.Firing = 1;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.AreEqual(105_000L, npc.NpcAi.FleeUntilMs, "flee latches until now + val1 timer");
        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState, "fleeing pins the wire state to Engage (client circling)");
        Assert.AreEqual((byte)0, npc.Firing, "a fleeing NPC ceases fire");
    }

    [TestMethod]
    public void NeverFleeProfile_ZeroVals_NeverFlees()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        npc.CreateGhost();
        SetFleeVals(npc, timerMs: 0f, fleeHp: 0f, reengage: 0f); // retail "never flee" row
        SetHp(npc, maxHp: 100, currentHp: 1); // almost dead, but zero-val profile must not flee

        var (player, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);
        player.ApplyTemplateBaseHp(100_000);
        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.AreEqual(HBAICombatState.Combat, npc.NpcAi.CombatState, "a zero-val profile never flees");
        Assert.AreEqual(0L, npc.NpcAi.FleeUntilMs, "no flee latch for a never-flee profile");
    }

    [TestMethod]
    public void Flee_MovesAwayTowardHome_StopsFiring()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(100f, 0f, 0f), driverFaction: 3, visionRange: 60f, speed: 10f);
        SetFleeVals(npc, timerMs: 5000f, fleeHp: 0.3f, reengage: 1f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.NpcAi.CombatState = HBAICombatState.Engage;
        npc.NpcAi.FleeUntilMs = 105_000L; // already fleeing, timer not yet expired
        npc.Firing = 1;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.5f);

        // speed 10 * dt 0.5 = 5 units toward home at (0,0,0): 100 -> 95.
        Assert.AreEqual(95f, npc.Position.X, 0.01f, "a fleeing NPC steers back toward its home anchor");
        Assert.AreEqual((byte)0, npc.Firing, "a fleeing NPC keeps its guns silent");
    }

    [TestMethod]
    public void Flee_ReengagesWhenHpAboveVal4_AtTimerExpiry()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        SetFleeVals(npc, timerMs: 5000f, fleeHp: 0.3f, reengage: 0.5f);
        SetHp(npc, maxHp: 100, currentHp: 80); // ratio 0.8 >= 0.5

        var (player, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);
        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Engage;
        npc.NpcAi.FleeUntilMs = 99_999L; // expired (< now)

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.AreEqual(0L, npc.NpcAi.FleeUntilMs, "recovering above val4 clears the flee latch");
        Assert.AreEqual(HBAICombatState.Combat, npc.NpcAi.CombatState, "a recovered NPC re-engages (Combat)");
    }

    [TestMethod]
    public void Flee_ContinuesWhenBelowVal4()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(100f, 0f, 0f), driverFaction: 3, visionRange: 60f, speed: 10f);
        SetFleeVals(npc, timerMs: 5000f, fleeHp: 0.3f, reengage: 0.5f);
        SetHp(npc, maxHp: 100, currentHp: 20); // ratio 0.2 < 0.5
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.NpcAi.CombatState = HBAICombatState.Engage;
        npc.NpcAi.FleeUntilMs = 99_999L; // expired

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.5f);

        Assert.AreEqual(105_000L, npc.NpcAi.FleeUntilMs, "still below val4: the flee timer re-extends");
        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState, "still fleeing (Engage wire state)");
        Assert.AreEqual(95f, npc.Position.X, 0.01f, "still running home while below the re-engage threshold");
    }

    // ----- call-for-help ------------------------------------------------------------------

    [TestMethod]
    public void Help_OnDamage_RollUnderVal6_AggrosSameFactionInVal7Radius()
    {
        var map = CreateFieldMap();
        NpcCombatAi.Rng = () => 0f; // roll 0 < val6

        var victim = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        SetHelpVals(victim, enabled: 1f, chance: 0.5f, range: 100f);
        var ally = PlaceNpcVehicle(map, new Vector3(50f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        var (attacker, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);

        victim.SetTargetObject(attacker);
        victim.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.OnDamaged(victim, attacker);

        Assert.AreSame(attacker, ally.Target, "help propagates the attacker to a same-faction idle ally in range");
        Assert.AreEqual(HBAICombatState.Engage, ally.NpcAi.CombatState, "an aggroed ally enters Engage");
        Assert.IsTrue(victim.NpcAi.HelpCalled, "help is marked called for this engagement");
    }

    [TestMethod]
    public void Help_DisabledProfile_NoPropagation()
    {
        var map = CreateFieldMap();
        NpcCombatAi.Rng = () => 0f;

        var victim = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        SetHelpVals(victim, enabled: 0f, chance: 0.5f, range: 100f); // help disabled
        var ally = PlaceNpcVehicle(map, new Vector3(50f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        var (attacker, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);

        victim.SetTargetObject(attacker);
        victim.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.OnDamaged(victim, attacker);

        Assert.IsNull(ally.Target, "a help-disabled profile spreads no aggro");
        Assert.AreEqual(HBAICombatState.IdlePatrol, ally.NpcAi.CombatState, "the ally stays idle");
        Assert.IsFalse(victim.NpcAi.HelpCalled, "help-disabled profile never consumes the help roll");
    }

    [TestMethod]
    public void Help_OnlyOncePerEngagement()
    {
        var map = CreateFieldMap();
        var rolls = 0;
        NpcCombatAi.Rng = () => { rolls++; return 0f; };

        var victim = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        SetHelpVals(victim, enabled: 1f, chance: 0.5f, range: 100f);
        var ally = PlaceNpcVehicle(map, new Vector3(50f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        var (attacker, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);

        victim.SetTargetObject(attacker);
        victim.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.OnDamaged(victim, attacker);
        NpcCombatAi.OnDamaged(victim, attacker);

        Assert.AreEqual(1, rolls, "help rolls exactly once per engagement");
        Assert.AreSame(attacker, ally.Target, "the ally is aggroed by the single help call");
    }

    // ----- damage-driven aggro ------------------------------------------------------------

    [TestMethod]
    public void TakeDamage_WithAttacker_AggrosIdleNpc()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        var (attacker, _) = PlacePlayerVehicle(map, new Vector3(500f, 0f, 0f), faction: 0); // far outside vision

        var actual = npc.TakeDamage(10, attacker);

        Assert.IsTrue(actual > 0, "damage must actually land to drive aggro");
        Assert.AreSame(attacker, npc.Target, "an idle NPC latches onto whoever damages it (aggro-list parity)");
        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState, "damage while idle enters Engage immediately");
    }

    // ----- helpers ------------------------------------------------------------------------

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 870,
            MapFileName = "tm_npc_flee_870",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private Vehicle PlaceNpcVehicle(
        SectorMap map,
        Vector3 position,
        int driverFaction,
        float visionRange,
        float speed = 5f)
    {
        var driverCbid = DriverCbid + (int)map.LocalCoidCounter;
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid);
        var driverSpec = AssetManager.Instance.GetCloneBase<CloneBaseCreature>(driverCbid).CreatureSpecific;
        driverSpec.VisionRange = visionRange;
        driverSpec.HearingRange = 0f;
        driverSpec.Speed = speed;

        var driver = new Creature();
        driver.LoadCloneBase(driverCbid);
        driver.SetCoid(map.LocalCoidCounter++, false);
        driver.Faction = driverFaction;

        var profile = new CreatureAiProfile { AiId = 1 };

        var vehicle = new Vehicle();
        vehicle.SetCoid(map.LocalCoidCounter++, false);
        vehicle.Position = position;
        vehicle.SetOwner(driver);
        vehicle.NpcAi = new NpcAiState { Profile = profile, HomePosition = position };
        vehicle.SetMap(map);
        return vehicle;
    }

    private (Vehicle Vehicle, TNLConnection Connection) PlacePlayerVehicle(SectorMap map, Vector3 position, int faction)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(map.LocalCoidCounter++, true);
        character.Faction = faction;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(map.LocalCoidCounter++, true);
        vehicle.Position = position;
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetMap(map);
        return (vehicle, connection);
    }

    private static void SetFleeVals(Vehicle npc, float timerMs, float fleeHp, float reengage)
    {
        var vals = npc.NpcAi.Profile.Vals;
        vals[0] = timerMs;  // val1 flee/engage timer
        vals[1] = 0f;       // val2 secondary flee band
        vals[2] = fleeHp;   // val3 primary flee HP band
        vals[3] = reengage; // val4 re-engage threshold
    }

    private static void SetHelpVals(Vehicle npc, float enabled, float chance, float range)
    {
        var vals = npc.NpcAi.Profile.Vals;
        vals[4] = enabled; // val5 help enable
        vals[5] = chance;  // val6 help chance
        vals[6] = range;   // val7 help range
    }

    private static void SetHp(Vehicle npc, int maxHp, int currentHp)
    {
        npc.ApplyTemplateBaseHp(maxHp);
        npc.SetHPForTests(currentHp);
    }

    private static void EquipFrontWeapon(Vehicle vehicle, float rangeMax)
    {
        var spec = new WeaponSpecific
        {
            RangeMin = 0f,
            RangeMax = rangeMax,
            RechargeTime = 1,
            DamageScalar = 1f,
            DmgMinMin = 1,
            DmgMaxMax = 2,
            MinMin = DamageSpecific.CreateEmpty(),
            MaxMax = DamageSpecific.CreateEmpty(),
        };
        var cloneBase = (CloneBaseWeapon)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseWeapon));
        cloneBase.WeaponSpecific = spec;
        cloneBase.SimpleObjectSpecific = new SimpleObjectSpecific();
        cloneBase.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 87_200, Type = (int)CloneBaseObjectType.Weapon };

        var weapon = new Weapon();
        weapon.SetCoid(9_998_001, false);
        typeof(ClonedObjectBase).GetProperty(nameof(ClonedObjectBase.CloneBaseObject))!
            .SetValue(weapon, cloneBase);

        vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponFront, weapon, out _);
    }
}
