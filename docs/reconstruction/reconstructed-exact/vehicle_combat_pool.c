/*
 * Human-readable reconstruction of autoassault.exe vehicle combat-pool RE.
 * Not compilable — Ghidra decomp cleaned for study / server porting.
 *
 * Session scope: tag session-combat-pool-re (see vehicle_combat_pool.md).
 * Names marked INFERRED are layout/type guesses, not PDB-verified.
 * Source of truth: Ghidra program autoassault.exe (plate comments + types).
 * Companion notes: vehicle_combat_pool.md
 */

#include <stdbool.h>
#include <stdint.h>
#include <math.h>

/* ---------- Ghost dirty masks (matches VehicleGhostMaskBits enum) ---------- */
enum VehicleGhostMaskBits {
    ShieldMask = 0x04000000,
    PowerMask  = 0x08000000,
    HeatMask   = 0x20000000,
};

/* ---------- Simplified vehicle / creature views ---------- */

typedef struct PowerPlant {
    /* clonebase-relative in live client; shown as fields for clarity */
    int16_t power_regen_rate;   /* +0xB8  sinPowerRegenRate */
    int16_t cool_rate;          /* +0xBA  CoolRate */
    float   skill_cd_modifier;  /* +0xCC  multiplies category cooldown scale */
    int     heat_capacity;      /* +0xB0  used by CalcHeatMaximum */
} PowerPlant;

typedef struct RaceItem {
    int16_t hp_regen_rate;      /* clonebase +0x3FA */
    int16_t shield_regen_rate;  /* clonebase +0x4B6 */
} RaceItem;

typedef struct Creature {
    int16_t current_power;      /* +0x12C */
    int16_t max_power;          /* +0x12E */
} Creature;

typedef struct Vehicle {
    int current_shield;         /* +0x144 */
    int max_shield;             /* +0x148 */
    int current_heat;           /* +0x150 */
    int cool_accumulator;       /* +0x154 */
    int heat_adjust;            /* +0x1E0  (CalcHeatMaximum) */
    int16_t cool_rate_adjust;   /* +0x1E4 */
    int max_heat;               /* +0x244 */
    PowerPlant *power_plant;    /* +0x268 */
    RaceItem *race_item;        /* +0x270 */
    void *combat_pool_action;   /* +0x27C  one-shot create slot */
    void *ghost_net_object;     /* graphics +0x18 path; non-null if ghosted */
    Creature *owner_creature;
} Vehicle;

typedef struct CombatPoolAction {
    int period_ms;              /* +0x08  3000 or 5000 */
    uint8_t heat_at_max_debounce;   /* +0x24  arms to 2 */
    uint8_t shield_empty_debounce;  /* +0x25  arms to 2 */
    uint8_t pad_26;             /* +0x26 */
    Vehicle *vehicle;           /* resolved via owner attach */
} CombatPoolAction;

/* Forward decls of helpers mirrored from Ghidra names */
void NetObject_SetMaskBits(void *this, uint32_t mask_lo, uint32_t mask_hi);
void CVOGHBList_Enqueue(void *list, CombatPoolAction *action);
void CVOGHBBase_Start(CombatPoolAction *action);
int  Creature_GetHpRegenFromEquippedRaceItem(Creature *creature);
void Vehicle_SetCurrentHP(Vehicle *v, int new_hp); /* via vtable path in client */

/* =====================================================================
 * 0x004F3870  Vehicle_GetPowerRegenRate
 *
 * Returns equipped power-plant sinPowerRegenRate, or 1 if none.
 * Units: points ADDED EACH PULSE (3000 ms for races 0–2), not per second.
 * ===================================================================== */
int Vehicle_GetPowerRegenRate(Vehicle *vehicle)
{
    if (vehicle->power_plant != NULL)
        return vehicle->power_plant->power_regen_rate;
    return 1;
}

/* =====================================================================
 * 0x004F3840  Vehicle_GetCoolRate
 *
 * plant.CoolRate (+0xBA) + vehicle cool adjust (+0x1E4).
 * Fallback plant-less: adjust + 1.
 * ===================================================================== */
