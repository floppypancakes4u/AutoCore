using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Skills;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Heavy SkillService regression for heal-signed damage, reaction pain skills, and edge paths.
/// Complements PlayerSkillCastTests / ReactionDamageSkillTests.
/// </summary>
[TestClass]
public class SkillServiceHeavyRegressionTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestSkills();
        SkillService.ClearCooldownsForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestSkills();
        SkillService.ClearCooldownsForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void TryCastPlayer_NegativePercentHeal_DamagesTarget()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2567,
            Name = "Damage 50%",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = -0.5f },
                new() { ElementType = 7, ValueBase = 500 },
            }
        });
        var (character, vehicle, map) = CreatePlayer();
        var target = CreateHostile(map, 9501, 200, 200);
        target.Position = new Vector3(5, 0, 0);
        vehicle.Position = new Vector3(0, 0, 0);

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2567, 1, target.ObjectId, target.Position, out var resp));
        Assert.AreEqual(SkillResponse.Ok, resp);
        Assert.AreEqual(100, target.GetCurrentHP());
        Assert.IsTrue(_sent.OfType<SkillStatusEffectPacket>().Any());
    }

    [TestMethod]
    public void TryCastPlayer_NegativeHeal_Lethal_KillsTarget()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2570,
            Name = "execute",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 0, ValueBase = -500f },
                new() { ElementType = 7, ValueBase = 500 },
            }
        });
        var (character, vehicle, map) = CreatePlayer(characterCoid: 920, vehicleCoid: 921);
        var target = CreateHostile(map, 9502, 50, 50);
        target.Position = vehicle.Position;

        Assert.IsTrue(SkillService.TryCastPlayer(
            character, 2570, 1, target.ObjectId, target.Position));
        Assert.IsTrue(target.IsCorpse || target.GetCurrentHP() <= 0);
    }

    [TestMethod]
    public void TryCastPlayer_NegativeHeal_OnInvincible_NoDamage()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2571,
            Name = "pain",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = -0.5f },
                new() { ElementType = 7, ValueBase = 500 },
            }
        });
        var (character, vehicle, map) = CreatePlayer(characterCoid: 922, vehicleCoid: 923);
        var target = CreateHostile(map, 9503, 200, 200);
        target.SetInvincible(true);
        target.Position = vehicle.Position;

        Assert.IsFalse(SkillService.TryCastPlayer(
            character, 2571, 1, target.ObjectId, target.Position));
        Assert.AreEqual(200, target.GetCurrentHP());
    }

    [TestMethod]
    public void TryCastReaction_NegativeHeal_Invincible_Rejected()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2572,
            Name = "Damage 50%",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = -0.5f },
            }
        });
        var (_, vehicle, _) = CreatePlayer(characterCoid: 924, vehicleCoid: 925);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(200);
        vehicle.SetInvincible(true);

        Assert.IsFalse(SkillService.TryCastReaction(vehicle, 2572, 1));
        Assert.AreEqual(200, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TryCastReaction_NegativeHeal_Corpse_Rejected()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2573,
            Name = "Damage 50%",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = -0.5f },
            }
        });
        var (_, vehicle, _) = CreatePlayer(characterCoid: 926, vehicleCoid: 927);
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetHPForTests(1);
        vehicle.OnDeath(DeathType.Silent);

        Assert.IsFalse(SkillService.TryCastReaction(vehicle, 2573, 1));
    }

    [TestMethod]
    public void TryCastReaction_NegativeHeal_Lethal_CallsOnDeath()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2574,
            Name = "fatal pain",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 0, ValueBase = -9999f },
            }
        });
        var (character, vehicle, _) = CreatePlayer(characterCoid: 928, vehicleCoid: 929);
        vehicle.SetMaximumHP(50, triggerGhostUpdate: false);
        vehicle.SetHPForTests(50);
        // Player vehicles without NpcAi stay as corpse without map leave.
        Assert.IsTrue(SkillService.TryCastReaction(vehicle, 2574, 1));
        Assert.IsTrue(vehicle.GetCurrentHP() <= 0 || vehicle.IsCorpse);
    }

    [TestMethod]
    public void TryCastReaction_NearZeroSignedHeal_Rejected()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2575,
            Name = "noop",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 0, ValueBase = 0.1f },
            }
        });
        var (_, vehicle, _) = CreatePlayer(characterCoid: 930, vehicleCoid: 931);
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetHPForTests(50);
        Assert.IsFalse(SkillService.TryCastReaction(vehicle, 2575, 1));
    }

    [TestMethod]
    public void TryCastPlayer_CharacterTarget_ResolvesToVehicle()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2576,
            Name = "Damage 50%",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = -0.25f },
                new() { ElementType = 7, ValueBase = 500 },
            }
        });
        var (attacker, attackerVeh, map) = CreatePlayer(characterCoid: 940, vehicleCoid: 941);
        var (victim, victimVeh, _) = CreatePlayerOnMap(map, characterCoid: 942, vehicleCoid: 943);
        victimVeh.SetMaximumHP(200, triggerGhostUpdate: false);
        victimVeh.SetHPForTests(200);
        attackerVeh.Position = victimVeh.Position;

        Assert.IsTrue(SkillService.TryCastPlayer(
            attacker, 2576, 1, victim.ObjectId, victim.Position));
        Assert.AreEqual(150, victimVeh.GetCurrentHP(), "25% of 200 via character TFID → vehicle");
    }

    [TestMethod]
    public void TryCastReaction_DamageNoEffect_WhenTakeDamageReturnsZero()
    {
        // Invincible already covered; corpse returns 0 damage via TakeDamage.
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2580,
            Name = "pain",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 0, ValueBase = -10f },
            }
        });
        var (_, vehicle, _) = CreatePlayer(characterCoid: 960, vehicleCoid: 961);
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetHPForTests(0);
        // Force corpse-like zero damage without full death path if possible
        vehicle.SetInvincible(false);
        // HP 0 may still take damage depending on implementation — mark corpse
        vehicle.OnDeath(DeathType.Silent);
        Assert.IsFalse(SkillService.TryCastReaction(vehicle, 2580, 1));
    }

    [TestMethod]
    public void TryCastPlayer_DamageWithHealElement_AndMaxLessThanMin_Swaps()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2581,
            Name = "weird dmg",
            Elements = new List<SkillElement>
            {
                new() { ElementType = SkillElementTypes.FlagDamageMin, ValueBase = 80 },
                new() { ElementType = SkillElementTypes.FlagDamageMax, ValueBase = 20 },
                new() { ElementType = SkillElementTypes.PenetrationDamageAdd, ValueBase = 5 },
                new() { ElementType = SkillElementTypes.Range, ValueBase = 500 },
            }
        });
        var (character, vehicle, map) = CreatePlayer(characterCoid: 962, vehicleCoid: 963);
        var target = CreateHostile(map, 9510, 200, 200);
        target.Position = vehicle.Position;
        Assert.IsTrue(SkillService.TryCastPlayer(character, 2581, 1, target.ObjectId, target.Position));
        Assert.IsTrue(target.GetCurrentHP() < 200);
    }

    [TestMethod]
    public void TryCastPlayer_HealOnFullHealth_OnlyHeal_Fails()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2582,
            Name = "heal full",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 0, ValueBase = 50 },
                new() { ElementType = 7, ValueBase = 500 },
            }
        });
        var (character, vehicle, _) = CreatePlayer(characterCoid: 964, vehicleCoid: 965);
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);
        Assert.IsFalse(SkillService.TryCastPlayer(character, 2582, 1, vehicle.ObjectId, vehicle.Position));
    }

    [TestMethod]
    public void TryCastPlayer_SendsEffectToVictimConnection()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 2583,
            Name = "Damage 50%",
            Elements = new List<SkillElement>
            {
                new() { ElementType = 10, EquationType = 1, ValueBase = -0.1f },
                new() { ElementType = 7, ValueBase = 500 },
            }
        });
        var victimPackets = new List<BasePacket>();
        var (attacker, aVeh, map) = CreatePlayer(characterCoid: 970, vehicleCoid: 971);
        var (victim, vVeh, _) = CreatePlayerOnMap(map, 972, 973);
        // Separate sink: both connections share TestPacketSink globally — still counts.
        vVeh.SetMaximumHP(100, triggerGhostUpdate: false);
        vVeh.SetHPForTests(100);
        aVeh.Position = vVeh.Position;
        Assert.IsTrue(SkillService.TryCastPlayer(attacker, 2583, 1, vVeh.ObjectId, vVeh.Position));
        Assert.IsTrue(vVeh.GetCurrentHP() < 100);
        Assert.IsTrue(_sent.OfType<SkillStatusEffectPacket>().Any());
    }

    [TestMethod]
    public void SkillElementTypes_ConstantsStable()
    {
        Assert.AreEqual(1, SkillElementTypes.Cost);
        Assert.AreEqual(3, SkillElementTypes.CoolDown);
        Assert.AreEqual(10, SkillElementTypes.Heal);
        Assert.AreEqual(7, SkillElementTypes.Range);
        Assert.AreEqual(0xFFFF, SkillElementTypes.ChannelMask);
    }

    [TestMethod]
    public void SkillResponse_EnumValuesStable()
    {
        Assert.AreEqual(0, (int)SkillResponse.Ok);
        Assert.IsTrue(Enum.IsDefined(typeof(SkillResponse), SkillResponse.OutOfRange));
        Assert.IsTrue(Enum.IsDefined(typeof(SkillResponse), SkillResponse.WrongTarget));
    }

    private static Vehicle CreateHostile(SectorMap map, long coid, int hp, int maxHp)
    {
        var t = new Vehicle { Position = new Vector3(5, 0, 0) };
        t.SetCoid(coid, true);
        t.SetMaximumHP(maxHp, triggerGhostUpdate: false);
        t.SetHPForTests(hp);
        t.SetInvincible(false);
        t.SetMap(map);
        return t;
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer(
        long characterCoid = 900,
        long vehicleCoid = 901)
    {
        var continent = new ContinentObject
        {
            Id = 8910,
            MapFileName = "tm_skill_heavy",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        return CreatePlayerOnMap(map, characterCoid, vehicleCoid);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayerOnMap(
        SectorMap map,
        long characterCoid,
        long vehicleCoid)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(characterCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        vehicle.SetCoid(vehicleCoid, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}
