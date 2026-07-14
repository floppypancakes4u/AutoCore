namespace AutoCore.Game.Combat;

using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Server-side vehicle combat pools (heat cool, shield/power regen) matching client
/// <c>CVOGHBRegeneration</c> / <c>FUN_005fbea0</c> @ 0x005FBEA0.
/// Retail fires one discrete pulse every 3000 ms (HB period at +0x8 for races 0/1/2).
/// HP does not recharge on this pulse (product design; shield/power still do).
/// </summary>
public static class VehicleCombatPool
{
    /// <summary>
    /// Client regeneration heartbeat period (ms).
    /// <c>CVOGHBRegeneration</c> constructor writes 3000 into HB+0x8; fire check at 0x005082C0
    /// requires GetTickCount elapsed &gt; that value before OnHeartBeat.
    /// </summary>
    public const int TickPeriodMs = 3000;

    /// <summary>When heat &gt;= max, cool rate is scaled by (1 - this). Client float @ 0x00A0F714 ≈ 0.3.</summary>
    public const float OverheatCoolScale = 0.7f;

    /// <summary>Client debounce after shield empties or heat hits max before regen/cool resumes.</summary>
    public const int EmptyShieldDebounceTicks = 2;

    /// <summary>
    /// Advance pool simulation by wall-clock <paramref name="deltaMs"/>, running as many
    /// 3000 ms client-equivalent pulses as fit.
    /// </summary>
    public static void Advance(Vehicle vehicle, Character owner, int deltaMs, bool weaponsFiring)
    {
        if (vehicle == null || deltaMs <= 0)
            return;

        vehicle.PoolTickAccumulatorMs += deltaMs;
        while (vehicle.PoolTickAccumulatorMs >= TickPeriodMs)
        {
            vehicle.PoolTickAccumulatorMs -= TickPeriodMs;
            Tick(vehicle, owner, weaponsFiring);
        }
    }

    /// <summary>One client-equivalent 3000 ms regeneration pulse (full raw rates).</summary>
    public static void Tick(Vehicle vehicle, Character owner, bool weaponsFiring)
    {
        if (vehicle == null || vehicle.GetIsCorpse())
            return;

        var heatBefore = vehicle.CurrentHeat;
        var shieldBefore = vehicle.CurrentShield;
        var hpBefore = vehicle.GetCurrentHP();
        short powerBefore = 0;
        if (owner != null)
            powerBefore = CharacterLevelManager.Instance.GetCurrentMana(owner.ObjectId.Coid);

        // Apply pool math without per-field client sends; notify once if anything changed.
        TickHeat(vehicle, weaponsFiring);
        TickShield(vehicle);
        TickPower(vehicle, owner);
        TickHp(vehicle);

        NotifyClientIfChanged(vehicle, owner, heatBefore, shieldBefore, hpBefore, powerBefore);
    }

    /// <summary>
    /// Push client updates for any pool field that changed this pulse.
    /// Power/HP use absolute <see cref="CharacterLevelPacket"/> (same as /power);
    /// heat/shield/HP/power also dirty vehicle ghost masks (same as /hp, /shield).
    /// </summary>
    private static void NotifyClientIfChanged(
        Vehicle vehicle,
        Character owner,
        int heatBefore,
        int shieldBefore,
        int hpBefore,
        short powerBefore)
    {
        var heatChanged = vehicle.CurrentHeat != heatBefore;
        var shieldChanged = vehicle.CurrentShield != shieldBefore;
        var hpChanged = vehicle.GetCurrentHP() != hpBefore;

        short powerAfter = powerBefore;
        if (owner != null)
            powerAfter = CharacterLevelManager.Instance.GetCurrentMana(owner.ObjectId.Coid);
        var powerChanged = powerAfter != powerBefore;

        if (!heatChanged && !shieldChanged && !hpChanged && !powerChanged)
            return;

        // Ghost masks — heat/shield require GhostVehicle (no CharacterLevel fields).
        // HP/power also dirty masks for observers; owner HUD uses CharacterLevel below.
        if (heatChanged)
            vehicle.EnsureGhostMaskDelivery(GhostVehicle.HeatMask);
        if (shieldChanged)
            vehicle.EnsureGhostMaskDelivery(GhostVehicle.ShieldMask);
        if (hpChanged)
            vehicle.EnsureGhostMaskDelivery(GhostObject.HealthMask);
        if (powerChanged)
            vehicle.EnsureGhostMaskDelivery(GhostVehicle.PowerMask);

        // Absolute CharacterLevel snapshot for the owning client (0x2017), matching /power.
        // Packet also carries Health / HealthMaximum, so HP regen is covered here too.
        if (owner != null && (powerChanged || hpChanged))
            CharacterLevelManager.Instance.SyncOwnedCombatHud(owner);
    }

