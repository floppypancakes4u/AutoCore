using System.Reflection;
using System.Runtime.CompilerServices;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Structures;
using TNL.Utils;

namespace AutoCore.Game.Tests.NpcAi;

/// <summary>
/// Pass 24 — NPC vehicle combat loop: target, turret wanted-direction, firing bits,
/// cooldown, damage, GhostVehicle dirtying. Driver stays unmapped/ghostless; 500 ms
/// Create→GhostVehicle hold is unchanged.
/// </summary>
[TestClass]
public class NpcVehicleCombatTests
{
    private const int ContId = 24_100;
    private const int VehicleCbid = 24_201;
    private const int DriverCbid = 24_202;
    private const int WheelsetCbid = 24_203;
    private const int WeaponCbid = 24_204;
    private const int TurretWeaponCbid = 24_214;
    private const int TemplateId = 24_205;
    private const long SpawnCoid = 24_301;
    private const long CreateRx = 24_501;
    private const long ActivateRx = 24_502;

    private readonly List<(TNLConnection Conn, BasePacket Packet)> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (c, p) => _sent.Add((c, p));
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        Vehicle.ClearCombatThrottleForTests();
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        Vehicle.ClearCombatThrottleForTests();
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
        NetObject.PIsInitialUpdate = false;
    }

    [TestMethod]
    public void NpcVehicleCombat_RealVehicleHasUsableWeapon()
    {
        var map = CreateFieldMap();
        RegisterArmedTemplate();
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        var npc = SingleOwnedVehicle(map, SpawnCoid);

        Assert.IsNotNull(npc.WeaponTurret, "template turret CBID must become a runtime Weapon");
        Assert.IsNotNull(npc.WeaponTurret.CloneBaseWeapon, "the equipped weapon must resolve its clonebase");
        Assert.IsTrue(npc.WeaponTurret.CloneBaseWeapon.WeaponSpecific.RangeMax > 0f);
        var (bit, weapon) = NpcCombatAi.SelectFiringWeapon(npc, npc);
        Assert.AreEqual((byte)2, bit);
        Assert.AreSame(npc.WeaponTurret, weapon);
    }

    [TestMethod]
    public void NpcVehicleCombat_TargetAcquireSelectsPlayerVehicle()
    {
        var (npc, player, _) = SpawnArmedWithPlayer();
        NpcTicker.Tick(npc.Map, nowMs: 100_000, dt: 0.05f);

        Assert.AreSame(player, npc.Target, "a seated player must be acquired as the Vehicle, not the Character");
        Assert.IsInstanceOfType(npc.Target, typeof(Vehicle));
        Assert.IsNotInstanceOfType(npc.Target, typeof(Character));
    }

    [TestMethod]
    public void NpcVehicleCombat_TargetMarksGhostDirty()
    {
        var (npc, _, ghostInfo) = SpawnArmedWithPlayer(scope: true);
        ghostInfo.UpdateMask = 0;
        NpcTicker.Tick(npc.Map, nowMs: 100_000, dt: 0.05f);
        NetObject.CollapseDirtyList();

        Assert.IsNotNull(npc.Target);
        Assert.AreEqual(GhostObject.TargetMask, ghostInfo.UpdateMask & GhostObject.TargetMask);
    }

    [TestMethod]
    public void NpcVehicleCombat_EngageUpdatesTurretWantedDirection()
    {
        var (npc, player, _) = SpawnArmedWithPlayer(engageTimerMs: 8_000f);
        npc.Rotation = new Quaternion(0f, 0f, 0f, 1f);
        player.Position = new Vector3(npc.Position.X + 20f, npc.Position.Y, npc.Position.Z);
        npc.Map.Grid.RebucketSweep();
        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Engage;
        npc.NpcAi.EngageStartedMs = 100_000;

        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);

        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState);
        Assert.AreEqual((byte)0, npc.Firing, "Engage aims without firing");
        Assert.AreEqual(MathF.PI * 0.5f, npc.WantedTurretDirection, 1e-3f);
    }

    [TestMethod]
    public void NpcVehicleCombat_TurretStateMarksGhostDirty()
    {
        var (npc, player, ghostInfo) = SpawnArmedWithPlayer(scope: true);
        npc.Rotation = new Quaternion(0f, 0f, 0f, 1f);
        player.Position = new Vector3(npc.Position.X + 20f, npc.Position.Y, npc.Position.Z);
        npc.Map.Grid.RebucketSweep();
        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Engage;
        npc.NpcAi.EngageStartedMs = 100_000;
        ghostInfo.UpdateMask = 0;

        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask,
            "turret wanted-direction lives on the Position block");
    }

    [TestMethod]
    public void NpcVehicleCombat_WeaponInRangeEntersFiringState()
    {
        var (npc, player, _) = SpawnArmedWithPlayer();
        ForceCombat(npc, player);
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);

        Assert.AreEqual((byte)2, npc.Firing, "turret-only loadout raises firing bit 2");
    }

    [TestMethod]
    public void NpcVehicleCombat_FiringStateMarksGhostDirty()
    {
        var (npc, player, ghostInfo) = SpawnArmedWithPlayer(scope: true);
        ForceCombat(npc, player);
        ghostInfo.UpdateMask = 0;
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);
        NetObject.CollapseDirtyList();

        Assert.AreNotEqual(0, npc.Firing);
        Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask);
    }

    [TestMethod]
    public void NpcVehicleCombat_FirstShotOccursWithinRetailCooldown()
    {
        var (npc, player, _) = SpawnArmedWithPlayer(rechargeMs: 400);
        player.ApplyTemplateBaseHp(80_000);
        npc.SetCombatRngForTests(new AlwaysHitRandom());
        ForceCombat(npc, player);

        var hpBefore = player.GetCurrentHP();
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);

        Assert.AreNotEqual(0, npc.Firing);
        Assert.IsTrue(player.GetCurrentHP() < hpBefore,
            "first Combat tick in range must consume the weapon schedule (nextFireAtMs starts at 0)");
    }

    [TestMethod]
    public void NpcVehicleCombat_RepeatedShotsRespectCooldown()
    {
        var (npc, player, _) = SpawnArmedWithPlayer(rechargeMs: 250);
        player.ApplyTemplateBaseHp(80_000);
        npc.SetCombatRngForTests(new AlwaysHitRandom());
        ForceCombat(npc, player);

        var shots = 0;
        var lastHp = player.GetCurrentHP();
        for (var i = 0; i < 8; i++)
        {
            NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000 + (i * 100), dt: 0.1f);
            var hp = player.GetCurrentHP();
            if (hp < lastHp)
            {
                shots++;
                lastHp = hp;
            }
        }

        Assert.IsTrue(shots >= 3, $"expected at least 3 shots over 700ms at 250ms recharge; got {shots}");
        Assert.IsTrue(shots <= 4, $"250ms recharge over 700ms must not dump a burst; got {shots}");
    }

    [TestMethod]
    public void NpcVehicleCombat_DamageReachesPlayerVehicle()
    {
        var (npc, player, _) = SpawnArmedWithPlayer();
        player.ApplyTemplateBaseHp(80_000);
        npc.SetCombatRngForTests(new AlwaysHitRandom());
        ForceCombat(npc, player);
        var hpBefore = player.GetCurrentHP();

        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);

        Assert.IsTrue(player.GetCurrentHP() < hpBefore);
        Assert.AreEqual(hpBefore, player.Owner.GetAsCharacter()!.GetCurrentHP() > 0
            ? hpBefore
            : hpBefore, "character body is not the damage target");
    }

    [TestMethod]
    public void NpcVehicleCombat_DamagePacketMatchesClientContract()
    {
        var (npc, player, _) = SpawnArmedWithPlayer();
        player.ApplyTemplateBaseHp(80_000);
        npc.SetCombatRngForTests(new AlwaysHitRandom());
        ForceCombat(npc, player);
        var connection = player.GetSuperCharacter(false)!.OwningConnection;

        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);

        var packet = _sent
            .Where(e => ReferenceEquals(e.Conn, connection) && e.Packet is DamagePacket)
            .Select(e => (DamagePacket)e.Packet)
            .FirstOrDefault();
        Assert.IsNotNull(packet, "victim connection must receive 0x2023");
        Assert.AreEqual(GameOpcode.Damage, packet.Opcode);
        Assert.AreEqual(npc.ObjectId.Coid, packet.Source.Coid);
        Assert.AreEqual(npc.ObjectId.Global, packet.Source.Global);
        Assert.IsTrue(packet.Entries.Count >= 1);
        Assert.AreEqual(player.ObjectId.Coid, packet.Entries[0].Target.Coid);
        Assert.AreEqual(player.ObjectId.Global, packet.Entries[0].Target.Global);
        Assert.IsTrue(packet.Entries[0].Amount > 0);

        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);
        Assert.IsTrue(stream.Read(out long sourceCoid));
        Assert.AreEqual(npc.ObjectId.Coid, sourceCoid);
        Assert.AreEqual(npc.ObjectId.Global, stream.ReadFlag());
        Assert.IsTrue(stream.ReadInt(16) >= 1);
        stream.ReadFlag(); // crit
        Assert.IsTrue(stream.ReadInt(16) >= 1);
        Assert.IsTrue(stream.Read(out long targetCoid));
        Assert.AreEqual(player.ObjectId.Coid, targetCoid);
    }

    [TestMethod]
    public void NpcVehicleCombat_HealthStateMatchesDamage()
    {
        var (npc, player, _) = SpawnArmedWithPlayer();
        player.ApplyTemplateBaseHp(80_000);
        npc.SetCombatRngForTests(new AlwaysHitRandom());
        ForceCombat(npc, player);
        var hpBefore = player.GetCurrentHP();

        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);

        var packet = _sent
            .Select(e => e.Packet)
            .OfType<DamagePacket>()
            .First(p => p.Source.Coid == npc.ObjectId.Coid);
        var dealt = packet.Entries.Where(e => e.Target.Coid == player.ObjectId.Coid).Sum(e => e.Amount);
        Assert.AreEqual(hpBefore - dealt, player.GetCurrentHP(),
            "vehicle HP after the hit must equal pre-hit HP minus the 0x2023 amount");
        Assert.IsNotNull(player.GetSuperCharacter(false)!.OwningConnection);
    }

    [TestMethod]
    public void NpcVehicleCombat_TargetLossClearsFiring()
    {
        var (npc, player, _) = SpawnArmedWithPlayer();
        ForceCombat(npc, player);
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);
        Assert.AreNotEqual(0, npc.Firing);
        Assert.AreNotEqual(0f, npc.WantedTurretDirection, 1e-4f);

        player.SetMap(null);
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_100, dt: 0.05f);

        Assert.IsNull(npc.Target);
        Assert.AreEqual((byte)0, npc.Firing);
        Assert.AreEqual(0f, npc.WantedTurretDirection, 1e-4f);
        Assert.AreEqual(HBAICombatState.IdlePatrol, npc.NpcAi.CombatState);
    }

    [TestMethod]
    public void NpcVehicleCombat_TargetSwitchRetargetsTurret()
    {
        var (npc, playerA, _) = SpawnArmedWithPlayer();
        npc.Rotation = new Quaternion(0f, 0f, 0f, 1f);
        var (playerB, _) = PlacePlayer(npc.Map, new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z + 20f), faction: 0);
        playerB.ApplyTemplateBaseHp(80_000);
        ForceCombat(npc, playerA);
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);
        var aimA = npc.WantedTurretDirection;

        playerA.SetMap(null);
        NpcTicker.Tick(npc.Map, nowMs: 100_600, dt: 0.05f); // TargetLost → IdlePatrol
        NpcTicker.Tick(npc.Map, nowMs: 101_200, dt: 0.05f); // Idle scan — must not wait to walk home
        Assert.AreSame(playerB, npc.Target, "after A leaves, the next aggro scan must latch B");

        npc.NpcAi.CombatState = HBAICombatState.Combat;
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_650, dt: 0.05f);
        Assert.AreNotEqual(aimA, npc.WantedTurretDirection, 0.05f, "turret must retarget onto B");
        Assert.AreNotEqual(0, npc.Firing);
    }

    [TestMethod]
    public void NpcVehicleCombat_MovingTargetUpdatesAim()
    {
        var (npc, player, _) = SpawnArmedWithPlayer();
        npc.Rotation = new Quaternion(0f, 0f, 0f, 1f);
        player.Position = new Vector3(npc.Position.X + 20f, npc.Position.Y, npc.Position.Z);
        npc.Map.Grid.RebucketSweep();
        ForceCombat(npc, player);
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);
        var aimA = npc.WantedTurretDirection;

        player.Position = new Vector3(npc.Position.X, npc.Position.Y, npc.Position.Z + 20f);
        npc.Map.Grid.RebucketSweep();
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_050, dt: 0.05f);

        Assert.AreEqual(MathF.PI * 0.5f, aimA, 1e-3f);
        Assert.AreEqual(0f, npc.WantedTurretDirection, 1e-3f);
    }

    [TestMethod]
    public void NpcVehicleCombat_MissionCreateCanFight()
    {
        var map = CreateFieldMap();
        RegisterArmedTemplate();
        var spawn = PlaceInactiveTemplateSpawn(map, SpawnCoid);
        PlaceCreate(map, CreateRx, SpawnCoid);
        var (character, player) = PlaceConnectedCharacter(map, new Vector3(0f, 0f, 0f), faction: 0);
        SeedKillQuest(character, TemplateId);
        map.ApplyMissionPhaseWorldState(player);

        Assert.IsTrue(spawn.HasLiveSpawn());
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        AssertCombatCapable(npc, player);
    }

    [TestMethod]
    public void NpcVehicleCombat_MissionActivateCanFight()
    {
        var map = CreateFieldMap();
        RegisterArmedTemplate();
        var spawn = PlaceInactiveTemplateSpawn(map, SpawnCoid);
        var actTpl = new ReactionTemplate { COID = (int)ActivateRx, ReactionType = ReactionType.Activate };
        actTpl.Objects.Add(SpawnCoid);
        var activate = new Reaction(actTpl);
        activate.SetCoid(ActivateRx, false);
        activate.SetMap(map);
        var (character, player) = PlaceConnectedCharacter(map, new Vector3(0f, 0f, 0f), faction: 0);

        Assert.IsTrue(activate.TriggerIfPossible(character));
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        AssertCombatCapable(npc, player);
    }

    [TestMethod]
    public void NpcVehicleCombat_RespawnCanFight()
    {
        var map = CreateFieldMap();
        RegisterArmedTemplate();
        var spawn = PlaceTemplateSpawn(map, SpawnCoid, respawnTime: 1_000f);
        spawn.Spawn();
        var first = SingleOwnedVehicle(map, SpawnCoid);
        first.SetMap(null);
        spawn.NotifySpawnedChildDied(first, null);
        map.TickSpawnRespawns(spawn.RespawnDueAtMs ?? 0);

        var replacement = SingleOwnedVehicle(map, SpawnCoid);
        Assert.AreNotEqual(first.ObjectId.Coid, replacement.ObjectId.Coid);
        var (player, _) = PlacePlayer(map, new Vector3(replacement.Position.X + 8f, 0f, replacement.Position.Z), 0);
        AssertCombatCapable(replacement, player);
    }

    [TestMethod]
    public void NpcVehicleCombat_TargetButNoWeapon_LogsOnce()
    {
        var logs = new List<string>();
        IncompleteHandlerLog.TestSink = msg => logs.Add(msg);
        try
        {
            var map = CreateFieldMap();
            AssetManagerTestHelper.RegisterCloneBase(WheelsetCbid, CloneBaseObjectType.WheelSet);
            AssetManagerTestHelper.RegisterVehicleCloneBase(VehicleCbid, defaultDriverCbid: DriverCbid, defaultWheelsetCbid: WheelsetCbid);
            AssetManagerTestHelper.RegisterCreatureCloneBase(DriverCbid, aiBehaviorId: 0, faction: 2, isNpc: 0);
            AssetManager.Instance.GetCloneBase<CloneBaseCreature>(DriverCbid).CreatureSpecific.VisionRange = 80f;
            AssetManager.Instance.SetTestVehicleTemplates(new[]
            {
                new VehicleTemplate { Id = TemplateId, VehicleCbid = VehicleCbid, DriverCbid = DriverCbid }
            });
            PlaceTemplateSpawn(map, SpawnCoid).Spawn();
            var npc = SingleOwnedVehicle(map, SpawnCoid);
            npc.CreateGhost();
            var (player, _) = PlacePlayer(map, new Vector3(8f, 0f, 0f), 0);
            ForceCombat(npc, player);
            NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.05f);
            NpcCombatAi.Tick(map, npc, nowMs: 100_100, dt: 0.05f);

            Assert.AreEqual(0, npc.Firing);
            Assert.IsTrue(logs.Any(l => l.Contains("no usable weapon", StringComparison.OrdinalIgnoreCase)),
                "hostile + Combat + empty hardpoints must log once. got: " + string.Join(" | ", logs));
        }
        finally
        {
            IncompleteHandlerLog.TestSink = null;
        }
    }

    [TestMethod]
    public void NpcVehicleCombat_DriverRemainsUnmappedGhostless()
    {
        var (npc, _, _) = SpawnArmedWithPlayer();
        Assert.IsNotNull(npc.Owner);
        Assert.IsNull(npc.Owner.Map);
        Assert.IsNull(npc.Owner.Ghost);
        Assert.IsFalse(npc.Map.Objects.ContainsKey(npc.Owner.ObjectId));
        Assert.IsInstanceOfType(npc.Ghost, typeof(GhostVehicle));
    }

    [TestMethod]
    public void NpcVehicleCombat_Preserves500msGhostHold()
    {
        Assert.AreEqual(500, TNLConnection.ForeignGhostScopeHoldMilliseconds);
        Assert.AreEqual(1, TNLConnection.ForeignGhostScopeHoldQueries);

        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 24_001;
        conn.NoteForeignVehicleCreateSent(coid);
        Assert.IsTrue(conn.HasActiveForeignCreateHold(coid));
        Assert.IsFalse(conn.TryAllowForeignVehicleGhostScope(coid),
            "combat must not release the Create→GhostVehicle hold");

        var (npc, player, _) = SpawnArmedWithPlayer();
        npc.SetCombatRngForTests(new AlwaysHitRandom());
        player.ApplyTemplateBaseHp(80_000);
        ForceCombat(npc, player);
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);
        Assert.AreNotEqual(0, npc.Firing, "AI/fire may run server-side during the hold");
        Assert.IsTrue(player.GetCurrentHP() < 80_000);
    }

    [TestMethod]
    public void NpcVehicleCombat_FirstScopeHasCoherentCombatState()
    {
        var (npc, player, _) = SpawnArmedWithPlayer(scope: false);
        npc.Rotation = new Quaternion(0f, 0f, 0f, 1f);
        player.Position = new Vector3(npc.Position.X + 20f, npc.Position.Y, npc.Position.Z);
        npc.Map.Grid.RebucketSweep();
        npc.SetCombatRngForTests(new AlwaysHitRandom());
        player.ApplyTemplateBaseHp(80_000);
        ForceCombat(npc, player);
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 100_000, dt: 0.05f);
        Assert.AreNotEqual(0, npc.Firing);
        Assert.IsNotNull(npc.Target);

        var observer = new TNLConnection();
        observer.SetGhostFrom(true);
        observer.SetGhostTo(false);
        observer.BeginGhostingForTests();
        observer.ObjectInScope(npc.Ghost!);
        NetObject.CollapseDirtyList();

        var stream = new BitStream(new byte[4096], 4096);
        NetObject.PIsInitialUpdate = true;
        try
        {
            npc.Ghost!.PackUpdate(observer, ~0UL, stream);
        }
        finally
        {
            NetObject.PIsInitialUpdate = false;
        }

        Assert.AreNotEqual(0, npc.Firing, "firing bit must still be set when the first ghost goes out");
        Assert.AreSame(player, npc.Target);
        Assert.AreNotEqual(0f, npc.WantedTurretDirection, 1e-4f);
        Assert.IsTrue(stream.GetBitPosition() > 0, "first scope must emit a GhostVehicle payload");
    }

    private void AssertCombatCapable(Vehicle npc, Vehicle player)
    {
        Assert.IsNotNull(npc.NpcAi);
        Assert.IsTrue(IsFirable(npc.WeaponTurret) || IsFirable(npc.WeaponFront),
            "mission/respawn vehicles must carry a firable runtime weapon");
        npc.CreateGhost();
        npc.SetCombatRngForTests(new AlwaysHitRandom());
        player.Position = new Vector3(npc.Position.X + 8f, npc.Position.Y, npc.Position.Z);
        npc.Map.Grid.RebucketSweep();
        player.ApplyTemplateBaseHp(80_000);
        ForceCombat(npc, player);
        var hpBefore = player.GetCurrentHP();
        NpcCombatAi.Tick(npc.Map, npc, nowMs: 200_000, dt: 0.05f);
        Assert.AreNotEqual(0, npc.Firing, "spawn/activate/respawn must reach the same fire path");
        Assert.IsTrue(player.GetCurrentHP() < hpBefore);
        Assert.IsNull(npc.Owner.Map);
        Assert.IsNull(npc.Owner.Ghost);
    }

    private (Vehicle Npc, Vehicle Player, GhostInfo GhostInfo) SpawnArmedWithPlayer(
        bool scope = false,
        int rechargeMs = 1,
        float engageTimerMs = 0f)
    {
        var map = CreateFieldMap();
        RegisterArmedTemplate(rechargeMs, engageTimerMs);
        PlaceTemplateSpawn(map, SpawnCoid).Spawn();
        var npc = SingleOwnedVehicle(map, SpawnCoid);
        if (scope)
            ScopeGhost(npc);
        var ghostInfo = npc.Ghost?.GetFirstObjectRef();
        var (player, _) = PlacePlayer(map, new Vector3(npc.Position.X + 8f, npc.Position.Y, npc.Position.Z), 0);
        return (npc, player, ghostInfo);
    }

    private static void ForceCombat(Vehicle npc, Vehicle player)
    {
        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;
        npc.NpcAi.EngageStartedMs = 0;
    }

    private static bool IsFirable(Weapon weapon) => weapon?.CloneBaseWeapon != null;

    private static void RegisterArmedTemplate(int rechargeMs = 1, float engageTimerMs = 0f)
    {
        AssetManagerTestHelper.RegisterCloneBase(WheelsetCbid, CloneBaseObjectType.WheelSet);
        AssetManagerTestHelper.RegisterVehicleCloneBase(VehicleCbid, defaultDriverCbid: DriverCbid, defaultWheelsetCbid: WheelsetCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(DriverCbid, aiBehaviorId: 24_206, faction: 2, isNpc: 0);
        var spec = AssetManager.Instance.GetCloneBase<CloneBaseCreature>(DriverCbid).CreatureSpecific;
        spec.VisionRange = 80f;

        var profile = new CreatureAiProfile { AiId = 24_206 };
        profile.Vals[0] = engageTimerMs;
        AssetManager.Instance.SetTestCreatureAiProfiles(new[] { profile });

        RegisterTurretWeapon(TurretWeaponCbid, rechargeMs);

        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = TemplateId,
                VehicleCbid = VehicleCbid,
                DriverCbid = DriverCbid,
                WeaponTurretCbid = TurretWeaponCbid,
                BaseHp = 400,
            }
        });
    }

    private static void RegisterTurretWeapon(int cbid, int rechargeMs)
    {
        AssetManagerTestHelper.RegisterWeaponCloneBase(cbid, rangeMax: 50f);
        var clone = AssetManager.Instance.GetCloneBase<CloneBaseWeapon>(cbid);
        var ws = clone.WeaponSpecific;
        ws.Flags = VehicleEquipmentSlotResolver.WeaponFlagTurret;
        ws.RechargeTime = rechargeMs;
        ws.RangeMin = 0f;
        ws.RangeMax = 50f;
        ws.ValidArc = -1f;
        ws.DamageScalar = 1f;
        ws.DmgMinMin = 8;
        ws.DmgMaxMax = 12;
        ws.MinMin = DamageSpecific.CreateEmpty();
        ws.MaxMax = DamageSpecific.CreateEmpty();
        clone.WeaponSpecific = ws;
    }

    private static SpawnPoint PlaceTemplateSpawn(SectorMap map, long spawnCoid, float respawnTime = -1f)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)spawnCoid,
            OriginalIsActive = true,
            IsActive = true,
            RespawnTime = respawnTime,
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = TemplateId,
            IsTemplate = true,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[spawnCoid] = tpl;
        var spawn = new SpawnPoint(tpl);
        spawn.SetCoid(spawnCoid, false);
        spawn.Position = new Vector3(0f, 0f, 0f);
        spawn.SetMap(map);
        return spawn;
    }

    private static SpawnPoint PlaceInactiveTemplateSpawn(SectorMap map, long spawnCoid)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)spawnCoid,
            OriginalIsActive = false,
            IsActive = false,
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = TemplateId,
            IsTemplate = true,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[spawnCoid] = tpl;
        var spawn = (SpawnPoint)tpl.Create();
        spawn.SetCoid(spawnCoid, false);
        spawn.Position = new Vector3(4f, 0f, -2f);
        spawn.SetMap(map);
        return spawn;
    }

    private static void PlaceCreate(SectorMap map, long reactionCoid, long targetCoid)
    {
        var tpl = new ReactionTemplate { COID = (int)reactionCoid, ReactionType = ReactionType.Create };
        tpl.Objects.Add(targetCoid);
        map.MapData.Templates[reactionCoid] = tpl;
        var rx = new Reaction(tpl);
        rx.SetCoid(reactionCoid, false);
        rx.SetMap(map);
    }

    private static void SeedKillQuest(Character character, int templateId)
    {
        var obj = AutoCore.Game.Mission.MissionObjective.CreateForTests(95_024, 0, 92_024, 1);
        obj.Requirements.Add(new AutoCore.Game.Mission.Requirements.ObjectiveRequirementKill(obj)
        {
            TargetCBID = templateId,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
        });
        var mission = AutoCore.Game.Mission.Mission.CreateForTests(92_024, obj);
        AssetManager.Instance.SetTestMission(mission);
        var quest = new CharacterQuest(92_024, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static SectorMap CreateFieldMap(int continentId = ContId)
    {
        return SectorMap.CreateForTests(new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_npc_vehicle_combat_{continentId}",
            DisplayName = "npc-vehicle-combat",
            IsTown = false,
            IsPersistent = true,
        }, new Vector4());
    }

    private static (Vehicle Vehicle, TNLConnection Connection) PlacePlayer(SectorMap map, Vector3 position, int faction)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(map.LocalCoidCounter++, true);
        character.Faction = faction;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle { Position = position };
        vehicle.SetCoid(map.LocalCoidCounter++, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (vehicle, connection);
    }

    private static (Character Character, Vehicle Vehicle) PlaceConnectedCharacter(SectorMap map, Vector3 position, int faction)
    {
        var (vehicle, _) = PlacePlayer(map, position, faction);
        return (vehicle.Owner.GetAsCharacter()!, vehicle);
    }

    private static void ScopeGhost(Vehicle vehicle)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.BeginGhostingForTests();
        connection.ObjectInScope(vehicle.Ghost!);
        connection.ObjectLocalScopeAlways(vehicle.Ghost!);
    }

    private static Vehicle SingleOwnedVehicle(SectorMap map, long spawnCoid)
        => map.Objects.Values.OfType<Vehicle>().Single(v => v.SpawnOwnerCoid == spawnCoid);

    private static byte[] WritePacket(DamagePacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        return ms.ToArray();
    }

    private sealed class AlwaysHitRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0d;
    }
}