int Vehicle_GetCoolRate(Vehicle *vehicle)
{
    if (vehicle->power_plant != NULL)
        return vehicle->cool_rate_adjust + vehicle->power_plant->cool_rate;
    return vehicle->cool_rate_adjust + 1;
}

/* =====================================================================
 * 0x004FB630  Vehicle_GetHpRegenRate
 *
 * Race-item RaceRegenRate at clonebase +0x3FA. 0 if no race item.
 * Live client walks: vehicle+0x270 → object base → clonebase+0x3C → +0x3FA.
 * ===================================================================== */
int Vehicle_GetHpRegenRate(Vehicle *vehicle)
{
    RaceItem *race_item = vehicle->race_item;
    if (race_item == NULL)
        return 0;
    return race_item->hp_regen_rate;
}

/* =====================================================================
 * 0x004FB600  Vehicle_GetShieldRegenRate
 *
 * Race-item RaceShieldRegenRate at clonebase +0x4B6. 0 if no race item.
 * ===================================================================== */
int Vehicle_GetShieldRegenRate(Vehicle *vehicle)
{
    RaceItem *race_item = vehicle->race_item;
    if (race_item == NULL)
        return 0;
    return race_item->shield_regen_rate;
}

/* =====================================================================
 * 0x00419140  Vehicle_SetCurrentShield
 *
 * Clamps into [0, MaxShield]. Does NOT dirty ghost masks —
 * VehicleCombatPool_OnTick dirties ShieldMask when the value actually changes.
 * ===================================================================== */
void Vehicle_SetCurrentShield(Vehicle *vehicle, int new_shield)
{
    int max_shield = vehicle->max_shield;
    int clamped = new_shield;

    if (max_shield <= new_shield)
        clamped = max_shield;

    if (clamped < 1) {
        vehicle->current_shield = 0;
        return;
    }

    if (new_shield < max_shield) {
        vehicle->current_shield = new_shield;
        return;
    }

    vehicle->current_shield = max_shield;
}

/* =====================================================================
 * 0x004F7210  Vehicle_AddHeat
 *
 * Adds heat_delta (negative cools). Clamps CurrentHeat to [0, MaxHeat*2].
 * Dirties HeatMask when value changes and the vehicle is ghosted.
 *
 * Decomp is messy (unaff_retaddr = heat_delta under __fastcall). Clean form:
 * ===================================================================== */
void Vehicle_AddHeat(Vehicle *vehicle, int heat_delta)
{
    int previous = vehicle->current_heat;

    if (vehicle->cool_accumulator < 0)
        vehicle->cool_accumulator = 0;

    /* Optional: divert some heat into character heat-sink when multiplier active.
     * Omitted here — see Ghidra body for vtable +0x210 / +0xC6C path. */

    vehicle->current_heat += heat_delta;

    int hard_cap = vehicle->max_heat * 2;
    if (vehicle->current_heat > hard_cap)
        vehicle->current_heat = hard_cap;
    if (vehicle->current_heat < 0)
        vehicle->current_heat = 0;

    if (vehicle->ghost_net_object != NULL && vehicle->current_heat != previous)
        NetObject_SetMaskBits(vehicle->ghost_net_object, HeatMask, 0);
}

/* =====================================================================
 * 0x004F7480  Vehicle_IsAnyWeaponFiring
 *
 * True if turret weapon (+0x264) or any of three hardpoint weapons
 * (table at +0x260) has firing flag at weapon+0xC7 set.
 * ===================================================================== */
bool Vehicle_IsAnyWeaponFiring(Vehicle *vehicle)
{
    /* Pseudocode — see Ghidra for exact hardpoint walk */
    (void)vehicle;
    return false;
}

/* =====================================================================
 * 0x004F7E10  Vehicle_CreateCombatPoolAction
 *   (was Vehicle_EnsureRegenerationHeartbeat / FUN_004F7E10)
 *
 * One-shot: if no action at +0x27C and sector map flag +0x7E is set,
 * allocate VehicleCombatPoolAction, enqueue, activate.
 * ===================================================================== */
