using System.IO;
using System.Reflection;
using System.Text;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using Microsoft.EntityFrameworkCore;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Prefixes;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.Skills;

/// <summary>
/// Heavy regression coverage for skills, HP, and power modules (90% gate).
/// </summary>
[TestClass]
public class SkillsHpPowerRegressionTests
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
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        CharacterSkillService.PersistForTests = _ => { };
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestSkills();
        CharacterLevelManager.Instance.ClearAllForTests();
        SkillService.ClearCooldownsForTests();
        Vehicle.ClearCombatThrottleForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        CharacterSkillService.PersistForTests = null;
        _sent.Clear();
    }

    // ── CharacterLevelManager / power ─────────────────────────────────

    [TestMethod]
    public void CharacterLevelManager_GetOrCreate_DefaultsAndSetters()
    {
        var character = MakeCharacter(501);
        var state = CharacterLevelManager.Instance.GetOrCreate(501);
        Assert.AreEqual((short)10, state.CurrentMana);
        Assert.AreEqual((short)10, state.MaxMana);

        CharacterLevelManager.Instance.SetMaxMana(character, 80, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 40, sendPacket: false);
        Assert.AreEqual((40, 80), CharacterLevelManager.Instance.GetPower(501));

        CharacterLevelManager.Instance.SetCurrentMana(character, 999, sendPacket: false);
        Assert.AreEqual((short)80, CharacterLevelManager.Instance.GetCurrentMana(501));

        CharacterLevelManager.Instance.SetPower(character, 55, sendPacket: false);
        Assert.AreEqual((55, 55), CharacterLevelManager.Instance.GetPower(501));
    }

    [TestMethod]
    public void CharacterLevelManager_EnsurePowerPlantCapacity_SeedsFromPlant()
    {
        const int cbid = 720_501;
        AssetManagerTestHelper.RegisterPowerPlantCloneBase(cbid);
        var ppClone = (CloneBasePowerPlant)AssetManager.Instance.GetCloneBase(cbid)!;
        ppClone.PowerPlantSpecific.PowerMaximum = 120;

        var character = MakeCharacter(502);
        var vehicle = new Vehicle();
        vehicle.SetCoid(503, true);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetOwner(character);
        var plant = new PowerPlant();
        plant.SetCoid(504, true);
        plant.LoadCloneBase(cbid);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.PowerPlant, plant, out _));

        CharacterLevelManager.Instance.EnsurePowerPlantCapacity(character);
        var (cur, max) = CharacterLevelManager.Instance.GetPower(502);
        Assert.AreEqual((short)120, max);
        Assert.AreEqual((short)120, cur);
    }

    [TestMethod]
    public void CharacterLevelManager_BuildPacket_IncludesHpAndPower()
    {
        var character = MakeCharacter(505);
        var vehicle = new Vehicle();
        vehicle.SetCoid(506, true);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetMaximumHP(900, triggerGhostUpdate: false);
        vehicle.SetHPForTests(300);
        CharacterLevelManager.Instance.SetMaxMana(character, 70, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 35, sendPacket: false);

        var packet = CharacterLevelManager.Instance.BuildPacket(character);
        Assert.AreEqual(300, packet.Health);
        Assert.AreEqual(900, packet.HealthMaximum);
        Assert.AreEqual((short)35, packet.CurrentMana);
        Assert.AreEqual((short)70, packet.MaxMana);
        Assert.AreEqual(character.ObjectId.Coid, packet.CharacterId.Coid);
    }

    [TestMethod]
    public void CharacterLevelManager_SyncCurrentPowerGhost_DirtiesPowerMask()
    {
        var character = MakeCharacter(507);
        var vehicle = new Vehicle();
        vehicle.SetCoid(508, true);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetOwner(character);
        vehicle.CreateGhost();

        CharacterLevelManager.Instance.SyncCurrentPowerGhost(character);
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.PowerMask));
    }

    [TestMethod]
    public void CharacterLevelManager_Setters_SendPacketWhenConnectionPresent()
    {
        var character = MakeCharacter(509);
        var connection = new TNLConnection();
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        CharacterLevelManager.Instance.SetCurrentMana(character, 5, sendPacket: true);
        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());

        _sent.Clear();
        CharacterLevelManager.Instance.SetMaxMana(character, 50, sendPacket: true);
        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());

        _sent.Clear();
        CharacterLevelManager.Instance.SetPower(character, 22, sendPacket: true);
        Assert.IsTrue(_sent.OfType<CharacterLevelPacket>().Any());
    }

    // ── SimpleObject HP regression ────────────────────────────────────

    [TestMethod]
    public void SimpleObject_HpApi_ClampTakeDamageReviveCorpseClear()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(601, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);

        vehicle.SetCurrentHP(250, triggerGhostUpdate: false);
        Assert.AreEqual(200, vehicle.GetCurrentHP());

        vehicle.SetCurrentHP(-5, triggerGhostUpdate: false);
        Assert.AreEqual(0, vehicle.GetCurrentHP());

        vehicle.SetCurrentHP(50, triggerGhostUpdate: false);
        var dmg = vehicle.TakeDamage(30);
        Assert.AreEqual(30, dmg);
        Assert.AreEqual(20, vehicle.GetCurrentHP());

        vehicle.SetCurrentHP(0, triggerGhostUpdate: false);
        vehicle.OnDeath(DeathType.Silent);
        Assert.IsTrue(vehicle.IsCorpse);
        Assert.AreEqual(0, vehicle.TakeDamage(10), "corpse takes no damage");

        vehicle.SetCurrentHP(80);
        Assert.IsFalse(vehicle.IsCorpse);
        Assert.AreEqual(80, vehicle.GetCurrentHP());

        vehicle.SetCurrentHP(0, triggerGhostUpdate: false);
        vehicle.OnDeath(DeathType.Silent);
        vehicle.Revive();
        Assert.IsFalse(vehicle.IsCorpse);
        Assert.IsTrue(vehicle.GetCurrentHP() >= 1);

        vehicle.SetMaximumHP(50, triggerGhostUpdate: false);
        vehicle.SetHPForTests(50);
        vehicle.SetMaximumHP(30, triggerGhostUpdate: false);
        Assert.AreEqual(30, vehicle.GetMaximumHP());
        Assert.AreEqual(30, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void SimpleObject_WriteToPacket_RequiresCloneBase()
    {
        var obj = new PowerPlant();
        obj.SetCoid(602, true);
        Assert.ThrowsException<InvalidOperationException>(() =>
            obj.WriteToPacket(new CreatePowerPlantPacket()));
    }

    // ── VehicleCombatPool remaining paths ─────────────────────────────

    [TestMethod]
    public void CombatPool_WeaponsFiring_HalvesCoolRate()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHeat(200);
        vehicle.SetCurrentHeat(100, triggerGhostUpdate: false);
        vehicle.SetCoolRateForTests(10);

        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: true);
        Assert.AreEqual(95, vehicle.CurrentHeat, "10 cool rate / 2 while firing");
    }

    [TestMethod]
    public void CombatPool_ShieldRegen_AndPowerFullNoOp()
    {
        var character = MakeCharacter(701);
        CharacterLevelManager.Instance.SetMaxMana(character, 20, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 20, sendPacket: false);

        var vehicle = new Vehicle();
        vehicle.SetCoid(702, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetMaximumShield(50);
        vehicle.SetCurrentShield(0, triggerGhostUpdate: false);
        vehicle.SetShieldRegenRateForTests(5);
        vehicle.SetPowerRegenRateForTests(3);
        vehicle.CreateGhost();

        // Debounce arm + countdown
        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);
        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);
        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);
        Assert.AreEqual(5, vehicle.CurrentShield);
        Assert.AreEqual((short)20, CharacterLevelManager.Instance.GetCurrentMana(701));
    }

    [TestMethod]
    public void CombatPool_Advance_ZeroDelta_NoOp()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHeat(100);
        vehicle.SetCurrentHeat(50, triggerGhostUpdate: false);
        vehicle.SetCoolRateForTests(5);
        VehicleCombatPool.Advance(vehicle, null, 0, false);
        Assert.AreEqual(50, vehicle.CurrentHeat);
    }

    // ── SkillService extra paths ──────────────────────────────────────

    [TestMethod]
    public void SkillService_NullCharacter_AndNoMap_AndWrongTarget()
    {
        Assert.IsFalse(SkillService.TryCastPlayer(null, 1, 1, new TFID { Coid = 1 }, default));

        var character = MakeCharacter(801);
        var vehicle = new Vehicle();
        vehicle.SetCoid(802, true);
        character.SetCurrentVehicleForTests(vehicle);
        // no map
        Assert.IsFalse(SkillService.TryCastPlayer(character, 1, 1, new TFID { Coid = 1 }, default, out var status));
        Assert.AreEqual(SkillResponse.Status, status);

        RegisterDamageSkill(8103, 5, 5, 0, 100, 0, 0);
        var (player, playerVehicle, map) = CreatePlayer(810, 811);
        Assert.IsFalse(SkillService.TryCastPlayer(
            player, 8103, 1, new TFID { Coid = 99999 }, default, out var wrong));
        Assert.AreEqual(SkillResponse.WrongTarget, wrong);
        _ = playerVehicle;
        _ = map;
    }

    [TestMethod]
    public void SkillService_HealNoEffect_AndCorpseTarget_AndReactionHeal()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 8201,
            Name = "fullheal",
            Elements = new List<SkillElement>
            {
                new() { ElementType = SkillElementTypes.Heal, ValueBase = 50 },
                new() { ElementType = SkillElementTypes.Range, ValueBase = 100 },
            }
        });
        var (character, vehicle, _) = CreatePlayer(820, 821);
        vehicle.SetMaximumHP(100, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);
        Assert.IsFalse(SkillService.TryCastPlayer(character, 8201, 1, vehicle.ObjectId, vehicle.Position));

        RegisterDamageSkill(8202, 50, 50, 0, 100, 0, 0);
        var map = vehicle.Map!;
        var corpse = CreateTarget(map, 822, 1, 1);
        corpse.SetCurrentHP(0, triggerGhostUpdate: false);
        corpse.OnDeath(DeathType.Silent);
        Assert.IsFalse(SkillService.TryCastPlayer(character, 8202, 1, corpse.ObjectId, corpse.Position, out var corpseResp));
        Assert.AreEqual(SkillResponse.Corpse, corpseResp);

        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 8203,
            Name = "pad",
            Elements = new List<SkillElement>
            {
                new() { ElementType = SkillElementTypes.Heal, ValueBase = 25 },
            }
        });
        vehicle.SetHPForTests(10);
        Assert.IsTrue(SkillService.TryCastReaction(vehicle, 8203, 1));
        Assert.IsTrue(vehicle.GetCurrentHP() > 10);
        Assert.IsFalse(SkillService.TryCastReaction(null, 8203, 1));
        Assert.IsFalse(SkillService.TryCastReaction(vehicle, 0, 1));
        Assert.IsFalse(SkillService.TryCastReaction(vehicle, 999888, 1));
    }

    [TestMethod]
    public void SkillService_ZeroCost_DoesNotRequirePowerPool()
    {
        RegisterDamageSkill(8301, 5, 5, 0, 100, 0, 0);
        var (character, vehicle, map) = CreatePlayer(830, 831);
        var target = CreateTarget(map, 832, 100, 100);
        target.Position = new Vector3(5, 0, 0);
        Assert.IsTrue(SkillService.TryCastPlayer(character, 8301, 1, target.ObjectId, target.Position));
        Assert.AreEqual(95, target.GetCurrentHP());
        _ = vehicle;
    }

    // ── CharacterSkillService ─────────────────────────────────────────

    [TestMethod]
    public void CharacterSkillService_IncrementQuickBarResetPoints()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 9001,
            Name = "learnable",
            MinimumLevel = 1,
            MaxSkillLevel = 3,
            SkillPrerequisite1 = 0,
        });
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 9002,
            Name = "needs_prereq",
            MinimumLevel = 1,
            MaxSkillLevel = 1,
            SkillPrerequisite1 = 9001,
        });
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 9003,
            Name = "high_level",
            MinimumLevel = 50,
            MaxSkillLevel = 1,
        });

        var character = MakeCharacter(901);
        character.SetLevel(5);
        character.SetSkillPoints(3);

        Assert.IsFalse(CharacterSkillService.Instance.TryIncrement(null, 9001, out _));
        Assert.IsFalse(CharacterSkillService.Instance.TryIncrement(character, 9999, out var unknown));
        Assert.AreEqual("Unknown skill.", unknown);

        character.SetSkillPoints(0);
        Assert.IsFalse(CharacterSkillService.Instance.TryIncrement(character, 9001, out var noPts));
        Assert.AreEqual("No skill points available.", noPts);

        character.SetSkillPoints(2);
        Assert.IsFalse(CharacterSkillService.Instance.TryIncrement(character, 9003, out var lowLvl));
        Assert.AreEqual("Character level is too low.", lowLvl);

        Assert.IsFalse(CharacterSkillService.Instance.TryIncrement(character, 9002, out var prereq));
        Assert.AreEqual("Skill prerequisite is not learned.", prereq);

        Assert.IsTrue(CharacterSkillService.Instance.TryIncrement(character, 9001, out _));
        Assert.AreEqual(1, character.LearnedSkills[9001]);
        Assert.AreEqual((short)1, character.SkillPoints);

        character.LearnedSkills[9001] = 3;
        character.SetSkillPoints(1);
        Assert.IsFalse(CharacterSkillService.Instance.TryIncrement(character, 9001, out var maxed));
        Assert.AreEqual("Skill is already at maximum rank.", maxed);

        Assert.IsFalse(CharacterSkillService.Instance.TryUpdateQuickBar(character, -1, -1, 0, out var badSlot));
        Assert.AreEqual("Invalid quick-bar slot.", badSlot);
        Assert.IsFalse(CharacterSkillService.Instance.TryUpdateQuickBar(character, 0, -1, 8888, out var notLearned));
        Assert.AreEqual("Skill is not learned.", notLearned);
        Assert.IsFalse(CharacterSkillService.Instance.TryUpdateQuickBar(character, 0, 12345, 0, out var noItem));
        Assert.AreEqual("Item is not in cargo.", noItem);

        Assert.IsTrue(CharacterSkillService.Instance.TryUpdateQuickBar(character, 0, -1, 9001, out _));
        Assert.AreEqual(9001, character.QuickBarSkills[0]);
        Assert.AreEqual(-1L, character.QuickBarItemCoids[0]);

        // Skill place must clear any prior item in the same slot (exclusive).
        character.QuickBarItemCoids[0] = 555;
        Assert.IsTrue(CharacterSkillService.Instance.TryUpdateQuickBar(character, 0, -1, 9001, out _));
        Assert.AreEqual(9001, character.QuickBarSkills[0]);
        Assert.AreEqual(-1L, character.QuickBarItemCoids[0]);

        // Item place clears skill; skillId < 0 normalizes to clear.
        Assert.IsTrue(CharacterSkillService.Instance.TryUpdateQuickBar(character, 0, -1, -1, out _));
        Assert.AreEqual(0, character.QuickBarSkills[0]);

        // itemCoid 0 normalizes to empty (-1).
        character.QuickBarItemCoids[1] = 42;
        Assert.IsTrue(CharacterSkillService.Instance.TryUpdateQuickBar(character, 1, 0, 0, out _));
        Assert.AreEqual(-1L, character.QuickBarItemCoids[1]);

        // Successful item place requires cargo membership and clears skill.
        character.LearnedSkills[9001] = 1;
        character.QuickBarSkills[2] = 9001;
        character.Inventory.LoadItems(new[]
        {
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "consumable", 0xBEEF, 0, 0, 1),
        });
        Assert.IsTrue(CharacterSkillService.Instance.TryUpdateQuickBar(character, 2, 0xBEEF, 0, out _));
        Assert.AreEqual(0xBEEFL, character.QuickBarItemCoids[2]);
        Assert.AreEqual(0, character.QuickBarSkills[2]);

        // Null character rejected.
        Assert.IsFalse(CharacterSkillService.Instance.TryUpdateQuickBar(null, 0, -1, 0, out var nullChar));
        Assert.AreEqual("Invalid quick-bar slot.", nullChar);

        // Slot 100 rejected.
        Assert.IsFalse(CharacterSkillService.Instance.TryUpdateQuickBar(character, 100, -1, 0, out var highSlot));
        Assert.AreEqual("Invalid quick-bar slot.", highSlot);

        CharacterSkillService.Instance.SetPoints(character, 9);
        Assert.AreEqual((short)9, character.SkillPoints);

        CharacterSkillService.Instance.Reset(character);
        Assert.AreEqual(0, character.LearnedSkills.Count);
        Assert.AreEqual(0, character.QuickBarSkills[0]);
    }

    // ── Binary / packet / structure coverage ──────────────────────────

    [TestMethod]
    public void SkillElement_And_SkillSet_And_Skill_ReadRoundTrip()
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(42); // skillId
            w.Write(10); // elementType
            w.Write((byte)1); // equation
            w.Write(new byte[3]);
            w.Write(0.25f);
            w.Write(0.05f);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var el = SkillElement.ReadNew(r);
            Assert.AreEqual(42, el.SkillId);
            Assert.AreEqual(10, el.ElementType);
            Assert.AreEqual(1, el.EquationType);
            Assert.AreEqual(0.25f, el.ValueBase);
            Assert.AreEqual(0.05f, el.ValuePerLevel);
        }

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(77);
            w.Write((ushort)100);
            w.Write((ushort)50);
            w.Write((ushort)2);
            w.Write(true);
            w.Write((byte)3);
            w.Write(10);
            w.Write(90);
            w.Write(1.5f);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var set = SkillSet.Read(r);
            Assert.AreEqual(77, set.SkillId);
            Assert.AreEqual((ushort)100, set.PauseTime);
            Assert.IsTrue(set.StopsToAttack);
            Assert.AreEqual("Id: 77", set.ToString());
        }

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(1001); // Id
            w.Write(0); // Class
            w.Write(0); // Race
            w.Write(0); w.Write(0); w.Write(0); // target*
            w.Write(0); w.Write(0); w.Write(0); // affected*
            w.Write(0); // status
            w.Write(0); w.Write(0); w.Write(0); // prereq
            w.Write((byte)1); w.Write((byte)2); w.Write((byte)1); w.Write((byte)0); // tree/line/min/type
            WriteUtf16Fixed(w, "TestSkill", 33);
            WriteUtf16Fixed(w, "A skill", 1025);
            WriteUtf16Fixed(w, "test_skill", 65);
            w.Write(new byte[2]);
            w.Write(0); w.Write(0); // chain/spray
            w.Write((byte)0); w.Write((byte)5); // optional/max
            w.Write(new byte[2]);
            w.Write(0); // useBody
            w.Write(0); w.Write(0); w.Write(0); // group/cat/summon
            w.Write(0); w.Write(0); w.Write(0); w.Write(0); // optional skills
            w.Write((short)1); // num elements
            w.Write(new byte[2]);
            // one element
            w.Write(1001);
            w.Write(10);
            w.Write((byte)0);
            w.Write(new byte[3]);
            w.Write(10f);
            w.Write(0f);

            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var skill = Skill.Read(r);
            Assert.AreEqual(1001, skill.Id);
            Assert.AreEqual("TestSkill", skill.Name);
            Assert.AreEqual(1, skill.Elements.Count);
            Assert.AreEqual(10, skill.Elements[0].ElementType);
        }
    }

    [TestMethod]
    public void Packets_SkillIncrement_Cancel_CreateHeartbeat_RoundTrip()
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(0x11223344);
            w.Write(2103);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var packet = new SkillIncrementPacket();
            packet.Read(r);
            Assert.AreEqual(2103, packet.SkillId);
            Assert.AreEqual(GameOpcode.SkillIncrement, packet.Opcode);
        }

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            Assert.AreEqual(GameOpcode.CancelSkill, new CancelSkillPacket().Opcode);
            w.Write(0); // pad4
            w.WriteTFID(new TFID { Coid = 9, Global = true });
            w.Write(55);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var p = new CancelSkillPacket();
            p.Read(r);
            Assert.AreEqual(9L, p.Target.Coid);
            Assert.AreEqual(55, p.SkillId);
        }

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            var hb = new CreateSkillHeartbeat
            {
                LastTickCount = 1,
                DiceSeed = 2,
                SkillId = 3,
                SkillLevel = 4,
                Target = new TFID { Coid = 10, Global = false },
                ForceDeath = true,
                SkillType = 8,
                DurationCountdown = 9,
                Caster = new TFID { Coid = 11, Global = true },
            };
            hb.Write(w);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var hb2 = new CreateSkillHeartbeat();
            hb2.Read(r);
            Assert.AreEqual(hb.SkillId, hb2.SkillId);
            Assert.AreEqual(hb.Target.Coid, hb2.Target.Coid);
            Assert.AreEqual(hb.Caster.Coid, hb2.Caster.Coid);
            Assert.AreEqual(hb.ForceDeath, hb2.ForceDeath);
            Assert.AreEqual(GameOpcode.CreateSkillHeartbeat, hb.Opcode);
        }
    }

    [TestMethod]
    public void PowerPlantSpecific_AndPrefix_AndCloneBase_AndCreatePacket()
    {
        var specific = new PowerPlantSpecific
        {
            HeatMaximum = 12,
            PowerMaximum = 30,
            PowerRegenRate = 12,
            CoolRate = 9,
        };
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            specific.Write(w);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var again = PowerPlantSpecific.ReadNew(r);
            Assert.AreEqual(12, again.HeatMaximum);
            Assert.AreEqual(30, again.PowerMaximum);
            Assert.AreEqual((short)12, again.PowerRegenRate);
            Assert.AreEqual((short)9, again.CoolRate);
        }

        // PrefixPowerPlant binary (PrefixBase + specific fields)
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(1); // Id
            w.Write(10); // ObjectType
            w.Write(1f); // ValuePercent
            w.Write(0); // IsComponent
            w.Write(1f); // Rarity
            w.Write(0); // Race
            w.Write(0); // Class
            WriteUtf16Fixed(w, "pref", 51);
            w.Write(new byte[2]);
            w.Write(1f); // MassPercent
            w.Write(0); // Skill
            for (var i = 0; i < 5; i++) w.Write(-1); // ingredients
            w.Write(100); // BaseValue
            w.Write(0); // IsGadgetOnly
            w.Write((short)0); // LevelOffset
            w.Write(new byte[2]);
            w.Write(0f); // AttributeRequirementIncrease
            w.Write((short)0); w.Write((short)0); w.Write((short)0); w.Write((short)0);
            w.Write((short)0); // ItemRarity
            w.Write(new byte[2]);
            w.Write(0); // Complexity
            w.Write(1); // IsPrefix
            WriteUtf16Fixed(w, "P", 33);
            w.Write(new byte[2]);
            // PrefixPowerPlant tail
            w.Write(0.1f); // HeatPercent
            w.Write(1); // HeadAdjust
            w.Write(0.2f); // PowerPercent
            w.Write(2); // PowerAdjust
            w.Write(0.3f); // CoolingRatePercent
            w.Write(3); // CoolingRateAdjust
            w.Write(0.4f); // PowerRegenRatePercent
            w.Write(4); // PowerRegenRateAdjust
            w.Write(0.5f); // CoolDownPercent
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var prefix = new PrefixPowerPlant(r);
            Assert.AreEqual(1, prefix.Id);
            Assert.AreEqual(0.4f, prefix.PowerRegenRatePercent);
            Assert.AreEqual(4, prefix.PowerRegenRateAdjust);
            Assert.AreEqual(0.5f, prefix.CoolDownPercent);
        }

        // CloneBasePowerPlant full binary ctor (CloneBase + SimpleObjectSpecific + PowerPlantSpecific)
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(2800);
            w.Write((int)CloneBaseObjectType.PowerPlant);
            w.Write(0);
            WriteUtf16Fixed(w, "pp", 65);
            WriteUtf16Fixed(w, "short", 65);
            WriteUtf16Fixed(w, "long", 257);
            WriteUtf16Fixed(w, "fx", 65);
            w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);
            w.Write(0); w.Write(0);
            w.Write(1u);
            for (var i = 0; i < 11; i++) w.Write(0); // Armor..RequiredClass
            w.Write(1.5f);
            w.Write(1f);
            w.Write(1f);
            w.Write((short)1);
            w.Write((short)0);
            w.Write((short)0);
            w.Write((short)10);
            w.Write((short)20);
            w.Write((short)0);
            w.Write((short)0);
            w.Write((short)0); w.Write((short)0); w.Write((short)0); w.Write((short)0);
            w.Write((byte)1); w.Write((byte)1);
            w.Write((byte)0); w.Write((byte)0);
            WriteUtf16Fixed(w, "phys", 65);
            for (var i = 0; i < 6; i++) w.Write((short)0); // DamageSpecific
            for (var i = 0; i < 5; i++) w.Write(0); // ingredients
            w.Write(0); w.Write(0); // discipline
            w.Write((short)0); // MaximumGadgets
            w.Write((short)0); // RaceShieldRegenerate
            w.Write((short)0); // ItemRarity
            w.Write((ushort)1); // StackSize
            w.Write((ushort)0); // MaxUses
            w.Write(false); // IsNotTradeable
            w.Write(false); // DropBrokenOnly
            // PowerPlantSpecific
            w.Write(12); // HeatMaximum
            w.Write(30); // PowerMaximum
            w.Write((short)12); // PowerRegenRate
            w.Write((short)9); // CoolRate
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var clone = new CloneBasePowerPlant(r);
            Assert.AreEqual(2800, clone.CloneBaseSpecific.CloneBaseId);
            Assert.AreEqual(30, clone.PowerPlantSpecific.PowerMaximum);
            Assert.AreEqual((short)12, clone.PowerPlantSpecific.PowerRegenRate);
            Assert.AreEqual(1.5f, clone.SimpleObjectSpecific.Mass);
        }

        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            CreatePowerPlantPacket.WriteEmptyPacket(w);
            Assert.IsTrue(ms.Length > 100);
        }

        const int cbid = 720_900;
        AssetManagerTestHelper.RegisterPowerPlantCloneBase(cbid, mass: 3.5f);
        var plant = new PowerPlant();
        plant.SetCoid(910, true);
        plant.LoadCloneBase(cbid);
        var packet = new CreatePowerPlantPacket();
        plant.WriteToPacket(packet);
        Assert.IsNotNull(packet.PowerPlantSpecific);
        Assert.AreEqual(3.5f, packet.Mass);
        Assert.AreEqual(1.0f, packet.SkillCooldown);
        Assert.AreEqual(GameOpcode.CreatePowerPlant, packet.Opcode);
    }

    [TestMethod]
    public void SkillElementTypes_And_SkillResponse_ConstantsStable()
    {
        Assert.AreEqual(1, SkillElementTypes.Cost);
        Assert.AreEqual(10, SkillElementTypes.Heal);
        Assert.AreEqual(0xFFFF, SkillElementTypes.ChannelMask);
        Assert.AreEqual((byte)0, (byte)SkillResponse.Ok);
        Assert.AreEqual((byte)4, (byte)SkillResponse.Power);
        Assert.AreEqual((byte)13, (byte)SkillResponse.OutOfRange);
    }

    [TestMethod]
    public void PowerPlant_LoadFromDB_And_SimpleObjectGhostWrite()
    {
        const int cbid = 720_910;
        AssetManagerTestHelper.RegisterPowerPlantCloneBase(cbid, mass: 2.25f);
        var dbName = "pp-load-" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<CharContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using (var ctx = new CharContext(options))
        {
            ctx.Database.EnsureCreated();
            var row = new SimpleObjectData
            {
                CBID = cbid,
                Type = (byte)CloneBaseObjectType.PowerPlant,
            };
            ctx.SimpleObjects.Add(row);
            ctx.SaveChanges();
            var seededCoid = row.Coid;
            Assert.IsTrue(seededCoid != 0 || ctx.SimpleObjects.Any(),
                "InMemory must persist a simple_object row");
            ctx.ChangeTracker.Clear();

            var plant = new PowerPlant();
            Assert.IsTrue(plant.LoadFromDB(ctx, seededCoid),
                $"LoadFromDB must resolve seeded coid={seededCoid}");
            Assert.AreEqual(cbid, plant.CBID);
            Assert.IsNotNull(plant.CloneBasePowerPlant);

            // Exercise SimpleObject.LoadFromDB + SetupCBFields (PowerPlant override skips SetupCBFields).
            var simple = new SimpleObject(GraphicsObjectType.Graphics);
            Assert.IsTrue(simple.LoadFromDB(ctx, seededCoid));
            simple.SetupCBFields();

            var missing = new PowerPlant();
            Assert.IsFalse(missing.LoadFromDB(ctx, long.MaxValue));
        }

        // SimpleObject ghost + WriteToPacket after clonebase load
        var so = new PowerPlant();
        so.SetCoid(99102, true);
        so.LoadCloneBase(cbid);
        // Registered test clonebase defaults MaxHitPoint=0; set a real pool for HP dirty tests.
        so.SetMaximumHP(100, triggerGhostUpdate: false);
        so.SetCurrentHP(50, triggerGhostUpdate: false);
        so.CreateGhost();
        so.CreateGhost(); // no-op second call
        Assert.IsNotNull(so.Ghost);
        ClearGhostDirtyMask(so.Ghost!);
        so.SetCurrentHP(15);
        Assert.IsTrue(GhostHasDirtyMask(so.Ghost!, GhostObject.HealthMask));

        var packet = new CreatePowerPlantPacket();
        so.WriteToPacket(packet);
        Assert.AreEqual(cbid, packet.CBID);
        Assert.AreEqual(15, packet.CurrentHealth);
        Assert.AreEqual(100, packet.MaximumHealth);
    }

    private static void ClearGhostDirtyMask(NetObject ghost)
    {
        var field = typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(ghost, 0UL);
    }

    [TestMethod]
    public void SimpleObject_InvincibleAndMaxHpNoChangePaths()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(99110, true);
        vehicle.SetInvincible(true);
        vehicle.SetHPForTests(50);
        Assert.AreEqual(0, vehicle.TakeDamage(10));
        vehicle.SetInvincible(false);

        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(200);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: true); // same max early return
        Assert.AreEqual(200, vehicle.GetMaximumHP());

        vehicle.SetMaximumHP(100, triggerGhostUpdate: false); // clamp HP without ghost
        Assert.AreEqual(100, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void CombatPool_HeatAtMaxDebounce_AndOverheatCool_AndZeroCoolRate()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHeat(100);
        vehicle.SetCoolRateForTests(10);
        vehicle.SetCurrentHeat(100, triggerGhostUpdate: false); // arms debounce to 2
        Assert.AreEqual(2, vehicle.HeatAtMaxDebounce);

        VehicleCombatPool.Tick(vehicle, null, false); // 2 -> 1
        Assert.AreEqual(100, vehicle.CurrentHeat);
        VehicleCombatPool.Tick(vehicle, null, false); // 1 -> 0 then cools
        Assert.IsTrue(vehicle.CurrentHeat < 100);

        vehicle.SetCurrentHeat(150, triggerGhostUpdate: false); // over max
        vehicle.SetCoolRateForTests(10);
        var before = vehicle.CurrentHeat;
        VehicleCombatPool.Tick(vehicle, null, false);
        Assert.AreEqual(before - 7, vehicle.CurrentHeat, "overheat cool uses 70% rate");

        vehicle.SetCurrentHeat(40, triggerGhostUpdate: false);
        vehicle.SetCoolRateForTests(0);
        VehicleCombatPool.Tick(vehicle, null, false);
        Assert.AreEqual(40, vehicle.CurrentHeat);

        vehicle.SetCoolRateForTests(1);
        VehicleCombatPool.Tick(vehicle, null, weaponsFiring: true);
        Assert.AreEqual(40, vehicle.CurrentHeat, "cool 1 while firing floors to 0 delta");
    }

    [TestMethod]
    public void CombatPool_ShieldFull_ClearsDebounce()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumShield(20);
        vehicle.SetCurrentShield(20, triggerGhostUpdate: false);
        vehicle.SetShieldRegenRateForTests(5);
        vehicle.ShieldEmptyDebounce = 2;
        VehicleCombatPool.Tick(vehicle, null, false);
        Assert.AreEqual(0, vehicle.ShieldEmptyDebounce);
        Assert.AreEqual(20, vehicle.CurrentShield);
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static void WriteUtf16Fixed(BinaryWriter w, string value, int charCount)
    {
        var bytes = new byte[charCount * 2];
        var encoded = Encoding.Unicode.GetBytes(value);
        Array.Copy(encoded, bytes, Math.Min(encoded.Length, bytes.Length - 2));
        w.Write(bytes);
    }

    private static Character MakeCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        var dbData = new CharacterData { Coid = coid, Name = "Regress", Level = 1, SkillPoints = 0 };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
    }

    private static void RegisterDamageSkill(
        int id, float min, float max, float pen, float range, float cooldownMs, float cost)
    {
        const int energy = 22;
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = id,
            Name = "dmg",
            Elements = new List<SkillElement>
            {
                new() { ElementType = SkillElementTypes.FlagDamageMin | energy, ValueBase = min },
                new() { ElementType = SkillElementTypes.FlagDamageMax | energy, ValueBase = max },
                new() { ElementType = SkillElementTypes.PenetrationDamageAdd, ValueBase = pen },
                new() { ElementType = SkillElementTypes.Range, ValueBase = range },
                new() { ElementType = SkillElementTypes.CoolDown, ValueBase = cooldownMs },
                new() { ElementType = SkillElementTypes.Cost, ValueBase = cost },
            }
        });
    }

    private static (Character Character, Vehicle Vehicle, AutoCore.Game.Map.SectorMap Map) CreatePlayer(
        long characterCoid, long vehicleCoid)
    {
        var map = AutoCore.Game.Map.SectorMap.CreateForTests(new ContinentObject
        {
            Id = 1999,
            MapFileName = "tm_regress",
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

    private static bool GhostHasDirtyMask(NetObject ghost, ulong mask)
    {
        var field = typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic);
        var bits = (ulong)field!.GetValue(ghost)!;
        return (bits & mask) != 0;
    }
}
