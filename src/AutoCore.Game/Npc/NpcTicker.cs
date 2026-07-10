namespace AutoCore.Game.Npc;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;

/// <summary>
/// Adapter that drives the pure <see cref="NpcPathFollower"/> over a map's live NPC AI entities.
/// Runs on the sector main loop under the interface lock (no concurrent map mutation). Only
/// <see cref="HBAICombatState.IdlePatrol"/> NPCs move here; combat states own movement in Stage 10.
/// </summary>
public static class NpcTicker
{
    /// <summary>Fallback patrol speed (u/s) for vehicles (and their drivers) when the clonebase has none.</summary>
    private const float DefaultVehicleSpeed = 12f;

    /// <summary>Fallback patrol speed (u/s) for foot creatures when the clonebase has none.</summary>
    private const float DefaultFootSpeed = 2.5f;

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
            if (npcAi == null || npcAi.CombatState != HBAICombatState.IdlePatrol)
                continue;

            if (!map.TryGetMapPath(GetPathCoid(entity), out var path) || path.Points.Count == 0)
                continue;

            var result = NpcPathFollower.Step(
                entity.Position, path, npcAi.PathIndex, npcAi.PathDirection,
                npcAi.WaitUntilMs, nowMs, ResolveSpeed(entity), dt);

            npcAi.PathIndex = result.NewIndex;
            npcAi.PathDirection = result.NewDirection;
            npcAi.WaitUntilMs = result.WaitUntilMs;

            ApplyMove(entity, result);

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

    private static long GetPathCoid(ClonedObjectBase entity) => entity switch
    {
        Vehicle vehicle => vehicle.CoidCurrentPath,
        Creature creature => creature.CoidCurrentPath,
        _ => -1L,
    };

    /// <summary>
    /// Movement speed from the driver (vehicles) or the creature itself; falls back to
    /// <see cref="DefaultSpeed"/> when no clonebase speed is available.
    /// </summary>
    private static float ResolveSpeed(ClonedObjectBase entity)
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

    private static void ApplyMove(ClonedObjectBase entity, PathStepResult result)
    {
        switch (entity)
        {
            case Vehicle vehicle:
                vehicle.ApplyServerMove(result.NewPosition, result.Rotation, result.Velocity);
                vehicle.PathReversing = result.NowReversing;
                break;
            case Creature creature:
                creature.ApplyServerMove(result.NewPosition, result.Rotation, result.Velocity, result.NewPosition);
                creature.PathReversing = result.NowReversing;
                break;
        }
    }
}
