// ============================================================================================
// VehicleCombat.reference.cs  —  REFERENCE EXTRACT (NOT a compilable drop-in)
// --------------------------------------------------------------------------------------------
// These are the combat methods extracted verbatim from our fork's Entities/Vehicle.cs (a
// multi-thousand-line partial class). Only the server-side fire model is reproduced here so the
// proposal stays readable; the surrounding Vehicle fields (Firing, WeaponFront/Turret/Rear,
// WantedTurretDirection, Heat/MaxHeat, Map, Owner, Ghost, ObjectId, Position, Rotation, the
// _lastFireMs* slot stamps, and the Debug* cone overrides) live on the full Vehicle class.
//
// Read this alongside Combat/DamageCalculator.cs (the actual damage math) and
// Packets/Sector/DamagePacket.cs (the multi-hit 0x2023 wire). Method order below:
//   1. ProcessCombatIfFiring          — tick entry (fires when Firing bits set; hard Target OPTIONAL)
//   2. CollectCombatWitnessConnections — attacker UNION victims recipient set (NPC-fire visibility)
//   3. ProcessCombatInternal          — fire ALL 3 slots, each into its own cone; one 0x2023/tick
//   4. AcquireFireTargets             — per-slot cone acquisition (ValidArc = cosine dot threshold)
//   5. AcquireExplosionTargets        — ExplosionRadius AoE splash (omnidirectional radius)
//   6. IsHostileFireTarget            — hostility/damageability gate for cone + splash
//   7. FireWeaponAtTarget             — roll one hit via DamageCalculator, apply, credit, kill
// ============================================================================================

// ---- 1. Tick entry. Called from BOTH movement packets AND the server tick so holding fire works
//         even when VehicleMoved packets are sparse. A hard Target is OPTIONAL (cone handles the
//         no-target case) — this is the divergence from a Target-required model. ----
public void ProcessCombatIfFiring()
{
    if (Ghost == null)
        return;

    // Fire when the Firing bits are set. A hard Target is OPTIONAL — cone acquisition handles the
    // no-target case (fire into the turret's cone); an invalid hard target is filtered downstream.
    if (Firing > 0)
        ProcessCombatInternal();
}

// ---- 2. Recipient set: the ATTACKER's connection (null when the attacker is an NPC — Owner is a
//         driver Creature, not a Character) UNION every VICTIM's connection, deduped. An
//         attacker-only send drops NPC-fired shots so the player-victim never sees them. ----
private HashSet<TNLConnection> CollectCombatWitnessConnections(Character? attackerChar, DamagePacket packet)
{
    var connections = new HashSet<TNLConnection>();

    if (attackerChar?.OwningConnection != null)
        connections.Add(attackerChar.OwningConnection);

    foreach (var (target, _, _, _) in packet.Hits)
    {
        var victimConn = ObjectManager.Instance.GetObject(target)?.GetSuperCharacter(false)?.OwningConnection;
        if (victimConn != null)
            connections.Add(victimConn);
    }

    return connections;
}

