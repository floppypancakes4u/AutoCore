using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Skills;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

[TestClass]
public class PlayerSkillCastTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = (_, packet) =>
        {
            lock (_sent)
                _sent.Add(packet);
        };
        CharacterLevelManager.Instance.ClearAllForTests();
        SkillService.ClearCooldownsForTests();
        Vehicle.ClearCombatThrottleForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestSkills();
        CharacterLevelManager.Instance.ClearAllForTests();
        SkillService.ClearCooldownsForTests();
        Vehicle.ClearCombatThrottleForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void TryCastPlayer_DamageSkill_ReducesTargetHpAndSendsPackets()
    {
        RegisterDamageSkill(id: 2103, min: 20, max: 20, pen: 5, range: 100, cooldownMs: 14000, cost: 10,
            chargeMs: 900);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 900, vehicleCoid: 901);
        var target = CreateTarget(map, coid: 950, hp: 100, maxHp: 100);
        vehicle.Position = new Vector3(0, 0, 0);
        target.Position = new Vector3(10, 0, 0);

        var ok = SkillService.TryCastPlayer(
            character,
            skillId: 2103,
            skillLevel: 2,
            targetId: target.ObjectId,
            targetPosition: target.Position);

        Assert.IsTrue(ok);
        Assert.AreEqual(75, target.GetCurrentHP(), "20 rolled + 5 pen should deal 25 damage");
        var effect = _sent.OfType<SkillStatusEffectPacket>().Single();
        Assert.AreEqual(2103, effect.SkillId);
        Assert.AreEqual((short)2, effect.SkillLevel);
        Assert.AreEqual(character.ObjectId.Coid, effect.Caster.Coid,
            "SkillStatusEffect +0x28 is the owning character TFID; the client resolves its vehicle");
        Assert.AreEqual((byte)0, effect.Flag, "learned skills must use the client learned-skill lifecycle");
        Assert.AreEqual(target.ObjectId.Coid, effect.Targets.Single().Target.Coid);
        Assert.AreEqual((short)0, effect.Targets.Single().Mana);
        Assert.AreEqual((short)0, effect.Targets.Single().MaxMana);
        Assert.AreEqual(0, effect.ApplyPower,
            "the authoritative effect is already applied, so no client cast delay remains");
        var effectIndex = _sent.FindIndex(packet => packet is SkillStatusEffectPacket);
        var damageIndex = _sent.FindIndex(packet => packet is DamagePacket);
        Assert.IsTrue(damageIndex >= 0, "Damage floater should be sent");
        Assert.IsTrue(effectIndex < damageIndex,
            "non-lethal casts must launch VFX before their damage just like lethal casts");
    }

    [TestMethod]
    public void TryCastPlayer_LethalDamage_SendsEffectBeforeDestroyingTarget()
    {
        RegisterDamageSkill(id: 2103, min: 20, max: 20, pen: 5, range: 100,
            cooldownMs: 14000, cost: 0, chargeMs: 900);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 910, vehicleCoid: 911);
        var target = CreateTarget(map, coid: 951, hp: 12, maxHp: 12);
        target.Position = new Vector3(10, 0, 0);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2103, 2, target.ObjectId, target.Position));

        var effectIndex = _sent.FindIndex(packet => packet is SkillStatusEffectPacket);
        var damageIndex = _sent.FindIndex(packet => packet is DamagePacket);
        var destroyIndex = _sent.FindIndex(packet => packet is DestroyObjectPacket);
        Assert.IsTrue(effectIndex >= 0);
        Assert.IsTrue(damageIndex < 0 || effectIndex < damageIndex,
            "the cast VFX must launch before the client processes its damage");
        Assert.IsTrue(destroyIndex < 0 || effectIndex < destroyIndex,
            "the client must receive the effect while its target still exists");
    }

    [TestMethod]
    public void TryCastPlayer_ObjectTarget_UsesResolvedTargetPositionForVfx()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100,
            cooldownMs: 0, cost: 0);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 912, vehicleCoid: 913);
        var target = CreateTarget(map, coid: 952, hp: 100, maxHp: 100);
        vehicle.Position = new Vector3(1, 2, 3);
        target.Position = new Vector3(20, 30, 40);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2103, 1, target.ObjectId, vehicle.Position));

        var effect = _sent.OfType<SkillStatusEffectPacket>().Single();
        Assert.AreEqual(target.Position.X, effect.PosX);
        Assert.AreEqual(target.Position.Y, effect.PosY);
        Assert.AreEqual(target.Position.Z, effect.PosZ);
        Assert.AreEqual(character.ObjectId, effect.Caster);
        Assert.AreEqual(target.ObjectId, effect.Targets.Single().Target);
    }

    [TestMethod]
    public void TryCastPlayer_CharacterTarget_PreservesSelectedTfidForVfxAndDamagesVehicle()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100,
            cooldownMs: 0, cost: 0);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 914, vehicleCoid: 915);
        var selectedCharacter = new Character();
        selectedCharacter.SetCoid(952, true);
        selectedCharacter.SetMap(map);
        var targetVehicle = CreateTarget(map, coid: 953, hp: 100, maxHp: 100);
        selectedCharacter.SetCurrentVehicleForTests(targetVehicle);
        vehicle.Position = new Vector3(0, 0, 0);
        targetVehicle.Position = new Vector3(10, 0, 0);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2103, 1, selectedCharacter.ObjectId, targetVehicle.Position));

        Assert.AreEqual(95, targetVehicle.GetCurrentHP());
        var effect = _sent.OfType<SkillStatusEffectPacket>().Single();
        Assert.AreEqual(character.ObjectId, effect.Caster);
        Assert.AreEqual(selectedCharacter.ObjectId, effect.Targets.Single().Target,
            "retail preserves the selected TFID for animation while applying combat to its vehicle body");
    }

    [TestMethod]
    public void TryCastPlayer_PlayerTarget_ReplicatesSameIdentityContractToBothOwners()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100,
            cooldownMs: 0, cost: 0);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 920, vehicleCoid: 921);
        var victimConnection = new TNLConnection();
        var victimCharacter = new Character();
        victimCharacter.SetCoid(930, true);
        victimCharacter.SetOwningConnection(victimConnection);
        victimConnection.CurrentCharacter = victimCharacter;
        victimCharacter.SetMap(map);
        var victimVehicle = CreateTarget(map, coid: 931, hp: 100, maxHp: 100);
        victimCharacter.SetCurrentVehicleForTests(victimVehicle);
        victimVehicle.Position = new Vector3(12, 0, 0);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2103, 1, victimCharacter.ObjectId, vehicle.Position));

        var effects = _sent.OfType<SkillStatusEffectPacket>().ToArray();
        Assert.AreEqual(2, effects.Length, "caster and victim owners must both observe the cast");
        foreach (var effect in effects)
        {
            Assert.AreEqual(character.ObjectId, effect.Caster);
            Assert.AreEqual(victimCharacter.ObjectId, effect.Targets.Single().Target);
            Assert.AreEqual(victimVehicle.Position.X, effect.PosX);
            Assert.AreEqual(victimVehicle.Position.Y, effect.PosY);
            Assert.AreEqual(victimVehicle.Position.Z, effect.PosZ);
            Assert.AreEqual((byte)0, effect.Flag);
            Assert.AreEqual((byte)SkillResponse.Ok, effect.Status);
            Assert.AreEqual(0, effect.ApplyPower);
        }
    }

    [TestMethod]
    public void TryCastPlayer_HealSkill_RestoresTargetHpAndSendsEffect()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 666,
            Name = "Repair (Self)",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 0, ValueBase = 40, ValuePerLevel = 10 },
                new() { ElementType = 7, ValueBase = 100 },
                new() { ElementType = 3, ValueBase = 1000 },
            }
        });
        var (character, vehicle, map) = CreatePlayer(characterCoid: 910, vehicleCoid: 911);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(50);
        vehicle.Position = new Vector3(0, 0, 0);

        var ok = SkillService.TryCastPlayer(
            character,
            skillId: 666,
            skillLevel: 2,
            targetId: vehicle.ObjectId,
            targetPosition: vehicle.Position);

        Assert.IsTrue(ok);
        Assert.AreEqual(110, vehicle.GetCurrentHP(), "retail evaluates 40 + 10*rank");
        var effect = _sent.OfType<SkillStatusEffectPacket>().Single();
        Assert.AreEqual(666, effect.SkillId);
        Assert.AreEqual(vehicle.ObjectId.Coid, effect.Targets.Single().Target.Coid);
        Assert.AreEqual((short)10, effect.Targets.Single().Mana);
        Assert.AreEqual((short)10, effect.Targets.Single().MaxMana);
    }

    [TestMethod]
    public void TryCastPlayer_PercentHealEquation_UsesMaximumHealth()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 857,
            Name = "percent heal",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = 0.15f },
                new() { ElementType = 7, ValueBase = 100 },
            }
        });
        var (character, vehicle, _) = CreatePlayer(characterCoid: 920, vehicleCoid: 921);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(10);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 857, 1, vehicle.ObjectId, vehicle.Position));

        Assert.AreEqual(40, vehicle.GetCurrentHP(), "15% of 200 max = 30 restored");
    }

    [TestMethod]
    public void TryCastPlayer_UnknownSkill_ReturnsFalse()
    {
        var (character, vehicle, map) = CreatePlayer(characterCoid: 930, vehicleCoid: 931);
        var target = CreateTarget(map, coid: 951, hp: 100, maxHp: 100);

        Assert.IsFalse(SkillService.TryCastPlayer(
            character, 999999, 1, target.ObjectId, target.Position));
        Assert.AreEqual(100, target.GetCurrentHP());
        Assert.IsFalse(_sent.OfType<SkillStatusEffectPacket>().Any());
    }

    [TestMethod]
    public void TryCastPlayer_NoSupportedEffect_ReturnsFalse()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2000,
            Name = "passive-ish",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 43, ValueBase = 1 },
                new() { ElementType = 5, ValueBase = -1 },
            }
        });
        var (character, vehicle, map) = CreatePlayer(characterCoid: 940, vehicleCoid: 941);
        var target = CreateTarget(map, coid: 952, hp: 100, maxHp: 100);

        Assert.IsFalse(SkillService.TryCastPlayer(
            character, 2000, 1, target.ObjectId, target.Position));
        Assert.AreEqual(100, target.GetCurrentHP());
    }

    [TestMethod]
    public void TryCastPlayer_OutOfRange_ReturnsFalseWithoutDamage()
    {
        RegisterDamageSkill(id: 2103, min: 50, max: 50, pen: 0, range: 20, cooldownMs: 1000, cost: 1);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 950, vehicleCoid: 951);
        var target = CreateTarget(map, coid: 953, hp: 100, maxHp: 100);
        vehicle.Position = new Vector3(0, 0, 0);
        target.Position = new Vector3(100, 0, 0);

        Assert.IsFalse(SkillService.TryCastPlayer(
            character, 2103, 1, target.ObjectId, target.Position, out var response));
        Assert.AreEqual(SkillResponse.OutOfRange, response);
        Assert.AreEqual(100, target.GetCurrentHP());
    }

    [TestMethod]
    public void TryCastPlayer_CooldownBlocksSecondCast()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100, cooldownMs: 14000, cost: 1);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 960, vehicleCoid: 961);
        var target = CreateTarget(map, coid: 954, hp: 100, maxHp: 100);
        vehicle.Position = new Vector3(0, 0, 0);
        target.Position = new Vector3(5, 0, 0);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2103, 1, target.ObjectId, target.Position));
        Assert.IsFalse(SkillService.TryCastPlayer(
            character, 2103, 1, target.ObjectId, target.Position, out var response));
        Assert.AreEqual(SkillResponse.Recharge, response);
        Assert.AreEqual(95, target.GetCurrentHP(), "only first cast should apply damage");
        Assert.AreEqual(9, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid),
            "rejected duplicate must not spend power again");
        Assert.AreEqual(1, _sent.OfType<SkillStatusEffectPacket>().Count(),
            "service emits only the accepted effect; handler owns rejection synchronization");
        Assert.AreEqual(1, _sent.OfType<DamagePacket>().Count());
    }

    [TestMethod]
    public void TryCastPlayer_ConcurrentDuplicate_ReservesCooldownBeforePowerDeduction()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100,
            cooldownMs: 14000, cost: 21, costPerLevel: 24);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 965, vehicleCoid: 966);
        var target = CreateTarget(map, coid: 959, hp: 100, maxHp: 100);
        CharacterLevelManager.Instance.SetMaxMana(character, 200, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 200, sendPacket: false);
        target.Position = new Vector3(5, 0, 0);
        var results = new bool[2];

        Parallel.Invoke(
            () => results[0] = SkillService.TryCastPlayer(character, 2103, 2, target.ObjectId, target.Position),
            () => results[1] = SkillService.TryCastPlayer(character, 2103, 2, target.ObjectId, target.Position));

        Assert.AreEqual(1, results.Count(result => result));
        Assert.AreEqual(131, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid));
    }

    [TestMethod]
    public void TryCastPlayer_SpendsPowerWhenPoolCanAffordCost()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100, cooldownMs: 0,
            cost: 21, costPerLevel: 24);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 970, vehicleCoid: 971);
        var target = CreateTarget(map, coid: 955, hp: 100, maxHp: 100);
        CharacterLevelManager.Instance.SetMaxMana(character, 100, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 100, sendPacket: false);
        vehicle.Position = new Vector3(0, 0, 0);
        target.Position = new Vector3(5, 0, 0);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2103, 2, target.ObjectId, target.Position));
        Assert.AreEqual(31, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid));
        Assert.IsFalse(_sent.OfType<CharacterLevelPacket>().Any(),
            "the client spends skill power optimistically; an immediate absolute sync double-depletes its HUD");
    }

    [TestMethod]
    public void TryCastPlayer_CostExceedsAvailablePower_IsRejected()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100, cooldownMs: 0, cost: 45);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 980, vehicleCoid: 981);
        var target = CreateTarget(map, coid: 956, hp: 100, maxHp: 100);
        // Default mana is 10/10 — cost 45 exceeds MaxMana, so power gate is skipped.
        vehicle.Position = new Vector3(0, 0, 0);
        target.Position = new Vector3(5, 0, 0);

        Assert.IsFalse(SkillService.TryCastPlayer(
            character, 2103, 1, target.ObjectId, target.Position, out var response));
        Assert.AreEqual(SkillResponse.Power, response);
        Assert.AreEqual(100, target.GetCurrentHP());
        Assert.AreEqual(10, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid));
    }

    [TestMethod]
    public void TryCastPlayer_InsufficientPowerWhenPoolWired_ReturnsFalse()
    {
        RegisterDamageSkill(id: 2103, min: 5, max: 5, pen: 0, range: 100, cooldownMs: 0, cost: 40);
        var (character, vehicle, map) = CreatePlayer(characterCoid: 990, vehicleCoid: 991);
        var target = CreateTarget(map, coid: 957, hp: 100, maxHp: 100);
        CharacterLevelManager.Instance.SetMaxMana(character, 100, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 10, sendPacket: false);
        vehicle.Position = new Vector3(0, 0, 0);
        target.Position = new Vector3(5, 0, 0);

        Assert.IsFalse(SkillService.TryCastPlayer(
            character, 2103, 1, target.ObjectId, target.Position));
        Assert.AreEqual(100, target.GetCurrentHP());
        Assert.AreEqual(10, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid));
    }

    private static void RegisterDamageSkill(
        int id, float min, float max, float pen, float range, float cooldownMs, float cost,
        float chargeMs = 0, float costPerLevel = 0)
    {
        // Energy channel (22) with min/max damage flags (esetFlagDamageMin/Max).
        const int energy = 22;
        const int flagDamageMin = 65536;
        const int flagDamageMax = 131072;
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = id,
            Name = "test_damage",
            CategoryId = 3,
            Elements = new List<SkillElement>
            {
                new() { ElementType = flagDamageMin | energy, ValueBase = min },
                new() { ElementType = flagDamageMax | energy, ValueBase = max },
                new() { ElementType = 68, ValueBase = pen },
                new() { ElementType = 7, ValueBase = range },
                new() { ElementType = 3, ValueBase = cooldownMs },
                new() { ElementType = 6, ValueBase = chargeMs },
                new() { ElementType = 1, ValueBase = cost, ValuePerLevel = costPerLevel },
            }
        });
    }

    private static (Character Character, Vehicle Vehicle, AutoCore.Game.Map.SectorMap Map) CreatePlayer(
        long characterCoid, long vehicleCoid)
    {
        var map = AutoCore.Game.Map.SectorMap.CreateForTests(new ContinentObject
        {
            Id = 1987,
            MapFileName = "tm_player_skill",
            DisplayName = "test",
            IsPersistent = true,
        }, new Vector4());
        var connection = new TNLConnection();
        var character = new Character();
        character.SetCoid(characterCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle();
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }

    private static Vehicle CreateTarget(AutoCore.Game.Map.SectorMap map, long coid, int hp, int maxHp)
    {
        var target = new Vehicle();
        target.SetCoid(coid, true);
        target.SetMap(map);
        target.SetMaximumHP(maxHp, triggerGhostUpdate: false);
        target.SetHPForTests(hp);
        return target;
    }
}
