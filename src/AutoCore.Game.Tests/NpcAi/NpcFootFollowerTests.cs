using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;

/// <summary>
/// Stage 9: <see cref="NpcTicker"/> drives foot creatures (no driver, no vehicle wrapper) over
/// their idle-patrol path the same way it drives vehicles — same <see cref="NpcPathFollower.Step"/>
/// call, same arrival-reaction path — but applies pose via
/// <see cref="Creature.ApplyServerMove(Vector3, Quaternion, Vector3, Vector3)"/> and falls back to a
/// foot-speed default distinct from the vehicle default when the clonebase has none.
/// <para>
/// TargetPosition is <b>not</b> a client interpolation goal, despite the name. RE (2026-08-15):
/// <c>CVOGCreature::DoPositionUpdate</c> @004c6360 does store the wire value into
/// <c>m_vMoveToTarget</c>, but the only reader is <c>CVOGCreature::DoMovement</c> @004c2f40, which
/// delegates to <c>m_pAI-&gt;vtbl+0x30</c> and no-ops when <c>m_pAI == null</c> — and foreign
/// server-driven NPCs have no client HBAI. Remote creature motion comes entirely from the pose
/// snapshot plus dead reckoning, so sending a lookahead here would change nothing. Do not build on
/// this field without re-verifying that reader.
/// </para>
/// </summary>
[TestClass]
public class NpcFootFollowerTests
{
    private const int ContId = 842;
    private const int CreatureCbid = 84_200;
    private const long PatrolPathCoid = 84_210;
    private const long ArrivalReactionCoid = 84_220;
    private const long TargetPathCoid = 84_230;
    private const long PatrolCreatureCoid = 84_201;
    private const long TargetVehicleCoid = 84_202;

    [TestInitialize]
    public void SetUp()
    {
        TriggerManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestCleanup]
    public void TearDown()
    {
        TriggerManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void CreatureTick_AppliesPoseVelocityAndTargetPosition()
    {
        var map = CreateMap();
        var path = SeedMapPath(map, PatrolPathCoid, reverse: false);
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(50f, 3f, 50f),
            AcceptDistance = 1f,
        });

