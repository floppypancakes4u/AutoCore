namespace AutoCore.Game.Npc;

using System.Collections.Generic;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Server-side combat brain for a single NPC vehicle/creature (client parity
/// <c>CVOGHBAIDriver_DoLogic</c>, NPC.md §10). Mirrors the retail state machine:
/// <list type="bullet">
///   <item><b>IdlePatrol</b> — throttled aggro scan of the spatial grid; on a hostile hit, latch
///   the target and enter Engage. Steers home while <see cref="NpcAiState.ReturningHome"/> is set.</item>
///   <item><b>Engage</b> — close toward weapon range; after the profile's flee/engage timer, drop
///   into Combat. Also the wire state used while fleeing (client circling visual).</item>
///   <item><b>Combat</b> — pursue when out of range, otherwise raise the firing bit for the equipped
///   weapon and reuse the player combat pipeline (<see cref="Vehicle.ProcessCombatIfFiring"/>). Once
///   HP drops to the profile's flee band the NPC breaks off and runs home (val1–val4).</item>
/// </list>
/// The aggro scan is throttled to <see cref="ScanIntervalMs"/> (staggered by coid); pursuit
/// movement runs every tick. Faction decisions funnel through <see cref="FactionHostility"/>.
/// </summary>
public static class NpcCombatAi
{
    /// <summary>Minimum leash radius (world units) regardless of the NPC's patrol distance.</summary>
    internal const float LeashRadius = 80f;

    /// <summary>Aggro scan cadence per NPC (ms); staggered across NPCs by coid.</summary>
    internal const long ScanIntervalMs = 500;

    /// <summary>Fallback aggro radius when neither clonebase vision/hearing nor a help range exists.</summary>
    internal const float DefaultAggroRange = 50f;

    /// <summary>Engage closes to this fraction of the weapon's max range before opening fire.</summary>
    private const float EngageCloseFactor = 0.8f;

    /// <summary>Distance (world units) at which a returning NPC is considered "home" and resumes patrol.</summary>
    private const float ResumePathRadius = 5f;

    private static readonly List<ClonedObjectBase> ScanBuffer = new();

    /// <summary>
    /// Uniform [0,1) source for the call-for-help roll. Injected so tests are deterministic;
    /// defaults to the shared thread-safe RNG.
    /// </summary>
    internal static System.Func<float> Rng = DefaultRng;

    private static float DefaultRng() => System.Random.Shared.NextSingle();

    /// <summary>Restores the default RNG between tests.</summary>
    internal static void ResetRngForTests() => Rng = DefaultRng;

    /// <summary>Advances one NPC's combat AI for this tick. No-op for non-NPC/corpse entities.</summary>
    public static void Tick(SectorMap map, ClonedObjectBase entity, long nowMs, float dt)
    {
        if (map == null || entity == null || entity.IsCorpse)
            return;

        var npcAi = GetNpcAi(entity);
        if (npcAi == null)
            return;

        // Resolve this tick's leash/return anchor once (nearest path point for a path NPC, else spawn)
        // so TryLeash / TickIdle / TickFlee all steer to the same target without re-querying the map.
        var (anchor, isPath) = ResolveReturnAnchor(map, entity, npcAi);
        npcAi.ReturnAnchor = anchor;
        npcAi.HasPathAnchor = isPath;
        npcAi.PursuingThisTick = false; // set only when a combat branch lunges toward the target

        // Flee lifecycle runs ahead of the state switch: while the latch holds we run home; at
        // expiry we either re-engage (recovered ≥ val4) or re-extend the latch and keep fleeing.
        if (npcAi.FleeUntilMs > 0 && TickFleeLifecycle(entity, npcAi, nowMs, dt))
            return;

        switch (npcAi.CombatState)
        {
            case HBAICombatState.IdlePatrol:
                TickIdle(map, entity, npcAi, nowMs, dt);
                break;
            case HBAICombatState.Engage:
                TickEngage(entity, npcAi, nowMs, dt);
                break;
            case HBAICombatState.Combat:
                TickCombat(entity, npcAi, nowMs, dt);
                break;
        }
    }

    private static void TickIdle(SectorMap map, ClonedObjectBase entity, NpcAiState npcAi, long nowMs, float dt)
    {
        // Walk back to the anchor before re-scanning, to avoid oscillating on a target that leashed us.
        if (npcAi.ReturningHome)
        {
            if (entity.Position.Dist(npcAi.ReturnAnchor) <= ResumePathRadius)
                npcAi.ReturningHome = false;
            else
            {
                SteerToward(entity, npcAi.ReturnAnchor, NpcTicker.ResolveSpeed(entity), dt);
                return;
            }
        }

        // Combat AI only engages on field maps — towns are safe zones.
        if (map.MapData.ContinentObject.IsTown)
            return;

        if (!AggroScanDue(entity, npcAi, nowMs))
            return;
        StampScan(entity, npcAi, nowMs);

        var target = AcquireTarget(map, entity);
        if (target == null)
            return;

        entity.SetTargetObject(target);
        npcAi.EngageStartedMs = nowMs;
        npcAi.HelpCalled = false;
        SetCombatState(entity, HBAICombatState.Engage);
        TryCallForHelp(entity, npcAi, target, nowMs);
    }

    private static void TickEngage(ClonedObjectBase entity, NpcAiState npcAi, long nowMs, float dt)
    {
        if (TargetLost(entity) || LeashOrDrop(entity, npcAi))
            return;

        var target = entity.Target;
        var (_, weapon) = SelectFiringWeapon(entity);
        var rangeMax = WeaponRangeMax(weapon);
        var desired = rangeMax > 0f ? rangeMax * EngageCloseFactor : 0f;
        var closed = entity.Position.Dist(target.Position) <= desired;

        CombatMove(entity, npcAi, target.Position, atRange: closed, dt);

        // After the profile's flee/engage timer, commit to Combat (where flee evaluation runs).
        var timerMs = npcAi.Profile?.ValFleeOrEngageTimerMs ?? 0f;
        if (nowMs - npcAi.EngageStartedMs >= (long)timerMs)
            SetCombatState(entity, HBAICombatState.Combat);
    }

    private static void TickCombat(ClonedObjectBase entity, NpcAiState npcAi, long nowMs, float dt)
    {
        if (TargetLost(entity) || LeashOrDrop(entity, npcAi))
            return;

        // Wounded past the flee band: break off and run home (the timer already elapsed to reach Combat).
        if (ShouldFlee(entity, npcAi))
        {
            EnterFlee(entity, npcAi, nowMs);
            return;
        }

        var target = entity.Target;
        var (bit, weapon) = SelectFiringWeapon(entity);
        var rangeMax = WeaponRangeMax(weapon);
        var inRange = rangeMax <= 0f || entity.Position.Dist(target.Position) <= rangeMax;

        // Fire when the target is in weapon range — independent of movement (client parity: FireWeapons
        // runs every tick with a may-fire flag, not gated on whether the NPC also pursued this tick).
        if (entity is Vehicle vehicle && weapon != null && inRange)
        {
            vehicle.SetTargetObject(target);
            vehicle.Firing = bit;
            vehicle.ProcessCombatIfFiring();
        }
        else
        {
            CeaseFire(entity);
        }

        CombatMove(entity, npcAi, target.Position, atRange: inRange, dt);
    }

    /// <summary>
    /// Combat movement.
    /// <list type="bullet">
    ///   <item>Pathless: close when out of range (spawn leash bounds it).</item>
    ///   <item>Path NPC: <b>do not leave the route</b> for pursuit. Client
    ///   <c>CVOGHBAIDriver::DoLogic</c> only calls <c>DoVehiclePursue</c> when
    ///   <c>ReturnToNormalLocation</c> returns false; with an active path waypoint
    ///   (<c>+0x52 != 0</c>) that helper keeps steering to the waypoint and returns true, so
    ///   pursue is skipped. <c>FireWeapons</c> still runs every tick. Server mirrors that:
    ///   path movement stays on <see cref="NpcTicker"/>; combat only targets/fires.</item>
    /// </list>
    /// Sets <see cref="NpcAiState.PursuingThisTick"/> only for pathless closes (path follower
    /// must not fight a combat lunge).
    /// </summary>
    private static void CombatMove(ClonedObjectBase entity, NpcAiState npcAi, Vector3 targetPos, bool atRange, float dt)
    {
        // Assigned MapPath (CoidCurrentPath > 0): never leave the route for a combat lunge —
        // even if the path template failed to resolve this tick (HasPathAnchor false). Client
        // DoLogic skips DoVehiclePursue while ReturnToNormalLocation is busy with the path.
        // FireWeapons / target lock still run in TickCombat independently.
        if (npcAi.HasPathAnchor || NpcTicker.GetPathCoid(entity) > 0)
            return;

        if (!atRange)
        {
            SteerToward(entity, targetPos, NpcTicker.ResolveSpeed(entity), dt);
            npcAi.PursuingThisTick = true;
        }
    }

    /// <summary>True when the current target is gone/dead; resets the NPC to a homing IdlePatrol.</summary>
    private static bool TargetLost(ClonedObjectBase entity)
    {
        var target = entity.Target;
        if (target != null && !target.IsCorpse && target.Map != null)
            return false;

        Disengage(entity, GetNpcAi(entity));
        return true;
    }

    /// <summary>
    /// This tick's leash/return anchor: the nearest point on the active path for a path-following NPC
    /// (CoidCurrentPath &gt; 0 with a resolvable, non-empty path), otherwise the spawn
    /// <see cref="NpcAiState.HomePosition"/>. Mirrors the client 005d6e80 waypoint <c>+0x52</c> branch,
    /// where a path NPC returns to its waypoint and only a pathless NPC leashes to spawn.
    /// </summary>
    private static (Vector3 Anchor, bool IsPath) ResolveReturnAnchor(SectorMap map, ClonedObjectBase entity, NpcAiState npcAi)
    {
        var coid = NpcTicker.GetPathCoid(entity);
        if (coid > 0 && map.TryGetMapPath(coid, out var path) && path.Points.Count > 0)
            return (NpcPathFollower.NearestPoint(entity.Position, path), true);

        return (npcAi.HomePosition, false);
    }

    /// <summary>
    /// Decides whether the NPC breaks off this tick. A <b>pathless</b> NPC leashes to spawn once dragged
    /// past <c>max(patrol, LeashRadius)</c> from home (client 005d6e80). A <b>path</b> NPC never leashes
    /// on distance — it stays tethered to its route and keeps its target (client parity: the aggro list
    /// is only pruned when the target leaves perception), so it disengages only when the target escapes
    /// its aggro/vision range. Either way <see cref="Disengage"/> routes it back to its anchor.
    /// </summary>
    private static bool LeashOrDrop(ClonedObjectBase entity, NpcAiState npcAi)
    {
        if (!npcAi.HasPathAnchor)
        {
            var leash = System.Math.Max(GetPatrolDistance(entity), LeashRadius);
            if (entity.Position.Dist(npcAi.ReturnAnchor) <= leash)
                return false;

            Disengage(entity, npcAi);
            return true;
        }

        var target = entity.Target;
        if (target == null || entity.Position.Dist(target.Position) <= ResolveAggroRange(entity, npcAi))
            return false;

        Disengage(entity, npcAi);
        return true;
    }

    private static void Disengage(ClonedObjectBase entity, NpcAiState npcAi)
    {
        CeaseFire(entity);
        entity.SetTargetObject(null);
        if (npcAi != null)
        {
            npcAi.ReturningHome = true;
            npcAi.FleeUntilMs = 0;
            // Path NPC: drop the cursor so NpcPathFollower.Step re-latches to the nearest node when
            // patrol resumes at the anchor (the point we are walking back to), not the stale index.
            if (npcAi.HasPathAnchor)
                npcAi.PathIndex = -1;
        }
        SetCombatState(entity, HBAICombatState.IdlePatrol);
    }

    private static void CeaseFire(ClonedObjectBase entity)
    {
        if (entity is Vehicle vehicle)
            vehicle.Firing = 0;
    }

    // ----- flee (val1–val4) -----------------------------------------------------------------

    /// <summary>
    /// Runs the flee latch each tick before the state switch. Returns <c>true</c> when it fully
    /// handled the tick (caller returns): while <see cref="NpcAiState.FleeUntilMs"/> holds the NPC
    /// runs home; on expiry it re-engages if recovered to <c>val4</c> (returns <c>false</c> so the
    /// Combat branch runs) or re-extends the latch and keeps fleeing. Zero-val profiles never enter
    /// this path because <see cref="EnterFlee"/> is gated by the single <see cref="ShouldFlee"/>
    /// predicate.
    /// </summary>
    private static bool TickFleeLifecycle(ClonedObjectBase entity, NpcAiState npcAi, long nowMs, float dt)
    {
        if (nowMs < npcAi.FleeUntilMs)
        {
            TickFlee(entity, npcAi, dt);
            return true;
        }

        var reengage = npcAi.Profile?.ValReengageThreshold ?? 0f;
        if (HpRatio(entity) >= reengage)
        {
            npcAi.FleeUntilMs = 0;
            SetCombatState(entity, HBAICombatState.Combat);
            return false;
        }

        npcAi.FleeUntilMs = nowMs + (long)(npcAi.Profile?.ValFleeOrEngageTimerMs ?? 0f);
        TickFlee(entity, npcAi, dt);
        return true;
    }

    /// <summary>
    /// Single flee predicate (NPC.md Risk 8): flee once HP has dropped to the profile's flee band,
    /// taken as the larger of the secondary (val2) and primary (val3) bands. Retail "never flee"
    /// rows zero val1–3, giving a 0 threshold that a live NPC's positive HP ratio never meets.
    /// </summary>
    private static bool ShouldFlee(ClonedObjectBase entity, NpcAiState npcAi)
    {
        var profile = npcAi.Profile;
        if (profile == null)
            return false;

        var threshold = System.Math.Max(profile.ValFleeHpSecondary, profile.ValFleeHpOrChance);
        return HpRatio(entity) <= threshold;
    }

    /// <summary>Latches the flee timer, ceases fire, and pins the wire state to Engage (client circling).</summary>
    private static void EnterFlee(ClonedObjectBase entity, NpcAiState npcAi, long nowMs)
    {
        npcAi.FleeUntilMs = nowMs + (long)(npcAi.Profile?.ValFleeOrEngageTimerMs ?? 0f);
        CeaseFire(entity);
        SetCombatState(entity, HBAICombatState.Engage);
    }

    /// <summary>Guns silent, run back toward the return anchor (nearest path point or spawn).</summary>
    private static void TickFlee(ClonedObjectBase entity, NpcAiState npcAi, float dt)
    {
        CeaseFire(entity);
        SteerToward(entity, npcAi.ReturnAnchor, NpcTicker.ResolveSpeed(entity), dt);
    }

    /// <summary>Current-to-maximum HP ratio; 1.0 when max HP is unknown (never forces a flee).</summary>
    private static float HpRatio(ClonedObjectBase entity)
    {
        var max = entity.GetMaximumHP();
        return max > 0 ? (float)entity.GetCurrentHP() / max : 1f;
    }

    // ----- damage aggro + call-for-help (val5–val7) -----------------------------------------

    /// <summary>
    /// Hook fired when <paramref name="entity"/> takes real damage from <paramref name="attacker"/>
    /// (<see cref="Entities.ClonedObjectBase.TakeDamage(int, Entities.ClonedObjectBase)"/>). An idle
    /// NPC latches onto its attacker immediately, bypassing the vision scan (aggro-list parity), and
    /// either path attempts a one-time call for help.
    /// </summary>
    internal static void OnDamaged(ClonedObjectBase entity, ClonedObjectBase attacker)
    {
        if (entity == null || attacker == null || entity.IsCorpse)
            return;

        var npcAi = GetNpcAi(entity);
        if (npcAi == null)
            return;

        var nowMs = System.Environment.TickCount64;

        if (npcAi.CombatState == HBAICombatState.IdlePatrol && entity.Target == null)
        {
            entity.SetTargetObject(attacker);
            npcAi.EngageStartedMs = nowMs;
            npcAi.ReturningHome = false;
            npcAi.HelpCalled = false;
            SetCombatState(entity, HBAICombatState.Engage);
        }

        TryCallForHelp(entity, npcAi, attacker, nowMs);
    }

    /// <summary>
    /// Once per engagement, rolls <c>val6</c> against the profile's help chance and, on success,
    /// spreads server-authoritative aggro (target + Engage) to same-faction idle NPCs within
    /// <c>val7</c>. Returns without touching <see cref="NpcAiState.HelpCalled"/> when help is disabled
    /// so a help-off profile never consumes its single roll; no help packet is emitted (NPC.md defers
    /// the client shout).
    /// </summary>
    private static void TryCallForHelp(ClonedObjectBase entity, NpcAiState npcAi, ClonedObjectBase attacker, long nowMs)
    {
        if (npcAi.HelpCalled || attacker == null)
            return;

        var profile = npcAi.Profile;
        if (profile == null || profile.ValHelpEnabled <= 0f || profile.ValHelpRange <= 0f)
            return;

        var map = entity.Map;
        if (map == null)
            return;

        npcAi.HelpCalled = true; // consume the one roll whether or not it lands
        if (Rng() >= profile.ValHelpChance)
            return;

        var myFaction = entity.GetIDFaction();
        map.Grid.QueryRadius(entity.Position, profile.ValHelpRange, ScanBuffer);

        foreach (var ally in ScanBuffer)
        {
            if (ReferenceEquals(ally, entity) || ally.IsCorpse)
                continue;
            if (ally.GetIDFaction() != myFaction)
                continue;

            var allyAi = GetNpcAi(ally);
            if (allyAi == null || allyAi.CombatState != HBAICombatState.IdlePatrol || ally.Target != null)
                continue;

            ally.SetTargetObject(attacker);
            allyAi.EngageStartedMs = nowMs;
            allyAi.HelpCalled = false;
            SetCombatState(ally, HBAICombatState.Engage);
        }
    }

    /// <summary>Finds the nearest hostile, targetable candidate within the NPC's aggro radius.</summary>
    private static ClonedObjectBase AcquireTarget(SectorMap map, ClonedObjectBase entity)
    {
        var range = ResolveAggroRange(entity, GetNpcAi(entity));
        map.Grid.QueryRadius(entity.Position, range, ScanBuffer);

        var myFaction = entity.GetIDFaction();
        ClonedObjectBase best = null;
        var bestSq = float.MaxValue;

        foreach (var candidate in ScanBuffer)
        {
            if (ReferenceEquals(candidate, entity) || candidate.IsCorpse || candidate.IsInvincible)
                continue;
            if (!IsAggroCandidate(candidate))
                continue;
            if (!FactionHostility.IsHostile(myFaction, candidate.GetIDFaction()))
                continue;

            var sq = entity.Position.DistSq(candidate.Position);
            if (sq < bestSq)
            {
                bestSq = sq;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Candidates are connected player vehicles or other live NPC AI entities.</summary>
    private static bool IsAggroCandidate(ClonedObjectBase candidate)
    {
        if (candidate is Vehicle vehicle && vehicle.GetSuperCharacter(false)?.OwningConnection != null)
            return true;

        return GetNpcAi(candidate) != null;
    }

    /// <summary>
    /// Aggro radius = max(vision, hearing) from the driver (vehicles) or the creature's own
    /// clonebase; falls back to the AI profile's help range, then <see cref="DefaultAggroRange"/>.
    /// </summary>
    private static float ResolveAggroRange(ClonedObjectBase entity, NpcAiState npcAi)
    {
        var spec = ResolveCreatureSpecific(entity);
        var senses = spec != null ? System.Math.Max(spec.VisionRange, spec.HearingRange) : 0f;
        if (senses > 0f)
            return senses;

        var help = npcAi?.Profile?.ValHelpRange ?? 0f;
        return help > 0f ? help : DefaultAggroRange;
    }

    private static CreatureSpecific ResolveCreatureSpecific(ClonedObjectBase entity)
    {
        var source = entity switch
        {
            Vehicle vehicle => vehicle.Owner?.GetAsCreature(),
            Creature creature => creature,
            _ => null,
        };

        return (source?.CloneBaseObject as CloneBaseCreature)?.CreatureSpecific;
    }

    /// <summary>Firing bit + weapon for the highest-priority equipped slot (front, then turret, then rear).</summary>
    private static (byte Bit, Weapon Weapon) SelectFiringWeapon(ClonedObjectBase entity)
    {
        if (entity is not Vehicle vehicle)
            return (0, null);

        if (vehicle.WeaponFront != null)
            return (1, vehicle.WeaponFront);
        if (vehicle.WeaponTurret != null)
            return (2, vehicle.WeaponTurret);
        if (vehicle.WeaponRear != null)
            return (4, vehicle.WeaponRear);

        return (0, null);
    }

    private static float WeaponRangeMax(Weapon weapon)
    {
        return weapon?.CloneBaseWeapon?.WeaponSpecific.RangeMax ?? 0f;
    }

    private static float GetPatrolDistance(ClonedObjectBase entity) => entity switch
    {
        Vehicle vehicle => vehicle.PatrolDistance,
        Creature creature => creature.PatrolDistance,
        _ => 0f,
    };

    /// <summary>Steers <paramref name="entity"/> toward <paramref name="targetPos"/> by one tick's travel.</summary>
    private static void SteerToward(ClonedObjectBase entity, Vector3 targetPos, float speed, float dt)
    {
        if (speed <= 0f || dt <= 0f)
            return;

        var dx = targetPos.X - entity.Position.X;
        var dz = targetPos.Z - entity.Position.Z;
        var dist = (float)System.Math.Sqrt((dx * dx) + (dz * dz));
        if (dist <= 0f)
            return;

        var step = System.Math.Min(speed * dt, dist);
        var inv = 1f / dist;
        var newPos = new Vector3(
            entity.Position.X + (dx * inv * step),
            entity.Position.Y,
            entity.Position.Z + (dz * inv * step));
        newPos = NpcTicker.SnapToTerrain(entity.Map, newPos);
        var velocity = new Vector3(dx * inv * speed, 0f, dz * inv * speed);
        var rotation = YawQuaternion(dx, dz);

        switch (entity)
        {
            case Vehicle vehicle:
                vehicle.ApplyServerMove(newPos, rotation, velocity);
                break;
            case Creature creature:
                creature.ApplyServerMove(newPos, rotation, velocity, newPos);
                break;
        }
    }

    private static Quaternion YawQuaternion(float dx, float dz)
    {
        var yaw = (float)System.Math.Atan2(dx, dz);
        var half = yaw * 0.5f;
        return new Quaternion(0f, (float)System.Math.Sin(half), 0f, (float)System.Math.Cos(half));
    }

    private static bool AggroScanDue(ClonedObjectBase entity, NpcAiState npcAi, long nowMs)
    {
        return nowMs - npcAi.LastAggroScanMs >= ScanIntervalMs;
    }

    /// <summary>
    /// Records a completed scan, offsetting the stored timestamp by <c>coid % ScanIntervalMs</c> so
    /// NPCs that first tick on the same frame re-scan on staggered frames afterward.
    /// </summary>
    private static void StampScan(ClonedObjectBase entity, NpcAiState npcAi, long nowMs)
    {
        npcAi.LastAggroScanMs = nowMs - (entity.ObjectId.Coid % ScanIntervalMs);
    }

    /// <summary>
    /// Sets the combat state and dirties the wire state field: the vehicle StateMask (its byte lives
    /// on the driver creature) or the creature StateMask. See <see cref="HBAICombatState"/>.
    /// </summary>
    internal static void SetCombatState(ClonedObjectBase entity, HBAICombatState state)
    {
        var npcAi = GetNpcAi(entity);
        if (npcAi == null)
            return;

        npcAi.CombatState = state;

        switch (entity)
        {
            case Vehicle vehicle:
                vehicle.Ghost?.SetMaskBits(GhostVehicle.StateMask);
                if (vehicle.Owner?.GetAsCreature() is Creature driver)
                    driver.AiCombatState = (byte)state;
                break;
            case Creature creature:
                creature.Ghost?.SetMaskBits(GhostCreature.StateMask);
                creature.AiCombatState = (byte)state;
                break;
        }
    }

    private static NpcAiState GetNpcAi(ClonedObjectBase entity) => entity switch
    {
        Vehicle vehicle => vehicle.NpcAi,
        Creature creature => creature.NpcAi,
        _ => null,
    };
}
