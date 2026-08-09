using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// SS-36 entity-level defense-in-depth: ApplyWeaponHit must consult CombatEligibility.CanDamage
/// itself (it computes the effective faction from the live entities, so no call site can hand it
/// a wrong chassis faction the way the splash path did in RC2). Acquisition filtering alone is
/// not enough — a wrongly-acquired victim must still be refused at the point of damage.
/// </summary>
[TestClass]
public class VehicleWeaponHitEligibilityTests
{
    private const int Human = 0;
    private const int Mutant = 1;
    private const int Attempts = 200; // enough rng draws that "no damage" cannot be a run of misses

    [TestMethod]
    public void ApplyWeaponHit_SameFactionVehicle_NeverDamages()
    {
        var map = CreateTestMap(9701);
        var attacker = CreatePlayerVehicle(map, 9711, Human);
        var victim = CreatePlayerVehicle(map, 9713, Human);

        FireRepeatedly(attacker, victim, isSprayTarget: false);

        Assert.AreEqual(500, victim.GetCurrentHP(),
            "same-faction direct fire must be refused at the damage sink");
    }

    [TestMethod]
    public void ApplyWeaponHit_SplashContext_SameFactionVehicle_NeverDamages()
    {
        var map = CreateTestMap(9702);
        var attacker = CreatePlayerVehicle(map, 9721, Human);
        var victim = CreatePlayerVehicle(map, 9723, Human);

        FireRepeatedly(attacker, victim, isSprayTarget: true);

        Assert.AreEqual(500, victim.GetCurrentHP(),
            "same-faction splash (the RC2 friendly-fire symptom) must be refused at the damage sink");
    }

    [TestMethod]
    public void ApplyWeaponHit_CrossRaceVehicle_TakesDamage()
    {
        var map = CreateTestMap(9703);
        var attacker = CreatePlayerVehicle(map, 9731, Human);
        var victim = CreatePlayerVehicle(map, 9733, Mutant);

        FireRepeatedly(attacker, victim, isSprayTarget: false);

        Assert.IsTrue(victim.GetCurrentHP() < 500,
            "retail policy pin: cross-race players are mutually hostile — damage must land");
    }

    private static void FireRepeatedly(Vehicle attacker, Vehicle victim, bool isSprayTarget)
    {
        var spec = new WeaponSpecific
        {
            RangeMin = 0f,
            RangeMax = 100f,
            DamageScalar = 1f,
            DmgMinMin = 1,
            DmgMaxMax = 2,
            MinMin = DamageSpecific.CreateEmpty(),
            MaxMax = DamageSpecific.CreateEmpty(),
        };
        var rng = new Random(42);
        var packet = new DamagePacket();
        var victims = new List<ClonedObjectBase>();

        for (var i = 0; i < Attempts; i++)
        {
            attacker.ApplyWeaponHit(
                victim, spec,
                attackerLevel: 10, attackerClass: 1,
                combat: 50, theory: 50, atkPerception: 10,
                attackerChar: attacker.Owner?.GetAsCharacter(),
                rng, isSprayTarget, distFromPrimary: 0f,
                packet, victims);
        }
    }

    private static SectorMap CreateTestMap(int id) =>
        SectorMap.CreateForTests(
            new ContinentObject
            {
                Id = id,
                MapFileName = $"tm_weapon_hit_gate_{id}",
                DisplayName = "test",
            },
            new Vector4(0, 0, 0, 0));

    private static Vehicle CreatePlayerVehicle(SectorMap map, long coid, int faction)
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
        return vehicle;
    }
}
