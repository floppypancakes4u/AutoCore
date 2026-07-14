# How character attributes scale combat (Destroyer140 fork)

This documents exactly how the four persistent character attributes feed the combat math in
our fork, so it can be reproduced independently. Every formula below is taken from **our source
as the implementation truth** (file:line), and corroborated where possible with the
reverse-engineered client (Ghidra address / in-game attribute tooltip text).

Reference source (self-contained) lives in [`reference/`](reference/):
`DamageCalculator.cs`, `CharacterStats.cs`, `DamageSpecific.cs`, `CreatureSpecific.cs`,
`Vehicle.pools.reference.cs`.

---

## The attribute set

Persistent, one row per character (`character_stats` table). All default to `1`.
Source: `reference/CharacterStats.cs` (`AutoCore.Database/Char/Models/CharacterStats.cs`).

| Attribute            | Field                          | In-game role                    | Combat quantities it scales in our fork |
|----------------------|--------------------------------|---------------------------------|-----------------------------------------|
| **Combat**           | `AttributeCombat` (:26)        | offensive to-hit                | Hit rating (attacker) |
| **Perception**       | `AttributePerception` (:28)    | targeting / awareness           | Crit chance (attacker) **and** defense/avoidance (victim) |
| **Theory**           | `AttributeTheory` (:27)        | tech penetration / power        | Armor-resistance penetration (attacker) **and** max Power pool |
| **Tech**             | `AttributeTech` (:25)          | durability / heat capacity      | Max Heat pool (**and** character-HP derivation) |

Attributes reach the damage math through `Character.Stats` (`Character.cs:50`), read into a
`Combatant` record in `DamageCalculator.ReadAttacker` / `ReadVictim`
(`DamageCalculator.cs:264-304`). NPC victims read the mirror fields off `CreatureSpecific`
(`reference/CreatureSpecific.cs:9-19`: `AttributeCombat`, `AttributePerception`,
`AttributeTheory`, `DefensiveBonus`), plus per-creature elite bonuses.

Everything is **server-authoritative**: the retail client is display-only for player damage
(live-confirmed 2026-07-07 — killing an Ostrake tripped neither `OnFire` nor
`CalculateCriticalHit`), so the server owns the whole formula and the client just applies the
`0x2023` damage int. (`DamageCalculator.cs:8-13`.)

---

## Combat quantity 1 — Hit rating / accuracy (the to-hit roll)

**Where:** `DamageCalculator.ComputeHitChance` (`DamageCalculator.cs:211-227`), called from
`Compute` (:100) only when the target is a `Creature`/`Vehicle` (inanimate targets auto-hit, :98).

**Formula (verbatim from our code):**

```
attackRating  = atk.Combat + weapon.OffenseBonus + weapon.HitBonusPerLevel * atk.Level     (:222)
defenseRating = vic.DefenseBonus + vic.Perception                                          (:223)
chance        = 0.75 + (attackRating - defenseRating) * (1/200) + modeHitAdd               (:224, 48-49)
if weapon.AccucaryModifier > 0:  chance *= weapon.AccucaryModifier                          (:225)
chance        = clamp(chance, 0.05, 0.95)                                                   (:226, 47)
```

Anti-farm level gate applied **first** (`:215-217`): `|atk.Level - vic.Level| > 9` pins the
result to 0.95 (attacker ≥10 levels up) or 0.05 (≥10 down). Battle-mode accuracy (`modeHitAdd`,
e.g. Frenzy −0.05, Sharpshooter +0.33) is folded **after** the gate so it can never break the pins.

**Per-attribute contribution:**

| Attribute (side)          | Effect on `chance`                                   | Constant | Line |
|---------------------------|------------------------------------------------------|----------|------|
| **Combat** (attacker)     | +1 attackRating per point → **+0.5% hit per point**  | `HitChancePerRating = 1/200` | `:222,49,224` |
| **Perception** (victim)   | +1 defenseRating per point → **−0.5% attacker hit per point** | same 1/200 | `:223,224` |

So Combat is the offensive accuracy stat and Perception is the defensive dodge stat; they cancel
one-for-one through the same `1/200` slope.

**Evidence / provenance:**
- Attacker Combat sourced from `Stats.AttributeCombat` — `DamageCalculator.cs:271`.
- Victim Perception sourced from owning char's `Stats.AttributePerception` (player) or
  `CreatureSpecific.AttributePerception (+ elite bonus)` (NPC) — `:286, :298`.
