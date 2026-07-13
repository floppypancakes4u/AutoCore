namespace AutoCore.Game.Npc;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Adapter that drives the pure <see cref="NpcPathFollower"/> over a map's live NPC AI entities.
/// Runs on the sector main loop under the interface lock (no concurrent map mutation). Only
/// <see cref="HBAICombatState.IdlePatrol"/> NPCs move here; combat states own movement in Stage 10.
/// </summary>
public static class NpcTicker
{
    /// <summary>Fallback patrol speed (u/s) for vehicles (and their drivers) when the clonebase has none.</summary>
    internal const float DefaultVehicleSpeed = 12f;

    /// <summary>Fallback patrol speed (u/s) for foot creatures when the clonebase has none.</summary>
    internal const float DefaultFootSpeed = 2.5f;

    public static void Tick(SectorMap map, long nowMs, float dt)
    {
        if (map == null)
            return;

        // Snapshot: a fired arrival reaction can mutate NpcAiEntities mid-iteration.
        var entities = map.NpcAiEntities.ToArray();
        foreach (var entity in entities)
        {
            if (entity == null || entity.IsCorpse)
                continue;

            var npcAi = GetNpcAi(entity);
            if (npcAi == null)
                continue;

            // Combat brain first: aggro scan (idle), fire, bounded pursuit lunge (engage/combat).
            NpcCombatAi.Tick(map, entity, nowMs, dt);

            // The path follower owns movement whenever the combat brain didn't: a path NPC keeps
            // riding (and returning to) its route even while engaged, and only stands down while it is
            // walking home, fleeing, or lunging at its target this tick. Pathless NPCs have no path and
            // fall through the TryGetMapPath check below.
            if (npcAi.ReturningHome || nowMs < npcAi.FleeUntilMs || npcAi.PursuingThisTick)
                continue;

            if (!map.TryGetMapPath(GetPathCoid(entity), out var path) || path.Points.Count == 0)
                continue;

            // Captured before WaitUntilMs is overwritten below: true exactly when this tick took
            // NpcPathFollower.Step's hold-in-place branch (nowMs still short of the wait deadline).
            var wasHolding = nowMs < npcAi.WaitUntilMs;

            // First latch onto a path: stagger start index so shared MapPaths do not all begin
            // at the same nearest waypoint (stacking on spawn).
            if (npcAi.PathIndex < 0)
            {
                npcAi.PathIndex = SoftNpcPathMotion.ResolveStaggeredPathIndex(
                    entity.Position, path, entity.ObjectId.Coid);
                if (MathF.Abs(npcAi.PathLaneOffset) < 1e-6f)
                    npcAi.PathLaneOffset = SoftNpcPathMotion.ResolveLaneOffset(entity.ObjectId.Coid);
            }

            var result = NpcPathFollower.Step(
                entity.Position, path, npcAi.PathIndex, npcAi.PathDirection,
                npcAi.WaitUntilMs, nowMs, ResolveSpeed(entity), dt);

            npcAi.PathIndex = result.NewIndex;
            npcAi.PathDirection = result.NewDirection;
            npcAi.WaitUntilMs = result.WaitUntilMs;

            // A holding/waiting NPC whose position didn't change this tick has nothing new to
            // broadcast — applying the move anyway would dirty PositionMask and re-send pose to
            // every scoped client for no reason. Ticks that actually arrive (and snap onto the
            // waypoint) are never "holding" per the check above, so arrival snapping still applies
            // even when the NPC happened to already be sitting on the waypoint.
            if (SoftNpcPathMotion.Enabled)
            {
                result = SoftNpcPathMotion.Apply(
                    result,
                    entity.Position,
                    GetRotation(entity),
                    ResolveSpeed(entity),
                    dt,
                    path,
                    nowMs,
                    GetVelocity(entity),
                    npcAi.PathLaneOffset);
            }

            if (!wasHolding || !PositionsEqual(result.NewPosition, entity.Position))
                ApplyMove(entity, result, dt);
            else if (entity is Vehicle holdVehicle && holdVehicle.CoidCurrentPath > 0)
            {
                // Holding on a waypoint: re-snap Y (soft wait parks at previous XYZ) and keep
                // pose dirty so TNL does not drop the ghost from the non-zero update list.
                var grounded = SnapToTerrain(map, holdVehicle.Position);
                if (MathF.Abs(grounded.Y - holdVehicle.Position.Y) > 1e-3f)
                    holdVehicle.ApplyServerMove(grounded, holdVehicle.Rotation, holdVehicle.Velocity, 0f);
                else
                    holdVehicle.Ghost?.SetMaskBits(GhostObject.PositionMask);
            }

            if (result.FireReactionCoid > 0)
                map.TriggerReactions(entity, new List<long> { result.FireReactionCoid });
        }
    }

