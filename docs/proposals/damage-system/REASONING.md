# Damage system proposal — Ghidra reasoning

**Proposal:** adopt a multi-slot cone / AoE, multi-hit weapon-fire model for vehicle combat, driven by
one `EMSG_Sector_Damage` (0x2023) packet per tick that carries *every* hit. Reference implementation is
in `reference/` (additive; drops under `docs/proposals/`, touches none of your combat files).

All addresses are in the retail client `autoassault.exe`, **image_base 0x400000**. Every claim below is
independently checkable by loading that binary in Ghidra at the cited address — no trust in our fork
required. Where a magnitude decompiles out of messy x87 FPU code we flag it as a live-tunable; the
*structure* is pinned by the disassembly.

---

## 0. TL;DR of the deltas vs your current master (0cbf5c66)

Your `DamagePacket` (Packets/Sector/DamagePacket.cs) has already converged on the correct **wire**:
a `Source` TFID + `hitCount` u16 + N `DamageEntry` (crit flag, s16 amount, target coid, target-global,
then IsResist / IsDeflect / 7 pad flags). That is exactly the shape this document argues is intended,
and your Resist/Deflect flag mapping is the piece **we** want to adopt back (see §6). Credit where due.

What is *not* yet there is the **server fire model that populates more than one entry**. Your
`Vehicle.ProcessCombatInternal` (Entities/Vehicle.cs) still:

- picks exactly **one** weapon slot per tick via an `if / else-if` chain (front > turret > rear), so
  holding two slots (Firing = 0x03/0x05/0x07) silently drops the others;
- **requires a hard `Target`** and fires only at it — there is no cone, so `WeaponSpecific.ValidArc`,
  `.SprayTargets`, and `.ExplosionRadius` are parsed but unused in combat;
- applies **no per-type armor/resistance mitigation**, **no crit roll**, **no per-class scalar**, and
  **no Theory penetration**;
- calls `TrySendDamagePacket` -> `AddHit` **once**, so the multi-entry packet only ever carries 1 hit.

The client, disassembled below, drives 3 independent slot cones and unpacks an N-hit packet. The
sections map each client fact to the corresponding piece of the reference code.

---

## 1. The wire is multi-hit by construction — `unpackDamage @0x636f00`

`EMSG_Sector_Damage_Unpack` (FUN_00636F00) reads a header then **loops `hitCount` entries**:

```
hitCount = readBits(0x10)                 // u16 count — NOT fixed at 1
for each hit:
    hit+0x14 = readFlag()                 // FIRST per-hit flag  -> CRIT
    hit+0x10 = readBits(0x10)             // s16 damage
    hit+0x00 = readBits(0x40)             // target coid (i64)
    hit+0x08 = readFlag()                 // target-global
    hit+0x1b = readFlag()                 // trailing flag 0 -> RESIST  (see §6)
    hit+0x1c = readFlag()                 // trailing flag 1 -> DEFLECT (see §6)
    ... 7 more trailing flags (pad)
```

The per-hit record carries its **own** target coid at +0x00. Multiple *different* targets in one packet
is therefore the intended shape, not a special case. A cone or a rocket splash that hits 4 things is one
0x2023 with `hitCount = 4`, each entry a distinct coid. Sending 4 separate single-hit packets is legal
but is not what the format is built for, and it multiplies RPC overhead per trigger-pull.

Reference: `reference/DamagePacket.cs` `Write()` walks `Hits` and emits one entry each;
`reference/VehicleCombat.reference.cs` `ProcessCombatInternal` accumulates every slot's + every splash
hit into that one `Hits` list before a single send.

## 2. Crit is the FIRST per-hit flag — `Process_EMSG_Sector_Damage @0x812a60`, `UpdateDamageNotifications @0x93ffb0`

The apply/repack path FUN_00812A60 copies `packet+0x15 <- hit+0x14` (uStack_43 = *puVar7), i.e. the
first flag read in §1. The notification formatter FUN_0093FFB0 renders the `!` / "Critical" styling from
`notif+0x29`, which traces back to that same first flag. So **crit must be the first per-hit flag**, and
it is per-hit (each entry crits independently), not a packet-level flag.

This also disproves an easy misread: the first flag is *not* a "primary/direct-hit" marker. We hit that
exact bug (writing `primary = true` on every hit made every number render as a crit). `reference/DamagePacket.cs`
`WriteHit` documents it; your packet already labels it `IsCrit`, so you are correct here.

Your fire model never *sets* crit, though — there is no crit roll in `ProcessCombatInternal`. §5 supplies it.

