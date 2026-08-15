using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using System.Collections.Generic;
using System.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

[TestClass]
public class CreatureSkillCombatTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) =>
        {
            lock (_sent)
                _sent.Add(packet);
        };
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        SkillService.ClearCooldownsForTests();
        Vehicle.ClearCombatThrottleForTests();
        AssetManager.Instance.SetTestCreatureAiProfiles(null);
        NpcTurretLos.TryHasClearLos = null;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        SkillService.ClearCooldownsForTests();
        Vehicle.ClearCombatThrottleForTests();
        AssetManager.Instance.ClearTestSkills();
        AssetManager.Instance.SetTestCreatureAiProfiles(null);
        NpcTurretLos.TryHasClearLos = null;
        _sent.Clear();
    }

    [TestMethod]
    public void HumanTurret_DamagesMutantInRange_IgnoresHuman()
    {
        var map = CreateFieldMap();
        RegisterPlasmaSkill();
        var turret = PlaceTurret(map, new Vector3(0f, 0f, 0f), faction: 0);

        var (mutant, _) = PlacePlayerVehicle(map, new Vector3(10f, 0f, 0f), faction: 1);
        mutant.ApplyTemplateBaseHp(1000);
        mutant.SetHPForTests(1000);

        FireUntilCombat(map, turret, nowMs: 10_000);
        Assert.IsTrue(mutant.GetCurrentHP() < 1000, "Human turret must damage a Mutant in range");

        SkillService.ClearCooldownsForTests();
        turret.NpcAi.LastSkillFireMs = 0;
        var (human, _) = PlacePlayerVehicle(map, new Vector3(8f, 0f, 0f), faction: 0);
        human.ApplyTemplateBaseHp(1000);
        human.SetHPForTests(1000);
        turret.SetTargetObject(human);
        turret.NpcAi.CombatState = HBAICombatState.Combat;
        NpcCombatAi.Tick(map, turret, nowMs: 20_000, dt: 0.1f);
        Assert.AreEqual(1000, human.GetCurrentHP(), "Human turret must not damage a Human");
    }

    [TestMethod]
    public void HumanTurret_OutOfRange_DoesNotDamage()
    {
        var map = CreateFieldMap();
        RegisterPlasmaSkill();
        var turret = PlaceTurret(map, new Vector3(0f, 0f, 0f), faction: 0);
        var (mutant, _) = PlacePlayerVehicle(map, new Vector3(200f, 0f, 0f), faction: 1);
        mutant.ApplyTemplateBaseHp(1000);
        mutant.SetHPForTests(1000);

        turret.SetTargetObject(mutant);
        turret.NpcAi.CombatState = HBAICombatState.Combat;
        NpcCombatAi.Tick(map, turret, nowMs: 10_000, dt: 0.1f);
        Assert.AreEqual(1000, mutant.GetCurrentHP());
    }

    [TestMethod]
    public void HumanTurret_RespectsCooldown()
    {
        var map = CreateFieldMap();
        RegisterPlasmaSkill();
        var turret = PlaceTurret(map, new Vector3(0f, 0f, 0f), faction: 0);
        var (mutant, _) = PlacePlayerVehicle(map, new Vector3(10f, 0f, 0f), faction: 1);
        mutant.ApplyTemplateBaseHp(1000);
        mutant.SetHPForTests(1000);

        turret.SetTargetObject(mutant);
        turret.NpcAi.CombatState = HBAICombatState.Combat;
        NpcCombatAi.Tick(map, turret, nowMs: 10_000, dt: 0.1f);
        var afterFirst = mutant.GetCurrentHP();
        Assert.IsTrue(afterFirst < 1000);
        NpcCombatAi.Tick(map, turret, nowMs: 10_100, dt: 0.1f);
        Assert.AreEqual(afterFirst, mutant.GetCurrentHP(), "second tick inside 4s CD must not fire");
    }

    /// <summary>
    /// Retail <c>IsEnemy</c> / FindTargetToAttack: Human (0) vs Wildlife (10) is hostile.
    /// Guard turrets use the same creature scan (layer 17 includes unpossessed creatures).
    /// </summary>
    [TestMethod]
    public void HumanTurret_AcquiresWildlifeWalker()
    {
        var map = CreateFieldMap();
        RegisterPlasmaSkill();
        var turret = PlaceTurret(map, new Vector3(0f, 0f, 0f), faction: 0);
        var walker = PlaceWildlifeWalker(map, new Vector3(10f, 0f, 0f), faction: 10);

        FireUntilCombat(map, turret, nowMs: 10_000);

        Assert.AreSame(walker, turret.Target,
            "human turret must auto-acquire wildlife (faction 10) in the open");
        Assert.IsTrue(walker.GetCurrentHP() < 1000, "turret must shoot the wildlife walker");
    }

    /// <summary>
    /// Turrets must not lock a target they cannot see (invis-physics / hull between them).
    /// </summary>
    [TestMethod]
    public void HumanTurret_DoesNotAcquireTargetWithoutLos()
    {
        var map = CreateFieldMap();
        RegisterPlasmaSkill();
        NpcTurretLos.TryHasClearLos = (_, _, _) => false;
        var turret = PlaceTurret(map, new Vector3(0f, 0f, 0f), faction: 0);
        var walker = PlaceWildlifeWalker(map, new Vector3(10f, 0f, 0f), faction: 10);

        FireUntilCombat(map, turret, nowMs: 10_000);

        Assert.IsNull(turret.Target, "turret must skip candidates with no line of sight");
        Assert.AreEqual(1000, walker.GetCurrentHP());
    }

    /// <summary>
    /// A turret that already has a target must not fire through a wall.
    /// </summary>
    [TestMethod]
    public void HumanTurret_DoesNotFireAtLockedTargetWithoutLos()
    {
        var map = CreateFieldMap();
        RegisterPlasmaSkill();
        var turret = PlaceTurret(map, new Vector3(0f, 0f, 0f), faction: 0);
        var walker = PlaceWildlifeWalker(map, new Vector3(10f, 0f, 0f), faction: 10);
        turret.SetTargetObject(walker);
        turret.NpcAi.CombatState = HBAICombatState.Combat;
        NpcTurretLos.TryHasClearLos = (_, _, _) => false;

        NpcCombatAi.Tick(map, turret, nowMs: 10_000, dt: 0.1f);

        Assert.AreEqual(1000, walker.GetCurrentHP(), "turret must not shoot without LOS");
    }

    /// <summary>
    /// Turrets are FAM-local and have no OwningConnection. When they shoot another NPC the
    /// only observer is a nearby player — SkillStatusEffect must still reach that client so
    /// caster vfunc+0x238(4) can play the muzzle / projectile on the turret.
    /// </summary>
    [TestMethod]
    public void HumanTurret_FiresAtNpc_SendsSkillFxToNearbyPlayer()
    {
        var map = CreateFieldMap();
        RegisterPlasmaSkill();
        var turret = PlaceTurret(map, new Vector3(0f, 0f, 0f), faction: 0);
        PlacePlayerVehicle(map, new Vector3(30f, 0f, 0f), faction: 0, putCharacterOnMap: true);
        var npc = PlaceNpcVictim(map, new Vector3(10f, 0f, 0f), faction: 1);

        turret.SetTargetObject(npc);
        turret.NpcAi.CombatState = HBAICombatState.Combat;
        NpcCombatAi.Tick(map, turret, nowMs: 10_000, dt: 0.1f);

        Assert.IsTrue(npc.GetCurrentHP() < 1000, "turret must still damage the NPC");
        var effect = _sent.OfType<SkillStatusEffectPacket>().SingleOrDefault();
        Assert.IsNotNull(effect,
            "nearby player must receive SkillStatusEffect so the turret fire anim plays. Packets: "
            + string.Join(", ", _sent.Select(p => p.GetType().Name)));
        Assert.AreEqual(1217, effect.SkillId);
        Assert.AreEqual(turret.ObjectId.Coid, effect.Caster.Coid);
        Assert.IsFalse(effect.Caster.Global, "FAM turret caster TFID is local");
        Assert.AreEqual(npc.ObjectId.Coid, effect.Targets.Single().Target.Coid);
        Assert.AreEqual((byte)0, effect.Flag);
        Assert.AreEqual((byte)0, effect.Status);
    }

    private static void FireUntilCombat(SectorMap map, Creature turret, long nowMs)
    {
        turret.NpcAi.LastAggroScanMs = 0;
        NpcCombatAi.Tick(map, turret, nowMs, dt: 0.1f);
        if (turret.NpcAi.CombatState == HBAICombatState.Engage)
            NpcCombatAi.Tick(map, turret, nowMs + 1, dt: 0.1f);
        if (turret.NpcAi.CombatState == HBAICombatState.Combat)
            NpcCombatAi.Tick(map, turret, nowMs + 2, dt: 0.1f);
    }

    private static void RegisterPlasmaSkill()
    {
        AssetManager.Instance.SetTestSkill(new Skill
        {
            Id = 1217,
            Name = "Human Plasma_Turret_Attack",
            Elements = new List<SkillElement>
            {
                new() { SkillId = 1217, ElementType = SkillElementTypes.FlagDamageMin | 22, EquationType = 1, ValueBase = 50f, ValuePerLevel = 0f },
                new() { SkillId = 1217, ElementType = SkillElementTypes.FlagDamageMax | 22, EquationType = 1, ValueBase = 80f, ValuePerLevel = 0f },
                new() { SkillId = 1217, ElementType = SkillElementTypes.PenetrationDamageAdd, EquationType = 1, ValueBase = 6f },
                new() { SkillId = 1217, ElementType = SkillElementTypes.Range, EquationType = 1, ValueBase = 100f },
                new() { SkillId = 1217, ElementType = SkillElementTypes.CoolDown, EquationType = 1, ValueBase = 4000f },
            }
        });
    }

    private static Creature PlaceTurret(SectorMap map, Vector3 position, int faction)
    {
        const int cbid = 2978;
        const int aiId = 21;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, aiBehaviorId: aiId, baseLevel: 50, faction: faction, isNpc: 0, hasTurret: 1);
        var spec = AssetManager.Instance.GetCloneBase<CloneBaseCreature>(cbid).CreatureSpecific;
        spec.VisionRange = 134f;
        spec.HearingRange = 70f;
        spec.Speed = 0f;
        spec.Skills = new Dictionary<byte, List<SkillSet>>
        {
            [2] = new List<SkillSet>
            {
                new() { SkillId = 1217, SkillLevel = 1, PauseTime = 1000 }
            }
        };
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = aiId }
        });

        var turret = new Creature();
        turret.LoadCloneBase(cbid);
        turret.SetCoid(map.LocalCoidCounter++, false);
        turret.Faction = faction;
        turret.Position = position;
        turret.SetInvincible(false);
        turret.NpcAi = new NpcAiState
        {
            Profile = AssetManager.Instance.GetCreatureAiProfile(aiId),
            HomePosition = position,
        };
        turret.SetMap(map);
        return turret;
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 871,
            MapFileName = "tm_turret_871",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static (Vehicle Vehicle, TNLConnection Connection) PlacePlayerVehicle(
        SectorMap map, Vector3 position, int faction, bool putCharacterOnMap = false)
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
        if (putCharacterOnMap)
            character.SetMap(map);
        vehicle.SetMap(map);
        return (vehicle, connection);
    }

    private static Creature PlaceNpcVictim(SectorMap map, Vector3 position, int faction)
    {
        var npc = new Creature();
        npc.SetCoid(map.LocalCoidCounter++, false);
        npc.Faction = faction;
        npc.Position = position;
        npc.SetInvincible(false);
        npc.SetMaximumHP(1000, triggerGhostUpdate: false);
        npc.SetHPForTests(1000);
        npc.SetMap(map);
        return npc;
    }

    private static Creature PlaceWildlifeWalker(SectorMap map, Vector3 position, int faction)
    {
        const int cbid = 2982;
        const int aiId = 1;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, aiBehaviorId: aiId, baseLevel: 16, faction: faction, isNpc: 0, hasTurret: 0);
        var spec = AssetManager.Instance.GetCloneBase<CloneBaseCreature>(cbid).CreatureSpecific;
        spec.VisionRange = 145f;
        spec.HearingRange = 70f;
        spec.Speed = 12f;
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = 21 },
            new CreatureAiProfile { AiId = aiId }
        });

        var walker = new Creature();
        walker.LoadCloneBase(cbid);
        walker.SetCoid(map.LocalCoidCounter++, false);
        walker.Faction = faction;
        walker.Position = position;
        walker.SetInvincible(false);
        walker.SetMaximumHP(1000, triggerGhostUpdate: false);
        walker.SetHPForTests(1000);
        walker.NpcAi = new NpcAiState
        {
            Profile = AssetManager.Instance.GetCreatureAiProfile(aiId),
            HomePosition = position,
        };
        walker.SetMap(map);
        return walker;
    }
}