void Vehicle_CreateCombatPoolAction(Vehicle *vehicle)
{
    if (vehicle->combat_pool_action != NULL)
        return;

    /* Map presence + sector-active flag (map+0x7E) — see Ghidra for pointer walk */
    bool map_allows_pool = true; /* placeholder for mapObject+0x7E check */
    if (!map_allows_pool)
        return;

    CombatPoolAction *regen = /* operator_new(0x28) */
        VehicleCombatPoolAction_ctor(/* vehicle base */, /* periodOverride */ 0);

    vehicle->combat_pool_action = regen;
    CVOGHBList_Enqueue(/* map HB list */, regen);
    CVOGHBBase_Start(regen);
}

/* =====================================================================
 * 0x005FBDB0  VehicleCombatPoolAction_ctor
 *
 * Period: override if non-zero, else by root race id:
 *   race 0,1,2 (Human/Biomek/Tribe) → 3000 ms
 *   else                            → 5000 ms
 * ===================================================================== */
CombatPoolAction *VehicleCombatPoolAction_ctor(
    CombatPoolAction *self,
    void *owner_object,
    int period_ms_override)
{
    /* CVOGHBBase_ctor, set vtable, clear debounce bytes */
    self->heat_at_max_debounce = 0;
    self->shield_empty_debounce = 0;
    self->pad_26 = 0;

    /* CVOGHBBase_SetPeriodAndCounter(self, -1000, always_fire=1) */
    self->period_ms = period_ms_override;

    if (period_ms_override == 0) {
        int race_id = /* Object_GetRootRaceId(owner) */ 0;
        if (race_id == 0 || race_id == 1 || race_id == 2)
            self->period_ms = 3000;
        else
            self->period_ms = 5000;
    }

    /* CVOGHBBase_AttachOwnerObject(owner_object) */
    (void)owner_object;
    return self;
}

/* =====================================================================
 * 0x005FBEA0  VehicleCombatPool_OnTick
 *   (was CVOGHBRegeneration_OnHeartBeat)
 *
 * One combat-pool pulse. Integer rates, NO dt multiply.
 * ===================================================================== */