## 3. The client fires 3 slots, each into its own cone — `SetWeaponsFiring @0x5021d0`, `FireWeaponsPrimary @0x4f50d0`, `FindDistanceToTarget @0x4e9aa0`

- `SetWeaponsFiring` (FUN_005021d0) and `FireWeaponsPrimary` (FUN_004f50d0) iterate the weapon slots
  and fire each **set firing bit independently** — front, turret, rear are not mutually exclusive. The
  `if/else-if` single-slot selection in the current server model is the divergence: with Firing = 0x03
  the client fires front *and* turret; the server fires only front.
- Cone geometry: `CVOGPhysicsUtils::FindDistanceToTarget` (FUN_004e9aa0) builds an `hkConvexVerticesShape`
  cone from the arc **half-angle `acos(ValidArc)`** and casts it against the target rigid body. So
  `WeaponSpecific.ValidArc` is stored as the **cosine of the cone half-angle**, and the cheap server
  equivalent is a dot-product test: a candidate is in-cone iff `dot(aimDir, unit(target - shooter)) >= ValidArc`.
  Live-confirmed value: `ValidArc = 0.987 = cos(9.25 deg)` = an 18.5 deg full cone. (An earlier server
  build wrongly treated ValidArc as an angle in radians; that is the bug this pins shut.)

Per-slot aim in the reference model: **front offset 0** (fixed vehicle-forward, no turret contribution),
**turret offset = WantedTurretDirection** (the rotating lock the client sends with 0x200A), **rear offset
PI** (fixed rearward). The single hard `Target` the client transmits is the *turret's* lock, so only the
turret pre-seeds it; front/rear draw their primary from their own cone. See `AcquireFireTargets`.

## 4. Multi-target spray + distance falloff — `CVOGWeapon::OnFire @0x56e000`

`OnFire` (FUN_0056e000) is the per-shot orchestrator: it resolves the primary hit, then walks secondary
cone targets up to the weapon's spray count, applying a **distance falloff measured from the primary
target's impact point** (not from the shooter). `WeaponSpecific.SprayTargets` is that cap.

Falloff factor = `1.05 - dist/range` (no upper clamp — a point-blank secondary can take a slight >1.0
bonus; the client has no `min(…,1.0)`). Reference: `DamageCalculator.Compute(isSprayTarget:true, dist)`
and `SprayFalloffBase = 1.05f`. Because the falloff is measured from the primary impact, a cluster of
targets downrange all take near-full damage, which matches the client.

## 5. Damage math — `OnHit @0x515520`, `GetTotalDamageLevelBonus @0x56b340`, crit @0x4cef70 / 0x4cf080, hit @0x4ceba0

Server owns the whole formula (live-confirmed 2026-07-07: killing an NPC tripped neither client `OnFire`
nor `CalculateCriticalHit` — the client just applies the s16 we send). Pipeline per target:

1. **Hit / miss** — `CalculateHitChance @0x4ceba0`. Inanimate victims (buildings, props, mission objects)
   short-circuit to 1.0 ("AutoHit"); only Creatures/Vehicles roll. The **`|atkLvl - vicLvl| > 9` anti-farm
   gate is SOLID** (pins outcome to 0.95 / 0.05). Base curve = attacker Combat + weapon offense vs victim
   defense; clamp 0.05/0.95. (Your current `0.65 + (offense-def)/200` is close in spirit but omits the
   level gate and the attacker Combat attribute.)

2. **Base damage** — `OnHit @0x515520` rolls each of the **6 damage-type channels** in `[min,max]`, adds
   the per-level bonus to the primary type (`GetTotalDamageLevelBonus @0x56b340` = `perLevelDmg * level`),
   then subtracts **per-type armor mitigation** ~`rand(1, resist * 0.1)` per channel. **This mitigation
   step is entirely absent from the current server model** — it sums the 6 channels raw. `DamageSpecific`
   (the short[6]) is the resistance/armor vector; `ArmorSpecific.Resistances` on the equipped vehicle
   armor takes precedence over the clonebase.

3. **Per-class attacker scalar** — `OnHit @0x515520` indexes `DAT_009cdf9c` by the **attacker's Class**
   (enumClassType 0-3, character+0x531; proven via `GetClassString @0x51f940` / `GetFileNameLetters
   @0x51f550`) and scales every raw channel *before* the level bonus and mitigation. Our live-tuned table:

   | Class | Career (per race) | Scalar |
   |------:|-------------------|:------:|
   | 0 | tank-bruiser (Commando / Champion / Terminator) | **1.35** |
   | 1 | healer/buffer (Engineer / Shaman / Constructor) | **1.15** |
   | 2 | summoner (Lieutenant / Archon / MasterMind) — baseline (damage lives in pets/skills) | **1.00** |
   | 3 | ranged DPS (Bounty Hunter / Avenger / Agent) | **1.23** |

   NPC creature attackers have no Class -> factor 1.0. The index is the *attacker* Class, never a weapon
   field. (Magnitudes are the live-tunable; the *index-by-attacker-class* structure is the RE fact.)