// ---- 3. The core: fire EVERY armed slot this tick, each against its OWN cone — mirroring the
//         client, which loops slots 0..2 (SetWeaponsFiring @0x5021d0 / FireWeaponsPrimary
//         @0x4f50d0) and fires each set bit independently. All slots accumulate into ONE
//         DamagePacket (single 0x2023 per tick) + one destroy queue. Firing bits: 1=front,
//         2=turret, 4=rear. Per-slot aim offset: front 0 / turret WantedTurretDirection / rear PI. ----
private void ProcessCombatInternal()
{
    if (Firing <= 0)
        return;

    var nowMs = Environment.TickCount64;
    var attackerLevel = Owner?.GetAsCreature()?.GetLevel() ?? 1;
    var attackerChar = Owner?.GetAsCharacter();
    var rng = new Random(unchecked((int)(nowMs ^ ObjectId.Coid)));
    // Active Combat/Battle Mode effect (refire/accuracy/crit/damage modifiers); None if no mode selected.
    var battleMode = Combat.BattleModeEffect.ForCharacter(attackerChar, attackerLevel);

    var damagePacket = new DamagePacket { Attacker = ObjectId };
    var destroyQueue = new List<ClonedObjectBase>();
    // Whether ANY slot actually acquired a target this tick. Gates the "Miss" feedback so firing into
    // empty air stays silent instead of spamming a miss every tick.
    var anyEngaged = false;

    // Per-slot aim = a turret-relative offset added to the vehicle's world yaw (see AcquireFireTargets):
    //   FRONT  offset 0                     -> fixed vehicle-forward cone, NO turret contribution
    //   TURRET offset WantedTurretDirection -> the rotating turret lock
    //   REAR   offset PI                    -> fixed rearward cone
    // The single hard Target the client sends with 0x200A is the TURRET's lock, so only the turret
    // auto-includes it at index 0; front/rear draw their primary from their own fixed cone.
    TryFireSlot(0x01, WeaponFront, 0f, includeHardTarget: false, ref _lastFireMsFront);
    TryFireSlot(0x02, WeaponTurret, WantedTurretDirection, includeHardTarget: true, ref _lastFireMsTurret);
    TryFireSlot(0x04, WeaponRear, (float)System.Math.PI, includeHardTarget: false, ref _lastFireMsRear);

    // Recipient set: attacker's connection UNION every victim's (deduped). Reused for the damage
    // packet AND the destroy sends. Recipients only — packet bytes/format untouched.
    var witnesses = CollectCombatWitnessConnections(attackerChar, damagePacket);

    if (damagePacket.Hits.Count > 0)
    {
        foreach (var conn in witnesses)
            try { conn.SendGamePacket(damagePacket, skipOpcode: true); }
            catch { }
    }
    else if (anyEngaged)
    {
        // A target was in a cone but every roll missed -> real miss feedback. Nothing engaged
        // (firing into empty air) sends nothing.
        TrySendCombatMissProbe(attackerChar);
    }

    // Destroy killed world objects AFTER the damage packet so the client applies HP->0 first and runs
    // its own destruction (debris FX) rather than snapping the object out on a bare DestroyObject.
    foreach (var obj in destroyQueue)
    {
        var destroy = new DestroyObjectPacket(obj.ObjectId);
        foreach (var conn in witnesses)
            try { conn.SendGamePacket(destroy); }
            catch { }
    }

    // Fire one armed weapon slot into its OWN cone: cooldown-gate on the slot's own last-fire stamp,
    // acquire targets around this slot's aim, apply direct + explosion damage, and append hits and any
    // kills to the shared packet + destroy queue.
    void TryFireSlot(byte bit, Weapon weapon, float aimOffset, bool includeHardTarget, ref long lastFireMs)
    {
        if ((Firing & bit) == 0 || weapon == null || weapon.CloneBaseWeapon == null)
            return;

        var weaponSpec = weapon.CloneBaseWeapon.WeaponSpecific;

        // Overheat lock (client IsHeatOk @0x56aca0): a pure threshold, no timer.
        if (MaxHeat > 0 && Heat >= MaxHeat)
            return;

        // Cooldown / rate-of-fire gating (per slot). Battle mode scales the cooldown.
        var cooldownMs = weaponSpec.RechargeTime > 0 ? weaponSpec.RechargeTime : 500;
        var effectiveCooldown = battleMode.RefireMultiplier > 0f
            ? (long)(cooldownMs * battleMode.RefireMultiplier) : cooldownMs;
        if (nowMs - lastFireMs < effectiveCooldown)
            return;
        lastFireMs = nowMs;

        // PER-SHOT HEAT — client DoFireCheck @0x56c860 -> AdjustHeat @0x56ad00 adds "Heat Per Shot"
        // (WeaponSpecific.Heat) on every trigger-pull, before target resolution. Clamp to [0, 2*MaxHeat].
        if (weaponSpec.Heat > 0 && MaxHeat > 0)
        {
            Heat = System.Math.Clamp(Heat + weaponSpec.Heat, 0, MaxHeat * 2);
            Ghost?.SetMaskBits(GhostVehicle.HeatMask);
        }

        // Build this slot's fire set: (turret only) the hard Target plus any hostiles inside the cone.
        var targets = AcquireFireTargets(weaponSpec, aimOffset, includeHardTarget);
        if (targets.Count == 0)
            return;
        anyEngaged = true;

        for (var i = 0; i < targets.Count; i++)
        {
            var tgt = targets[i];
            // Index 0 = primary (full damage); the rest are secondary cone/spray targets that take the
            // OnFire distance falloff measured from the PRIMARY's impact point (client OnFire @0x56e000).
            var isSprayTarget = i > 0;
            var dist = isSprayTarget ? targets[0].Position.Dist(tgt.Position) : 0f;
            var (dealt, isCrit) = FireWeaponAtTarget(
                tgt, weaponSpec, attackerChar, attackerLevel, rng, destroyQueue, isSprayTarget, dist, battleMode);
            if (dealt > 0)
                damagePacket.Hits.Add((tgt.ObjectId, dealt, true, isCrit));
        }

        // --- Explosion / AoE splash ---
        // Explosive ordnance (WeaponSpecific.ExplosionRadius > 0 — rocket/missile launchers carry 8-10)
        // detonates at the impact point and damages EVERY hostile in the blast radius, excluding every
        // target already hit directly (no double-hit). Distance falloff measured from the blast center.
        if (weaponSpec.ExplosionRadius > 0f && targets.Count > 0)
        {
            var impact = targets[0].Position;
            var alreadyHit = new HashSet<ClonedObjectBase>(targets);
            var splashTargets = AcquireExplosionTargets(impact, weaponSpec.ExplosionRadius, alreadyHit);
            foreach (var splashTgt in splashTargets)
            {
                var sdist = impact.Dist(splashTgt.Position);
                var (sdealt, sCrit) = FireWeaponAtTarget(
                    splashTgt, weaponSpec, attackerChar, attackerLevel, rng, destroyQueue, true, sdist, battleMode);
                if (sdealt > 0)
                    damagePacket.Hits.Add((splashTgt.ObjectId, sdealt, true, sCrit));
            }
        }
    }
}

