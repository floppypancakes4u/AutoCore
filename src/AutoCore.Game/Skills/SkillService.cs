namespace AutoCore.Game.Skills;

using System.Collections.Concurrent;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Server-authoritative entry point for skill sources. Supported effect families:
/// direct heal (<see cref="SkillElementTypes.Heal"/>; negative heal = damage, e.g. 10:1:-0.5),
/// and direct damage (min/max damage flags). Unsupported definitions fail without state change.
/// </summary>
public static class SkillService
{
    private static readonly ConcurrentDictionary<(long CharacterCoid, int SkillId), long> CooldownUntilMs = new();
    private static readonly object CooldownReservationLock = new();

    /// <summary>Test helper: clear player-cast cooldown state between tests.</summary>
    internal static void ClearCooldownsForTests() => CooldownUntilMs.Clear();

    /// <summary>
    /// Player-initiated skill cast from <c>RequestCastSkill</c>. Applies supported direct
    /// damage/heal effects; returns false when validation fails or no effect is supported.
    /// </summary>
    public static bool TryCastPlayer(
        Character character,
        int skillId,
        int skillLevel,
        TFID targetId,
        Vector3 targetPosition)
        => TryCastPlayer(character, skillId, skillLevel, targetId, targetPosition, out _);

    public static bool TryCastPlayer(
        Character character,
        int skillId,
        int skillLevel,
        TFID targetId,
        Vector3 targetPosition,
        out SkillResponse response)
    {
        response = SkillResponse.GenericFailed;
        if (character == null || skillId <= 0)
            return false;

        var caster = character.CurrentVehicle ?? (ClonedObjectBase)character;
        if (caster.Map == null)
        {
            response = SkillResponse.Status;
            Logger.WriteLog(LogType.Debug,
                "Skill cast rejected: source=Player skillId={0} reason=no-map",
                skillId);
            return false;
        }

        var skill = AssetManager.Instance.GetSkill(skillId);
        if (skill == null)
        {
            response = SkillResponse.ServerChecksFailed;
            Logger.WriteLog(LogType.Error,
                "Skill cast rejected: source=Player skillId={0} reason=definition-not-found",
                skillId);
            return false;
        }

        var level = Math.Max(1, skillLevel);
        var hasHealElement = FindElement(skill, SkillElementTypes.Heal) != null;
        var hasDamage = TryGetDamageRange(skill, level, out _, out _, out _);
        if (!hasHealElement && !hasDamage)
        {
            Logger.WriteLog(LogType.Error,
                "Skill cast rejected: source=Player skillId={0} name='{1}' reason=no-supported-effect elements=[{2}]",
                skill.Id,
                skill.Name ?? string.Empty,
                FormatElements(skill));
            return false;
        }

        // Heal element can be positive (restore) or negative (damage / "Damage 50%" style).
        var hasHeal = hasHealElement;
        var target = ResolveTarget(character, caster, skill, targetId, hasDamage, hasHeal);
        if (target == null)
        {
            response = SkillResponse.WrongTarget;
            Logger.WriteLog(LogType.Debug,
                "Skill cast rejected: source=Player skillId={0} reason=target-not-found coid={1}",
                skillId,
                targetId?.Coid ?? 0);
            return false;
        }

        var range = GetScalarElement(skill, SkillElementTypes.Range, level);
        if (range > 0f)
        {
            var dist = caster.Position.Dist(target.Position);
            if (dist > range)
            {
                response = SkillResponse.OutOfRange;
                Logger.WriteLog(LogType.Debug,
                    "Skill cast rejected: source=Player skillId={0} reason=out-of-range dist={1:F1} max={2:F1}",
                    skillId,
                    dist,
                    range);
                return false;
            }
        }

        var cooldownMs = (long)MathF.Round(GetScalarElement(skill, SkillElementTypes.CoolDown, level));
        var nowMs = Environment.TickCount64;
        var cooldownKey = (character.ObjectId.Coid, skillId);
        var cooldownReserved = false;
        long untilMs = 0;
        if (cooldownMs > 0)
        {
            lock (CooldownReservationLock)
            {
                if (CooldownUntilMs.TryGetValue(cooldownKey, out untilMs) && nowMs < untilMs)
                {
                    response = SkillResponse.Recharge;
                }
                else
                {
                    untilMs = nowMs + cooldownMs;
                    CooldownUntilMs[cooldownKey] = untilMs;
                    cooldownReserved = true;
                }
            }

            if (!cooldownReserved)
            {
                Logger.WriteLog(LogType.Debug,
                    "Skill cast rejected: source=Player skillId={0} reason=cooldown remainingMs={1}",
                    skillId,
                    untilMs - nowMs);
                return false;
            }
        }

        var cost = (int)MathF.Round(GetScalarElement(skill, SkillElementTypes.Cost, level));
        if (!TrySpendPower(character, cost))
        {
            ReleaseCooldownReservation(cooldownKey, untilMs, cooldownReserved);
            response = SkillResponse.Power;
            Logger.WriteLog(LogType.Debug,
                "Skill cast rejected: source=Player skillId={0} reason=insufficient-power cost={1}",
                skillId,
                cost);
            return false;
        }

        var applied = false;
        var targetKilled = false;
        var damageToReport = 0;
        var powerDelta = 0;
        // This path only handles resolved object-targeted direct effects. Retail
        // resolves the effect position from that object; RequestCastSkill's vector
        // can be the caster/aim origin and must not become the VFX destination.
        var effectPosition = target.Position;

        if (hasHealElement
            && TryGetHealSignedAmount(skill, level, target.GetMaximumHP(), out var healSigned))
        {
            if (healSigned > 0)
            {
                var restored = target.RestoreHealth(healSigned);
                if (restored > 0)
                {
                    applied = true;
                    powerDelta = restored;
                }
                else if (!hasDamage)
                {
                    // Full-health heal still "succeeds" for pad-style callers, but player cast
                    // with only heal and nothing restored is a no-op fail (no cooldown burn).
                    Logger.WriteLog(LogType.Debug,
                        "Skill cast rejected: source=Player skillId={0} reason=heal-no-effect",
                        skillId);
                    return false;
                }
            }
            else if (healSigned < 0 && !target.IsCorpse && !target.IsInvincible)
            {
                // Negative heal element (e.g. 10:1:-0.5 = 50% max HP damage).
                var actual = target.TakeDamage(-healSigned, caster);
                if (actual > 0)
                {
                    applied = true;
                    powerDelta = Math.Max(powerDelta, actual);
                    damageToReport = actual;
                    if (target.GetCurrentHP() <= 0)
                    {
                        target.SetMurderer(caster);
                        targetKilled = true;
                    }
                }
            }
        }

        if (hasDamage)
        {
            if (target.IsCorpse || target.IsInvincible)
            {
                if (!applied)
                {
                    response = SkillResponse.Corpse;
                    Logger.WriteLog(LogType.Debug,
                        "Skill cast rejected: source=Player skillId={0} reason=target-not-damageable",
                        skillId);
                    return false;
                }
            }
            else if (TryGetDamageRange(skill, level, out var minDmg, out var maxDmg, out var pen))
            {
                var damage = RollDamage(minDmg, maxDmg, pen, nowMs, caster.ObjectId.Coid, target.ObjectId.Coid);
                if (damage > 0)
                {
                    var actual = target.TakeDamage(damage, caster);
                    if (actual > 0)
                    {
                        applied = true;
                        powerDelta = Math.Max(powerDelta, actual);
                        damageToReport = actual;

                        if (target.GetCurrentHP() <= 0)
                        {
                            target.SetMurderer(caster);
                            targetKilled = true;
                        }
                    }
                }
            }
        }

        if (!applied)
        {
            ReleaseCooldownReservation(cooldownKey, untilMs, cooldownReserved);
            Logger.WriteLog(LogType.Debug,
                "Skill cast rejected: source=Player skillId={0} reason=no-effect-applied",
                skillId);
            return false;
        }

        // The authoritative effect has already resolved, so no cast delay remains.
        // A positive value creates a target-bound active-skill heartbeat; that can
        // become stranded when a lethal cast destroys its target immediately.
        const int applyPower = 0;

        SendEffect(
            character,
            caster,
            target,
            targetId is { Coid: > 0 } ? targetId : target.ObjectId,
            skill.Id,
            level,
            applyPower,
            powerDelta,
            effectPosition,
            isItemSkill: false);

        if (damageToReport > 0)
            Vehicle.TrySendDamagePacket(character, target, caster.ObjectId, damageToReport);

        // Let the client resolve the successful effect while the target still exists.
        // DestroyObject must follow SkillStatusEffect for lethal casts.
        if (targetKilled)
            target.OnDeath(DeathType.Silent);

        response = SkillResponse.Ok;

        Logger.WriteLog(LogType.Network,
            "Skill cast applied: source=Player skillId={0} name='{1}' rank={2} caster={3} target={4} powerDelta={5} hp={6}/{7}",
            skill.Id,
            skill.Name ?? string.Empty,
            level,
            caster.ObjectId.Coid,
            target.ObjectId.Coid,
            powerDelta,
            target.GetCurrentHP(),
            target.GetMaximumHP());
        return true;
    }

