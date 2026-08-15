using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Combat;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Sim.Collision;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Sim.Tests.Collision;

[TestClass]
public class MilitiaPitLosProbeTests
{
    private const string InstallPath = @"C:\Program Files (x86)\NetDevil\Auto Assault";
    private const string Fam = "sec_f_h_map_hwy_j2_01_militiabase_01";

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void Unload() => AssetManager.Instance.ClearLiveAssetsForTests();

    [TestMethod]
    public void MilitiaBase_PitWall_BlocksPlazaTurretLosToCrawler()
    {
        if (!File.Exists(Path.Combine(InstallPath, "clonebase.wad")))
        {
            Assert.Inconclusive("no install");
            return;
        }

        var wad = (WADLoader)typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance)!;
        if (wad.CloneBases.Count == 0)
            Assert.IsTrue(wad.Load(Path.Combine(InstallPath, "clonebase.wad")));

        foreach (var id in new[] { 1346, 2357, 2502, 1333, 2978, 2982 })
        {
            if (!wad.CloneBases.TryGetValue(id, out var cb))
            {
                Console.WriteLine($"CBID {id} MISSING");
                continue;
            }

            var obj = cb as CloneBaseObject;
            var name = cb.CloneBaseSpecific.UniqueName;
            var phys = obj?.SimpleObjectSpecific.PhysicsName;
            var minHp = obj?.SimpleObjectSpecific.MinHitPoints;
            var type = cb.CloneBaseSpecific.Type;
            var soft = VehicleMapPropRam.IsSoftDestructibleCloneBase(obj);
            Console.WriteLine(
                $"CBID {id} {name} type={type} minHp={minHp} phys={phys} soft={soft}");
        }

        var glm = new GLMLoader();
        Assert.IsTrue(glm.Load(InstallPath));
        using var famStream = glm.GetStream($"{Fam}.fam");
        var mapData = new MapData(new ContinentObject
        {
            Id = 426,
            MapFileName = Fam,
            DisplayName = "Militia Base",
            IsTown = false,
            IsPersistent = true,
        });
        using (var reader = new BinaryReader(famStream))
            mapData.Read(reader);

        var turret = mapData.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == 23766);
        var crawler = mapData.Templates.Values.OfType<SpawnPointTemplate>().Single(s => s.COID == 23602);
        Console.WriteLine(
            $"turret {turret.Location.X:0.##},{turret.Location.Y:0.##},{turret.Location.Z:0.##}");
        Console.WriteLine(
            $"crawler {crawler.Location.X:0.##},{crawler.Location.Y:0.##},{crawler.Location.Z:0.##}");

        var builder = new MapCollisionWorldBuilder(
            cbid => (AssetManager.Instance.GetCloneBase(cbid) as CloneBaseObject)
                ?.SimpleObjectSpecific.PhysicsName,
            glm.EnumerateFileNames(),
            name =>
            {
                using var s = glm.GetStream(name);
                if (s == null)
                    return null;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            },
            cbid => VehicleMapPropRam.IsSoftDestructibleCloneBase(
                AssetManager.Instance.GetCloneBase(cbid) as CloneBaseObject));

        var world = builder.Build(mapData.Templates.Values);
        Console.WriteLine($"world instances={world.InstanceCount}");

        var from = new Vector3(turret.Location.X, turret.Location.Y + 1.2f, turret.Location.Z);
        var to = new Vector3(crawler.Location.X, crawler.Location.Y + 1.2f, crawler.Location.Z);
        var clear = LineOfSight.IsClear(world, from, to);
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dz = to.Z - from.Z;
        var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        var dir = new Vector3(dx / dist, dy / dist, dz / dist);
        var hit = world.Raycast(from, dir, dist, out var hitDist, out _, out var label);
        Console.WriteLine($"dist={dist:0.##} clear={clear} hit={hit} hitDist={hitDist:0.##} label={label}");

        // Nearby hulls around the pit
        var mid = new Vector3(
            (from.X + to.X) * 0.5f, (from.Y + to.Y) * 0.5f, (from.Z + to.Z) * 0.5f);
        var near = 0;
        foreach (var tpl in mapData.Templates.Values.OfType<GraphicsObjectTemplate>())
        {
            var ddx = tpl.Location.X - mid.X;
            var ddz = tpl.Location.Z - mid.Z;
            if (ddx * ddx + ddz * ddz > 40f * 40f)
                continue;
            near++;
        }
        Console.WriteLine($"graphics templates within 40 of midpoint={near}");

        // This probe is evidence; the production assertion is that the pit wall blocks.
        Assert.IsTrue(hit, $"turret→crawler must hit a pit wall (label={label})");

        Console.WriteLine("all nearby combat spawns vs crawler:");
        foreach (var sp in mapData.Templates.Values.OfType<SpawnPointTemplate>()
                     .Where(s => s.OriginalIsActive)
                     .Select(s => (s, dx: s.Location.X - crawler.Location.X, dz: s.Location.Z - crawler.Location.Z))
                     .Select(t => (t.s, dist: MathF.Sqrt(t.dx * t.dx + t.dz * t.dz)))
                     .Where(t => t.dist <= 150f && t.s.COID != 23602)
                     .OrderBy(t => t.dist))
        {
            var slots = string.Join(",", sp.s.Spawns.Where(x => x.SpawnType != -1)
                .Select(x => x.SpawnType.ToString()));
            var a = new Vector3(sp.s.Location.X, sp.s.Location.Y + 1.2f, sp.s.Location.Z);
            var b = new Vector3(crawler.Location.X, crawler.Location.Y + 1.2f, crawler.Location.Z);
            var open = LineOfSight.IsClear(world, a, b);
            Console.WriteLine(
                $"  d={sp.dist:0.0} coid={sp.s.COID} respawn={sp.s.RespawnTime} slots=[{slots}] " +
                $"xyz=({sp.s.Location.X:0.#},{sp.s.Location.Y:0.#},{sp.s.Location.Z:0.#}) losClear={open}");
        }
    }
}
