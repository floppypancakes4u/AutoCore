using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Sector.Network;

/// <summary>
/// Heavy regression for foreign NPC path pace after the AcceptDistance snap fix.
/// Live capture (Final Exam / Gunny): |v|≈18 predicted ~1u/50ms but dist jumped ~14u every
/// few hundred ms because path AcceptDistance≈15 teleported the remaining gap on arrival.
/// These tests lock continuous stepLen motion + dense sector timing + pose stream contracts.
/// </summary>
[TestClass]
public class NpcPathPaceRegressionTests
{
    private const float Tol = 1e-3f;
    private const int ContId = 942;
    private const long PathCoid = 94210;
    private const long VehicleCoid = 94201;

    [TestInitialize]
    public void SetUp()
    {
        TriggerManager.Instance.ClearAllForTests();
        SoftNpcPathMotion.Enabled = false;
        GhostVehicle.EnableClientSidePathVisual = false;
    }

    [TestCleanup]
    public void TearDown()
    {
        TriggerManager.Instance.ClearAllForTests();
        SoftNpcPathMotion.Enabled = false;
        GhostVehicle.EnableClientSidePathVisual = false;
    }

    #region Pure stepper multi-tick pace

    [TestMethod]
    public void MultiTick_LargeAcceptRings_NeverExceedsStepLenPerTick()
    {
        // Retail-scale accept rings (15u) at cruise 18 u/s with 50ms sector ticks.
        const float speed = 18f;
        const float dt = 0.05f;
        const float accept = 15f;
        var stepLen = speed * dt; // 0.9u

        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(0f, 0f, 0f), accept, wait: 0));
        path.Points.Add(Wp(new Vector3(100f, 0f, 0f), accept, wait: 0));
        path.Points.Add(Wp(new Vector3(100f, 0f, 100f), accept, wait: 0));
        path.Points.Add(Wp(new Vector3(0f, 0f, 100f), accept, wait: 0));

        var pos = new Vector3(-20f, 0f, 0f); // outside first ring
        var index = 0;
        var dir = 1;
        long waitUntil = 0;
        long now = 1000;
        var maxStep = 0f;
        var arrivals = 0;

        for (var tick = 0; tick < 800; tick++)
        {
            var before = pos;
            var result = NpcPathFollower.Step(pos, path, index, dir, waitUntil, now, speed, dt);
            pos = result.NewPosition;
            index = result.NewIndex;
            dir = result.NewDirection;
            waitUntil = result.WaitUntilMs;
            now += 50;

            var moved = XzDistance(before, pos);
            if (moved > maxStep)
                maxStep = moved;

            Assert.IsTrue(moved <= stepLen + Tol,
                $"Tick {tick}: moved {moved:F4}u > stepLen {stepLen} (teleport regression)");

            if (result.Arrived)
                arrivals++;
        }

        Assert.IsTrue(arrivals >= 3, $"Expected several waypoint accepts over the run, got {arrivals}");
        Assert.IsTrue(maxStep > 0f, "Must actually move");
        Assert.IsTrue(maxStep <= stepLen + Tol, $"Max single-tick step {maxStep} exceeds stepLen {stepLen}");
    }

    [TestMethod]
    public void MultiTick_AcceptArrival_AdvancesIndexWithoutClosingFullGap()
    {
        // Start 14u inside a 15u accept ring — classic capture: old code teleported 14u.
        const float speed = 18f;
        const float dt = 0.05f;
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(100f, 0f, 0f), accept: 15f, wait: 0, reaction: 77));
        path.Points.Add(Wp(new Vector3(200f, 0f, 0f), accept: 15f, wait: 0));

        var start = new Vector3(86f, 0f, 0f);
        var r0 = NpcPathFollower.Step(start, path, 0, 1, 0, 1000, speed, dt);

        Assert.IsTrue(r0.Arrived);
        Assert.AreEqual(1, r0.NewIndex);
        Assert.AreEqual(77L, r0.FireReactionCoid);
        var moved0 = r0.NewPosition.X - start.X;
        Assert.IsTrue(moved0 > 0f && moved0 <= speed * dt + Tol,
            $"Accept tick must step ≤{speed * dt}, moved {moved0}");
        Assert.IsTrue(XzDistance(r0.NewPosition, path.Points[0].Position) > 1f,
            "Must leave a residual gap to the accepted waypoint (no full snap)");

        // Next ticks aim at index 1 and keep stepLen caps.
        var pos = r0.NewPosition;
        var index = r0.NewIndex;
        for (var i = 0; i < 20; i++)
        {
            var before = pos;
            var r = NpcPathFollower.Step(pos, path, index, 1, 0, 1000 + (i + 1) * 50, speed, dt);
            pos = r.NewPosition;
            index = r.NewIndex;
            Assert.IsTrue(XzDistance(before, pos) <= speed * dt + Tol);
        }
    }

    [TestMethod]
    public void MultiTick_GeometricArrival_StillSnapsWhenStepCoversRemainder()
    {
        // When remaining distance ≤ stepLen, snap is correct (not AcceptDistance teleport).
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(10f, 0f, 0f), accept: 15f, wait: 0));
        path.Points.Add(Wp(new Vector3(50f, 0f, 0f), accept: 1f, wait: 0));

        var result = NpcPathFollower.Step(
            new Vector3(9.5f, 0f, 0f), path, 0, 1, 0, 1000, speed: 18f, dt: 0.05f);

        Assert.IsTrue(result.Arrived);
        Assert.AreEqual(10f, result.NewPosition.X, Tol, "true geometric arrival still lands on waypoint");
        Assert.AreEqual(1, result.NewIndex);
    }

    [TestMethod]
    public void MultiTick_WaitOnAccept_ZerosVelocityAndHoldsOnSubsequentTicks()
    {
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(100f, 0f, 0f), accept: 15f, wait: 2000));
        path.Points.Add(Wp(new Vector3(200f, 0f, 0f), accept: 1f, wait: 0));

        var arrive = NpcPathFollower.Step(
            new Vector3(90f, 0f, 0f), path, 0, 1, 0, 5000, speed: 18f, dt: 0.05f);

        Assert.IsTrue(arrive.Arrived);
        Assert.AreEqual(0f, arrive.Velocity.X, Tol);
        Assert.AreEqual(0f, arrive.Velocity.Z, Tol);
        Assert.AreEqual(7000L, arrive.WaitUntilMs);

        var hold = NpcPathFollower.Step(
            arrive.NewPosition, path, arrive.NewIndex, arrive.NewDirection,
            arrive.WaitUntilMs, nowMs: 6000, speed: 18f, dt: 0.05f);

        Assert.IsFalse(hold.Arrived);
        Assert.AreEqual(arrive.NewPosition.X, hold.NewPosition.X, Tol);
        Assert.AreEqual(0f, hold.Velocity.X + hold.Velocity.Z, Tol);
    }

    [TestMethod]
    public void MultiTick_ZeroWaitAccept_KeepsCruiseVelocity()
    {
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(100f, 0f, 0f), accept: 15f, wait: 0));
        path.Points.Add(Wp(new Vector3(200f, 0f, 0f), accept: 1f, wait: 0));

        var result = NpcPathFollower.Step(
            new Vector3(90f, 0f, 0f), path, 0, 1, 0, 1000, speed: 18f, dt: 0.05f);

        Assert.IsTrue(result.Arrived);
        Assert.IsTrue(result.Velocity.X > 1f,
            "Zero-wait accept must keep non-zero cruise velocity for ghost throttle/Havok");
    }

    [TestMethod]
    public void MultiTick_PredictedVsActual_DisplacementTracksSpeedTimesDt()
    {
        // Over a long cruise outside accept rings, total XZ should ≈ speed * time.
        const float speed = 18f;
        const float dt = 0.05f;
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(10_000f, 0f, 0f), accept: 1f, wait: 0));

        var pos = new Vector3(0f, 0f, 0f);
        var ticks = 100; // 5s
        for (var i = 0; i < ticks; i++)
        {
            var r = NpcPathFollower.Step(pos, path, 0, 1, 0, 1000 + i * 50, speed, dt);
            pos = r.NewPosition;
            Assert.IsFalse(r.Arrived);
        }

        var expected = speed * dt * ticks;
        Assert.AreEqual(expected, pos.X, 0.05f,
            $"Cruise displacement should match |v|*dt*ticks ({expected}), got {pos.X}");
    }

    [TestMethod]
    public void Step_NegativeAcceptDistance_TreatedAsZero()
    {
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(10f, 0f, 0f), accept: -5f, wait: 0));

        // 2u away, step 0.9 — must not arrive via negative accept.
        var result = NpcPathFollower.Step(
            new Vector3(8f, 0f, 0f), path, 0, 1, 0, 1000, speed: 18f, dt: 0.05f);

        Assert.IsFalse(result.Arrived);
        Assert.AreEqual(8f + 0.9f, result.NewPosition.X, Tol);
    }

    [TestMethod]
    public void Step_NegativeDt_DoesNotMoveOrTeleport()
    {
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(100f, 0f, 0f), accept: 15f, wait: 0));

        var start = new Vector3(50f, 0f, 0f);
        var result = NpcPathFollower.Step(start, path, 0, 1, 0, 1000, speed: 18f, dt: -0.05f);

        Assert.AreEqual(start.X, result.NewPosition.X, Tol);
        Assert.IsFalse(result.Arrived);
    }

    #endregion

    #region Soft motion + accept

    [TestMethod]
    public void SoftMotion_OnZeroWaitAccept_CarriesVelocityAfterHardStep()
    {
        SoftNpcPathMotion.Enabled = true;
        var path = new MapPathTemplate { ReverseDirection = false };
        path.Points.Add(Wp(new Vector3(100f, 0f, 0f), accept: 15f, wait: 0));
        path.Points.Add(Wp(new Vector3(200f, 0f, 0f), accept: 1f, wait: 0));

        var hard = NpcPathFollower.Step(
            new Vector3(90f, 0f, 0f), path, 0, 1, 0, 1000, speed: 18f, dt: 0.05f);
        Assert.IsTrue(hard.Arrived);

        var soft = SoftNpcPathMotion.Apply(
            hard,
            previousPosition: new Vector3(90f, 0f, 0f),
            previousRotation: Quaternion.Default,
            speed: 18f,
            dt: 0.05f,
            path: path,
            nowMs: 1000);

        var softSpeed = MathF.Sqrt((soft.Velocity.X * soft.Velocity.X) + (soft.Velocity.Z * soft.Velocity.Z));
        Assert.IsTrue(softSpeed > 10f, $"Soft zero-wait accept must carry cruise speed, got {softSpeed}");
        Assert.IsTrue(XzDistance(new Vector3(90f, 0f, 0f), soft.NewPosition) <= 18f * 0.05f + Tol);
    }

    #endregion

    #region NpcTicker integration (sector 50ms dt)

    [TestMethod]
    public void NpcTicker_FiftyMsTicks_LargeAccept_NeverTeleports()
    {
        const float dt = 0.05f;
        const float speed = NpcTicker.DefaultVehicleSpeed; // 12 u/s
        var stepLen = speed * dt;

        var map = CreateMap();
        var path = SeedPath(map, PathCoid);
        path.Points.Add(Wp(new Vector3(0f, 0f, 0f), accept: 15f, wait: 0));
        path.Points.Add(Wp(new Vector3(80f, 0f, 0f), accept: 15f, wait: 0));
        path.Points.Add(Wp(new Vector3(80f, 0f, 80f), accept: 15f, wait: 0));

        var patrol = PlaceVehicle(map, VehicleCoid, new Vector3(-10f, 0f, 0f), npcAi: true);
        patrol.CoidCurrentPath = PathCoid;
        patrol.NpcAi.CombatState = HBAICombatState.IdlePatrol;

        long now = 10_000;
        for (var tick = 0; tick < 400; tick++)
        {
            var before = patrol.Position;
            NpcTicker.Tick(map, now, dt);
            now += 50;

            var moved = XzDistance(before, patrol.Position);
            Assert.IsTrue(moved <= stepLen + Tol,
                $"Ticker tick {tick}: moved {moved:F4} > stepLen {stepLen}");
        }

        Assert.IsTrue(patrol.Position.X > -10f || patrol.Position.Z > 0f, "Must progress along path");
    }

    [TestMethod]
    public void NpcTicker_HoldingPathVehicle_ReDirtiesPositionMask()
    {
        // Live: zero-vel hold dropped GhostZeroUpdateIndex → only ~3 pose packs then silence.
        var map = CreateMap();
        var path = SeedPath(map, PathCoid);
        path.Points.Add(Wp(new Vector3(20f, 0f, 20f), accept: 2f, wait: 0));

        var patrol = PlaceVehicle(map, VehicleCoid, new Vector3(5f, 0f, 5f), npcAi: true);
        patrol.CoidCurrentPath = PathCoid;
        patrol.NpcAi.CombatState = HBAICombatState.IdlePatrol;
        patrol.NpcAi.WaitUntilMs = 20_000L;

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        patrol.CreateGhost();
        connection.ActivateGhosting();
        connection.ObjectLocalScopeAlways(patrol.Ghost);

        var ghostInfo = patrol.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo);
        ghostInfo.UpdateMask = 0;

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.05f);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(5f, patrol.Position.X, Tol, "must not move while waiting");
        Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask,
            "Holding path vehicle must re-dirty pose so TNL keeps streaming packs");
    }

    [TestMethod]
    public void NpcTicker_AcceptInsideRing_AdvancesPathWithoutFullSnap()
    {
        var map = CreateMap();
        var path = SeedPath(map, PathCoid);
        path.Points.Add(Wp(new Vector3(100f, 0f, 0f), accept: 15f, wait: 0, reaction: 0));
        path.Points.Add(Wp(new Vector3(200f, 0f, 0f), accept: 1f, wait: 0));

        // 10u inside accept of WP0. NpcAi must exist before SetMap (EnterMap registers it).
        var patrol = PlaceVehicle(map, VehicleCoid, new Vector3(90f, 0f, 0f), npcAi: true);
        patrol.CoidCurrentPath = PathCoid;
        patrol.NpcAi.CombatState = HBAICombatState.IdlePatrol;
        patrol.NpcAi.PathIndex = 0;
        patrol.NpcAi.PathDirection = 1;

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.05f);

        Assert.AreEqual(1, patrol.NpcAi.PathIndex, "accept must advance cursor");
        var moved = patrol.Position.X - 90f;
        Assert.IsTrue(moved > 0f && moved <= NpcTicker.DefaultVehicleSpeed * 0.05f + Tol,
            $"must step not teleport; moved {moved}");
        Assert.IsTrue(MathF.Abs(patrol.Position.X - 100f) > 1f,
            "must not snap onto the accepted waypoint when still outside geometric range");
    }

    #endregion

    #region Sector / ghost contracts

    [TestMethod]
    public void SectorServer_MainLoopTime_IsFiftyMilliseconds()
    {
        // Dense path steps + TNL ghost floor align at 50ms (was 100ms → larger snaps).
        Assert.AreEqual(50, SectorServer.MainLoopTime);
    }

    [TestMethod]
    public void SectorGhostRateFloor_MaxPeriod_IsFiftyMilliseconds()
    {
        Assert.AreEqual(50u, TNLConnection.SectorGhostMaxSendPeriodMs);
    }

    [TestMethod]
    public void CruiseThrottle_ConstantSpeed_StaysNearOneAcrossManyTicks()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(94299, true);
        vehicle.ApplyServerMove(new Vector3(0, 0, 0), Quaternion.Default, new Vector3(0, 0, 18f), dt: 0.05f);

        for (var i = 1; i <= 40; i++)
        {
            vehicle.ApplyServerMove(
                new Vector3(0, 0, i * 0.9f),
                Quaternion.Default,
                new Vector3(0, 0, 18f),
                dt: 0.05f);
            Assert.IsTrue(vehicle.Acceleration > 0.5f,
                $"Tick {i}: cruise throttle collapsed to {vehicle.Acceleration}");
        }
    }

    [TestMethod]
    public void ShouldStreamPose_PathPatrolZeroVel_True_NoPathFalse()
    {
        var withPath = new Vehicle();
        withPath.SetCoid(94300, true);
        withPath.CoidCurrentPath = 1;
        withPath.NpcAi = new NpcAiState { CombatState = HBAICombatState.IdlePatrol };
        withPath.ApplyServerMove(new Vector3(0, 0, 0), Quaternion.Default, new Vector3(0, 0, 0));

        var noPath = new Vehicle();
        noPath.SetCoid(94301, true);
        noPath.CoidCurrentPath = 0;
        noPath.ApplyServerMove(new Vector3(0, 0, 0), Quaternion.Default, new Vector3(0, 0, 0));

        Assert.IsTrue(GhostVehicle.ShouldStreamPose(withPath));
        Assert.IsFalse(GhostVehicle.ShouldStreamPose(noPath));
        Assert.IsFalse(GhostVehicle.IsMovingForPoseStream(withPath));
    }

    [TestMethod]
    public void ShouldStreamPose_ClientSidePathVisual_SuppressesIdlePatrol()
    {
        GhostVehicle.EnableClientSidePathVisual = true;
        var vehicle = new Vehicle();
        vehicle.SetCoid(94302, true);
        vehicle.CoidCurrentPath = 7;
        vehicle.NpcAi = new NpcAiState { CombatState = HBAICombatState.IdlePatrol };
        vehicle.ApplyServerMove(new Vector3(0, 0, 0), Quaternion.Default, new Vector3(12f, 0, 0));

        Assert.IsTrue(vehicle.ShouldSuppressPatrolPoseGhost());
        Assert.IsFalse(GhostVehicle.ShouldStreamPose(vehicle));
        Assert.IsFalse(GhostVehicle.IsMovingForPoseStream(vehicle));
    }

    #endregion

    #region Helpers

    private static MapPathTemplate.MapPathPoint Wp(
        Vector3 pos, float accept, int wait, long reaction = 0) =>
        new()
        {
            Position = pos,
            AcceptDistance = accept,
            WaitTime = wait,
            ReactionCoid = reaction,
        };

    private static float XzDistance(Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_path_pace_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static MapPathTemplate SeedPath(SectorMap map, long pathCoid)
    {
        var path = new MapPathTemplate { COID = (int)pathCoid, ReverseDirection = false };
        map.MapData.Templates[pathCoid] = path;
        return path;
    }

    private static Vehicle PlaceVehicle(SectorMap map, long coid, Vector3 position, bool npcAi = false)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, false);
        vehicle.Position = position;
        // NpcAi must be set before SetMap so EnterMap registers into NpcAiEntities.
        if (npcAi)
            vehicle.NpcAi = new NpcAiState();
        vehicle.SetMap(map);
        return vehicle;
    }

    #endregion
}