    public static bool TryCastReaction(ClonedObjectBase activator, int skillId, int skillLevel)
    {
        if (activator == null || skillId <= 0)
            return false;

        Logger.WriteLog(LogType.Debug,
            "Skill cast attempt: source=Reaction skillId={0} level={1} activator={2} coid={3} hp={4}/{5}",
            skillId,
            skillLevel,
            activator.GetType().Name,
            activator.ObjectId.Coid,
            activator.GetCurrentHP(),
            activator.GetMaximumHP());

        var skill = AssetManager.Instance.GetSkill(skillId);
        if (skill == null)
        {
            Logger.WriteLog(LogType.Error,
                "Skill cast rejected: source=Reaction skillId={0} reason=definition-not-found",
                skillId);
            return false;
        }

        var level = Math.Max(1, skillLevel);
        // Heal element 10: positive restores, negative damages (retail "Damage 50%" = 10:1:-0.5).
        if (!TryGetHealSignedAmount(skill, level, activator.GetMaximumHP(), out var healSigned)
            || healSigned == 0)
        {
            Logger.WriteLog(LogType.Error,
                "Skill cast rejected: source=Reaction skillId={0} name='{1}' reason=no-supported-effect elements=[{2}]",
                skill.Id,
                skill.Name ?? string.Empty,
                FormatElements(skill));
            return false;
        }

        var character = activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
        var powerDelta = 0;
        var damageDealt = 0;

        if (healSigned > 0)
        {
            powerDelta = activator.RestoreHealth(healSigned);
        }
        else
        {
            // Self-damage trap / pain skill: apply to the activator (player vehicle).
            if (activator.IsCorpse || activator.IsInvincible)
            {
                Logger.WriteLog(LogType.Debug,
                    "Skill cast rejected: source=Reaction skillId={0} reason=target-not-damageable",
                    skill.Id);
                return false;
            }

            damageDealt = activator.TakeDamage(-healSigned, activator);
            powerDelta = damageDealt;
            if (damageDealt <= 0)
            {
                Logger.WriteLog(LogType.Debug,
                    "Skill cast rejected: source=Reaction skillId={0} reason=damage-no-effect",
                    skill.Id);
                return false;
            }
        }

        SendEffect(
            character,
            activator,
            activator,
            activator.ObjectId,
            skill.Id,
            level,
            applyPower: Math.Max(1, Math.Abs(powerDelta)),
            powerDelta: powerDelta,
            position: activator.Position,
            isItemSkill: true);

        if (damageDealt > 0)
            Vehicle.TrySendDamagePacket(character, activator, activator.ObjectId, damageDealt);

        if (activator.GetCurrentHP() <= 0 && !activator.IsCorpse)
        {
            activator.SetMurderer(activator);
            activator.OnDeath(DeathType.Silent);
        }

        Logger.WriteLog(LogType.Debug,
            "Skill cast applied: source=Reaction skillId={0} name='{1}' target={2} signedHeal={3} powerDelta={4} damage={5} hp={6}/{7}",
            skill.Id,
            skill.Name ?? string.Empty,
            activator.ObjectId.Coid,
            healSigned,
            powerDelta,
            damageDealt,
            activator.GetCurrentHP(),
            activator.GetMaximumHP());
        return true;
    }

