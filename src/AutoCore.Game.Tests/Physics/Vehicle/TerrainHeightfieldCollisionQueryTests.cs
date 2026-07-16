using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Map;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Tests.Map;

/// <summary>
/// Synthetic height-function tests for <see cref="TerrainHeightfieldCollisionQuery"/>.
/// Peer WheelCollide suite covers basic flat/miss/slope; this file deepens edge cases
/// and MapTerrainHeightfield wrapping without duplicating the production type.
/// </summary>
[TestClass]
public class TerrainHeightfieldCollisionQueryTests
{
    private const float Tol = 1e-5f;

    private static TerrainHeightfieldCollisionQuery Flat(float y)
        => new((float x, float z, out float h) => { h = y; return true; });

    private static TerrainHeightfieldCollisionQuery Synthetic(
        Func<float, float, float> height,
        float normalEps = 0.5f)
        => new((float x, float z, out float h) => { h = height(x, z); return true; }, normalEps);

    [TestMethod]
    public void CastRay_MaxDistanceZeroOrNegative_Misses()
    {
        var q = Flat(0f);
        Assert.IsFalse(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, 0f, out _));
        Assert.IsFalse(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, -1f, out _));
    }

    [TestMethod]
    public void CastRay_HorizontalDirection_Misses()
    {
        // dirY == 0 → no intersection with horizontal height plane
        var q = Flat(0f);
        Assert.IsFalse(q.CastRay(0f, 1f, 0f, 1f, 0f, 0f, 10f, out _));
    }

    [TestMethod]
    public void CastRay_OriginOnTerrain_FractionZero()
    {
        var q = Flat(1f);
        Assert.IsTrue(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, 2f, out var hit));
        Assert.AreEqual(0f, hit.Fraction, Tol);
        Assert.AreEqual(1f, hit.PointY, Tol);
        Assert.IsTrue(hit.IsTerrain);
    }

    [TestMethod]
    public void CastRay_OriginBelowTerrain_Downward_HitsAtFractionZero()
    {
        // Live bug: origin under ground + dir down used to miss → free fall through map.
        var q = Flat(5f);
        Assert.IsTrue(q.CastRay(0f, 2f, 0f, 0f, -1f, 0f, 3f, out var hit));
        Assert.AreEqual(0f, hit.Fraction, Tol);
        Assert.AreEqual(5f, hit.PointY, Tol);
        Assert.IsTrue(hit.NormalY > 0.5f);
        Assert.IsTrue(hit.IsTerrain);
    }

    [TestMethod]
    public void CastRay_UpwardFromBelow_HitsWhenWithinRange()
    {
        // origin Y=0, terrain Y=1, dir (0,+1,0), maxDist=2 → frac = 1/2
        var q = Flat(1f);
        Assert.IsTrue(q.CastRay(0f, 0f, 0f, 0f, 1f, 0f, 2f, out var hit));
        Assert.AreEqual(0.5f, hit.Fraction, Tol);
        Assert.AreEqual(1f, hit.PointY, Tol);
    }

    [TestMethod]
    public void CastRay_LinearSlopeInZ_NormalHasZComponent()
    {
        // y = 0.2 * z → rising +Z; normal should have -Z component
        var q = Synthetic((x, z) => 0.2f * z, normalEps: 0.5f);
        Assert.IsTrue(q.CastRay(0f, 2f, 0f, 0f, -1f, 0f, 5f, out var hit));
        Assert.IsTrue(hit.NormalZ < -0.1f, $"expected -Z normal, got {hit.NormalZ}");
        Assert.IsTrue(hit.NormalY > 0.9f);
        var len = MathF.Sqrt(hit.NormalX * hit.NormalX + hit.NormalY * hit.NormalY + hit.NormalZ * hit.NormalZ);
        Assert.AreEqual(1f, len, 1e-4f);
    }

    [TestMethod]
    public void CastRay_LinearPlane_ExactHeightAndFraction()
    {
        // y = 2 + 0.05*x − 0.03*z; pure vertical cast at (4, ?, −2)
        float Height(float x, float z) => 2f + 0.05f * x - 0.03f * z;
        const float ox = 4f, oz = -2f;
        var terrainY = Height(ox, oz);
        const float originY = 5f;
        const float maxDist = 10f;
        var expectedTravel = originY - terrainY;
        var expectedFrac = expectedTravel / maxDist;

        var q = Synthetic(Height);
        Assert.IsTrue(q.CastRay(ox, originY, oz, 0f, -1f, 0f, maxDist, out var hit));
        Assert.AreEqual(expectedFrac, hit.Fraction, 1e-4f);
        Assert.AreEqual(ox, hit.PointX, Tol);
        Assert.AreEqual(terrainY, hit.PointY, 1e-4f);
        Assert.AreEqual(oz, hit.PointZ, Tol);
        Assert.IsTrue(hit.IsTerrain);

        // Normal for plane: (−∂y/∂x, 1, −∂y/∂z) = (−0.05, 1, 0.03) normalized
        var nx = -0.05f;
        var ny = 1f;
        var nz = 0.03f;
        var nLen = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        nx /= nLen;
        ny /= nLen;
        nz /= nLen;
        Assert.AreEqual(nx, hit.NormalX, 1e-3f);
        Assert.AreEqual(ny, hit.NormalY, 1e-3f);
        Assert.AreEqual(nz, hit.NormalZ, 1e-3f);
    }

    [TestMethod]
    public void CastRay_NonVerticalRay_RefinesAgainstFlatHeight()
    {
        // Flat y=0.5; ray from (0,1,0) with slight +X tilt still hits the height plane.
        // dir not unit-normalized intentionally? Interface says unit — use unit vector.
        var dirX = 0.2f;
        var dirY = -MathF.Sqrt(1f - dirX * dirX);
        var dirZ = 0f;
        const float maxDist = 5f;
        var q = Flat(0.5f);

        Assert.IsTrue(q.CastRay(0f, 1f, 0f, dirX, dirY, dirZ, maxDist, out var hit));
        Assert.AreEqual(0.5f, hit.PointY, 1e-3f);
        // fraction = (0.5 - 1) / dirY / maxDist
        var expectedFrac = (0.5f - 1f) / dirY / maxDist;
        Assert.AreEqual(expectedFrac, hit.Fraction, 1e-3f);
        Assert.IsTrue(hit.PointX > 0f, "tilted ray should travel +X before hit");
    }

    [TestMethod]
    public void CastRay_NonVerticalRay_FirstSampleMiss_UsesEndSample()
    {
        // Deep hole under origin (still below origin so no penetration contact); ray too short
        // to reach the floor. Tilted end XZ lands on a plateau within range.
        // Sample: y = -100 when |x|<0.5 else y = 0.5
        TerrainHeightfieldCollisionQuery.SampleHeight sample = (float x, float z, out float y) =>
        {
            y = MathF.Abs(x) < 0.5f ? -100f : 0.5f;
            return true;
        };
        var q = new TerrainHeightfieldCollisionQuery(sample);

        // Vertical: need ~102 units down, maxDist 5 → miss (origin above terrain under hole)
        Assert.IsFalse(q.CastRay(0f, 2f, 0f, 0f, -1f, 0f, 5f, out _));

        // Tilted toward +X so end XZ is past the hole edge onto y=0.5 plateau
        var dirX = 0.8f;
        var dirY = -MathF.Sqrt(1f - dirX * dirX);
        Assert.IsTrue(q.CastRay(0f, 2f, 0f, dirX, dirY, 0f, 5f, out var hit));
        Assert.IsTrue(hit.IsTerrain);
        Assert.IsTrue(hit.PointY < 3f);
        Assert.IsTrue(hit.Fraction > 0f, "should hit along ray, not penetration frac0");
    }

    [TestMethod]
    public void CastRay_MapTerrainHeightfield_WrapsBilinearSample()
    {
        // 2x2 flat height16=256 → world Y = 256 * 4/256 = 4
        using var tga = MapTerrainHeightfieldTests.BuildHeightTga(2, 2, new ushort[] { 256, 256, 256, 256 });
        Assert.IsTrue(MapTerrainHeightfield.TryLoad(tga, 2, 2, 10f, out var field, out var err), err);

        var q = new TerrainHeightfieldCollisionQuery(field);
        Assert.IsTrue(q.CastRay(5f, 6f, 5f, 0f, -1f, 0f, 5f, out var hit));
        Assert.AreEqual(4f, hit.PointY, 1e-3f);
        Assert.AreEqual((6f - 4f) / 5f, hit.Fraction, 1e-3f);
        Assert.IsTrue(hit.IsTerrain);
        Assert.IsTrue(hit.NormalY > 0.99f);
    }

    [TestMethod]
    public void Ctor_NullSample_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TerrainHeightfieldCollisionQuery((TerrainHeightfieldCollisionQuery.SampleHeight)null!));
    }

    [TestMethod]
    public void Ctor_NullHeightfield_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TerrainHeightfieldCollisionQuery((MapTerrainHeightfield)null!));
    }

    [TestMethod]
    public void Ctor_NonPositiveNormalEpsilon_UsesDefault()
    {
        // Still samples; should not throw and should produce unit up normal on flat
        var q = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0f; return true; },
            normalSampleEpsilon: 0f);

        Assert.IsTrue(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, 2f, out var hit));
        Assert.AreEqual(0f, hit.NormalX, 1e-3f);
        Assert.AreEqual(1f, hit.NormalY, 1e-3f);
        Assert.AreEqual(0f, hit.NormalZ, 1e-3f);
    }

    [TestMethod]
    public void CastRay_SampleFailsAtOrigin_Misses()
    {
        var calls = 0;
        var q = new TerrainHeightfieldCollisionQuery((float x, float z, out float y) =>
        {
            calls++;
            y = 0f;
            return false;
        });
        Assert.IsFalse(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, 5f, out _));
        Assert.IsTrue(calls >= 1);
    }
}