    private static void TickHeat(Vehicle vehicle, bool weaponsFiring)
    {
        if (vehicle.CurrentHeat <= 0)
        {
            vehicle.HeatAtMaxDebounce = 0;
            return;
        }

        var coolRate = Math.Max(0, vehicle.CoolRate);
        if (coolRate == 0)
            return;

        // Client: when heat == max and debounce==0, arm to 2 (skip cool that tick);
        // while debounce > 0 and still at max, countdown without cooling.
        // Heat above max cools immediately at the overheat rate.
        if (vehicle.MaxHeat > 0 && vehicle.CurrentHeat == vehicle.MaxHeat)
        {
            if (vehicle.HeatAtMaxDebounce == 0)
            {
                vehicle.HeatAtMaxDebounce = EmptyShieldDebounceTicks;
                return;
            }

            vehicle.HeatAtMaxDebounce--;
            if (vehicle.HeatAtMaxDebounce > 0)
                return;
        }
        else if (vehicle.CurrentHeat < vehicle.MaxHeat || vehicle.MaxHeat <= 0)
        {
            vehicle.HeatAtMaxDebounce = 0;
        }

        var amount = coolRate;
        if (vehicle.MaxHeat > 0 && vehicle.CurrentHeat >= vehicle.MaxHeat)
            amount = (int)(coolRate * OverheatCoolScale);

        // While firing, client adjusts cool accumulator (+0x154); MVP: half cool rate.
        if (weaponsFiring)
            amount = Math.Max(0, amount / 2);

        if (amount <= 0)
            return;

        // Ghost notify is consolidated in NotifyClientIfChanged after the full pulse.
        vehicle.AddHeat(-amount, triggerGhostUpdate: false);
    }

    private static void TickShield(Vehicle vehicle)
    {
        if (vehicle.MaxShield <= 0 || vehicle.ShieldRegenRate <= 0)
            return;

        if (vehicle.CurrentShield >= vehicle.MaxShield)
        {
            vehicle.ShieldEmptyDebounce = 0;
            return;
        }

        // Client: when shield is 0 and debounce==0, arm to 2 (no regen that tick);
        // each subsequent empty tick decrements; regen when debounce returns to 0.
        if (vehicle.CurrentShield == 0)
        {
            if (vehicle.ShieldEmptyDebounce == 0)
            {
                vehicle.ShieldEmptyDebounce = EmptyShieldDebounceTicks;
                return;
            }

            vehicle.ShieldEmptyDebounce--;
            if (vehicle.ShieldEmptyDebounce > 0)
                return;
        }
        else
        {
            vehicle.ShieldEmptyDebounce = 0;
        }

        // Ghost dirty consolidated in NotifyClientIfChanged (ShieldMask); owner absolute
        // MultipleStatUpdate is sent from SetCurrentShield when triggerGhostUpdate is true —
        // pool tick batches ghost once, so notify owner explicitly when shield changes.
        var before = vehicle.CurrentShield;
        vehicle.SetCurrentShield(vehicle.CurrentShield + vehicle.ShieldRegenRate, triggerGhostUpdate: false);
        // Owner StatUpdate is required even when ghost is batched: client UI tracks +0x144
        // via FUN_0080B3A0 type=1 more reliably than owner-combat ghost deltas alone.
        if (vehicle.CurrentShield != before)
            vehicle.NotifyShieldChanged(includeMax: false);
    }

    private static void TickPower(Vehicle vehicle, Character owner)
    {
        if (owner == null || vehicle.PowerRegenRate <= 0)
            return;

        var (current, maximum) = CharacterLevelManager.Instance.GetPower(owner.ObjectId.Coid);
        if (current >= maximum)
            return;

        var next = (short)Math.Min(maximum, current + vehicle.PowerRegenRate);
        if (next == current)
            return;

        // Memory only; CharacterLevel + PowerMask sent from NotifyClientIfChanged.
        CharacterLevelManager.Instance.SetCurrentMana(owner, next, sendPacket: false);
    }

    /// <summary>
    /// Product design: vehicle HP does not recharge. Race-item <c>RaceRegenRate</c> may still be
    /// cached on the vehicle for future use, but is not applied each pool pulse.
    /// Shield and power continue to regen via <see cref="TickShield"/> / <see cref="TickPower"/>.
    /// </summary>
    private static void TickHp(Vehicle vehicle)
    {
        // Intentionally no-op.
    }
}