4. **Theory penetration** — `OnHit @0x515520` sets the per-hit combat penetration to the attacker's
   `GetAttribTheory @0x4c4140`, overwriting the weapon's own penetration for attribute-bearing attackers.
   In-game attribute tooltip: "Enemy Resistance Reduction %". Formula in the reference:
   `effResist = resist * (1 - min(0.9, Theory * 0.004))` applied *before* the mitigation subtraction in
   step 2. So Theory 100 trims 40% of the victim's resistance, capped at 90%. (0.004/point is the
   live-tunable; the "attacker Theory shaves victim resistance before mitigation" structure is RE-pinned.)

5. **Crit** — `GetCriticalHitChance @0x4cef70` = base(Perception) + attacker crit-offense − victim
   crit-defense, floored at 0.05 (`DAT_009cbf80`) with **no** upper cap (a low-Perception attacker
   correctly stays below 5%). `CalculateCriticalHit @0x4cf080`: `d100 <= chance`; multiplier =
   `level*0.01 + 1.2` (`GetCriticalHitMultiplier @0x4cd550`, SOLID). Sets the per-hit crit flag (§2).

6. **Global + weapon scalar** — `* DamageScalar * GlobalDamageScalar` (retail `setplayerdamageglobal`
   knob, default 1.0), floor 1.

Full implementation with every constant is `reference/DamageCalculator.cs`. It is a self-contained pure
function (`Compute(attacker, level, target, weapon, rng, isSpray, dist, battleMode) -> (Damage, IsCrit,
Miss)`); the fire loop just calls it per target.

## 6. AoE splash — `WeaponSpecific.ExplosionRadius`

Explosive ordnance (`ExplosionRadius > 0`; rocket/missile launchers carry 8-10) detonates at the primary
impact point and damages **every** hostile inside the radius, not just the direct target. It is
omnidirectional — a pure 3D radius, no cone/arc — and excludes every target already hit directly (no
double-hit). Splash victims run through the same `DamageCalculator` path with falloff measured from the
blast center, and demolishable world objects in radius are caught too. Reference: `AcquireExplosionTargets`
+ the splash loop in `ProcessCombatInternal`. `ExplosionRadius` already exists in your `WeaponSpecific`;
it is simply never read by the fire model today.

## 7. BIDIRECTIONAL — we adopt your Resist/Deflect flags

This is a genuinely two-way merge. Your `DamagePacket` already encodes the trailing flags correctly and
cites the same client funcs:

- `IsCrit`    -> event+0x29 (crit styling) — first per-hit flag (§2)
- `IsResist`  -> event+0x2B ("Resist")  — trailing flag 0 (`hit+0x1b`, `uStack_d = puVar7[7]` in 0x812a60,
  case 0 of 0x93ffb0)
- `IsDeflect` -> event+0x2C ("Deflect") — trailing flag 1 (`hit+0x1c`, `uStack_c = puVar7[8]`)

Our reference `DamagePacket.WriteHit` currently writes the crit flag correctly but leaves all 9 trailing
flags `false`. The trailing-flag **count matches yours (9)**, and positions 0 and 1 of that block are
exactly Resist/Deflect — so the wire is already byte-compatible; adoption is literally replacing our
first two `false` trailing writes with `isResist` / `isDeflect` and computing them in `DamageCalculator`
(Resist when per-type mitigation fully absorbs a channel; Deflect on a glance/deflect outcome). Net: you
take our fire model, we take your flag semantics; no schema break either direction.

---

## 8. How to integrate into your current tree

The reference code is additive and self-contained. Suggested landing against your file names:

1. **`Combat/DamageCalculator.cs`** *(new file)* — drop `reference/DamageCalculator.cs` in as-is. It
   depends only on your existing `Character` / `ClonedObjectBase` / `WeaponSpecific` / `Creature` /
   `Vehicle` / `CloneBaseCharacter.CharacterSpecific.Class` / `ArmorSpecific.Resistances`. Your
   `WeaponSpecific` already has every field it reads (`MinMin/MaxMax` short[6], `DamageBonusPerLevel`,
   `DamageScalar`, `RangeMax`, `AccucaryModifier`, `OffenseBonus`, `HitBonusPerLevel`). `BattleModeEffect`
   is optional — pass `default` if you are not wiring combat modes yet; every mode term degrades to a no-op.

