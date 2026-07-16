using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// TDD for wheel cast + compression from preUpdate 0x64cf20 / wheelCollide 0x64bbd0.
/// Compression (wheel+0xB0): (radius + restLen) · hitFraction − radius; miss → restLen.
/// Evidence: docs/reconstruction/physics/0.5-wheel-collide.md
/// </summary>
[TestClass]
public class HkVehicleWheelCollideTests
{
    private const float Radius = 0.4f;
    private const float RestLen = 0.3f;
    private const float MaxCast = Radius + RestLen; // 0.7

    // --- CastWheel compression goldens (0.5 formula) ---

    [TestMethod]
    public void CastWheel_Miss_YieldsRestLength_NotInContact_FractionOne()
    {
        var contact = HkVehicleWheelCollide.CastWheel(
            NullVehicleCollisionQuery.Instance,
            hardpointX: 0f, hardpointY: 1f, hardpointZ: 0f,
            downDirX: 0f, downDirY: -1f, downDirZ: 0f,
            radius: Radius, restLen: RestLen);

        Assert.IsFalse(contact.InContact);
        Assert.AreEqual(RestLen, contact.Length, 1e-6f);
        Assert.AreEqual(1f, contact.Fraction, 1e-6f);
        // Airborne normal = -downAxis (0.4 miss path)
        Assert.AreEqual(0f, contact.NormalX, 1e-6f);
        Assert.AreEqual(1f, contact.NormalY, 1e-6f);
        Assert.AreEqual(0f, contact.NormalZ, 1e-6f);
        Assert.AreEqual(0f, contact.ClosingSpeed, 1e-6f); // placeholder
    }

    [TestMethod]
    public void CastWheel_FractionOne_Contact_LengthEqualsRestLen()
    {
        // Hit at full cast end: (r+L)*1 - r = L
        var q = new FakeRayQuery(hit: true, fraction: 1f, nx: 0f, ny: 1f, nz: 0f);
        var contact = HkVehicleWheelCollide.CastWheel(
            q, 0f, 1f, 0f, 0f, -1f, 0f, Radius, RestLen);

        Assert.IsTrue(contact.InContact);
        Assert.AreEqual(RestLen, contact.Length, 1e-6f);
        Assert.AreEqual(1f, contact.Fraction, 1e-6f);
        Assert.AreEqual(1f, contact.NormalY, 1e-6f);
    }

    [TestMethod]
    public void CastWheel_FractionHalf_CompressionGolden()
    {
        // (0.4+0.3)*0.5 - 0.4 = -0.05
        var q = new FakeRayQuery(hit: true, fraction: 0.5f, nx: 0f, ny: 1f, nz: 0f);
        var contact = HkVehicleWheelCollide.CastWheel(
            q, 0f, 1f, 0f, 0f, -1f, 0f, Radius, RestLen);

        Assert.IsTrue(contact.InContact);
        Assert.AreEqual(-0.05f, contact.Length, 1e-6f);
        Assert.AreEqual(0.5f, contact.Fraction, 1e-6f);
        // No chassis velocity → closingSpeed 0 (default)
        Assert.AreEqual(0f, contact.ClosingSpeed, 1e-6f);
    }

    // --- ClosingSpeed (wheel+0xB4) from chassis linVel · contact normal ---
    // Sign convention matches suspension damper: <0 compress, >=0 extend.
    // Formula: ClosingSpeed = dot(chassisLinVel, contactNormal)
    // (upward normal + falling vel → negative → compression damp path)

    [TestMethod]
    public void ComputeClosingSpeed_ApproachingGround_IsNegative()
    {
        // Falling onto flat ground: vel=(0,-5,0), normal=(0,1,0) → −5
        float cs = HkVehicleWheelCollide.ComputeClosingSpeed(
            velX: 0f, velY: -5f, velZ: 0f,
            normalX: 0f, normalY: 1f, normalZ: 0f);
        Assert.AreEqual(-5f, cs, 1e-6f);
        Assert.IsTrue(cs < 0f, "approaching ground must select compression damp");
    }

    [TestMethod]
    public void ComputeClosingSpeed_SeparatingFromGround_IsPositive()
    {
        // Rising off ground: vel=(0,+3,0), normal=(0,1,0) → +3
        float cs = HkVehicleWheelCollide.ComputeClosingSpeed(
            velX: 0f, velY: 3f, velZ: 0f,
            normalX: 0f, normalY: 1f, normalZ: 0f);
        Assert.AreEqual(3f, cs, 1e-6f);
        Assert.IsTrue(cs > 0f, "separating must select extension damp");
    }

    [TestMethod]
    public void ComputeClosingSpeed_PureTangentVelocity_IsZero()
    {
        // Sliding along ground: vel along +X, normal +Y → 0
        float cs = HkVehicleWheelCollide.ComputeClosingSpeed(
            velX: 10f, velY: 0f, velZ: -4f,
            normalX: 0f, normalY: 1f, normalZ: 0f);
        Assert.AreEqual(0f, cs, 1e-6f);
    }

