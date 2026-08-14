using System.Reflection;
using System.Text;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers.Asset;

/// <summary>
/// Pass 21 — live FAM dump of condition-gated Create/Delete/Death graphics/gates.
/// </summary>
[TestClass]
public class LiveFamMissionGateBaselineTests
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
    public void RealMissionMaps_DumpConditionGatedGraphics()
    {
        WithLiveAssets((glm, wad) =>
        {
            var output = new StringBuilder();
            output.AppendLine("| Map | Trigger | Name | coll | cond | scale | actCount | Conditions | Reactions | Targets |");
            output.AppendLine("| --- | ---: | --- | --- | --- | ---: | ---: | --- | --- | --- |");

            foreach (var (fam, label, continentId) in Maps)
            {
                var mapData = ReadFam(glm, fam, label, continentId);
                output.AppendLine();
                output.AppendLine($"## {label} ({continentId}) vars={mapData.Variables.Count} triggers={mapData.Templates.Values.OfType<TriggerTemplate>().Count()}");

                foreach (var v in mapData.Variables.Values.OrderBy(v => v.Id))
                {
                    output.AppendLine(
                        $"  var id={v.Id} type={v.Type} value={v.Value} init={v.InitialValue} name='{v.Name}'");
                }

                var triggers = mapData.Templates.Values.OfType<TriggerTemplate>()
                    .Where(t => t.Conditions.Count > 0 && t.Reactions.Count > 0)
                    .OrderBy(t => t.COID)
                    .ToList();

                foreach (var trigger in triggers)
                {
                    var conds = DescribeConditions(trigger, mapData);
                    var rxs = DescribeReactions(trigger, mapData);
                    var targets = DescribeTargets(trigger, mapData);
                    var isGateish = IsGateish(trigger, mapData);
                    if (!isGateish)
                        continue;

                    output.AppendLine(
                        $"| {label} | {trigger.COID} | {trigger.Name} | {Bool(trigger.DoCollision)} | {Bool(trigger.DoConditionals)} | {trigger.Scale:0.##} | {trigger.ActivationCount} | {conds} | {rxs} | {targets} |");
                }
            }

            var path = Path.Combine(Path.GetTempPath(), "autocore-pass21-gates.md");
            File.WriteAllText(path, output.ToString());
            Console.WriteLine(output.ToString());
            Console.WriteLine($"wrote {path}");
            Assert.IsTrue(output.ToString().Contains("Hestia Ark Bay 313"));
        });
    }

    private static bool IsGateish(TriggerTemplate trigger, MapData mapData)
    {
        foreach (var rxCoid in trigger.Reactions)
        {
            if (!mapData.Templates.TryGetValue(rxCoid, out var tpl) || tpl is not ReactionTemplate rx)
                continue;

            if (rx.ReactionType is not (ReactionType.Create or ReactionType.Delete or ReactionType.Death
                or ReactionType.Activate or ReactionType.Deactivate or ReactionType.Enable or ReactionType.Disable))
            {
                continue;
            }

            foreach (var target in rx.Objects)
            {
                if (!mapData.Templates.TryGetValue(target, out var targetTpl) || targetTpl == null)
                    continue;

                if (targetTpl is GraphicsObjectTemplate or SpawnPointTemplate)
                    return true;

                var name = (rx.Name ?? "") + " " + (trigger.Name ?? "");
                if (name.Contains("door", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("gate", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("airlock", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("barrier", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("graphic", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("open", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("close", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string DescribeConditions(TriggerTemplate trigger, MapData mapData)
    {
        return string.Join("; ", trigger.Conditions.Select(c =>
        {
            var left = DescribeVar(mapData, c.LeftId);
            var right = DescribeVar(mapData, c.RightId);
            return $"{left} {c.Type} {right}";
        }));
    }

    private static string DescribeVar(MapData mapData, int id)
    {
        if (!mapData.Variables.TryGetValue(id, out var v) || v == null)
            return $"var{id}?";

        var typeName = v.Type switch
        {
            LogicVariableStore.TypeConstant => "const",
            LogicVariableStore.TypePlayerHealthPercent => "hp%",
            LogicVariableStore.TypeHasCompletedMission => "doneMis",
            LogicVariableStore.TypeHasCompletedObjective => "doneObj",
            LogicVariableStore.TypeHasActiveMission => "actMis",
            LogicVariableStore.TypeHasActiveObjective => "actObj",
            _ => $"t{v.Type}",
        };
        return $"{id}:{typeName}({v.Value}/{v.InitialValue},'{v.Name}')";
    }

    private static string DescribeReactions(TriggerTemplate trigger, MapData mapData)
    {
        return string.Join("; ", trigger.Reactions.Select(c =>
        {
            if (!mapData.Templates.TryGetValue(c, out var tpl) || tpl is not ReactionTemplate rx)
                return $"{c}=?";
            return $"{c}:{rx.ReactionType}('{rx.Name}')";
        }));
    }

    private static string DescribeTargets(TriggerTemplate trigger, MapData mapData)
    {
        var parts = new List<string>();
        foreach (var rxCoid in trigger.Reactions)
        {
            if (!mapData.Templates.TryGetValue(rxCoid, out var tpl) || tpl is not ReactionTemplate rx)
                continue;
            foreach (var target in rx.Objects)
            {
                if (!mapData.Templates.TryGetValue(target, out var t) || t == null)
                {
                    parts.Add($"{target}=MISSING");
                    continue;
                }

                var kind = t.GetType().Name.Replace("Template", "");
                var active = t is SpawnPointTemplate sp ? sp.OriginalIsActive : t.IsActive;
                parts.Add($"{target}:{kind} cbid={t.CBID} active={active}");
            }
        }

        return string.Join("; ", parts);
    }

    private static string Bool(bool v) => v ? "Y" : "N";

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