2. **`Combat/BattleMode.cs`** *(optional new file)* — only if you want the class-3 (Frenzy / Sharpshooter
   / Sniper) refire / accuracy / crit / flat-physical modifiers. Otherwise skip it and pass
   `BattleModeEffect.None`.

3. **`Packets/Sector/DamagePacket.cs`** *(your file — no wire change)* — your `Entries` + `AddHit` +
   Resist/Deflect flags are already correct. The only functional change is that the fire loop should call
   `AddHit` **once per hit** (cone + splash), so a multi-target tick emits one packet with N entries. Keep
   your `TrySendDamagePacket` throttle and your attacker+victim recipient dedup — the reference
   `CollectCombatWitnessConnections` is the same idea; use whichever you prefer.

4. **`Entities/Vehicle.cs` `ProcessCombatInternal`** *(your file — the real change)* — replace the
   single-slot `if/else-if` + single-`Target` body with the reference `ProcessCombatInternal` +
   `AcquireFireTargets` + `AcquireExplosionTargets` + `IsHostileFireTarget` + `FireWeaponAtTarget`. These
   reference the same `Firing` bits, `WeaponFront/Turret/Rear`, `WantedTurretDirection`, `Heat`/`MaxHeat`,
   `Map.Objects`, `_lastFireMs*` slot stamps, and `Target` you already have. The per-slot `_lastFireMs*`
   stamps let each slot cool independently. Drop the `Debug*` cone overrides unless you want the `/conetest`
   tuning command.

5. **NPC fire visibility** — your `TrySendDamagePacket` already sends to attacker+victim, so NPC-fired
   shots are visible; the reference witness set is equivalent and reused for the trailing `DestroyObject`
   sends too.

No change is required to `WeaponSpecific`, `DamageSpecific`, `CreateWeaponPacket`, or your pool/heat
(`VehicleCombatPool` / `VehicleHeatCalculator`) code — those are orthogonal and stay as they are. This
proposal is purely the *target-acquisition + damage-math + multi-hit-send* layer.

---

## 9. Verification checklist (for independent Ghidra confirmation)

| Claim | Address | What to look for |
|-------|---------|------------------|
| N-hit packet, per-hit coid | `unpackDamage` 0x636f00 | loop over `hitCount = readBits(0x10)`; coid `readBits(0x40)` -> hit+0x00 |
| crit = first per-hit flag | `Process_EMSG_Sector_Damage` 0x812a60 | `packet+0x15 <- hit+0x14`; `UpdateDamageNotifications` 0x93ffb0 renders '!' from notif+0x29 |
| resist/deflect trailing flags | 0x812a60 / 0x93ffb0 | `uStack_d = puVar7[7]` (hit+0x1b -> notif+0x2b "Resist"), `uStack_c = puVar7[8]` (hit+0x1c -> notif+0x2c "Deflect") |
| 3 independent slot cones | `SetWeaponsFiring` 0x5021d0, `FireWeaponsPrimary` 0x4f50d0 | per-slot firing-bit loop, not mutually exclusive |
| ValidArc = cos(half-angle) | `FindDistanceToTarget` 0x4e9aa0 | Havok cone from `acos(ValidArc)` half-angle |
| spray + falloff from impact | `OnFire` 0x56e000 | secondary loop up to spray cap; falloff vs primary impact point |
| 6-channel roll + mitigation | `OnHit` 0x515520 | per-type `[min,max]` roll; per-type armor subtract |
| per-class scalar by attacker Class | `OnHit` 0x515520 + `GetClassString` 0x51f940 | `DAT_009cdf9c[Class]`; Class = character+0x531 |
| level bonus on primary type | `GetTotalDamageLevelBonus` 0x56b340 | `perLevelDmg * level` |
| Theory penetration | `OnHit` 0x515520 + `GetAttribTheory` 0x4c4140 | penetration set from attacker Theory |
| crit chance floor 0.05, no cap | `GetCriticalHitChance` 0x4cef70 | `DAT_009cbf80` floor only |
| crit mult level*0.01+1.2 | `GetCriticalHitMultiplier` 0x4cd550 | `__real_3f99999a`-ish + level term |
| hit `|levelDelta|>9` gate | `CalculateHitChance` 0x4ceba0 | pin 0.95/0.05 outside ±9 |