    [TestMethod]
    public void ComputeClosingSpeed_SlopedNormal_UsesFullDot()
    {
        // vel=(1,-2,0), n≈ unit(−0.6, 0.8, 0) → 1*(-0.6) + (-2)*0.8 = −2.2
        float cs = HkVehicleWheelCollide.ComputeClosingSpeed(
            velX: 1f, velY: -2f, velZ: 0f,
            normalX: -0.6f, normalY: 0.8f, normalZ: 0f);
        Assert.AreEqual(-2.2f, cs, 1e-5f);
    }

    [TestMethod]
    public void CastWheel_WithLinVelApproaching_SetsNegativeClosingSpeed()
    {
        var q = new FakeRayQuery(hit: true, fraction: 0.5f, nx: 0f, ny: 1f, nz: 0f);
        var contact = HkVehicleWheelCollide.CastWheel(
            q, 0f, 1f, 0f, 0f, -1f, 0f, Radius, RestLen,
            velX: 0f, velY: -2.5f, velZ: 0f);

        Assert.IsTrue(contact.InContact);
        Assert.AreEqual(-2.5f, contact.ClosingSpeed, 1e-6f);
    }

    [TestMethod]
    public void CastWheel_WithLinVelSeparating_SetsPositiveClosingSpeed()
    {
        var q = new FakeRayQuery(hit: true, fraction: 0.5f, nx: 0f, ny: 1f, nz: 0f);
        var contact = HkVehicleWheelCollide.CastWheel(
            q, 0f, 1f, 0f, 0f, -1f, 0f, Radius, RestLen,
            velX: 1f, velY: 4f, velZ: -1f);

        Assert.IsTrue(contact.InContact);
        Assert.AreEqual(4f, contact.ClosingSpeed, 1e-6f); // only Y contributes vs up normal
    }

    [TestMethod]
    public void CastWheel_Miss_WithVelocity_StillZeroClosingSpeed()
    {
        // Miss path always writes +0xB4 = 0 (preUpdate 0x64cf20)
        var contact = HkVehicleWheelCollide.CastWheel(
            NullVehicleCollisionQuery.Instance,
            hardpointX: 0f, hardpointY: 1f, hardpointZ: 0f,
            downDirX: 0f, downDirY: -1f, downDirZ: 0f,
            radius: Radius, restLen: RestLen,
            velX: 0f, velY: -10f, velZ: 0f);

        Assert.IsFalse(contact.InContact);
        Assert.AreEqual(0f, contact.ClosingSpeed, 1e-6f);
    }

    [TestMethod]
    public void CastWheel_FractionZero_LengthEqualsNegRadius()
    {
        // (r+L)*0 - r = -r
        var q = new FakeRayQuery(hit: true, fraction: 0f, nx: 0.1f, ny: 0.99f, nz: 0f);
        var contact = HkVehicleWheelCollide.CastWheel(
            q, 0f, 1f, 0f, 0f, -1f, 0f, Radius, RestLen);

        Assert.IsTrue(contact.InContact);
        Assert.AreEqual(-Radius, contact.Length, 1e-6f);
        Assert.AreEqual(0f, contact.Fraction, 1e-6f);
        Assert.AreEqual(0.1f, contact.NormalX, 1e-6f);
        Assert.AreEqual(0.99f, contact.NormalY, 1e-6f);
    }

    [TestMethod]
    public void CastWheel_PassesHardpointDownAndMaxDistanceToQuery()
    {
        var q = new RecordingRayQuery();
        _ = HkVehicleWheelCollide.CastWheel(
            q,
            hardpointX: 1.5f, hardpointY: 2.5f, hardpointZ: 3.5f,
            downDirX: 0.1f, downDirY: -0.9f, downDirZ: 0.2f,
            radius: Radius, restLen: RestLen);

        Assert.AreEqual(1, q.CallCount);
        Assert.AreEqual(1.5f, q.OriginX, 1e-6f);
        Assert.AreEqual(2.5f, q.OriginY, 1e-6f);
        Assert.AreEqual(3.5f, q.OriginZ, 1e-6f);
        Assert.AreEqual(0.1f, q.DirX, 1e-6f);
        Assert.AreEqual(-0.9f, q.DirY, 1e-6f);
        Assert.AreEqual(0.2f, q.DirZ, 1e-6f);
        Assert.AreEqual(MaxCast, q.MaxDistance, 1e-6f);
    }

    [TestMethod]
    public void CastWheel_NullQuery_TreatedAsMiss()
    {
        var contact = HkVehicleWheelCollide.CastWheel(
            null!, 0f, 1f, 0f, 0f, -1f, 0f, Radius, RestLen);

        Assert.IsFalse(contact.InContact);
        Assert.AreEqual(RestLen, contact.Length, 1e-6f);
    }

    // --- TerrainHeightfieldCollisionQuery ---

