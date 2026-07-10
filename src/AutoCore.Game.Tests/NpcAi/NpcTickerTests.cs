using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.NpcAi;

using System.Reflection;
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

/// <summary>
/// Stage 8: <see cref="NpcTicker"/> drives idle-patrol NPCs over their path and fires arrival
/// reactions through <see cref="SectorMap.TriggerReactions"/>; corpses and non-idle states skip.
/// </summary>
[TestClass]
public class NpcTickerTests
{
    private const int ContId = 841;
    private const long PatrolPathCoid = 84010;
    private const long ArrivalReactionCoid = 84020;
    private const long TargetPathCoid = 84030;
    private const long PatrolVehicleCoid = 84001;
    private const long TargetVehicleCoid = 84002;

    [TestInitialize]
    public void SetUp() => TriggerManager.Instance.ClearAllForTests();

    [TestCleanup]
    public void TearDown() => TriggerManager.Instance.ClearAllForTests();

    [TestMethod]
    public void NpcTicker_FiresPointReaction_ViaTriggerReactions()
    {
        var map = CreateMap();

        // Target NPC whose path a SetPath reaction will assign — the observable side effect.
        var target = PlaceNpcVehicle(map, TargetVehicleCoid, new Vector3(0f, 0f, 0f), npcAi: false);
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

        // Patrolling NPC placed exactly on the waypoint → arrives this tick.
        var patrol = PlaceNpcVehicle(map, PatrolVehicleCoid, new Vector3(20f, 0f, 20f), npcAi: true);
        patrol.CoidCurrentPath = PatrolPathCoid;
        patrol.NpcAi.CombatState = HBAICombatState.IdlePatrol;

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.1f);

        Assert.AreEqual(TargetPathCoid, target.CoidCurrentPath,
            "Arrival reaction must run through SectorMap.TriggerReactions and assign the target path");
        Assert.AreEqual(0, patrol.NpcAi.PathIndex, "single-point non-reverse path wraps back to 0 after arrival");
        Assert.AreEqual(10_000L + 1000L, patrol.NpcAi.WaitUntilMs, "arrival sets the wait deadline");
    }

    [TestMethod]
    public void NpcTicker_SkipsCorpsesAndNonIdleStates()
    {
        var map = CreateMap();

        var patrolPath = SeedMapPath(map, PatrolPathCoid, reverse: false);
        patrolPath.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(1000f, 0f, 0f),
            AcceptDistance = 1f,
        });

        var corpseStart = new Vector3(0f, 0f, 0f);
        var corpse = PlaceNpcVehicle(map, PatrolVehicleCoid, corpseStart, npcAi: true);
        corpse.CoidCurrentPath = PatrolPathCoid;
        corpse.NpcAi.CombatState = HBAICombatState.IdlePatrol;
        SetCorpse(corpse);

        var engagedStart = new Vector3(5f, 0f, 5f);
        var engaged = PlaceNpcVehicle(map, TargetVehicleCoid, engagedStart, npcAi: true);
        engaged.CoidCurrentPath = PatrolPathCoid;
        engaged.NpcAi.CombatState = HBAICombatState.Engage;

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.5f);

        Assert.AreEqual(corpseStart, corpse.Position, "corpses must not be moved by the patrol tick");
        Assert.AreEqual(engagedStart, engaged.Position, "non-IdlePatrol NPCs must not be moved by the patrol tick");
    }

    [TestMethod]
    public void NpcTicker_WaitingNpc_DoesNotDirtyPositionMask()
    {
        var map = CreateMap();

        var patrolPath = SeedMapPath(map, PatrolPathCoid, reverse: false);
        patrolPath.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(20f, 0f, 20f),
            AcceptDistance = 2f,
        });

        // Already dwelling (WaitUntilMs is in the future) — not yet at the deadline, so the path
        // stepper holds in place. This tick must not re-broadcast a pose that did not change.
        var start = new Vector3(5f, 0f, 5f);
        var patrol = PlaceNpcVehicle(map, PatrolVehicleCoid, start, npcAi: true);
        patrol.CoidCurrentPath = PatrolPathCoid;
        patrol.NpcAi.CombatState = HBAICombatState.IdlePatrol;
        patrol.NpcAi.WaitUntilMs = 20_000L;

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        patrol.CreateGhost();
        connection.ActivateGhosting();
        connection.ObjectLocalScopeAlways(patrol.Ghost);

        var ghostInfo = patrol.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo, "expected the patrol vehicle ghost to be scoped");
        ghostInfo.UpdateMask = 0; // clear the "everything dirty" state ObjectInScope seeds

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.1f);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(start, patrol.Position, "a waiting NPC must not move");
        Assert.AreEqual(0ul, ghostInfo.UpdateMask & GhostObject.PositionMask,
            "a waiting/holding NPC whose pose did not change must not dirty PositionMask");
    }

    [TestMethod]
    public void NpcTicker_ArrivalTick_StillDirtiesPositionMaskAndSnaps()
    {
        var map = CreateMap();

        var patrolPath = SeedMapPath(map, PatrolPathCoid, reverse: false);
        patrolPath.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(20f, 0f, 20f),
            AcceptDistance = 2f,
            WaitTime = 1000,
        });

        // Placed exactly on the waypoint, so NewPosition == current Position on this tick even
        // though it is a genuine arrival (not a hold) — the position-equality early-out must not
        // suppress this case.
        var start = new Vector3(20f, 0f, 20f);
        var patrol = PlaceNpcVehicle(map, PatrolVehicleCoid, start, npcAi: true);
        patrol.CoidCurrentPath = PatrolPathCoid;
        patrol.NpcAi.CombatState = HBAICombatState.IdlePatrol;

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        patrol.CreateGhost();
        connection.ActivateGhosting();
        connection.ObjectLocalScopeAlways(patrol.Ghost);

        var ghostInfo = patrol.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo, "expected the patrol vehicle ghost to be scoped");
        ghostInfo.UpdateMask = 0; // clear the "everything dirty" state ObjectInScope seeds

        NpcTicker.Tick(map, nowMs: 10_000, dt: 0.1f);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(start, patrol.Position, "arrival snaps onto the waypoint position");
        Assert.AreEqual(10_000L + 1000L, patrol.NpcAi.WaitUntilMs, "arrival sets the wait deadline");
        Assert.AreEqual(GhostObject.PositionMask, ghostInfo.UpdateMask & GhostObject.PositionMask,
            "an arrival tick must still dirty PositionMask even though position was already on the waypoint");
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_npc_ticker_{ContId}",
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

    private static Vehicle PlaceNpcVehicle(SectorMap map, long coid, Vector3 position, bool npcAi)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, false);
        vehicle.Position = position;
        if (npcAi)
            vehicle.NpcAi = new NpcAiState();
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

    private static void SetCorpse(ClonedObjectBase entity)
    {
        var field = typeof(ClonedObjectBase).GetField("<IsCorpse>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(entity, true);
    }
}