- Client anchor: `CVOGSectorMap::CalculateHitChance @0x4ceba0` — inanimate→1.0 "AutoHit",
  else attacker Combat vs victim Perception + offense, `|levelDelta|>9` gate, clamp .05/.95
  (`DamageCalculator.cs:19-20`).
- **Confidence:** the ±9 gate and the .05/.95 pins are RE-SOLID. The base curve
  (`HitChanceBase = 0.75`, slope `1/200`) is **structurally** authentic but the two magnitudes
  are **live-tune knobs** — the client curve decompiles out of messy x87 FPU code
  (flagged in code at `:44-49`). This is the least-pinned constant in the model; treat the
  *shape* (Combat up = hit up, victim Perception up = hit down, one-for-one) as the reliable part.

---

## Combat quantity 2 — Damage (base roll → class scalar → level bonus → mitigation)

Attributes do **not** roll the base damage — that comes from the weapon's 6-channel
`[min,max]` arrays (`DamageSpecific`, 6× `short`; `reference/DamageSpecific.cs`) and the attacker
**Class** (not an attribute). For completeness, the damage pipeline is
(`DamageCalculator.cs:106-160`):

```
for each of 6 damage channels t:
    lo = round(min[t] * classMul);  hi = round(max[t] * classMul)          (:131-132)
    if t == primaryType:  lo += levelBonus;  hi += levelBonus              (:134)   // perLevelDmg * level
    roll = rand(lo, hi)                                                     (:136)
    roll -= <Theory-penetrated armor mitigation>   // see quantity 3       (:138-147)
    baseTotal += max(0, roll)
```

- `classMul = ClassDamageBalance[attackerClass]` = `{1.35, 1.15, 1.0, 1.23}` indexed by
  `CharacterSpecific.Class` (0-3), **not** an attribute and **not** a weapon field
  (`DamageCalculator.cs:70-77, 121-124`). Client `OnHit @0x515520` indexes `DAT_009cdf9c` by the
  attacker Class (verified `read_memory @0x009cdf9c`).

The only **attribute** input to the damage number itself is **Theory**, via armor penetration
(next section), and — indirectly — crit, which multiplies the final number (quantity 4).

---

## Combat quantity 3 — Theory penetration (armor / resistance bypass)

**Where:** inside the per-channel mitigation step of `Compute` (`DamageCalculator.cs:138-147`),
and identically in the deterministic test path `ComputeFixed` (`:199-205`).

**Formula (verbatim):**

```
pen       = min(0.9, atk.Theory * 0.004)                                   (:143, 60-61)
effResist = resist[t] * (1 - pen)                                          (:144)
cap       = ceil(effResist * 0.1)                                          (:145, 52)
if cap > 0:  roll -= rand(1, cap)     // per-type armor mitigation roll     (:146)
```

**Per-attribute contribution:**

| Attribute (side)      | Effect                                                        | Constant | Line |
|-----------------------|--------------------------------------------------------------|----------|------|
| **Theory** (attacker) | Shaves **0.4% of victim effective resistance per point**, capped at **90%** (Theory ≥ 225 → cap). Lower effective resist → smaller armor-mitigation roll → more damage lands. | `TheoryPenetrationPerPoint = 0.004`, `TheoryPenetrationMax = 0.9` | `:60-61,143` |

Worked example (from the RE report): attacker Theory=250, target per-type resist=100 →
`pen = min(0.9, 1.0) = 0.9` → `effResist = 10` → `cap = 1`, versus Theory=1 → `pen ≈ 0.004` →
`effResist ≈ 99.6` → `cap = 10`. The high-Theory attacker punches through nearly all of that
channel's armor.

**Evidence / provenance:**
- Attacker Theory sourced from `Stats.AttributeTheory` — `DamageCalculator.cs:273`.
- In-game attribute tooltip: **"Enemy Resistance Reduction %"** (`DamageCalculator.cs:56`).
- Client anchor: `OnHit @0x515520` sets the per-hit combat penetration from
  `GetAttribTheory @0x4c4140`, then `fVar21 = mitRoll * penDeflect * DAT_009cdf80(~0.004);
  mitigation -= fVar21` (RE report §1.2).
- **Confidence:** the *mechanism* (attacker Theory reduces per-type armor mitigation) is
  RE-CONFIRMED, and the **0.004 factor is verified** (`DAT_009cdf80 = 0x3b83126f`, RE report §1.2).
  The 90% cap and the exact "reduce the mitigation cap vs reduce the rolled amount" placement are
  our tuning choice (both flagged live-tunable at `:56-61`). This overturns the fork's old
  "attributes are pure equip gates" assumption — retail intends Theory to modify damage.

