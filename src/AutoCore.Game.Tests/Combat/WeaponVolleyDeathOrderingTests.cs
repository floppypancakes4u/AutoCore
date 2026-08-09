using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

using System.Runtime.CompilerServices;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// SS-37 tripwires: ApplyWeaponHit called OnDeath inline, so the killing blow's removal packet
/// (InitCreateObject doDeath for props, DestroyObject for NPCs) was sent BEFORE the volley's
/// DamagePacket flush at the end of ProcessCombatInternal. Same ordered connection ⇒ the client
/// tears the object down first, then cannot resolve the damage entry's TFID and silently drops
/// it (client FUN_00812a60) — no damage number for the killing blow. Most scenery is soft
/// (dies on hit one), so props "very often" showed no numbers while destruction always played.
/// Deaths must drain AFTER the damage packet flush.
/// </summary>
[TestClass]
public class WeaponVolleyDeathOrderingTests
{
    private const int ContId = 9971;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
        MapPropCorpseDespawn.ResetForTests();
        Vehicle.ClearCombatThrottleForTests();
        ServerConfig.ResetToDefaults();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
        MapPropCorpseDespawn.ResetForTests();
        Vehicle.ClearCombatThrottleForTests();
        ServerConfig.ResetToDefaults();
    }

    [TestMethod]
    public void LethalVolleyOnProp_SendsDamagePacketBeforeRemovalPacket()
    {
        var (shooter, map) = CreateShooter();
        var prop = CreateSoftProp(map, coid: 77001, position: new Vector3(0f, 0f, 10f));

        shooter.ProcessCombatIfFiring();

        var damageIdx = _sent.FindIndex(p => p is DamagePacket dp
            && dp.Entries.Any(e => e.Target.Coid == prop.ObjectId.Coid));
        var removalIdx = _sent.FindIndex(p => p is InitCreateObjectPacket);
        Assert.IsTrue(prop.IsCorpse, "precondition: the soft prop must die to the volley");
        Assert.IsTrue(removalIdx >= 0, "prop death must broadcast InitCreateObject doDeath");
        Assert.IsTrue(damageIdx >= 0, "the killing blow must produce a damage entry");
        Assert.IsTrue(damageIdx < removalIdx,
            "the DamagePacket must be flushed BEFORE the removal packet, or the client " +
            "can no longer resolve the victim TFID and drops the floater (SS-37)");
    }

    [TestMethod]
    public void LethalVolleyOnNpcVehicle_SendsDamagePacketBeforeDestroyPacket()
    {
        var (shooter, map) = CreateShooter();
        shooter.SetCombatRngForTests(new AlwaysHitRandom());
        var victim = CreateNpcVehicle(map, coid: 77011, position: new Vector3(0f, 0f, 10f), hp: 1);

        shooter.ProcessCombatIfFiring();

        var damageIdx = _sent.FindIndex(p => p is DamagePacket dp
            && dp.Entries.Any(e => e.Target.Coid == victim.ObjectId.Coid));
        var destroyIdx = _sent.FindIndex(p => p is DestroyObjectPacket dop
            && dop.ObjectId.Coid == victim.ObjectId.Coid);
        Assert.IsTrue(victim.IsCorpse, "precondition: the 1-HP NPC vehicle must die to the volley");
        Assert.IsTrue(destroyIdx >= 0, "NPC vehicle death must broadcast DestroyObject");
        Assert.IsTrue(damageIdx >= 0, "the killing blow must produce a damage entry");
        Assert.IsTrue(damageIdx < destroyIdx,
            "the DamagePacket must be flushed BEFORE DestroyObject (SS-37)");
    }

    /// <summary>Deferring OnDeath must not lose murderer credit or corpse state (green pin).</summary>
    [TestMethod]
    public void LethalVolleyOnProp_StillSetsMurdererAndCorpse()
    {
        var (shooter, map) = CreateShooter();
        var prop = CreateSoftProp(map, coid: 77021, position: new Vector3(0f, 0f, 10f));

        shooter.ProcessCombatIfFiring();

        Assert.IsTrue(prop.IsCorpse);
        Assert.AreEqual(shooter.ObjectId.Coid, prop.Murderer.Coid,
            "murderer must be stamped before OnDeath for loot/XP attribution");
    }

    /// <summary>A victim at 0 HP takes no further damage in the same volley (pin).</summary>
    [TestMethod]
    public void LethalVolley_ProducesExactlyOneDamageEntryForTheVictim()
    {
        var (shooter, map) = CreateShooter();
        var prop = CreateSoftProp(map, coid: 77031, position: new Vector3(0f, 0f, 10f));

        shooter.ProcessCombatIfFiring();

        var entries = _sent.OfType<DamagePacket>()
            .SelectMany(dp => dp.Entries)
            .Count(e => e.Target.Coid == prop.ObjectId.Coid);
        Assert.AreEqual(1, entries, "a dead-but-pending victim must not be re-hit in the volley");
    }

    // ----- helpers -------------------------------------------------------------------------

    private static (Vehicle Shooter, SectorMap Map) CreateShooter()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_volley_order_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(77101, true);
        character.Faction = 0;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var shooter = new Vehicle();
        shooter.SetCoid(77102, true);
        shooter.InitializeHealthForTests(500);
        shooter.Position = new Vector3(0f, 0f, 0f);
        shooter.Rotation = new Quaternion(0f, 0f, 0f, 1f); // yaw 0 → forward +Z
        character.SetCurrentVehicleForTests(shooter);
        character.SetMap(map);
        shooter.SetMap(map);

        EquipFrontWeapon(shooter, rangeMax: 50f);
        shooter.CreateGhost(); // ProcessCombatIfFiring requires a ghost
        shooter.Firing = 1;
        return (shooter, map);
    }

    /// <summary>Soft destructible scenery: exact GraphicsObject, no clonebase, 1 HP.</summary>
    private static GraphicsObject CreateSoftProp(SectorMap map, long coid, Vector3 position)
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.SetCoid(coid, false);
        prop.InitializeHealthForTests(1);
        prop.Position = position;
        prop.SetMap(map);
        return prop;
    }

    private static Vehicle CreateNpcVehicle(SectorMap map, long coid, Vector3 position, int hp)
    {
        var driver = new Creature();
        driver.SetCoid(coid - 1, false);
        driver.Faction = 21;

        var victim = new Vehicle();
        victim.SetCoid(coid, false);
        victim.InitializeHealthForTests(hp);
        victim.Position = position;
        victim.SetOwner(driver);
        victim.NpcAi = new NpcAiState { Profile = new CreatureAiProfile { AiId = 1 } };
        victim.SetMap(map);
        return victim;
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
        cloneBase.CloneBaseSpecific = new CloneBaseSpecific
        {
            CloneBaseId = 9_888_001,
            Type = (int)CloneBaseObjectType.Weapon,
        };

        var weapon = new Weapon();
        weapon.SetCoid(9_888_101, false);
        typeof(ClonedObjectBase).GetProperty(nameof(ClonedObjectBase.CloneBaseObject))!
            .SetValue(weapon, cloneBase);

        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponFront, weapon, out _),
            "failed to equip front weapon");
    }

    /// <summary>Deterministic rolls: always hit, minimum damage.</summary>
    private sealed class AlwaysHitRandom : Random
    {
        public override double NextDouble() => 0.0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override int Next(int maxValue) => 0;
    }
}