    [TestMethod]
    public void HeightfieldQuery_FlatGround_HitFractionAndUpNormal()
    {
        // hardpoint Y=1, terrain Y=0.5, down (0,-1,0), maxDist=0.7 → frac = 0.5/0.7
        var q = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0.5f; return true; });

        Assert.IsTrue(q.CastRay(
            0f, 1f, 0f,
            0f, -1f, 0f,
            MaxCast,
            out var hit));

        Assert.AreEqual(0.5f / MaxCast, hit.Fraction, 1e-5f);
        Assert.AreEqual(0f, hit.PointX, 1e-5f);
        Assert.AreEqual(0.5f, hit.PointY, 1e-5f);
        Assert.AreEqual(0f, hit.PointZ, 1e-5f);
        Assert.AreEqual(0f, hit.NormalX, 1e-4f);
        Assert.IsTrue(hit.NormalY > 0.9f);
        Assert.AreEqual(0f, hit.NormalZ, 1e-4f);
        Assert.IsTrue(hit.IsTerrain);
    }

    [TestMethod]
    public void HeightfieldQuery_TooFarBelow_Misses()
    {
        // hardpoint Y=1, terrain Y=0, maxDist=0.5 → need 1.0 along ray → miss
        var q = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0f; return true; });

        Assert.IsFalse(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, 0.5f, out _));
    }

    [TestMethod]
    public void HeightfieldQuery_OriginBelowTerrain_Downward_HitsAtFractionZero()
    {
        // Penetration: origin under surface + dir down must contact (frac 0), not free-fall miss.
        var q = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 2f; return true; });

        Assert.IsTrue(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, 5f, out var hit));
        Assert.AreEqual(0f, hit.Fraction, 1e-5f);
        Assert.AreEqual(2f, hit.PointY, 1e-5f);
        Assert.IsTrue(hit.NormalY > 0.9f);
        Assert.IsTrue(hit.IsTerrain);
    }

    [TestMethod]
    public void HeightfieldQuery_SampleFails_Misses()
    {
        var q = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0f; return false; });

        Assert.IsFalse(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, 5f, out _));
    }

    [TestMethod]
    public void HeightfieldQuery_SlopedNormal_PointsUpSlope()
    {
        // Terrain: y = 0.1 * x  (rising to +X) → normal should have -X component
        var q = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0.1f * x; return true; },
            normalSampleEpsilon: 0.5f);

        Assert.IsTrue(q.CastRay(0f, 1f, 0f, 0f, -1f, 0f, 5f, out var hit));
        Assert.IsTrue(hit.NormalX < -0.05f, $"expected leftward normal component, got {hit.NormalX}");
        Assert.IsTrue(hit.NormalY > 0.9f);
        // Unit normal
        var len = MathF.Sqrt(hit.NormalX * hit.NormalX + hit.NormalY * hit.NormalY + hit.NormalZ * hit.NormalZ);
        Assert.AreEqual(1f, len, 1e-4f);
    }

    [TestMethod]
    public void CastWheel_ThroughHeightfieldQuery_MatchesCompressionFormula()
    {
        // hardpoint Y=1, terrain 0.65 → travel 0.35, max=0.7, frac=0.5
        // length = 0.7*0.5 - 0.4 = -0.05
        var q = new TerrainHeightfieldCollisionQuery(
            (float x, float z, out float y) => { y = 0.65f; return true; });

        var contact = HkVehicleWheelCollide.CastWheel(
            q, 0f, 1f, 0f, 0f, -1f, 0f, Radius, RestLen);

        Assert.IsTrue(contact.InContact);
        Assert.AreEqual(0.5f, contact.Fraction, 1e-5f);
        Assert.AreEqual(-0.05f, contact.Length, 1e-5f);
    }

    // --- fakes ---

    private sealed class FakeRayQuery : IVehicleCollisionQuery
    {
        private readonly bool _hit;
        private readonly float _fraction;
        private readonly float _nx, _ny, _nz;

        public FakeRayQuery(bool hit, float fraction, float nx, float ny, float nz)
        {
            _hit = hit;
            _fraction = fraction;
            _nx = nx;
            _ny = ny;
            _nz = nz;
        }

        public bool CastRay(
            float originX, float originY, float originZ,
            float dirX, float dirY, float dirZ,
            float maxDistance,
            out VehicleRayHit hit)
        {
            if (!_hit)
            {
                hit = default;
                return false;
            }

            var t = maxDistance * _fraction;
            hit = new VehicleRayHit(
                _fraction,
                originX + dirX * t,
                originY + dirY * t,
                originZ + dirZ * t,
                _nx, _ny, _nz,
                isTerrain: false);
            return true;
        }
    }

    private sealed class RecordingRayQuery : IVehicleCollisionQuery
    {
        public int CallCount;
        public float OriginX, OriginY, OriginZ;
        public float DirX, DirY, DirZ;
        public float MaxDistance;

        public bool CastRay(
            float originX, float originY, float originZ,
            float dirX, float dirY, float dirZ,
            float maxDistance,
            out VehicleRayHit hit)
        {
            CallCount++;
            OriginX = originX;
            OriginY = originY;
            OriginZ = originZ;
            DirX = dirX;
            DirY = dirY;
            DirZ = dirZ;
            MaxDistance = maxDistance;
            hit = default;
            return false;
        }
    }
}