    private static ClonedObjectBase ResolveTarget(
        Character character,
        ClonedObjectBase caster,
        Skill skill,
        TFID targetId,
        bool hasDamage,
        bool hasHeal)
    {
        ClonedObjectBase resolved = null;
        if (targetId != null && targetId.Coid > 0)
        {
            var map = caster.Map;
            resolved = map?.GetObjectByCoid(targetId.Coid)
                ?? map?.GetObject(targetId.Coid)
                ?? ObjectManager.Instance?.GetObject(targetId);

            // Character TFID → combat body is the vehicle.
            if (resolved is Character targetChar && targetChar.CurrentVehicle != null)
                resolved = targetChar.CurrentVehicle;
        }

        if (resolved != null)
            return resolved;

        // Self-cast fallback for heals when the client sends self/empty target.
        if (hasHeal && !hasDamage)
            return caster;

        return null;
    }

    private static bool TrySpendPower(Character character, int cost)
    {
        if (cost <= 0)
            return true;

        CharacterLevelManager.Instance.EnsurePowerPlantCapacity(character);
        var mana = CharacterLevelManager.Instance.GetOrCreate(character.ObjectId.Coid);
        short current;
        short max;
        lock (mana)
        {
            current = mana.CurrentMana;
            max = mana.MaxMana;
        }

        // Default login stub is 10/10 while real skill costs are often 20–80+.
        // Only enforce power when the pool is large enough to represent a real cost.
        if (current < cost)
            return false;

        var remaining = (short)(current - cost);
        // RequestCastSkill already makes the retail client spend the authored
        // power locally. Keep the authoritative server pool in step without
        // sending a second immediate power adjustment to the caster.
        CharacterLevelManager.Instance.SetCurrentMana(character, remaining, sendPacket: false);
        Logger.WriteLog(LogType.Network,
            "Skill power spent: character={0} before={1}/{2} cost={3} after={4}/{2}",
            character.ObjectId.Coid, current, max, cost, remaining);
        return true;
    }