// ---- 4. Per-slot cone acquisition. The cone axis is the vehicle's world yaw plus aimOffset (a
//         turret-relative angle): 0 = fixed vehicle-forward, WantedTurretDirection = rotating turret,
//         PI = fixed rearward. ValidArc = COSINE of the cone half-angle (live-confirmed 0.987 =
//         cos(9.25 deg) = an 18.5 deg full cone), used DIRECTLY as the dot-product threshold. RangeMax
//         = reach; SprayTargets = max simultaneous hits. Cone is a full 3D dot test (dy folded in), so
//         geometry above/below the aim line falls outside the arc (mirrors the client Havok cone-cast). ----
private List<ClonedObjectBase> AcquireFireTargets(WeaponSpecific weaponSpec, float aimOffset, bool includeHardTarget)
{
    var result = new List<ClonedObjectBase>();
    var rangeMax = DebugConeRange > 0 ? DebugConeRange : (weaponSpec.RangeMax > 0 ? weaponSpec.RangeMax : 100f);
    var maxTargets = DebugConeSpray > 0 ? DebugConeSpray : (weaponSpec.SprayTargets > 0 ? (int)weaponSpec.SprayTargets : 1);
    var hasCone = DebugConeArcRad > 0 || weaponSpec.ValidArc > 0;
    var cosArc = DebugConeArcRad > 0 ? (float)System.Math.Cos(DebugConeArcRad) : weaponSpec.ValidArc;

    // Primary hard target first (turret slot only — it IS the turret's lock). Accept a live target OR a
    // no-HP mission destroyable (an active Kill target).
    if (includeHardTarget && Target != null && Target != this && Target != Owner
        && !Target.IsCorpse && !Target.IsInvincible
        && Position.Dist(Target.Position) <= rangeMax
        && (Target.GetCurrentHP() > 0
            || (Target is GraphicsObject && NpcInteractHandler.IsKillObjectiveTarget(Owner?.GetAsCharacter(), Target))))
        result.Add(Target);

    // Cone acquisition around the aim forward vector.
    if (Map != null && hasCone && result.Count < maxTargets)
    {
        // Compose the turret-relative aimOffset with the vehicle's world yaw so the cone axis is
        // world-absolute regardless of which way the car points.
        float vehFx = 2f * (Rotation.X * Rotation.Z + Rotation.W * Rotation.Y);
        float vehFz = 1f - 2f * (Rotation.X * Rotation.X + Rotation.Y * Rotation.Y);
        var worldAim = (float)System.Math.Atan2(vehFx, vehFz) + aimOffset;
        var fx = (float)System.Math.Sin(worldAim);
        var fz = (float)System.Math.Cos(worldAim);
        var candidates = new List<(ClonedObjectBase obj, float dot)>();
        foreach (var obj in Map.Objects.Values)
        {
            // Exclude the hard Target from the cone ONLY when it was already added at index 0 (turret
            // slot). For front/rear the hard target isn't pre-added, so let the cone pick it up naturally.
            if (obj == this || (includeHardTarget && obj == Target) || obj == Owner || !IsHostileFireTarget(obj))
                continue;
            var dx = obj.Position.X - Position.X;
            var dy = obj.Position.Y - Position.Y;
            var dz = obj.Position.Z - Position.Z;
            // Full 3D cone (client CVOGPhysicsUtils::FindDistanceToTarget @0x4e9aa0 casts a Havok
            // cone built from the arc half-angle acos(ValidArc)). Fold dy into BOTH range and the
            // direction normalization; aim axis stays horizontal (fx,0,fz) since the turret dir is a yaw.
            var d2 = dx * dx + dy * dy + dz * dz;
            if (d2 > rangeMax * rangeMax || d2 < 0.0001f)
                continue;
            var dot = (dx * fx + dz * fz) / (float)System.Math.Sqrt(d2); // cos(3D angle to horizontal aim)
            if (dot >= cosArc)
                candidates.Add((obj, dot));
        }

        // Most-aligned with the aim first, so a single-target cone shot hits what you're pointing at.
        candidates.Sort((a, b) => b.dot.CompareTo(a.dot));
        foreach (var c in candidates)
        {
            if (result.Count >= maxTargets)
                break;
            result.Add(c.obj);
        }
    }
    return result;
}

