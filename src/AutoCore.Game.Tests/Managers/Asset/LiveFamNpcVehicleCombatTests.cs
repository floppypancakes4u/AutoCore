using System.Reflection;
using System.Text;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>
/// Pass 24 — live FAM + wad.xml + clonebase characterization of NPC vehicle combat
/// (target → turret aim → fire bit → damage → GhostVehicle dirty). Written and run
/// before any production combat change.
/// </summary>
[TestClass]
public class LiveFamNpcVehicleCombatTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";
    private const string WadXmlPath = InstallPath + @"\wad.xml";

    private readonly List<(TNLConnection Conn, BasePacket Packet)> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (c, p) => _sent.Add((c, p));
        Vehicle.ClearCombatThrottleForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
        Vehicle.ClearCombatThrottleForTests();
    }

    [TestMethod]
    public void LiveFam_SelectedNpcVehicles_CombatBaselineBeforeProductionChange()
    {
        WithRetailCatalog((glm, wad, catalog, profiles) =>
        {
            var report = new StringBuilder();
            report.AppendLine("| Vehicle | Weapon(s) | Range | AI profile | Expected attack style |");
            report.AppendLine("| --- | --- | --- | --- | --- |");

            var rows = new List<CombatRow>
            {
                RunMissionCar(report, glm, wad, catalog, profiles,
                    "sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 698, 3882, 587),
                RunMissionCar(report, glm, wad, catalog, profiles,
                    "sec_f_b_map_mis_a3_1_wastes", "The Wastes", 708, 18609, 593),
                RunMissionCar(report, glm, wad, catalog, profiles,
                    "sec_f_b_map_mis_a2_1_canyonrun_01", "The Canyon Run", 399, 23413, 636),
                RunHighway(report, glm, wad, catalog, profiles, 12500, 606),
            };

            report.AppendLine();
            report.AppendLine("| Vehicle | Target | Firing | Damage | Client state | Result |");
            report.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var row in rows)
            {
                report.AppendLine(
                    $"| {row.Label} | {row.Target} | {row.Firing} | {row.Damage} | {row.ClientState} | {row.Result} |");
            }

            Console.WriteLine(report.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autocore-pass24-combat-baseline.md"), report.ToString());

            foreach (var row in rows)
            {
                Assert.IsTrue(row.HasUsableWeapon,
                    $"{row.Label} is an expected armed hostile; runtime weapon missing. {row.WeaponDump}");
                Assert.IsTrue(row.Acquired,
                    $"{row.Label} must acquire the seated player vehicle once in vision");
                Assert.IsTrue(row.TurretAimed,
                    $"{row.Label} must update WantedTurretDirection toward the player");
                Assert.IsTrue(row.Fired,
                    $"{row.Label} must raise a firing bit in Combat while the player is in range. {row.WeaponDump}");
                Assert.IsTrue(row.Damaged,
                    $"{row.Label} must apply damage to the player vehicle. {row.WeaponDump}");
            }
        });
    }

    [TestMethod]
    public void TierraRoja3882_CombatMatchesRetailTemplate()
        => AssertNamedCombat("sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 698, 3882, 587, expectPath: true);

    [TestMethod]
    public void Wastes18609_CombatMatchesRetailTemplate()
        => AssertNamedCombat("sec_f_b_map_mis_a3_1_wastes", "The Wastes", 708, 18609, 593, expectPath: true);

    [TestMethod]
    public void Canyon23413_CombatMatchesRetailTemplate()
        => AssertNamedCombat("sec_f_b_map_mis_a2_1_canyonrun_01", "The Canyon Run", 399, 23413, 636, expectPath: false);

    [TestMethod]
    public void Scrap12500_CombatMatchesRetailTemplate()
        => AssertNamedHighwayCombat(12500, 606);

    [TestMethod]
    public void LiveFam_PikeSprayer622_FrontWeaponHighway_CombatBaseline()
    {
        WithRetailCatalog((glm, wad, catalog, profiles) =>
        {
            Assert.IsTrue(catalog.TryGetValue(622, out var row), "template 622 Pike Sprayer missing");
            Assert.IsTrue(row.WeaponFrontCbid > 0, "622 authors a front weapon");
            Assert.IsTrue(row.WeaponTurretCbid <= 0, "622 is the front-only highway control");

            var scrap = ReadFam(glm, "sec_f_b_map_hwy_a2_1_scrapvalley", "Scrap Valley", 398);
            var spawnTpl = scrap.Templates.Values.OfType<SpawnPointTemplate>()
                .Where(s => s.OriginalIsActive
                            && s.Spawns.Any(sl => sl.IsTemplate && sl.SpawnType == 622))
                .OrderBy(s => s.COID)
                .FirstOrDefault();
            Assert.IsNotNull(spawnTpl, "Scrap Valley must author at least one template-622 highway spawn");

            var report = new StringBuilder();
            var combat = RunSpawn(report, "Scrap Valley", scrap, wad, catalog, profiles, spawnTpl!, 622);
            Console.WriteLine(report);
            Assert.IsTrue(combat.HasUsableWeapon, combat.WeaponDump);
            Assert.IsTrue(combat.Acquired);
            Assert.IsTrue(combat.Fired, "front-armed Pike Sprayer in range must enter firing state");
            Assert.IsTrue(combat.Damaged, combat.WeaponDump);
        });
    }

    private void AssertNamedCombat(string fam, string label, int continentId, long spawnCoid, int templateId, bool expectPath)
    {
        WithRetailCatalog((glm, wad, catalog, profiles) =>
        {
            var report = new StringBuilder();
            var row = RunMissionCar(report, glm, wad, catalog, profiles, fam, label, continentId, spawnCoid, templateId);
            Console.WriteLine(report);
            Assert.AreEqual(templateId, row.TemplateId);
            if (expectPath)
                Assert.IsTrue(row.PathCoid > 0, $"{label} {spawnCoid} must keep its authored MapPathCoid");
            else
                Assert.IsTrue(row.PathCoid <= 0, $"{label} {spawnCoid} is the pathless wait-in-place car");
            Assert.IsTrue(row.HasUsableWeapon, row.WeaponDump);
            Assert.IsTrue(row.Acquired);
            Assert.IsTrue(row.TurretAimed);
            Assert.IsTrue(row.Fired, row.WeaponDump);
            Assert.IsTrue(row.Damaged, row.WeaponDump);
            Assert.IsNull(row.DriverMappedOrGhosted);
        });
    }

    private void AssertNamedHighwayCombat(long spawnCoid, int templateId)
    {
        WithRetailCatalog((glm, wad, catalog, profiles) =>
        {
            var report = new StringBuilder();
            var row = RunHighway(report, glm, wad, catalog, profiles, spawnCoid, templateId);
            Console.WriteLine(report);
            Assert.IsTrue(row.HasUsableWeapon, row.WeaponDump);
            Assert.IsTrue(row.Acquired);
            Assert.IsTrue(row.Fired, row.WeaponDump);
            Assert.IsTrue(row.Damaged, row.WeaponDump);
        });
    }

    private CombatRow RunMissionCar(
        StringBuilder report,
        GLMLoader glm,
        WADLoader wad,
        IDictionary<int, VehicleTemplate> catalog,
        IDictionary<int, CreatureAiProfile> profiles,
        string fam,
        string label,
        int continentId,
        long spawnCoid,
        int templateId)
    {
        var mapData = ReadFam(glm, fam, label, continentId);
        var spawnTpl = mapData.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == spawnCoid);
        Assert.AreEqual(templateId, spawnTpl.Spawns[0].SpawnType);
        return RunSpawn(report, label, mapData, wad, catalog, profiles, spawnTpl, templateId);
    }

    private CombatRow RunHighway(
        StringBuilder report,
        GLMLoader glm,
        WADLoader wad,
        IDictionary<int, VehicleTemplate> catalog,
        IDictionary<int, CreatureAiProfile> profiles,
        long spawnCoid,
        int templateId)
    {
        var scrap = ReadFam(glm, "sec_f_b_map_hwy_a2_1_scrapvalley", "Scrap Valley", 398);
        var spawnTpl = scrap.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == spawnCoid);
        Assert.AreEqual(templateId, spawnTpl.Spawns[0].SpawnType);
        return RunSpawn(report, "Scrap Valley", scrap, wad, catalog, profiles, spawnTpl, templateId);
    }

    private CombatRow RunSpawn(
        StringBuilder report,
        string label,
        MapData mapData,
        WADLoader wad,
        IDictionary<int, VehicleTemplate> catalog,
        IDictionary<int, CreatureAiProfile> profiles,
        SpawnPointTemplate spawnTpl,
        int templateId)
    {
        Assert.IsTrue(catalog.TryGetValue(templateId, out var tpl), $"{label} template {templateId} missing");
        // Player must enter before mission-Create cars are spawned. EnterMap hygiene despawns
        // children of fam-inactive markers (Pass 20 / Gunny combat car). Spawning first then
        // SetMap(player) deletes the car under test — that is not a combat-loop failure.
        var (map, spawn) = PlaceSpawn(mapData, spawnTpl);
        var playerHpBefore = 50_000;
        var (player, _) = PlaceConnectedPlayer(map, new Vector3(spawn.Position.X + 8f, spawn.Position.Y, spawn.Position.Z), faction: 0);
        player.ApplyTemplateBaseHp(playerHpBefore);
        player.SetCurrentHP(playerHpBefore);
        if (!spawn.HasLiveSpawn())
        {
            Assert.IsTrue(spawn.Spawn(),
                $"{label} spawn {spawnTpl.COID} failed after player enter: {spawn.LastFailureDiagnostic}");
        }
        var vehicle = map.Objects.Values.OfType<Vehicle>()
            .Last(v => v.SpawnOwnerCoid == spawnTpl.COID);
        vehicle.SetCombatRngForTests(new AlwaysHitRandom());
        player.Position = new Vector3(vehicle.Position.X + 8f, vehicle.Position.Y, vehicle.Position.Z);
        map.Grid.RebucketSweep();

        var front = DescribeWeapon(vehicle.WeaponFront);
        var turret = DescribeWeapon(vehicle.WeaponTurret);
        var melee = DescribeWeapon(vehicle.WeaponMelee);
        var weaponDump =
            $"tpl front={tpl.WeaponFrontCbid} turret={tpl.WeaponTurretCbid} melee={tpl.WeaponMeleeCbid}; " +
            $"runtime front={front} turret={turret} melee={melee}";

        var aiId = 0;
        var vision = 0f;
        if (vehicle.Owner?.GetAsCreature()?.CloneBaseObject is CloneBaseCreature driver)
        {
            aiId = driver.CreatureSpecific.AIBehavior;
            vision = Math.Max(driver.CreatureSpecific.VisionRange, driver.CreatureSpecific.HearingRange);
        }
        profiles.TryGetValue(aiId, out var profile);
        var engageMs = profile?.ValFleeOrEngageTimerMs ?? 0f;
        var style = tpl.WeaponTurretCbid > 0
            ? "turret track + fire bit 2"
            : tpl.WeaponFrontCbid > 0
                ? "chassis-front fire bit 1"
                : "unarmed";

        report.AppendLine(
            $"| {label} {spawnTpl.COID}/{templateId} {tpl.ShortDesc} | {weaponDump} | " +
            $"front={RangeOf(vehicle.WeaponFront)} turret={RangeOf(vehicle.WeaponTurret)} vision={vision} | " +
            $"AIID {aiId} val1={engageMs} | {style} |");
        report.AppendLine(
            $"  diag spawnDirty={spawnTpl.FactionDirty} origFac={spawnTpl.OriginalFaction} " +
            $"vehFac={vehicle.Faction} getId={vehicle.GetIDFaction()} drvFac={vehicle.Owner?.Faction} " +
            $"inAi={vehicle.Map.NpcAiEntities.Contains(vehicle)} town={vehicle.Map.MapData.ContinentObject.IsTown} " +
            $"pos={vehicle.Position}");

        var row = new CombatRow
        {
            Label = $"{label} {spawnTpl.COID}/{templateId}",
            TemplateId = templateId,
            PathCoid = vehicle.CoidCurrentPath,
            WeaponDump = weaponDump,
            HasUsableWeapon = IsFirable(vehicle.WeaponFront) || IsFirable(vehicle.WeaponTurret) || IsFirable(vehicle.WeaponRear),
        };

        if (vehicle.Owner != null && (vehicle.Owner.Map != null || vehicle.Owner.Ghost != null))
            row.DriverMappedOrGhosted = "driver mapped/ghosted";

        ScopeGhost(vehicle);
        var ghostInfo = vehicle.Ghost!.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo);

        NpcTicker.Tick(vehicle.Map, nowMs: 100_000, dt: 0.05f);
        row.Acquired = vehicle.Target != null;
        var targetIsPlayerVehicle = ReferenceEquals(vehicle.Target, player);
        report.AppendLine(
            $"  after-scan target={vehicle.Target?.ObjectId.Coid} state={vehicle.NpcAi?.CombatState} " +
            $"playerFac={player.GetIDFaction()} dist={vehicle.Position.Dist(player.Position):F2} " +
            $"playerHp={player.GetCurrentHP()} invuln={player.IsInvincible}");

        // Production AI: skip the engage-commit wait so the fire path is reached this test.
        if (vehicle.NpcAi != null)
        {
            vehicle.NpcAi.EngageStartedMs = 0;
            vehicle.NpcAi.CombatState = HBAICombatState.Combat;
        }

        if (vehicle.Target == null)
            vehicle.SetTargetObject(player);

        ghostInfo!.UpdateMask = 0;
        var sentBefore = _sent.Count;
        NpcCombatAi.Tick(vehicle.Map, vehicle, nowMs: 100_050, dt: 0.05f);
        NetObject.CollapseDirtyList();

        row.TurretAimed = MathF.Abs(vehicle.WantedTurretDirection) > 1e-4f
                          || (vehicle.Target != null && vehicle.Position.Dist(vehicle.Target.Position) < 1f);
        row.Fired = vehicle.Firing != 0;
        row.Damaged = player.GetCurrentHP() < playerHpBefore
                      || _sent.Skip(sentBefore).Any(e => e.Packet is DamagePacket);
        row.Target = row.Acquired
            ? (targetIsPlayerVehicle ? "player vehicle" : $"other {vehicle.Target?.ObjectId.Coid}")
            : "none";
        row.Firing = $"bit={vehicle.Firing} aim={vehicle.WantedTurretDirection:F3} state={vehicle.NpcAi?.CombatState}";
        row.Damage = $"hp {playerHpBefore}->{player.GetCurrentHP()} packets={_sent.Skip(sentBefore).Count(e => e.Packet is DamagePacket)}";
        var dirty = ghostInfo.UpdateMask;
        row.ClientState =
            $"Pos={(dirty & GhostObject.PositionMask) != 0} " +
            $"Tgt={(dirty & GhostObject.TargetMask) != 0} " +
            $"AI={(dirty & GhostVehicle.StateMask) != 0} " +
            $"HP={(dirty & GhostObject.HealthMask) != 0}";
        row.Result = row.HasUsableWeapon && row.Acquired && row.Fired && row.Damaged ? "full loop" : "INCOMPLETE";
        return row;
    }

    private static string DescribeWeapon(Weapon weapon)
    {
        if (weapon == null)
            return "none";
        var clone = weapon.CloneBaseWeapon;
        if (clone == null)
            return $"cbid={weapon.CBID} (no clonebase)";
        var spec = clone.WeaponSpecific;
        return $"cbid={weapon.CBID} flags=0x{spec.Flags:X2} range={spec.RangeMin}-{spec.RangeMax} " +
               $"cd={spec.RechargeTime} arc={spec.ValidArc} heat={spec.Heat} sub={spec.SubType}";
    }

    private static string RangeOf(Weapon weapon)
        => weapon?.CloneBaseWeapon == null
            ? "-"
            : weapon.CloneBaseWeapon.WeaponSpecific.RangeMax.ToString("0.###");

    private static bool IsFirable(Weapon weapon) => weapon?.CloneBaseWeapon != null;

    private static (SectorMap Map, SpawnPoint Spawn) PlaceSpawn(MapData mapData, SpawnPointTemplate spawnTpl)
    {
        var map = SectorMap.CreateForTests(mapData.ContinentObject, spawnTpl.Location);
        map.MapData.Templates[spawnTpl.COID] = spawnTpl;
        if (spawnTpl.MapPathCoid > 0 && mapData.Templates.TryGetValue(spawnTpl.MapPathCoid, out var pathTpl))
            map.MapData.Templates[spawnTpl.MapPathCoid] = pathTpl;

        var spawn = (SpawnPoint)spawnTpl.Create();
        spawn.SetCoid(spawnTpl.COID, false);
        spawn.Position = spawnTpl.Location.ToVector3();
        spawn.SetMap(map);
        return (map, spawn);
    }

    private static MapData ReadFam(GLMLoader glm, string famName, string label, int continentId)
    {
        using var famStream = glm.GetStream($"{famName}.fam");
        Assert.IsNotNull(famStream, $"{famName}.fam missing from GLM packs");
        var mapData = new MapData(new ContinentObject
        {
            Id = continentId,
            MapFileName = famName,
            DisplayName = label,
            IsTown = famName.Contains("town", StringComparison.OrdinalIgnoreCase),
            IsPersistent = true,
        });
        using var reader = new BinaryReader(famStream);
        mapData.Read(reader);
        return mapData;
    }

    private static void ScopeGhost(Vehicle vehicle)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.ActivateGhosting();
        connection.ObjectInScope(vehicle.Ghost!);
        connection.ObjectLocalScopeAlways(vehicle.Ghost!);
    }

    private static (Vehicle Vehicle, TNLConnection Connection) PlaceConnectedPlayer(
        SectorMap map,
        Vector3 position,
        int faction)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(900_000 + map.ContinentId, true);
        character.Faction = faction;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle { Position = position };
        vehicle.SetCoid(910_000 + map.ContinentId, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (vehicle, connection);
    }

    private static void WithRetailCatalog(
        Action<GLMLoader, WADLoader, IDictionary<int, VehicleTemplate>, IDictionary<int, CreatureAiProfile>> body)
    {
        if (!File.Exists(Path.Combine(InstallPath, "clonebase.wad")) || !File.Exists(WadXmlPath))
        {
            Assert.Inconclusive($"retail data not at {InstallPath}");
            return;
        }

        var wad = (WADLoader)typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        var world = (WorldDBLoader)typeof(AssetManager)
            .GetProperty("WorldDBLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;

        var loadedWadHere = wad.CloneBases.Count == 0;
        var previousTemplates = world.VehicleTemplates;
        var previousProfiles = world.CreatureAiProfiles;
        if (loadedWadHere)
        {
            wad.Missions.Clear();
            wad.Skills.Clear();
            wad.ArmorPrefixes.Clear();
            wad.PowerPlantPrefixes.Clear();
            wad.WeaponPrefixes.Clear();
            wad.VehiclePrefixes.Clear();
            wad.OrnamentPrefixes.Clear();
            wad.RaceItemPrefixes.Clear();
            Assert.IsTrue(wad.Load(Path.Combine(InstallPath, "clonebase.wad")), "WAD load failed");
        }

        var catalog = WadXmlWorldDataLoader.LoadVehicleTemplates(WadXmlPath);
        var profiles = WadXmlWorldDataLoader.LoadCreatureAiProfiles(WadXmlPath);
        world.VehicleTemplates = catalog;
        world.CreatureAiProfiles = profiles;
        AssetManager.Instance.ClearTestNpcData();

        try
        {
            var glm = new GLMLoader();
            Assert.IsTrue(glm.Load(InstallPath), "GLM load failed");
            body(glm, wad, catalog, profiles);
        }
        finally
        {
            world.VehicleTemplates = previousTemplates;
            world.CreatureAiProfiles = previousProfiles;
            AssetManager.Instance.ClearTestNpcData();
            if (loadedWadHere)
                wad.CloneBases.Clear();
        }
    }

    private sealed class CombatRow
    {
        public string Label = "";
        public int TemplateId;
        public long PathCoid;
        public string WeaponDump = "";
        public bool HasUsableWeapon;
        public bool Acquired;
        public bool TurretAimed;
        public bool Fired;
        public bool Damaged;
        public string Target = "";
        public string Firing = "";
        public string Damage = "";
        public string ClientState = "";
        public string Result = "";
        public string DriverMappedOrGhosted;
    }

    private sealed class AlwaysHitRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0d;
    }
}