void *VehicleCombatPool_OnTick(CombatPoolAction *self, void *out_status)
{
    Vehicle *vehicle = /* resolve from action owner attach */;
    Creature *creature = vehicle ? vehicle->owner_creature : NULL;
    if (vehicle == NULL || creature == NULL)
        goto done;

    /* --- 1) HP regen --- */
    int hp_now = /* get current HP via vtable */;
    int hp_delta = Vehicle_GetHpRegenRate(vehicle)
                 + Creature_GetHpRegenFromEquippedRaceItem(creature);
    Vehicle_SetCurrentHP(vehicle, hp_now + hp_delta);

    /* --- 2) Power regen --- */
    int16_t power_before = creature->current_power;
    int power_delta = Vehicle_GetPowerRegenRate(vehicle);
    int16_t power_after = (int16_t)(power_before + power_delta);
    if (power_after > creature->max_power)
        power_after = creature->max_power;
    creature->current_power = power_after;

    if (power_after != power_before && vehicle->ghost_net_object != NULL)
        NetObject_SetMaskBits(vehicle->ghost_net_object, PowerMask, 0);

    /* --- 3) Heat cool (with overheat debounce + fire slowdown) --- */
    if (self->heat_at_max_debounce == 0
        && vehicle->current_heat == vehicle->max_heat) {
        /* Just hit max: arm 2-tick debounce, force heat ghost dirty */
        self->heat_at_max_debounce = 2;
        if (vehicle->ghost_net_object != NULL)
            NetObject_SetMaskBits(vehicle->ghost_net_object, HeatMask, 0);
    } else {
        bool force_dirty = false;

        if (self->heat_at_max_debounce != 0) {
            if (vehicle->current_heat == vehicle->max_heat)
                self->heat_at_max_debounce--;
            else
                self->heat_at_max_debounce = 0;
            if (self->heat_at_max_debounce == 0)
                force_dirty = true;
        }

        if (self->heat_at_max_debounce == 0) {
            /* Cool accumulator nudge while firing */
            if (Vehicle_IsAnyWeaponFiring(vehicle)) {
                int acc = vehicle->cool_accumulator + 1; /* simplified */
                if (acc < 0) acc = 0;
                vehicle->cool_accumulator = acc;
            }

            int cool = Vehicle_GetCoolRate(vehicle);
            /* Overheat: cool at 70% (g_flOverheatCoolFrac ≈ 0.3 → *0.7) */
            if (vehicle->current_heat >= vehicle->max_heat) {
                cool = (int)((float)cool * 0.7f);
            }

            int heat_before = vehicle->current_heat;
            Vehicle_AddHeat(vehicle, -cool);
            if (vehicle->current_heat != heat_before || force_dirty) {
                if (vehicle->ghost_net_object != NULL)
                    NetObject_SetMaskBits(vehicle->ghost_net_object, HeatMask, 0);
            }
        }
    }

    /* --- 4) Shield regen (empty-shield 2-tick debounce) --- */
    if (vehicle->max_shield != 0) {
        if (self->shield_empty_debounce == 0 && vehicle->current_shield == 0) {
            self->shield_empty_debounce = 2;
            if (vehicle->ghost_net_object != NULL)
                NetObject_SetMaskBits(vehicle->ghost_net_object, ShieldMask, 0);
        } else {
            bool force_dirty = false;
            uint8_t deb = self->shield_empty_debounce;

            if (deb != 0) {
                if (vehicle->current_shield == 0)
                    self->shield_empty_debounce = deb - 1;
                else
                    self->shield_empty_debounce = 0;
                if (self->shield_empty_debounce == 0)
                    force_dirty = true;
            }

            int shield_before = vehicle->current_shield;
            if (self->shield_empty_debounce == 0) {
                int add = Vehicle_GetShieldRegenRate(vehicle);
                Vehicle_SetCurrentShield(vehicle, shield_before + add);
            }

            if ((vehicle->current_shield != shield_before || force_dirty)
                && vehicle->ghost_net_object != NULL) {
                NetObject_SetMaskBits(vehicle->ghost_net_object, ShieldMask, 0);
            }
        }
    }

done:
    /* TimedAction_GetNextDelayMs(out_status) — schedule next pulse */
    return out_status;
}

/* =====================================================================
 * 0x0056AD00  Weapon_ApplyShotHeatAndPowerCost
 *
 * On successful fire for player vehicle (owner type 0xE):
 *   1. Weapon_CanFireHeatCheck — abort if overheated
 *   2. Optional power cost from weapon short +0xD6 vs creature power
 *   3. Vehicle_AddHeat(weapon.sinHeat at +0xD4)
 * ===================================================================== */
int Weapon_ApplyShotHeatAndPowerCost(void *weapon)
{
    (void)weapon;
    /* See Ghidra body for vtable walks; returns 0 = blocked, 1 = applied */
    return 1;
}

/* =====================================================================
 * 0x0052A9B0  Vehicle_GetSkillCooldownModifier
 *
 * base_category_scale (default 1.0) * plant.skill_cd_modifier (+0xCC)
 * ===================================================================== */
float Vehicle_GetSkillCooldownModifier(Vehicle *vehicle, float category_scale)
{
    float scale = category_scale; /* looked up; default 1.0 if unmapped */
    if (vehicle != NULL && vehicle->power_plant != NULL)
        scale *= vehicle->power_plant->skill_cd_modifier;
    return scale;
}

/* Stubs referenced above (exist under other Ghidra names). */
void NetObject_SetMaskBits(void *this, uint32_t mask_lo, uint32_t mask_hi)
{
    (void)this;
    (void)mask_lo;
    (void)mask_hi;
}
void CVOGHBList_Enqueue(void *list, CombatPoolAction *action)
{
    (void)list;
    (void)action;
}
void CVOGHBBase_Start(CombatPoolAction *action) { (void)action; }
int Creature_GetHpRegenFromEquippedRaceItem(Creature *c) { (void)c; return 0; }
void Vehicle_SetCurrentHP(Vehicle *v, int new_hp) { (void)v; (void)new_hp; }