---

## Combat quantity 4 — Crit chance & magnitude

**Where:** `ComputeCritChance` (`DamageCalculator.cs:229-239`); rolled and applied in `Compute`
(`:171-176`).

**Formulas (verbatim):**

```
critChance = 0.02 + atk.Perception * 0.0018 + atk.CritOffense - vic.CritDefense    (:235-236, 39-40)
if critChance < 0:  critChance = 0.05          // negative-only floor, NO upper cap  (:237-238, 38)
// battle-mode crit add (e.g. Sniper +0.33) added at the roll site                   (:172)

on crit:  dmg *= attackerLevel * 0.01 + 1.2    // magnitude                          (:175, 32-33)
```

**Per-attribute contribution:**

| Attribute (side)        | Effect on crit                                        | Constant | Line |
|-------------------------|-------------------------------------------------------|----------|------|
| **Perception** (attacker) | **+0.18% crit chance per point** (base 2%)          | `CritChancePerPerception = 0.0018`, `CritChanceBase = 0.02` | `:39-40,235` |

Crit **magnitude** scales with attacker **level**, not an attribute
(`level*0.01 + 1.2`, e.g. level 20 → 1.4× damage; `:32-33,175`).

`atk.CritOffense` and `vic.CritDefense` are wired into the formula but **hardcoded 0** today
(`:273-274, 287, 299`) — see the honesty section.

**Evidence / provenance:**
- Attacker Perception sourced from `Stats.AttributePerception` — `DamageCalculator.cs:272`.
- Client anchors: base-from-Perception `GetBaseCriticalHitChance @0x4c4dd0`; assembly
  `GetCriticalHitChance @0x4cef70` (negative-only 0.05 floor `DAT_009cbf80`, no upper cap);
  multiplier `GetCriticalHitMultiplier @0x4cd550` = `level*0.01 + 1.2`
  (`DamageCalculator.cs:17-18, 31, 231-234`).
- **Confidence:** crit **multiplier** (`level*0.01 + 1.2`) and the **negative-only floor / no
  upper cap** are RE-SOLID. The **Perception→chance base magnitude** (`0.02 + Perception*0.0018`)
  is structurally authentic but FPU-approximated → **live-tune knob** (flagged `:35-40`; watch
  `GetBaseCriticalHitChance @0x4c4dd0` when tuning).

---

## Combat quantity 5 — Power & Heat pools

These are vehicle pools recomputed on spawn/load/equip and whenever attributes change
(`Character.SaveStats` → `ComputeAndSetMaxHitPoints`, `Character.cs:304-305`). Source:
`reference/Vehicle.pools.reference.cs` (`Vehicle.cs:717-777`).

**Formulas (verbatim):**

```
MaxHeat  = ceil(level + Tech*0.5   + powerplant.HeatMaximum)     // else 10 if no plant   (Vehicle.cs:732-734)
MaxPower = ceil(level*PowerLevelCoeff[class] + Theory*2 + powerplant.PowerMaximum)  // else 0 (Vehicle.cs:737-738)
           PowerLevelCoeff = { 0.6, 1.0, 1.0, 0.75 }  by class                          (Vehicle.cs:211)
```

**Per-attribute contribution:**

| Attribute (side)      | Pool      | Effect                          | Constant | Line |
|-----------------------|-----------|---------------------------------|----------|------|
| **Tech** (self)       | Max Heat  | **+0.5 heat cap per point**     | `Tech*0.5f` | `Vehicle.cs:733` |
| **Theory** (self)     | Max Power | **+2 power per point** (coeff hard 2.0) | `theory*2` | `Vehicle.cs:738` |

Also relevant: character HP is derived from **Tech** in the stat block —
`MaxHp = 100 + (AttributeTech - 1) * 3` (`reference/CharacterStats.cs:39`) — used for the
on-foot / town-map mana fallback path (`Character.cs:281-282`).

**Evidence / provenance:**
- Client anchors: `CalculateMaximumHeat @0x4f7360` (level + Tech*0.5 + plant heat; level/class
  coeff all 1.0) and `CalculateMaximumMana @0x4f74c0` (Theory coeff hard 2.0, per-class level
  coeff table) — `Vehicle.cs:730-738`.