    private static bool TryGetHealAmount(Skill skill, int level, int poolMax, out int heal)
    {
        heal = 0;
        if (!TryGetHealSignedAmount(skill, level, poolMax, out var signed) || signed <= 0)
            return false;

        heal = signed;
        return true;
    }

    /// <summary>
    /// Evaluates heal element (type 10). Positive = restore HP; negative = damage
    /// (EquationType 1 with fractional base is a fraction of <paramref name="poolMax"/>).
    /// </summary>
    private static bool TryGetHealSignedAmount(Skill skill, int level, int poolMax, out int signedAmount)
    {
        signedAmount = 0;
        var element = FindElement(skill, SkillElementTypes.Heal);
        if (element == null)
            return false;

        var value = EvaluateElement(element.Value, level, poolMax, percentOfPool: true);
        if (MathF.Abs(value) < 0.5f)
            return false;

        signedAmount = (int)MathF.Round(value);
        return signedAmount != 0;
    }

    private static bool TryGetDamageRange(Skill skill, int level, out int min, out int max, out int pen)
    {
        min = 0;
        max = 0;
        pen = 0;
        if (skill.Elements == null || skill.Elements.Count == 0)
            return false;

        var found = false;
        float minSum = 0f;
        float maxSum = 0f;

        foreach (var element in skill.Elements)
        {
            var type = element.ElementType;
            if ((type & SkillElementTypes.FlagDamageMin) != 0)
            {
                minSum += EvaluateElement(element, level, poolMax: 0, percentOfPool: false);
                found = true;
            }
            else if ((type & SkillElementTypes.FlagDamageMax) != 0)
            {
                maxSum += EvaluateElement(element, level, poolMax: 0, percentOfPool: false);
                found = true;
            }
            else if (type == SkillElementTypes.PenetrationDamageAdd)
            {
                pen = Math.Max(0, (int)MathF.Round(EvaluateElement(element, level, poolMax: 0, percentOfPool: false)));
            }
        }

        if (!found)
            return false;

        min = Math.Max(0, (int)MathF.Round(minSum));
        max = Math.Max(0, (int)MathF.Round(maxSum));
        if (max < min)
            (min, max) = (max, min);
        return min > 0 || max > 0 || pen > 0;
    }

    private static int RollDamage(int min, int max, int pen, long nowMs, long casterCoid, long targetCoid)
    {
        if (max < min)
            (min, max) = (max, min);

        var rolled = max > min
            ? new Random(unchecked((int)(nowMs ^ casterCoid ^ targetCoid))).Next(min, max + 1)
            : Math.Max(0, min);

        return Math.Max(0, rolled + Math.Max(0, pen));
    }

