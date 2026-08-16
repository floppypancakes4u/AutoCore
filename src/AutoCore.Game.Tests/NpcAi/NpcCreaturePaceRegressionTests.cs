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
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using global::TNL.Entities;
using global::TNL.Structures;

/// <summary>
/// Creature counterparts to <see cref="NpcPathPaceRegressionTests"/>. Every keep-the-pose-stream-warm
/// mitigation in this repo was built for vehicles only; foot creatures (humanoids, wildlife) got
/// none of them, which is why they rubberband far worse than NPC vehicles.
/// <para>
/// Why a cold dirty list is visible: the client never blends. <c>CVOGCreature::DoPositionUpdate</c>
/// @004c6360 → <c>CVOGPhysicsBase::DoPositionUpdate</c> @0053eec0 keeps exactly one last-server
/// snapshot, applies <b>no</b> positional correction while drift is under <c>cfMaxNetworkOffset</c>,
/// and <b>hard-snaps</b> (setPosition/setLinearVelocity/setRotation) the moment drift exceeds it.
/// Every correction large enough to matter is therefore a teleport, and its size is exactly the
/// drift accumulated since the previous pose pack. Gaps in the pose stream are the bug.
/// </para>
/// </summary>
[TestClass]
public class NpcCreaturePaceRegressionTests
{
    private const int ContId = 8_431;
    private const int CreatureCbid = 84_310;
    private const long PatrolPathCoid = 84_311;
    private const long PatrolCreatureCoid = 84_312;

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
        SoftNpcPathMotion.MaxStaggerDelayMs = 0;
    }

    /// <summary>
    /// A path creature dwelling out a waypoint's WaitTime holds position, so
    /// <see cref="NpcTicker"/>'s position-equality early-out skips ApplyMove — and the
    /// re-dirty branch that rescues that case was gated on <c>entity is Vehicle</c>. The creature
    /// ghost therefore leaves TNL's non-zero update list entirely and stops being packed until it
    /// moves again. This is the vehicle regression
    /// <c>NpcTicker_WaitingPathVehicle_ReDirtiesPositionMask</c>, for creatures.
    /// </summary>
    [TestMethod]
    public void NpcTicker_HoldingPathCreature_ReDirtiesPositionMask()
    {
        var map = CreateMap();
        var patrolPath = SeedMapPath(map, PatrolPathCoid);
        patrolPath.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(20f, 0f, 20f),
            AcceptDistance = 2f,
        });

        var start = new Vector3(5f, 0f, 5f);
        var creature = PlaceNpcCreature(map, PatrolCreatureCoid, start, speed: 5f);
        creature.CoidCurrentPath = PatrolPathCoid;
        creature.NpcAi.CombatState = HBAICombatState.IdlePatrol;
        creature.NpcAi.WaitUntilMs = 20_000L; // still dwelling at nowMs 10_000

        var ghostInfo = ScopeGhost(creature);

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.1f);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(start, creature.Position, "a waiting NPC must not move");
        Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask,
            "a holding path creature must re-dirty PositionMask or TNL drops it from the "
            + "non-zero update list and the client is left dead-reckoning on a stale pose");
    }

    /// <summary>
    /// Same hazard one layer up: the per-tick force-dirty sweep that guarantees pathing NPCs
    /// re-enter the dirty queue every tick bailed out on anything that was not a
    /// <see cref="Vehicle"/>, so no creature ever benefited from it.
    /// </summary>
    [TestMethod]
    public void ForcePathNpcPoseDirty_IncludesGhostedPathCreatures()
    {
        var map = CreateMap();
        SeedMapPath(map, PatrolPathCoid);

        var creature = PlaceNpcCreature(map, PatrolCreatureCoid, new Vector3(5f, 0f, 5f), speed: 5f);
        creature.CoidCurrentPath = PatrolPathCoid;
        creature.NpcAi.CombatState = HBAICombatState.IdlePatrol;

        var ghostInfo = ScopeGhost(creature);
        AddPlayer(map);

        MapManager.Instance.ClearMapsForTests();
        MapManager.Instance.RegisterMapForTests(map);
        try
        {
            var dirtied = MapManager.Instance.ForcePathNpcPoseDirty();
            NetObject.CollapseDirtyList();

            Assert.AreEqual(1, dirtied, "the ghosted path creature must be counted");
            Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask,
                "path creatures need the same per-tick dirty guarantee path vehicles already have");
        }
        finally
        {
            MapManager.Instance.ClearMapsForTests();
        }
    }

    /// <summary>
    /// The guards must survive the generalisation: a corpse has nothing to animate, and dirtying an
    /// unghosted shell cannot enqueue a pack (CollapseDirtyList finds no GhostInfo) so it is pure
    /// per-tick waste on a list that can hold hundreds of creatures.
    /// </summary>
    [TestMethod]
    public void ForcePathNpcPoseDirty_SkipsUnghostedAndCorpseCreatures()
    {
        var map = CreateMap();
        SeedMapPath(map, PatrolPathCoid);

        // Ghosted, but a corpse.
        var corpse = PlaceNpcCreature(map, PatrolCreatureCoid, new Vector3(5f, 0f, 5f), speed: 5f);
        corpse.CoidCurrentPath = PatrolPathCoid;
        corpse.NpcAi.CombatState = HBAICombatState.IdlePatrol;
        ScopeGhost(corpse);
        SetCorpse(corpse);

        // Has a path and a ghost object, but was never scoped to any connection.
        var unghosted = PlaceNpcCreature(map, PatrolCreatureCoid + 1, new Vector3(9f, 0f, 9f), speed: 5f);
        unghosted.CoidCurrentPath = PatrolPathCoid;
        unghosted.NpcAi.CombatState = HBAICombatState.IdlePatrol;
        unghosted.CreateGhost();

        AddPlayer(map);

        MapManager.Instance.ClearMapsForTests();
        MapManager.Instance.RegisterMapForTests(map);
        try
        {
            Assert.AreEqual(0, MapManager.Instance.ForcePathNpcPoseDirty(),
                "corpses and unghosted shells must not be force-dirtied");
        }
        finally
        {
            MapManager.Instance.ClearMapsForTests();
        }
    }

    /// <summary>
    /// The clump. Creatures sharing a MapPath all latch to the same geometric-nearest waypoint (by
    /// design — index staggering was tried and reverted because it made NPCs aim cross-country and
    /// circle), so they also departed together and then travelled as one body forever. That is the
    /// "large groups of humans running together" symptom, and it is what puts ~100 movers inside the
    /// interest radius simultaneously, collapsing per-creature pose rate.
    /// </summary>
    [TestMethod]
    public void NpcTicker_CreaturesSharingAPath_DoNotAllDepartTogether()
    {
        SoftNpcPathMotion.MaxStaggerDelayMs = 6000; // lever-gated; off by default
        var map = CreateMap();
        var path = SeedMapPath(map, PatrolPathCoid);
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(400f, 0f, 0f),
            AcceptDistance = 2f,
        });

        // A spawn group: same path, same start, differing only by COID.
        var group = new List<Creature>();
        for (var i = 0; i < 12; i++)
        {
            var creature = PlaceNpcCreature(map, PatrolCreatureCoid + i, new Vector3(0f, 0f, 0f), speed: 5f);
            creature.CoidCurrentPath = PatrolPathCoid;
            creature.NpcAi.CombatState = HBAICombatState.IdlePatrol;
            group.Add(creature);
        }

        // Tick a couple of seconds of simulated time.
        for (var t = 0; t < 40; t++)
            NpcTicker.Tick(map, nowMs: 10_000 + (t * 50), dt: 0.05f);

        var distinctPositions = group
            .Select(c => MathF.Round(c.Position.X, 1))
            .Distinct()
            .Count();

        Assert.IsTrue(distinctPositions > 1,
            "creatures sharing a path must not all sit at the same X after two seconds — "
            + $"lockstep departure is the clump ({distinctPositions} distinct positions of {group.Count})");
    }

    private static GhostInfo ScopeGhost(ClonedObjectBase entity)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        entity.CreateGhost();
        connection.BeginGhostingForTests();
        connection.ObjectLocalScopeAlways(entity.Ghost);

        var ghostInfo = entity.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo, "expected the creature ghost to be scoped");
        ghostInfo.UpdateMask = 0; // clear the "everything dirty" state ObjectInScope seeds
        return ghostInfo;
    }

    private static void SetCorpse(ClonedObjectBase entity)
    {
        var field = typeof(ClonedObjectBase).GetField("<IsCorpse>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field!.SetValue(entity, true);
    }

    private static void AddPlayer(SectorMap map)
    {
        var player = new Character { Position = new Vector3(0f, 0f, 0f) };
        player.SetMap(map);
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_npc_creature_pace_{ContId}",
            DisplayName = "creature-pace",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static MapPathTemplate SeedMapPath(SectorMap map, long pathCoid)
    {
        var path = new MapPathTemplate { COID = (int)pathCoid, ReverseDirection = false };
        map.MapData.Templates[pathCoid] = path;
        return path;
    }

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
}