// ---- 5. AoE splash gather: every hostile/damageable object within radius of a blast impact point,
//         excluding the shooter, its owner, and anything already hit directly. Pure 3D radius (an
//         explosion is omnidirectional — no cone/arc). Same hostility gate as cone acquisition. ----
private List<ClonedObjectBase> AcquireExplosionTargets(
    Vector3 impact, float radius, HashSet<ClonedObjectBase> exclude)
{
    var result = new List<ClonedObjectBase>();
    if (Map == null || radius <= 0f)
        return result;

    var r2 = radius * radius;
    foreach (var obj in Map.Objects.Values)
    {
        if (obj == this || obj == Owner || exclude.Contains(obj) || !IsHostileFireTarget(obj))
            continue;
        var dx = obj.Position.X - impact.X;
        var dy = obj.Position.Y - impact.Y;
        var dz = obj.Position.Z - impact.Z;
        if (dx * dx + dy * dy + dz * dz <= r2)
            result.Add(obj);
    }
    return result;
}

// ---- 6. Hostility/damageability gate for cone acquisition AND splash. Check Creature/Vehicle FIRST
//         (Creature derives from GraphicsObject). Creatures/Vehicles: alive + hostile faction.
//         Non-creature GraphicsObjects: a destructible world object OR an active mission destroy target.
//         Indestructible scenery (Flags bit12) is skipped so the cone can't level terrain. ----
private bool IsHostileFireTarget(ClonedObjectBase obj)
{
    if (obj == null || obj.IsCorpse || obj.IsInvincible)
        return false;

    if (obj is Creature || obj is Vehicle)
        return obj.GetCurrentHP() > 0 && obj.Faction != Faction; // alive + hostile faction

    if (obj is GraphicsObject go)
        return go.IsDemolishable || NpcInteractHandler.IsKillObjectiveTarget(Owner?.GetAsCharacter(), obj);

    return false;
}