    private static float GetScalarElement(Skill skill, int elementType, int level)
    {
        var element = FindElement(skill, elementType);
        if (element == null)
            return 0f;

        return EvaluateElement(element.Value, level, poolMax: 0, percentOfPool: false);
    }

    private static void ReleaseCooldownReservation(
        (long CharacterCoid, int SkillId) key,
        long reservedUntilMs,
        bool reserved)
    {
        if (!reserved)
            return;

        lock (CooldownReservationLock)
        {
            if (CooldownUntilMs.TryGetValue(key, out var current) && current == reservedUntilMs)
                CooldownUntilMs.TryRemove(key, out _);
        }
    }

    private static SkillElement? FindElement(Skill skill, int elementType)
    {
        if (skill.Elements == null)
            return null;

        foreach (var element in skill.Elements)
        {
            if (element.ElementType == elementType)
                return element;
        }

        return null;
    }

    /// <summary>
    /// Absolute by default: base + perLevel*(level-1).
    /// For heal elements only, equation type 1 with fractional base/per-level is a fraction of
    /// <paramref name="poolMax"/> (retail pad heals like 0.15 of max HP).
    /// </summary>
    private static float EvaluateElement(SkillElement element, int level, int poolMax, bool percentOfPool)
    {
        // Retail FUN_0054b4a0 receives the learned rank and evaluates authored
        // fields as base + (rank * per-level); rank is not converted to zero-based.
        var value = element.ValueBase + (element.ValuePerLevel * Math.Max(0, level));
        if (percentOfPool
            && element.EquationType == 1
            && Math.Abs(element.ValueBase) <= 1f
            && Math.Abs(element.ValuePerLevel) <= 1f)
        {
            value *= Math.Max(0, poolMax);
        }

        return value;
    }

    private static void SendEffect(
        Character character,
        ClonedObjectBase caster,
        ClonedObjectBase target,
        TFID visualTargetId,
        int skillId,
        int skillLevel,
        int applyPower,
        int powerDelta,
        Vector3 position,
        bool isItemSkill)
    {
        var connection = character?.OwningConnection
            ?? caster.GetAsCharacter()?.OwningConnection
            ?? caster.GetSuperCharacter(false)?.OwningConnection;
        if (connection == null)
            return;

        var effectSourceId = !isItemSkill && character != null
            ? character.ObjectId
            : caster.ObjectId;
        var packet = new SkillStatusEffectPacket
        {
            SkillId = skillId,
            SkillLevel = (short)Math.Clamp(skillLevel, short.MinValue, short.MaxValue),
            ApplyPower = Math.Max(0, applyPower),
            Caster = effectSourceId,
            PosX = position.X,
            PosY = position.Y,
            PosZ = position.Z,
            Flag = isItemSkill ? (byte)1 : (byte)0,
        };
        var targetPower = GetTargetPower(target);
        packet.AddTarget(visualTargetId, targetPower.Current, targetPower.Maximum);
        connection.SendGamePacket(packet);

        // Victim may need the cast FX too when they are a different player.
        var victimConn = target.GetSuperCharacter(false)?.OwningConnection;
        if (victimConn != null && !ReferenceEquals(victimConn, connection))
        {
            var victimPacket = new SkillStatusEffectPacket
            {
                SkillId = skillId,
                SkillLevel = (short)Math.Clamp(skillLevel, short.MinValue, short.MaxValue),
                ApplyPower = Math.Max(0, applyPower),
                Caster = effectSourceId,
                PosX = position.X,
                PosY = position.Y,
                PosZ = position.Z,
                Flag = isItemSkill ? (byte)1 : (byte)0,
            };
            victimPacket.AddTarget(visualTargetId, targetPower.Current, targetPower.Maximum);
            victimConn.SendGamePacket(victimPacket);
        }
    }

    private static (short Current, short Maximum) GetTargetPower(ClonedObjectBase target)
    {
        var character = target.GetAsCharacter() ?? target.GetSuperCharacter(false);
        return character == null
            ? ((short)0, (short)0)
            : CharacterLevelManager.Instance.GetPower(character.ObjectId.Coid);
    }

    private static string FormatElements(Skill skill) =>
        string.Join(',', skill.Elements?.Select(element =>
            $"{element.ElementType}:{element.EquationType}:{element.ValueBase}") ?? []);
}