- **Confidence:** RE-CONFIRMED structure and coefficients (Tech×0.5, Theory×2, per-class power
  coeff). Note the **vehicle combat-HP** formula (`ComputeAndSetMaxHitPoints`, `Vehicle.cs:687-709`)
  does **not** currently fold in Tech (`armorHpBonus = 0`) — a known gap where SCAR's
  `VehicleHitPointCalculator` is actually ahead of us (it adds `techPool*3`).

---

## How this plugs into a damage / hit pipeline (integration note for SCAR's tree)

Our whole model lives in one static class, `DamageCalculator.Compute(attackerChar, attackerLevel,
target, weapon, rng, isSprayTarget, dist, battleMode)` returning
`Result(int Damage, bool IsCrit, bool Miss)` (`DamageCalculator.cs:80-182`). It reads attributes
itself via `attackerChar.Stats` and the victim's clonebase — the caller passes entities, not raw
numbers. Order inside `Compute`: **hit/miss → base roll × class scalar + level bonus →
Theory-penetrated mitigation → spray falloff → crit → global scalar**.

Mapping onto your `0cbf5c66` combat files:

- **`SectorCombatTick` / `ProcessCombatIfFiring`** — this is where a `Compute(...)` call would slot
  in per firing slot; today your tick is exception-isolation + pool bookkeeping and applies no
  attribute-derived hit/damage/crit number.
- **`ClonedObjectBase.TakeDamage(int, attacker)`** (your `ClonedObjectBase.cs:145`) — the natural
  sink for `Result.Damage`; the hit-roll (`Result.Miss`) and crit flag (`Result.IsCrit`, for the
  floater) would be decided *before* this call, by the attribute math.
- **`VehicleHitPointCalculator` / `VehicleHeatCalculator`** — already your Tech→HP and Tech→heat
  path; the missing sibling is a **Theory→MaxPower** calc (`level*coeff + Theory*2 + plant`), which
  we do inline in `ComputeAndSetMaxPools` (`reference/Vehicle.pools.reference.cs`).
- **`CharacterAttributeService`** — your `ApplyTechCombatSideEffects` already recomputes pools on
  Tech spend; the same hook is where a **Theory** spend would recompute MaxPower, and where nothing
  needs to happen for Combat/Perception (they're read live at hit time, not cached).

Nothing here requires our SQLite/persistence choices — it's pure per-hit math over four `short`
attribute fields you already store.

---

## Honesty ledger — RE-confirmed vs live-tuned vs not-yet-modeled

**RE-confirmed structure + verified constants (safe to trust):**
- Theory-penetration mechanism and the **0.004** factor (`DAT_009cdf80`).
- Per-class damage table `{1.35,1.15,1.0,1.23}` (`read_memory @0x009cdf9c`) — Class, not attribute.
- Crit multiplier `level*0.01 + 1.2`; crit negative-only floor, no upper cap.
- `±9` level hit gate with 0.95/0.05 pins.
- Per-type armor cap `ceil(resist*0.1)`; primary channel = argmax(max).
- Pool coefficients: Heat `Tech*0.5`, Power `Theory*2`, per-class power coeff `{0.6,1.0,1.0,0.75}`.

**Structure authentic, magnitude is a LIVE-TUNE knob (don't quote the constant as gospel):**
- Hit curve baseline `HitChanceBase = 0.75` and slope `HitChancePerRating = 1/200`
  (x87-fuzzy; the *shape* — Combat↑ hit↑, victim Perception↑ hit↓, one-for-one — is the reliable part).
- Crit base from Perception `0.02 + Perception*0.0018` (FPU-approximated).
- Theory penetration cap `0.9` and the reduce-the-cap placement.

**Wired but NOT yet modeled (placeholder `0` — do not claim these work):**
- **Crit-offense / crit-defense** gear stats (`atk.CritOffense`, `vic.CritDefense` = `0f`,
  `DamageCalculator.cs:273-274,287,299`). RE-confirmed to exist in the client
  (`+0x1d8/+0x1dc` offense, `+0x1e0/+0x1e4` defense) but the stat fields don't exist in our data yet.
- **Attacker defense** (`atk.DefenseBonus = 0`); only victim defense is populated.
- **Vehicle combat-HP does not include Tech** yet (`armorHpBonus = 0`, `Vehicle.cs:702-704`).

Bottom line: the **shape** of every attribute→combat relationship in this document is
RE-derived and reproducible from our source; a handful of **magnitudes** (hit slope, crit base,
penetration cap) are explicitly live-tune knobs, and the crit-gear stats are stubs. We flag these
so nothing here is over-claimed.