// ---- 7. Roll hit + damage for one target via the authentic DamageCalculator (server-authoritative;
//         the client is display-only for player damage), apply it (TakeDamage), credit an active Kill
//         objective for no-HP mission destroyables, and handle death/destruction + break-loot. Returns
//         (dealtDamage>0, wasCrit) or (0,false) on miss/no-apply. isSprayTarget = a secondary cone/splash
//         target (distance falloff); false = primary. ----
private (short dealt, bool isCrit) FireWeaponAtTarget(ClonedObjectBase tgt, WeaponSpecific weaponSpec,
    Character attackerChar, int attackerLevel, Random rng, List<ClonedObjectBase> destroyQueue,
    bool isSprayTarget, float dist, Combat.BattleModeEffect battleMode)
{
    var result = Combat.DamageCalculator.Compute(
        attackerChar, attackerLevel, tgt, weaponSpec, rng, isSprayTarget, dist, battleMode);
    if (result.Miss || result.Damage <= 0)
        return (0, false);

    var actualDamage = tgt.TakeDamage(result.Damage, this);

    if (actualDamage <= 0)
    {
        // No-HP mission destroyables: credit an active matching Kill objective.
        var attackerConn = attackerChar?.OwningConnection;
        if (attackerConn != null && !tgt.IsCorpse &&
            NpcInteractHandler.TryCreditKillObjectiveTarget(attackerConn, attackerChar, tgt))
        {
            tgt.SetMurderer(this);
            tgt.OnDeath(DeathType.Silent);
            attackerConn.SendGamePacket(new DestroyObjectPacket(tgt.ObjectId));
        }
        return (0, false);
    }

    if (tgt.GetCurrentHP() <= 0)
    {
        tgt.SetMurderer(this);
        // A demolishable that is ALSO an active mission Kill target must still credit the objective.
        var attackerConn = attackerChar?.OwningConnection;
        if (attackerConn != null)
            NpcInteractHandler.TryCreditKillObjectiveTarget(attackerConn, attackerChar, tgt);

        tgt.OnDeath(DeathType.Silent);
        // World destructibles have HP but no creature death flow — the client destroys (debris FX) on a
        // DestroyObject. Deferred until AFTER the damage packet (see ProcessCombatInternal).
        var isObject = tgt is GraphicsObject;
        if (isObject)
        {
            destroyQueue.Add(tgt);
            // Building/prop break loot (wad.xml tLootWeights, keyed by the destroyed object's own CBID).
            var lootCbid = LootManager.Instance.RollDestructionLoot(tgt.CBID);
            if (lootCbid.HasValue)
            {
                if (LootManager.Instance.RequiresAutoLoot(lootCbid.Value) && attackerChar != null)
                    LootManager.Instance.AutoLootItem(lootCbid.Value, attackerChar);
                else if (Map != null)
                    LootManager.Instance.SpawnLootItem(lootCbid.Value, tgt.Position, tgt.Rotation, Map);
            }
        }
    }

    return ((short)Math.Clamp(actualDamage, 1, (int)short.MaxValue), result.IsCrit);
}
