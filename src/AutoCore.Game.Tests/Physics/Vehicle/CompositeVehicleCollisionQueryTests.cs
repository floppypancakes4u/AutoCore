using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;

/// <summary>
/// CW — composite terrain + map-object proxy wheel casts (improve, not close).
/// </summary>
[TestClass]
public class CompositeVehicleCollisionQueryTests
{
    private const float Tol = 1e-4f;

    [TestInitialize]
    public void SetUp()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        ServerConfig.ResetToDefaults();
    }

    [TestCleanup]
    public void TearDown()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        ServerConfig.ResetToDefaults();
    }

    [TestMethod]
    public void CastRay_ProxyBoxHit_IsNonTerrainWithSynthesizedNormal()
    {
        // Terrain far below (Y=0). Raised box prop at Y=2 with half-extents 1 → top face Y=3.
        var terrain = Flat(0f);
        var map = CreateMap();
        RegisterPhysicsProp(cbid: 9101, scale: 2f); // half-extent 1 when Scale is full size
        var prop = CreatePropOnMap(map, coid: 91001, cbid: 9101, position: new Vector3(0f, 2f, 0f), scale: 2f);

        var q = new CompositeVehicleCollisionQuery(terrain, map);
        // Cast down from Y=5 through the box top (Y=3).
        Assert.IsTrue(q.CastRay(0f, 5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsFalse(hit.IsTerrain, "object hit must set IsTerrain=false");
        Assert.AreEqual(0.2f, hit.Fraction, Tol); // (5-3)/10
        Assert.AreEqual(3f, hit.PointY, Tol);
        Assert.IsTrue(hit.NormalY > 0.9f, $"expected upward face normal, got {hit.NormalY}");
        Assert.AreEqual(0f, hit.NormalX, 1e-3f);
        Assert.AreEqual(0f, hit.NormalZ, 1e-3f);
        Assert.IsNotNull(prop);
    }

    [TestMethod]
    public void CastRay_ObjectCloserThanTerrain_PrefersObject()
    {
        // Terrain at Y=0; box top at Y=2 → object nearer for a downward cast from Y=4.
        var terrain = Flat(0f);
        var map = CreateMap();
        RegisterPhysicsProp(cbid: 9102, scale: 2f);
        CreatePropOnMap(map, coid: 91002, cbid: 9102, position: new Vector3(0f, 1f, 0f), scale: 2f);

        var q = new CompositeVehicleCollisionQuery(terrain, map);
        Assert.IsTrue(q.CastRay(0f, 4f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsFalse(hit.IsTerrain);
        Assert.AreEqual(0.2f, hit.Fraction, Tol); // (4-2)/10
        Assert.AreEqual(2f, hit.PointY, Tol);
    }

    [TestMethod]
    public void CastRay_TerrainCloserThanObject_PrefersTerrain()
    {
        // Terrain at Y=1; object box sits higher in XZ but off to the side so ray misses it.
        // A second scenario: object under terrain plane but ray hits terrain first.
        // Prop box center (0, -5, 0) half=1 → top at -4, terrain plane Y=0 is nearer from Y=5.
        var terrain = Flat(0f);
        var map = CreateMap();
        RegisterPhysicsProp(cbid: 9103, scale: 2f);
        CreatePropOnMap(map, coid: 91003, cbid: 9103, position: new Vector3(0f, -5f, 0f), scale: 2f);

        var q = new CompositeVehicleCollisionQuery(terrain, map);
        Assert.IsTrue(q.CastRay(0f, 5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsTrue(hit.IsTerrain);
        Assert.AreEqual(0.5f, hit.Fraction, Tol); // (5-0)/10
        Assert.AreEqual(0f, hit.PointY, Tol);
    }

    [TestMethod]
    public void CastRay_NonCollidableProp_IsIgnored_FallsBackToTerrain()
    {
        var terrain = Flat(0f);
        var map = CreateMap();
        RegisterObjectProp(cbid: 9104, collidable: false, scale: 4f);
        CreatePropOnMap(map, coid: 91004, cbid: 9104, position: new Vector3(0f, 2f, 0f), scale: 4f);

        var q = new CompositeVehicleCollisionQuery(terrain, map);
        Assert.IsTrue(q.CastRay(0f, 5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsTrue(hit.IsTerrain, "non-collidable prop must not produce an object hit");
        Assert.AreEqual(0f, hit.PointY, Tol);
    }

    [TestMethod]
    public void CastRay_MissObject_ReturnsTerrain()
    {
        var terrain = Flat(1f);
        var map = CreateMap();
        RegisterPhysicsProp(cbid: 9105, scale: 1f);
        // Prop far in XZ from the cast origin.
        CreatePropOnMap(map, coid: 91005, cbid: 9105, position: new Vector3(50f, 5f, 50f), scale: 1f);

        var q = new CompositeVehicleCollisionQuery(terrain, map);
        Assert.IsTrue(q.CastRay(0f, 5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsTrue(hit.IsTerrain);
        Assert.AreEqual(1f, hit.PointY, Tol);
    }

    [TestMethod]
    public void CastRay_NullMap_DelegatesToTerrainOnly()
    {
        var terrain = Flat(3f);
        var q = new CompositeVehicleCollisionQuery(terrain, map: null);
        Assert.IsTrue(q.CastRay(0f, 5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsTrue(hit.IsTerrain);
        Assert.AreEqual(3f, hit.PointY, Tol);
    }

    [TestMethod]
    public void CastRay_ExcludesSelfVehicle_FromObjectPass()
    {
        var terrain = Flat(0f);
        var map = CreateMap();
        AssetManagerTestHelper.RegisterVehicleCloneBase(9201);
        var cb = (AutoCore.Game.CloneBases.CloneBaseVehicle)AssetManager.Instance.GetCloneBase(9201)!;
        // SkirtExtents large enough that a cast from chassis would hit self.
        var vs = cb.VehicleSpecific;
        vs.SkirtExtents = new Vector3(2f, 1f, 3f);
        cb.VehicleSpecific = vs;

        var self = new Vehicle();
        self.SetCoid(92001, true);
        self.LoadCloneBase(9201);
        self.Position = new Vector3(0f, 1f, 0f);
        self.Scale = 1f;
        self.SetMap(map);

        var q = new CompositeVehicleCollisionQuery(terrain, map, excludeSelf: self);
        Assert.IsTrue(q.CastRay(0f, 5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsTrue(hit.IsTerrain, "own chassis proxy must not steal the wheel cast");
    }

    [TestMethod]
    public void CastRay_OtherVehicle_UsesSkirtExtentsProxy()
    {
        var terrain = Flat(0f);
        var map = CreateMap();
        AssetManagerTestHelper.RegisterVehicleCloneBase(9202);
        var cb = (AutoCore.Game.CloneBases.CloneBaseVehicle)AssetManager.Instance.GetCloneBase(9202)!;
        var vs = cb.VehicleSpecific;
        vs.SkirtExtents = new Vector3(1f, 0.5f, 1f);
        cb.VehicleSpecific = vs;

        var other = new Vehicle();
        other.SetCoid(92002, true);
        other.LoadCloneBase(9202);
        other.Position = new Vector3(0f, 2f, 0f);
        other.Scale = 1f;
        other.SetMap(map);

        var q = new CompositeVehicleCollisionQuery(terrain, map);
        Assert.IsTrue(q.CastRay(0f, 5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsFalse(hit.IsTerrain);
        // Top of box at 2 + 0.5 = 2.5 → frac = (5-2.5)/10 = 0.25
        Assert.AreEqual(0.25f, hit.Fraction, Tol);
        Assert.AreEqual(2.5f, hit.PointY, Tol);
    }

    [TestMethod]
    public void BuildCollisionQuery_FlagOff_ReturnsTerrainOnly()
    {
        Assert.IsFalse(ServerConfig.CompositeWheelCollisionEnabled);
        var map = CreateMap();
        RegisterPhysicsProp(cbid: 9110, scale: 4f);
        CreatePropOnMap(map, coid: 91100, cbid: 9110, position: new Vector3(0f, 2f, 0f), scale: 4f);

        var q = NpcVehiclePhysicsController.BuildCollisionQuery(map, fallbackGroundY: 0f);
        Assert.IsInstanceOfType(q, typeof(TerrainHeightfieldCollisionQuery));
        // Flat fallback plane (no heightfield on CreateForTests map) at Y=0.
        Assert.IsTrue(q.CastRay(0f, 5f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsTrue(hit.IsTerrain);
        Assert.AreEqual(0f, hit.PointY, Tol);
    }

    [TestMethod]
    public void BuildCollisionQuery_FlagOn_UsesCompositeAndHitsProp()
    {
        ServerConfig.CompositeWheelCollisionEnabled = true;
        var map = CreateMap();
        RegisterPhysicsProp(cbid: 9111, scale: 2f);
        CreatePropOnMap(map, coid: 91101, cbid: 9111, position: new Vector3(0f, 1f, 0f), scale: 2f);

        var q = NpcVehiclePhysicsController.BuildCollisionQuery(map, fallbackGroundY: 0f);
        Assert.IsInstanceOfType(q, typeof(CompositeVehicleCollisionQuery));
        Assert.IsTrue(q.CastRay(0f, 4f, 0f, 0f, -1f, 0f, 10f, out var hit));
        Assert.IsFalse(hit.IsTerrain);
        Assert.AreEqual(2f, hit.PointY, Tol); // center 1 + half 1
    }

    [TestMethod]
    public void TryIntersectAabbSegment_HitsTopFace()
    {
        // Pure math seam: segment (0,5,0)→(0,-5,0) against box center (0,0,0) half (1,1,1)
        Assert.IsTrue(CompositeVehicleCollisionQuery.TryIntersectAabbSegment(
            originX: 0f, originY: 5f, originZ: 0f,
            deltaX: 0f, deltaY: -10f, deltaZ: 0f,
            centerX: 0f, centerY: 0f, centerZ: 0f,
            halfX: 1f, halfY: 1f, halfZ: 1f,
            out var t, out var nx, out var ny, out var nz));
        Assert.AreEqual(0.4f, t, Tol); // y=1 at t=0.4 of [5→-5]
        Assert.AreEqual(0f, nx, 1e-3f);
        Assert.IsTrue(ny > 0.9f);
        Assert.AreEqual(0f, nz, 1e-3f);
    }

    // --- helpers ---

    private static TerrainHeightfieldCollisionQuery Flat(float y)
        => new((float x, float z, out float h) => { h = y; return true; });

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = 812,
            MapFileName = "tm_cw_composite",
            DisplayName = "cw",
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static void RegisterPhysicsProp(int cbid, float scale)
    {
        AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.ObjectGraphicsPhysics);
        var cb = (AutoCore.Game.CloneBases.CloneBaseObject)AssetManager.Instance.GetCloneBase(cbid)!;
        cb.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            MinHitPoints = 1,
            MaxHitPoint = 50,
            Flags = 1,
            Scale = scale,
        };
    }

    private static void RegisterObjectProp(int cbid, bool collidable, float scale)
    {
        AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.Object);
        var cb = (AutoCore.Game.CloneBases.CloneBaseObject)AssetManager.Instance.GetCloneBase(cbid)!;
        cb.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            MinHitPoints = 1,
            MaxHitPoint = 50,
            Flags = (short)(collidable ? 1 : 0),
            Scale = scale,
        };
    }

    private static GraphicsObject CreatePropOnMap(SectorMap map, long coid, int cbid, Vector3 position, float scale)
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.SetCoid(coid, false);
        prop.LoadCloneBase(cbid);
        prop.InitializeHealthForTests(50);
        prop.Position = position;
        prop.Scale = scale;
        prop.SetInvincible(false);
        prop.SetMap(map);
        return prop;
    }
}