        var creature = PlaceNpcCreature(map, PatrolCreatureCoid, new Vector3(0f, 0f, 0f), speed: 5f);
        creature.CoidCurrentPath = PatrolPathCoid;
        creature.NpcAi.CombatState = HBAICombatState.IdlePatrol;

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.5f);

        // speed 5 * dt 0.5 = 2.5 step, well short of the ~70.7 distance to the waypoint.
        // Full clonebase speed, because that is the rate the client integrates the creature at:
        // CVOGHBAICreatureBase::DoMovement @005cd3b0 uses min(distToGoal, GetCreatureSpeed) and
        // only halves it while m_bWandering, which any server pose clears. Server and client must
        // advance at the same rate or the client outruns the server and snaps back.
        const float expectedStep = 2.5f;
        const float dist = 70.71068f; // sqrt(50^2 + 50^2)
        var expectedX = 50f * (expectedStep / dist);
        var expectedZ = expectedX;

        Assert.AreEqual(expectedX, creature.Position.X, 0.01f, "creature must steer toward the waypoint in X");
        Assert.AreEqual(expectedZ, creature.Position.Z, 0.01f, "creature must steer toward the waypoint in Z");
        Assert.AreNotEqual(0f, creature.Velocity.X, "velocity must be applied while moving");
        Assert.AreNotEqual(0f, creature.Velocity.Z, "velocity must be applied while moving");
        // Default is server-authoritative (NpcPathFollower.PublishSteerGoal off): TargetPosition is
        // the creature's own position, which parks the client AI so the server's pose stream is the
        // only thing that moves it. Publishing a real goal makes the client walk the creature itself
        // and it diverges from the server's path at every lookahead tried (p50 ~44 units).
        Assert.AreEqual(creature.Position, creature.TargetPosition,
            "with client-driven motion off, the steer goal must be the creature's own position");
    }

    [TestMethod]
    public void CreatureTick_UsesCreatureSpeed_FallbackWhenZero()
    {
        var map = CreateMap();
        var path = SeedMapPath(map, PatrolPathCoid, reverse: false);
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(100f, 0f, 0f),
            AcceptDistance = 1f,
        });

        var creature = PlaceNpcCreature(map, PatrolCreatureCoid, new Vector3(0f, 0f, 0f), speed: 0f);
        creature.CoidCurrentPath = PatrolPathCoid;
        creature.NpcAi.CombatState = HBAICombatState.IdlePatrol;

        NpcTicker.Tick(map, nowMs: 10_000, dt: 1f);

        // Foot fallback is 2.5 u/s (distinct from the vehicle fallback of 12 u/s), applied at the
        // rate the client integrates — see ClientMatchedFootSpeedScale.
        Assert.AreEqual(2.5f, creature.Position.X, 0.001f,
            "a foot creature with no clonebase speed must fall back to 2.5 u/s, not the vehicle default");
    }

    [TestMethod]
    public void CreatureTick_FiresPointReactionsAndWaits()
    {
        var map = CreateMap();

        // Target vehicle whose path a SetPath reaction will assign — the observable side effect.
        var target = PlaceTargetVehicle(map, TargetVehicleCoid);
        SeedMapPath(map, TargetPathCoid, reverse: false);
        PlaceSetPathReaction(map, ArrivalReactionCoid, objectCoid: TargetVehicleCoid, pathCoid: (int)TargetPathCoid);

        // Single-point patrol path whose waypoint fires the reaction on arrival.
        var patrolPath = SeedMapPath(map, PatrolPathCoid, reverse: false);
        patrolPath.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(20f, 0f, 20f),
            AcceptDistance = 2f,
            ReactionCoid = ArrivalReactionCoid,
            WaitTime = 1000,
        });

        // Foot creature placed exactly on the waypoint → arrives this tick.
        var creature = PlaceNpcCreature(map, PatrolCreatureCoid, new Vector3(20f, 0f, 20f), speed: 5f);
        creature.CoidCurrentPath = PatrolPathCoid;
        creature.NpcAi.CombatState = HBAICombatState.IdlePatrol;

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.1f);

        Assert.AreEqual(TargetPathCoid, target.CoidCurrentPath,
            "Arrival reaction must run through SectorMap.TriggerReactions and assign the target path");
        Assert.AreEqual(0, creature.NpcAi.PathIndex, "single-point non-reverse path wraps back to 0 after arrival");
        Assert.AreEqual(10_000L + 1000L, creature.NpcAi.WaitUntilMs, "arrival sets the wait deadline");
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_npc_foot_follower_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static MapPathTemplate SeedMapPath(SectorMap map, long pathCoid, bool reverse)
    {
        var path = new MapPathTemplate { COID = (int)pathCoid, ReverseDirection = reverse };
        map.MapData.Templates[pathCoid] = path;
        return path;
    }

    /// <summary>Foot creature with no driver/vehicle wrapper — the entity moves and owns its own AI.</summary>
    private static Creature PlaceNpcCreature(SectorMap map, long coid, Vector3 position, float speed)
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid);
        AssetManager.Instance.GetCloneBase<CloneBaseCreature>(CreatureCbid).CreatureSpecific.Speed = speed;

        var creature = new Creature();
        creature.LoadCloneBase(CreatureCbid);
        creature.SetCoid(coid, false);
        creature.Position = position;
        creature.NpcAi = new NpcAiState();
        creature.SetMap(map);
        return creature;
    }

    private static Vehicle PlaceTargetVehicle(SectorMap map, long coid)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, false);
        vehicle.SetMap(map);
        return vehicle;
    }

    private static void PlaceSetPathReaction(SectorMap map, long reactionCoid, long objectCoid, int pathCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            Name = "arrival_set_path",
            ReactionType = ReactionType.SetPath,
            ActOnActivator = false,
            GenericVar1 = pathCoid,
        };
        tpl.Objects.Add(objectCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }
}
