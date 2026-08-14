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
using AutoCore.Game.Mission.Requirements;
using MissionDef = AutoCore.Game.Mission.Mission;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>
/// Pass 20 — live FAM + WAD mission baseline for mission-controlled world objects.
/// Parses retail FAMs and WAD missions; AutoCore-after is InitializeLocalObjects +
/// ApplyMissionPhaseWorldState for the continent's first auto-assign / PerPlayerLoad mission.
/// </summary>
[TestClass]
public class LiveFamMissionWorldBaselineTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";

    private static readonly (string Fam, string Label, int ContinentId)[] Maps =
    {
        ("sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", 707),
        ("sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 698),
        ("sec_f_b_map_mis_a3_1_wastes", "The Wastes", 708),
        ("sec_f_b_map_mis_a2_1_canyonrun_01", "The Canyon Run", 399),
    };

    /// <summary>
    /// Unload the retail catalog this suite loaded into the process-wide
    /// <see cref="AssetManager"/>. Without it every later test in the assembly resolves
    /// against real WAD data instead of its own fixtures. See <c>LiveAssetIsolationTests</c>.
    /// </summary>
    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void UnloadLiveAssets() => AssetManager.Instance.ClearLiveAssetsForTests();

    [TestMethod]
    public void RealMissionMaps_DumpMissionControlledObjects()
    {
        WithLiveAssets((glm, wad) =>
        {
            var output = new StringBuilder();
            output.AppendLine("| Map | Mission | Expected mission objects | AutoCore actual | Difference |");
            output.AppendLine("| --- | --- | --- | --- | --- |");

            foreach (var (fam, label, continentId) in Maps)
            {
                var mapData = ReadFam(glm, fam, label, continentId);
                var missions = wad.Missions.Values
                    .Where(m => m.Continent == continentId)
                    .OrderBy(m => m.Id)
                    .ToList();

                var inactive = mapData.Templates.Values.OfType<SpawnPointTemplate>()
                    .Where(s => !s.OriginalIsActive && s.Spawns.Any(sl => sl.SpawnType != -1))
                    .OrderBy(s => s.COID)
                    .ToList();
                var creates = mapData.Templates.Values.OfType<ReactionTemplate>()
                    .Where(r => r.ReactionType == ReactionType.Create)
                    .ToList();
                var activates = mapData.Templates.Values.OfType<ReactionTemplate>()
                    .Where(r => r.ReactionType == ReactionType.Activate)
                    .ToList();
                var deactivates = mapData.Templates.Values.OfType<ReactionTemplate>()
                    .Where(r => r.ReactionType == ReactionType.Deactivate)
                    .ToList();

                output.AppendLine();
                output.AppendLine($"## {label} ({continentId})");
                output.AppendLine($"fam={fam} templates={mapData.Templates.Count} " +
                                  $"perPlayerLoad={mapData.PerPlayerLoadTrigger} " +
                                  $"missions={missions.Count} " +
                                  $"inactiveFilledSpawns={inactive.Count} " +
                                  $"create={creates.Count} activate={activates.Count} deactivate={deactivates.Count}");

                foreach (var mission in missions)
                {
                    var reqs = SummarizeMission(mission);
                    output.AppendLine(
                        $"  mission {mission.Id} '{mission.Title ?? mission.Name}' npc={mission.NPC} " +
                        $"auto={mission.AutoAssign} objs={mission.Objectives.Count} {reqs}");
                }

                output.AppendLine("  inactive filled SpawnPoints:");
                foreach (var sp in inactive)
                {
                    var slots = string.Join("; ", sp.Spawns.Where(s => s.SpawnType != -1)
                        .Select(s => $"type={s.SpawnType} tpl={s.IsTemplate} {s.LowerNumberOfSpawns}-{s.UpperNumberOfSpawns}"));
                    output.AppendLine($"    coid={sp.COID} radius={sp.Radius} respawn={sp.RespawnTime} [{slots}]");
                }

                DumpReactions(output, "Create", creates, mapData);
                DumpReactions(output, "Activate", activates, mapData);
                DumpReactions(output, "Deactivate", deactivates, mapData);

                var (expected, actual, missing) = PredictInitialPhase(mapData, continentId, wad);
                output.AppendLine(
                    $"| {label} | initial / PerPlayerLoad + auto-assign | {expected} | {actual} | {missing} |");
            }

            Console.WriteLine(output.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autocore-pass20-mission-baseline.md"), output.ToString());
            Assert.IsTrue(output.ToString().Contains("Hestia Ark Bay 313"));
        });
    }

    [TestMethod]
    public void RealMissionMaps_InactiveCreateTargetsNotCoveredByReplay()
    {
        WithLiveAssets((glm, wad) =>
        {
            var output = new StringBuilder();
            output.AppendLine("| Map | Create COID | Name | Target COID | Target kind | Active? | Replay path |");
            output.AppendLine("| --- | ---: | --- | ---: | --- | --- | --- |");
            var uncovered = 0;

            foreach (var (fam, label, continentId) in Maps)
            {
                var mapData = ReadFam(glm, fam, label, continentId);
                var killTypes = new HashSet<int>();
                var deliverTypes = new HashSet<int>();
                foreach (var mission in wad.Missions.Values.Where(m => m.Continent == continentId))
                {
                    foreach (var obj in mission.Objectives.Values)
                    {
                        foreach (var req in obj.Requirements)
                        {
                            if (req is ObjectiveRequirementKill k && k.TargetCBID > 0)
                                killTypes.Add(k.TargetCBID);
                            if (req is ObjectiveRequirementDeliver d && d.NPCTargetCBID > 0)
                                deliverTypes.Add(d.NPCTargetCBID);
                        }
                    }
                }

                foreach (var rx in mapData.Templates.Values.OfType<ReactionTemplate>()
                             .Where(r => r.ReactionType == ReactionType.Create)
                             .OrderBy(r => r.COID))
                {
                    foreach (var target in rx.Objects)
                    {
                        if (!mapData.Templates.TryGetValue(target, out var tpl) || tpl == null)
                        {
                            output.AppendLine($"| {label} | {rx.COID} | {rx.Name} | {target} | MISSING | — | lookup fail |");
                            uncovered++;
                            continue;
                        }

                        var active = tpl is SpawnPointTemplate sp ? sp.OriginalIsActive : tpl.IsActive;
                        if (active)
                            continue;

                        var kind = tpl is SpawnPointTemplate spawn
                            ? $"SP types=[{string.Join(',', spawn.Spawns.Where(s => s.SpawnType != -1).Select(s => s.SpawnType))}]"
                            : $"{tpl.GetType().Name} cbid={tpl.CBID}";

                        var covered = false;
                        if (tpl is SpawnPointTemplate spawn2)
                        {
                            covered = spawn2.Spawns.Any(s =>
                                s.SpawnType != -1 && (killTypes.Contains(s.SpawnType) || deliverTypes.Contains(s.SpawnType)));
                        }
                        else
                            covered = killTypes.Contains(tpl.CBID) || deliverTypes.Contains(tpl.CBID);

                        if (covered)
                            continue;

                        uncovered++;
                        output.AppendLine($"| {label} | {rx.COID} | {rx.Name} | {target} | {kind} | {active} | NOT in replay |");
                    }
                }
            }

            Console.WriteLine(output.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autocore-pass20-uncovered-creates.md"), output.ToString());
            Assert.IsTrue(output.Length > 0);
            Console.WriteLine($"uncovered inactive Create targets={uncovered}");
        });
    }

    [TestMethod]
    public void RealMissionMaps_KillTargetsThatAreGraphicsMustBeCreated()
    {
        WithLiveAssets((glm, wad) =>
        {
            var cases = new (int ContinentId, string Fam, string Label, int Cbid, int MissionId)[]
            {
                (707, "sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", 7431, 3041),
                (698, "sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 11825, 2971),
                (708, "sec_f_b_map_mis_a3_1_wastes", "The Wastes", 12565, 2968),
            };

            var output = new StringBuilder();
            var missing = new List<string>();
            foreach (var (continentId, fam, label, cbid, missionId) in cases)
            {
                var mapData = ReadFam(glm, fam, label, continentId);
                var gfx = mapData.Templates.Values
                    .Where(t => t.CBID == cbid)
                    .ToList();
                var creates = mapData.Templates.Values.OfType<ReactionTemplate>()
                    .Where(r => r.ReactionType == ReactionType.Create
                                && r.Objects.Any(o => mapData.Templates.TryGetValue(o, out var t) && t.CBID == cbid))
                    .ToList();

                output.AppendLine($"## {label} mission {missionId} kill CBID {cbid}");
                output.AppendLine($"  templates with CBID: {gfx.Count}");
                foreach (var t in gfx)
                    output.AppendLine($"    coid={t.COID} type={t.GetType().Name} active={t.IsActive} orig={(t is SpawnPointTemplate sp ? sp.OriginalIsActive.ToString() : "?")}");
                output.AppendLine($"  Create reactions targeting that CBID: {creates.Count}");
                foreach (var rx in creates)
                    output.AppendLine($"    create={rx.COID} '{rx.Name}' objs=[{string.Join(',', rx.Objects)}]");

                var map = SectorMap.CreateForTests(mapData.ContinentObject, new Vector4());
                foreach (var t in gfx)
                    map.MapData.Templates[t.COID] = t;
                foreach (var rx in creates)
                    map.MapData.Templates[rx.COID] = rx;

                foreach (var t in gfx)
                {
                    var obj = t.Create();
                    obj.SetCoid(t.COID, false);
                    obj.SetMap(map);
                }

                foreach (var rx in creates)
                {
                    var reaction = (Reaction)rx.Create();
                    reaction.SetCoid(rx.COID, false);
                    reaction.SetMap(map);
                }

                var (character, vehicle) = PlacePlayer(map, continentId);
                if (wad.Missions.TryGetValue(missionId, out var mission))
                    AssetManager.Instance.SetTestMission(mission);
                var quest = new CharacterQuest(missionId, 0);
                quest.PopulateFromAssets();
                character.CurrentQuests.Add(quest);
                map.ApplyMissionPhaseWorldState(vehicle);

                var live = map.Objects.Values.Count(o => o.CBID == cbid);
                var materialized = creates.SelectMany(r => r.Objects)
                    .Count(c => character.MapPresence.IsMaterialized(c));
                output.AppendLine($"  after phase: liveCbid={live} materializedCreates={materialized}");

                if (gfx.Count == 0 && creates.Count == 0)
                    missing.Add($"{label} {cbid}: no FAM template and no Create");
                else if (creates.Count > 0 && materialized == 0 && live == 0)
                    missing.Add($"{label} {cbid}: Create exists but phase did not materialize");
            }

            Console.WriteLine(output.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autocore-pass20-kill-graphics.md"), output.ToString());
            Assert.AreEqual(0, missing.Count, string.Join("; ", missing));
        });
    }

    [TestMethod]
    public void RealMissionMaps_KillAndDeliverTargetsHaveSpawns()
    {
        WithLiveAssets((glm, wad) =>
        {
            var output = new StringBuilder();
            output.AppendLine("| Map | Mission | Seq | Kind | Type | Spawn COID | Active? | Replay covers? |");
            output.AppendLine("| --- | --- | ---: | --- | ---: | ---: | --- | --- |");
            var missing = new List<string>();

            foreach (var (fam, label, continentId) in Maps)
            {
                var mapData = ReadFam(glm, fam, label, continentId);
                var spawns = mapData.Templates.Values.OfType<SpawnPointTemplate>()
                    .Where(s => s.Spawns.Any(sl => sl.SpawnType != -1))
                    .ToList();

                foreach (var mission in wad.Missions.Values.Where(m => m.Continent == continentId).OrderBy(m => m.Id))
                {
                    foreach (var obj in mission.Objectives.Values.OrderBy(o => o.Sequence))
                    {
                        foreach (var req in obj.Requirements)
                        {
                            int type = 0;
                            var kind = "";
                            if (req is ObjectiveRequirementKill kill && kill.TargetCBID > 0)
                            {
                                type = kill.TargetCBID;
                                kind = kill.TargetIsTemplateVehicle ? "killT" : "kill";
                            }
                            else if (req is ObjectiveRequirementDeliver d && d.NPCTargetCBID > 0)
                            {
                                type = d.NPCTargetCBID;
                                kind = "deliver";
                            }
                            else
                                continue;

                            var matches = spawns.Where(s => s.Spawns.Any(sl => sl.SpawnType == type)).ToList();
                            if (matches.Count == 0)
                            {
                                output.AppendLine($"| {label} | {mission.Id} {mission.Title} | {obj.Sequence} | {kind} | {type} | — | — | NO SPAWN |");
                                missing.Add($"{label} mission {mission.Id} {kind} {type}");
                                continue;
                            }

                            foreach (var sp in matches)
                            {
                                var replay = !sp.OriginalIsActive;
                                output.AppendLine(
                                    $"| {label} | {mission.Id} {mission.Title} | {obj.Sequence} | {kind} | {type} | {sp.COID} | {sp.OriginalIsActive} | {(replay ? "inactive Create/Activate" : "fam-active load")} |");
                            }
                        }
                    }
                }
            }

            Console.WriteLine(output.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "autocore-pass20-kill-deliver.md"), output.ToString());

            var templateMisses = missing.Where(m => m.Contains("killT")).ToList();
            Assert.AreEqual(0, templateMisses.Count,
                "template-vehicle kill types must have a FAM SpawnPoint: " + string.Join("; ", templateMisses));
        });
    }

    [TestMethod]
    public void RealMissionMaps_CreateOnlyTemplateKillTargetMaterializes()
    {
        WithLiveAssets((glm, wad) =>
        {
            var mapData = ReadFam(glm, "sec_f_m_map_mis_c7_1_tierraroja_tutorial", "Tierra Roja Dam", 698);
            var spawnTpl = mapData.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == 3882);
            Assert.IsFalse(spawnTpl.OriginalIsActive);
            Assert.IsTrue(spawnTpl.Spawns[0].IsTemplate);
            Assert.AreEqual(587, spawnTpl.Spawns[0].SpawnType);

            var create = mapData.Templates.Values.OfType<ReactionTemplate>()
                .First(r => r.ReactionType == ReactionType.Create && r.Objects.Contains(3882));
            var activate = mapData.Templates.Values.OfType<ReactionTemplate>()
                .FirstOrDefault(r => r.ReactionType == ReactionType.Activate && r.Objects.Contains(3882));
            Assert.IsNull(activate, "Champion car 3882 is Create-only — that is the production hole");

            var killMission = FindKillMissionForType(wad, 698, 587);
            Assert.IsNotNull(killMission);

            var map = SectorMap.CreateForTests(mapData.ContinentObject, new Vector4());
            map.MapData.Templates[3882] = spawnTpl;
            map.MapData.Templates[create.COID] = create;
            var spawn = (SpawnPoint)spawnTpl.Create();
            spawn.SetCoid(3882, false);
            spawn.SetMap(map);
            var createRx = (Reaction)create.Create();
            createRx.SetCoid(create.COID, false);
            createRx.SetMap(map);

            var (character, vehicle) = PlacePlayer(map, 698);
            var quest = new CharacterQuest(killMission.Id, 0);
            quest.PopulateFromAssets();
            character.CurrentQuests.Add(quest);
            map.ApplyMissionPhaseWorldState(vehicle);

            Assert.IsTrue(spawn.HasLiveSpawn(),
                $"Create-only Champion car 3882 must spawn template 587 (mission {killMission.Id}); diag={spawn.LastFailureDiagnostic}");
        });
    }

    [TestMethod]
    public void BackRange_MissionGiverSpawns_HaveNoAuthoredTriggers()
    {
        // Retail CVOGSectorMap::CreateMissionFlow synthesizes a GiveMissionDialog trigger
        // only when the creature has no authored TriggerEvents (all −1). These town givers
        // are that case: interact icons come from CreateMissionFlow, not FAM triggers.
        WithLiveAssets((glm, wad) =>
        {
            var mapData = ReadFam(glm, "sec_f_h_map_hwy_j2_backrange", "The Hestia Back Range", 693);
            var giverCbids = new HashSet<int>
            {
                11786, 11787, 11788, 11789, 11790, 11791, 11792, 11793, 11794, 11795,
                12468, 2469, 2470, 2472,
            };
            var found = 0;
            foreach (var spawn in mapData.Templates.Values.OfType<SpawnPointTemplate>())
            {
                if (!spawn.Spawns.Any(s => s.SpawnType > 0 && giverCbids.Contains(s.SpawnType)))
                    continue;
                found++;
                Assert.IsTrue(spawn.OriginalIsActive, $"giver spawn {spawn.COID} should be fam-active");
                var raw = spawn.TriggerEvents ?? Array.Empty<long>();
                Assert.AreEqual(3, raw.Length);
                Assert.IsTrue(raw.All(t => t == -1),
                    $"giver spawn {spawn.COID} must have no authored TriggerEvents so CreateMissionFlow can attach one");
            }

            Assert.IsTrue(found >= 14, $"expected the 14 town giver CBIDs, found {found}");
        });
    }

    [TestMethod]
    public void BackRange_Givers_AreNpcsAndStillHaveOffersAfterTbagSet()
    {
        // CreateMissionFlow @0x004d4040 no-ops unless clonebase wbIsNPC==1.
        // CheckForAvailableMissionsByObject then requires an incomplete continent-693
        // mission on that CBID whose prereqs TBAG already satisfies.
        var tbagCompleted = new HashSet<int>
        {
            554, 2943, 2944, 3032, 3035, 3036, 3037, 3040, 3041, 3050, 3052, 3055,
            3094, 3979, 3980, 3981,
        };
        var giverCbids = new[]
        {
            11786, 11787, 11788, 11789, 11790, 11791, 11792, 11793, 11794, 11795,
            12468, 2469, 2470, 2472,
        };

        WithLiveAssets((_, wad) =>
        {
            foreach (var cbid in giverCbids)
            {
                Assert.IsTrue(wad.CloneBases.TryGetValue(cbid, out var clone),
                    $"missing clonebase {cbid}");
                Assert.IsInstanceOfType(clone, typeof(CloneBaseCreature),
                    $"giver {cbid} must be a creature");
                Assert.AreEqual(1, ((CloneBaseCreature)clone).CreatureSpecific.IsNPC,
                    $"CreateMissionFlow requires wbIsNPC==1 on {cbid}");
            }

            var offersByNpc = new Dictionary<int, List<int>>();
            foreach (var mission in wad.Missions.Values)
            {
                if (mission.Continent != 693 || mission.NPC <= 0)
                    continue;
                if (!giverCbids.Contains(mission.NPC))
                    continue;
                if (tbagCompleted.Contains(mission.Id) && mission.IsRepeatable == 0)
                    continue;
                if (mission.ReqLevelMin > 4)
                    continue;
                if (mission.ReqLevelMax > 0 && 4 > mission.ReqLevelMax)
                    continue;
                if (!MissionWorldPhaseRules.MeetsMissionPrerequisites(
                        mission.ReqMissionId, mission.RequirementsOred, tbagCompleted))
                    continue;

                if (!offersByNpc.TryGetValue(mission.NPC, out var list))
                {
                    list = new List<int>();
                    offersByNpc[mission.NPC] = list;
                }

                list.Add(mission.Id);
            }

            Assert.IsTrue(offersByNpc.Count >= 3,
                "TBAG should still have offerable Back Range missions on several givers; " +
                $"found {offersByNpc.Count}: " +
                string.Join("; ", offersByNpc.Select(kv =>
                    $"{kv.Key}=[{string.Join(',', kv.Value.OrderBy(x => x))}]")));
        });
    }

    [TestMethod]
    public void RealMissionMaps_ArkBayGunnyCombatExistsAfterKillPhase()
    {
        WithLiveAssets((glm, wad) =>
        {
            var mapData = ReadFam(glm, "sec_f_h_map_tut_j2_arkbaytutorial", "Hestia Ark Bay 313", 707);
            var gunny = mapData.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == 14138);
            Assert.IsFalse(gunny.OriginalIsActive, "combat Gunny is fam-inactive");
            Assert.IsTrue(gunny.Spawns[0].IsTemplate);
            Assert.AreEqual(580, gunny.Spawns[0].SpawnType);

            var create = mapData.Templates.Values.OfType<ReactionTemplate>()
                .FirstOrDefault(r => r.ReactionType == ReactionType.Create && r.Objects.Contains(14138));
            var activate = mapData.Templates.Values.OfType<ReactionTemplate>()
                .FirstOrDefault(r => r.ReactionType == ReactionType.Activate && r.Objects.Contains(14138));
            Assert.IsNotNull(create, "FAM must author a Create targeting 14138");
            Assert.IsNotNull(activate, "FAM must author an Activate targeting 14138");

            var killMission = FindKillMissionForType(wad, 707, 580);
            Assert.IsNotNull(killMission, "Ark Bay must have a kill mission targeting template 580");

            var map = PlaceGunnyGraph(mapData, create!, activate!);
            var spawn = map.GetObjectByCoid(14138) as SpawnPoint;
            Assert.IsNotNull(spawn, "inactive combat marker must be placed at load");
            Assert.IsFalse(spawn!.HasLiveSpawn(), "combat Gunny must not exist at map load");

            var (character, vehicle) = PlacePlayer(map, 707);
            var quest = new CharacterQuest(killMission!.Id, 0);
            quest.PopulateFromAssets();
            character.CurrentQuests.Add(quest);

            map.ApplyMissionPhaseWorldState(vehicle);

            spawn = map.GetObjectByCoid(14138) as SpawnPoint;
            Assert.IsNotNull(spawn);
            Assert.IsTrue(spawn!.HasLiveSpawn(),
                $"kill-phase login must Activate Gunny 14138 children (mission {killMission.Id}); " +
                $"materialized={character.MapPresence.IsMaterialized(14138)} " +
                $"createRx={create!.COID} activateRx={activate!.COID} diag={spawn.LastFailureDiagnostic}");
        });
    }

    private static (string Expected, string Actual, string Diff) PredictInitialPhase(
        MapData mapData,
        int continentId,
        WADLoader wad)
    {
        var auto = wad.Missions.Values
            .Where(m => m.Continent == continentId && m.AutoAssign != 0)
            .OrderBy(m => m.Id)
            .ToList();
        var give = mapData.Templates.Values.OfType<ReactionTemplate>()
            .Where(r => r.ReactionType == ReactionType.GiveMission)
            .Select(r => r.GenericVar1)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var seedIds = give.Concat(auto.Select(m => m.Id)).Distinct().ToList();
        var types = new HashSet<int>();
        foreach (var id in seedIds)
        {
            if (!wad.Missions.TryGetValue(id, out var mission))
                continue;
            if (!mission.Objectives.TryGetValue(0, out var obj))
                continue;
            foreach (var req in obj.Requirements)
            {
                if (req is ObjectiveRequirementKill kill && kill.TargetCBID > 0)
                    types.Add(kill.TargetCBID);
                if (req is ObjectiveRequirementDeliver d && d.NPCTargetCBID > 0 && d.NPCTargetCBID != mission.NPC)
                    types.Add(d.NPCTargetCBID);
            }
        }

        var phaseTargets = mapData.Templates.Values.OfType<SpawnPointTemplate>()
            .Where(s => !s.OriginalIsActive && s.Spawns.Any(sl => sl.SpawnType != -1 && types.Contains(sl.SpawnType)))
            .Select(s => s.COID)
            .ToList();

        var activeMin = mapData.Templates.Values.OfType<SpawnPointTemplate>()
            .Where(s => s.OriginalIsActive)
            .Sum(s => s.ExpectedMinimumChildren());

        var expected = $"activeMin={activeMin}; phaseTargets=[{string.Join(',', phaseTargets)}]; seedMissions=[{string.Join(',', seedIds)}]";
        var actual = phaseTargets.Count == 0
            ? "no kill/deliver inactive targets on seq0"
            : $"replay should Create/Activate {phaseTargets.Count} inactive spawn(s)";
        return (expected, actual, phaseTargets.Count == 0 ? "0 (seq0 has no Create-later targets)" : "needs live Apply");
    }

    private static SectorMap PlaceGunnyGraph(MapData mapData, ReactionTemplate create, ReactionTemplate activate)
    {
        var map = SectorMap.CreateForTests(mapData.ContinentObject, new Vector4());
        var gunny = mapData.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == 14138);
        map.MapData.Templates[14138] = gunny;
        map.MapData.Templates[create.COID] = create;
        map.MapData.Templates[activate.COID] = activate;

        var spawn = (SpawnPoint)gunny.Create();
        spawn.SetCoid(14138, false);
        spawn.SetMap(map);

        var createRx = (Reaction)create.Create();
        createRx.SetCoid(create.COID, false);
        createRx.SetMap(map);

        var activateRx = (Reaction)activate.Create();
        activateRx.SetCoid(activate.COID, false);
        activateRx.SetMap(map);
        return map;
    }

    private static MissionDef FindKillMissionForType(WADLoader wad, int continentId, int spawnType)
    {
        foreach (var mission in wad.Missions.Values.Where(m => m.Continent == continentId))
        {
            foreach (var obj in mission.Objectives.Values)
            {
                foreach (var req in obj.Requirements.OfType<ObjectiveRequirementKill>())
                {
                    if (req.TargetCBID == spawnType)
                        return mission;
                }
            }
        }

        return null;
    }

    private static void DumpReactions(
        StringBuilder output,
        string label,
        List<ReactionTemplate> reactions,
        MapData mapData)
    {
        output.AppendLine($"  {label} reactions ({reactions.Count}):");
        foreach (var rx in reactions.OrderBy(r => r.COID).Take(40))
        {
            var targets = string.Join(',', rx.Objects);
            var targetKinds = string.Join(',', rx.Objects.Select(c =>
            {
                if (!mapData.Templates.TryGetValue(c, out var t) || t == null)
                    return $"{c}=MISSING";
                return t is SpawnPointTemplate sp
                    ? $"{c}=SP(active={sp.OriginalIsActive})"
                    : $"{c}={t.GetType().Name}";
            }));
            output.AppendLine(
                $"    coid={rx.COID} name='{rx.Name}' g1={rx.GenericVar1} doAll={rx.DoForAllPlayers} " +
                $"objs=[{targets}] kinds=[{targetKinds}]");
        }

        if (reactions.Count > 40)
            output.AppendLine($"    … {reactions.Count - 40} more");
    }

    private static string SummarizeMission(MissionDef mission)
    {
        var kills = new List<string>();
        var delivers = new List<int>();
        foreach (var obj in mission.Objectives.Values)
        {
            foreach (var req in obj.Requirements)
            {
                if (req is ObjectiveRequirementKill kill && kill.TargetCBID > 0)
                    kills.Add($"{kill.TargetCBID}{(kill.TargetIsTemplateVehicle ? "T" : "")}");
                if (req is ObjectiveRequirementDeliver d && d.NPCTargetCBID > 0)
                    delivers.Add(d.NPCTargetCBID);
            }
        }

        return $"kills=[{string.Join(',', kills)}] deliver=[{string.Join(',', delivers)}]";
    }

    private static (Character Character, Vehicle Vehicle) PlacePlayer(SectorMap map, int continentId)
    {
        var character = new Character();
        character.SetCoid(900_000 + continentId, true);
        var vehicle = new Vehicle { Position = new Vector3() };
        vehicle.SetCoid(900_100 + continentId, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle);
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
            IsTown = false,
            IsPersistent = continentId is 698 or 707 or 708,
        });
        using var reader = new BinaryReader(famStream);
        mapData.Read(reader);
        return mapData;
    }

    private static void WithLiveAssets(Action<GLMLoader, WADLoader> body)
    {
        if (!File.Exists(Path.Combine(InstallPath, "clonebase.wad")))
        {
            Assert.Inconclusive($"clonebase.wad not at {InstallPath}");
            return;
        }

        var wad = (WADLoader)typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        var glm = (GLMLoader)typeof(AssetManager)
            .GetProperty("GLMLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        var world = (WorldDBLoader)typeof(AssetManager)
            .GetProperty("WorldDBLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;

        var loadedWadHere = wad.CloneBases.Count == 0;
        var loadedGlmHere = !glm.CanGetReader("sec_f_h_map_tut_j2_arkbaytutorial.fam");
        var previousTemplates = world.VehicleTemplates;

        if (loadedGlmHere)
            Assert.IsTrue(glm.Load(InstallPath), "GLM load failed");

        if (loadedWadHere)
        {
            wad.Missions.Clear();
            wad.Skills.Clear();
            wad.CloneBases.Clear();
            wad.ArmorPrefixes.Clear();
            wad.PowerPlantPrefixes.Clear();
            wad.WeaponPrefixes.Clear();
            wad.VehiclePrefixes.Clear();
            wad.OrnamentPrefixes.Clear();
            wad.RaceItemPrefixes.Clear();
            Assert.IsTrue(wad.Load(Path.Combine(InstallPath, "clonebase.wad")), "WAD load failed");
        }

        var wadXml = Path.Combine(InstallPath, "wad.xml");
        if (File.Exists(wadXml))
            world.VehicleTemplates = WadXmlWorldDataLoader.LoadVehicleTemplates(wadXml);

        TriggerManager.Instance.ClearAllForTests();
        try
        {
            body(glm, wad);
        }
        finally
        {
            world.VehicleTemplates = previousTemplates;
            TriggerManager.Instance.ClearAllForTests();
        }
    }
}
