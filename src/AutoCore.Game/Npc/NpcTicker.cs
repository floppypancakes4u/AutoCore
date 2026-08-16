namespace AutoCore.Game.Npc;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils.Reliability;

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

    /// <summary>
    /// Server foot speed relative to the clonebase value. Must equal the rate the <b>client</b>
    /// integrates, because the client simulates the creature itself between pose updates.
    /// <para>
    /// <c>CVOGHBAICreatureBase::DoMovement</c> @005cd3b0 sets
    /// <c>vel = dir * min(distToGoal, GetCreatureSpeed(creature))</c> and hands it to Havok, and
    /// <c>GetCreatureSpeed</c> @004c55e0 returns the full clonebase speed. The one halving it applies
    /// is gated on <c>m_bWandering</c>, which <c>CVOGCreature::DoPositionUpdate</c> @004c6360 clears
    /// on any real position change — so a server-driven creature always walks at <b>full</b> speed.
    /// </para>
    /// <para>
    /// This was briefly 0.5, taken from <c>LowFrequencyPathMove</c> @005ce990. That 0.5 belongs to a
    /// low-frequency path-progression nudge, not to the rate the client integrates; applying it made
    /// the server advance at half the client's speed, so the client outran it continuously and was
    /// snapped back. Client-side measurement (MotionDiag) is what corrected the reading.
    /// </para>
    /// </summary>
    internal const float ClientMatchedFootSpeedScale = 1.0f;

    /// <summary>
    /// Advances every NPC on the map.
    /// <para>
    /// SS-12: each NPC is isolated. Previously this was a bare <c>foreach</c>, so one bad NPC
    /// aborted every remaining NPC on this map — and, because <c>MapManager.TickNpcs</c> loops
    /// maps without isolation either, every remaining map too. A single corrupt entity could
    /// therefore freeze AI server-wide while the tick itself kept running.
    /// </para>
    /// </summary>
    public static void Tick(SectorMap map, long nowMs, float dt)
    {
        if (map == null)
            return;

        // Snapshot: a fired arrival reaction can mutate NpcAiEntities mid-iteration.
        var entities = map.NpcAiEntities.ToArray();

        Guard.ForEach(
            entities,
            "NPC tick",
            entity => TickEntity(map, entity, nowMs, dt),
            describe: DescribeEntity);
    }

    /// <summary>Identifies an NPC in diagnostics without ever throwing.</summary>
    private static string DescribeEntity(ClonedObjectBase entity)
    {
        if (entity == null)
            return "null";

        try
        {
            return $"{entity.GetType().Name} coid={entity.ObjectId.Coid}";
        }
        catch (Exception ex)
        {
            return $"<describe failed: {ex.GetType().Name}>";
        }
    }

    private static void TickEntity(SectorMap map, ClonedObjectBase entity, long nowMs, float dt)
    {
        {
            if (entity == null || entity.IsCorpse)
                return;

            var npcAi = GetNpcAi(entity);
            if (npcAi == null)
                return;

            // Combat brain first: aggro scan (idle), fire, bounded pursuit lunge (engage/combat).
            NpcCombatAi.Tick(map, entity, nowMs, dt);

            // The path follower owns movement whenever the combat brain didn't: a path NPC keeps
            // riding (and returning to) its route even while engaged, and only stands down while it is
            // walking home, fleeing, or lunging at its target this tick. Pathless NPCs have no path and
            // fall through the TryGetMapPath check below.
            if (npcAi.ReturningHome || nowMs < npcAi.FleeUntilMs || npcAi.PursuingThisTick)
                return;

            if (!map.TryGetMapPath(GetPathCoid(entity), out var path) || path.Points.Count == 0)
                return;

            // AutoCore.Sim vehicle mover (serverConfig sim.npcVehicles, default on): a claimed
            // vehicle is driven by SimHost.Tick with real physics — the legacy movers below
            // must not touch it. Creatures always fall through to the foot movers.
            if (entity is Vehicle simVehicle && NpcVehicleSimControl.TrySimDrive?.Invoke(simVehicle) == true)
                return;

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

                // One-time departure stagger. Everyone latches to the same nearest waypoint (see
                // ResolveStaggeredPathIndex), so without this a whole spawn group also sets off
                // together and then travels as one clump for the rest of its life. Applied once per
                // NPC: combat disengage clears PathIndex to force a re-latch, and re-staggering
                // there would pause the NPC every time it dropped aggro.
                if (!npcAi.PathStartStaggered)
                {
                    npcAi.PathStartStaggered = true;
                    var stagger = SoftNpcPathMotion.ResolveStaggerDelayMs(entity.ObjectId.Coid);
                    if (stagger > 0)
                        npcAi.WaitUntilMs = Math.Max(npcAi.WaitUntilMs, nowMs + stagger);
                }
            }

            var result = NpcPathFollower.Step(
                entity.Position, path, npcAi.PathIndex, npcAi.PathDirection,
                npcAi.WaitUntilMs, nowMs, ResolveSpeed(entity), dt,
                GetRotation(entity));

            npcAi.PathIndex = result.NewIndex;
            npcAi.PathDirection = result.NewDirection;
            npcAi.WaitUntilMs = result.WaitUntilMs;

            // A holding/waiting NPC whose position didn't change this tick has nothing new to
            // broadcast — applying the move anyway would dirty PositionMask and re-send pose to
            // every scoped client for no reason. Ticks that actually arrive (and snap onto the
            // waypoint) are never "holding" per the check above, so arrival snapping still applies
            // even when the NPC happened to already be sitting on the waypoint.
            //
            // Vehicle movers: ServerConfig tier (+ wire-lever back-compat). Foot creatures: soft/hard only.
            if (entity is Vehicle driveVehicle)
            {
                var tier = ServerConfig.ResolveVehicleMoverTier();
                if (tier == NpcVehicleControllerTier.Physics)
                {
                    result = NpcVehiclePhysicsController.Apply(
                        result, driveVehicle, path, nowMs, dt, map, npcAi);
                }
                else if (tier == NpcVehicleControllerTier.Kinematic)
                {
                    result = NpcVehicleDriveController.Apply(
                        result,
                        entity.Position,
                        GetRotation(entity),
                        ResolveSpeed(entity),
                        dt,
                        path,
                        nowMs,
                        GetVelocity(entity),
                        npcAi.PathLaneOffset,
                        map.MapData?.Heightfield,
                        driveVehicle);
                }
                else if (tier == NpcVehicleControllerTier.Soft)
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
                // Hard: leave NpcPathFollower result
            }
            else if (SoftNpcPathMotion.Enabled)
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
                // Physics tier already zeroed thr/sharp via controller; still dirty the ghost.
                if (result.HasDriveInputs)
                {
                    holdVehicle.ApplyServerMove(
                        holdVehicle.Position, holdVehicle.Rotation, default, 0f,
                        result.Throttle, result.Steering, result.SharpTurn,
                        result.AngularVelocity);
                    holdVehicle.Ghost?.SetMaskBits(GhostObject.PositionMask);
                }
                else
                {
                    var grounded = SnapToTerrain(map, holdVehicle.Position);
                    if (MathF.Abs(grounded.Y - holdVehicle.Position.Y) > 1e-3f)
                        holdVehicle.ApplyServerMove(grounded, holdVehicle.Rotation, holdVehicle.Velocity, 0f);
                    else
                        holdVehicle.Ghost?.SetMaskBits(GhostObject.PositionMask);
                }
            }
            else if (entity is Creature holdCreature && holdCreature.CoidCurrentPath > 0)
            {
                // Same keep-warm guarantee for foot creatures. Without it a humanoid dwelling out a
                // waypoint's WaitTime drops off TNL's non-zero update list entirely, and the client
                // is left dead-reckoning a stale pose until the NPC moves again — which lands as a
                // hard snap, since CVOGPhysicsBase::DoPositionUpdate @0053eec0 never blends.
                holdCreature.Ghost?.SetMaskBits(GhostObject.PositionMask);
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
        var resolved = speed > 0f ? speed : fallback;

        // Foot creatures must advance at exactly the rate the client integrates them, or the two
        // simulations diverge every tick and the client snaps the creature back. See
        // ClientMatchedFootSpeedScale for the client-side derivation and for why a 0.5 taken from
        // LowFrequencyPathMove was wrong here.
        if (entity is not Vehicle)
            resolved *= ClientMatchedFootSpeedScale;

        return resolved;
    }

    private static void ApplyMove(ClonedObjectBase entity, PathStepResult result, float dt)
    {
        // Kinematic drive controller / physics sim already author a grounded pose.
        // Re-snapping to a single TGA sample would flatten pitch stance and strip ride height.
        var pos = (entity is Vehicle && result.HasDriveInputs)
            ? result.NewPosition
            : SnapToTerrain(entity.Map, result.NewPosition);
        // The client's steer goal — the waypoint being walked to, NOT the position reached this
        // tick. CVOGHBAICreatureBase::DoMovement @005cd3b0 walks the creature toward
        // m_vMoveToTarget and stops it outright inside 1.0 unit of it, so publishing the current
        // position reads as "arrived" every update: the client zeroes velocity and the creature
        // only ever moves in discrete pose-sized jumps. Grounded so the goal sits on terrain.
        var targetPos = SnapToTerrain(entity.Map, result.SteerGoal);

        switch (entity)
        {
            case Vehicle vehicle:
                // Pack thr/steer/sharp so client VehicleAction spins wheels (ghost +0x614/+0x618).
                if (result.HasDriveInputs)
                {
                    vehicle.ApplyServerMove(
                        pos, result.Rotation, result.Velocity, dt,
                        result.Throttle, result.Steering, result.SharpTurn,
                        result.AngularVelocity);
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