    private static NpcAiState GetNpcAi(ClonedObjectBase entity) => entity switch
    {
        Vehicle vehicle => vehicle.NpcAi,
        Creature creature => creature.NpcAi,
        _ => null,
    };

    internal static long GetPathCoid(ClonedObjectBase entity) => entity switch
    {
        Vehicle vehicle => vehicle.CoidCurrentPath,
        Creature creature => creature.CoidCurrentPath,
        _ => -1L,
    };

    private static Quaternion GetRotation(ClonedObjectBase entity) => entity switch
    {
        Vehicle vehicle => vehicle.Rotation,
        Creature creature => creature.Rotation,
        _ => Quaternion.Default,
    };

    private static Vector3 GetVelocity(ClonedObjectBase entity) => entity switch
    {
        Vehicle vehicle => vehicle.Velocity,
        Creature creature => creature.Velocity,
        _ => default,
    };

    /// <summary>True when both positions are exactly equal on all three axes.</summary>
    private static bool PositionsEqual(Vector3 a, Vector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// Movement speed from the driver (vehicles) or the creature itself; falls back to
    /// <see cref="DefaultVehicleSpeed"/> / <see cref="DefaultFootSpeed"/> when no clonebase speed
    /// is available.
    /// </summary>
    internal static float ResolveSpeed(ClonedObjectBase entity)
    {
        var source = entity switch
        {
            Vehicle vehicle => vehicle.Owner?.GetAsCreature(),
            Creature creature => creature,
            _ => null,
        };

        var fallback = entity is Vehicle ? DefaultVehicleSpeed : DefaultFootSpeed;
        var speed = (source?.CloneBaseObject as CloneBaseCreature)?.CreatureSpecific.Speed ?? 0f;
        return speed > 0f ? speed : fallback;
    }

    private static void ApplyMove(ClonedObjectBase entity, PathStepResult result, float dt)
    {
        var pos = SnapToTerrain(entity.Map, result.NewPosition);
        // Keep target position grounded for foot creatures (client pose target).
        var targetPos = pos;

        switch (entity)
        {
            case Vehicle vehicle:
                // Pack MoveToTarget3DPoint-style thr/steer when soft path computed them so the
                // client VehicleAction spins wheels and steers (ghost +0x614/+0x618).
                if (result.HasDriveInputs)
                {
                    vehicle.ApplyServerMove(
                        pos, result.Rotation, result.Velocity, dt,
                        result.Throttle, result.Steering, result.SharpTurn);
                }
                else
                {
                    vehicle.ApplyServerMove(pos, result.Rotation, result.Velocity, dt);
                }

                vehicle.PathReversing = result.NowReversing;
                break;
            case Creature creature:
                creature.ApplyServerMove(pos, result.Rotation, result.Velocity, targetPos);
                creature.PathReversing = result.NowReversing;
                break;
        }
    }

    /// <summary>
    /// Sample the map TGA heightfield when present; otherwise leave Y unchanged.
    /// Pure terrain only — do not add the retail AI foot offset here. Ghost unpack applies
    /// server XYZ as-is; live check showed +foot (~1.18) floats server-driven creatures.
    /// Static IsNPC still use <see cref="SpawnPoint.ApplyStaticNpcSpawnHeight"/> (spawn map Y + foot).
    /// </summary>
    internal static Vector3 SnapToTerrain(SectorMap map, Vector3 position)
    {
        var field = map?.MapData?.Heightfield;
        if (field == null || !field.TrySample(position.X, position.Z, out var y))
            return position;

        return new Vector3(position.X, y, position.Z);
    }
}
