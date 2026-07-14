# Skill Cast

## Reaction skill cast

`ReactionType.SkillCast` (12) is server-dispatched through `SkillService.TryCastReaction`.
For a map reaction, `GenericVar1` is the WAD skill ID and `GenericVar3` is its rank. The
currently authoritative reaction effect family is direct repair (`SkillElement.ElementType == 10`): it
restores the activating object's HP, dirties its health ghost state, and sends
`SkillStatusEffect` (0x2031) to the owning player. Missing or unsupported definitions return
false and make no state change.

Retail skill 857 is `INC Repair station heal`. Authored elements (clonebase.wad):

| Element | Id | Equation | Base | Effect |
|---------|----|----------|------|--------|
| `esetHeal` | **10** | 1 | 0.15 | **15% max HP** per pulse |
| `esetPower` | **12** | 1 | 0.15 | **15% max power** per pulse |
| `esetShield` | **58** | — | — | **not present** |

Fractional equation-1 heals are evaluated against the target's maximum pool.
Absolute heals (e.g. base `3` with per-level growth) stay absolute and are not multiplied by max HP.

**Shield:** repair pads do **not** restore shield. Shield refills only via race-item
`RaceShieldRegenerate` on the 3000 ms combat-pool pulse (and skills that actually
author `esetShield=58`). Server `RestoreHealth` is HP-only.

Collision triggers containing a `SkillCast` reaction pulse that reaction once per second while
the collider remains inside. Pulse deadlines are keyed by vehicle, trigger, and reaction, allowing
multiple vehicles to use the same pad independently. A full-health vehicle emits no skill traffic;
the cadence remains armed so repair resumes within one second if it takes damage while still on the
pad. Exiting, disconnecting, clearing, or resetting the trigger removes that vehicle's pulse state.

## Player skill cast

`RequestCastSkill` (0x2030) is routed after learned-skill validation. The handler calls
`SkillService.TryCastPlayer` with the character, skill id, learned rank, target TFID, and aim
position.

Supported effect families for player casts:

| Family | Elements | Behavior |
|---|---|---|
| Direct damage | `esetFlagDamageMin` (0x10000) / `esetFlagDamageMax` (0x20000) OR'd with channel type, plus optional `esetPenetrationDamageAdd` (68) | Roll min–max (summed per channel), add pen, `TakeDamage`, optional death, `Damage` (0x2023) floaters |
| Direct heal | `esetHeal` (10) | `RestoreHealth` on resolved target (self fallback when no damage and target missing) |

Validation (server authoritative):

- Skill definition must exist and include a supported effect
- Caster vehicle/map required
- Target resolved via map COID / ObjectManager; character TFID uses their vehicle
- Range (`esetRange=7`) when authored range &gt; 0
- Cooldown (`esetCoolDown=3`) per `(characterCoid, skillId)`
- Power cost (`esetCost=1`) is deducted from the **server** power pool before effects are applied.
  The client optimistically spends the authored cost when requesting the cast; the server must
  match that on **approve only** and must **not** push a spend update (`CharacterLevel` /
  `PowerMask`) on success — that would double-hit the HUD. Plant regen
  (`VehicleCombatPool.TickPower`) refills the server pool and dirties `GhostVehicle.PowerMask` so
  the bar climbs over time. On **reject** (`SkillStatusEffect` error status), the handler dirties
  `PowerMask` via `CharacterLevelManager.SyncCurrentPowerGhost` so an optimistic client spend can
  snap back to server truth (often full if cost was never taken). Login/commands still use
  `CharacterLevel` for max/current snapshots.

On success the server sends `SkillStatusEffect` (0x2031) to the caster (and victim owner when different).

Ghidra verification (`Client_RecvSkillStatusEffect` @ `0x00811170`,
`CVOGReaction_CastSkillOnTarget` @ `0x004D09A0`; named helpers from AutoCore RE session) establishes:

- `+0x10` is `lDelayTime`, the remaining charge/cast delay, not cooldown. Values greater than
  zero construct the active-skill heartbeat/VFX.
- `+0x14` is `eSkillResponses`: `0` success, `4` insufficient power, `6` busy,
  `7` recharge, `13` range, `14` target. Error responses abort the client's optimistic cooldown.
- `+0x38` is `bIsItemSkill`; learned player skills must send `0`, while reaction/item skills use `1`.
- Target entries are `{TFID, int16 mana, int16 maxMana, pad4}`, not damage deltas.
- The terminal TFID occupies a complete 24-byte target slot, including eight trailing zero bytes.
- `+0x28` is the source-owner object. For a learned player cast it must be the character TFID,
  not the current vehicle TFID. At `Client_RecvSkillStatusEffect +0x2FC` the client compares this
  field directly to `localCharacter+0x164`; a vehicle TFID fails that local-caster match. The
  retail player-cast assembly at `0x005D18A2-0x005D18CD` likewise pushes the character/root object
  for this field, while the resolved target is a separate argument. The character's object methods
  route presentation through its equipped vehicle.
- Retail's authoritative cast path (`FUN_005D1280`) resolves both the object-target list and effect
  position before calling `CVOGReaction_CastSkillOnTarget`. For direct object-targeted casts the
  server therefore uses the resolved target object's position; echoing the request vector can
  anchor VFX on the caster because that vector may be the aim/caster origin.
- The retail player path preserves the selected target TFID in the effect target list. A selected
  creature/driver may resolve to its vehicle for authoritative HP and combat, but replacing the
  selected TFID with that vehicle's adjacent COID breaks the client's animation attachment. The
  server now keeps visual target identity separate from the combat body.

The outbound client path (`Client_RequestCastSkill` @ `0x00941590` and
`Client_QuickBarActivateSkillSlot` @ `0x00921b50`) calls `Skill_StartCastAgainHeartbeat`
(`0x00519200`) before it sends `RequestCastSkill`. That constructs heartbeat type 8
(`CVOGHBOKToCastAgain_ctor` @ `0x0051e240`). Duration is
`ceil(skillRuntime+0x10 cooldown * Vehicle_GetSkillCooldownModifier) + chargeDelay`. Consequently there
is no separate server cooldown-start packet: a successful `SkillStatusEffect` confirms the cast,
while a failure response makes `Client_RecvSkillStatusEffect` find and destroy that optimistic
cooldown heartbeat. Sending cooldown in `lDelayTime` incorrectly creates a second active-skill
heartbeat instead of controlling the cooldown overlay.

The equipped modifier is `Vehicle_GetSkillCooldownModifier` (`0x0052a9b0`): base category scale
(default 1.0 / `g_flOne`) multiplied by the vehicle power plant's runtime `SkillCooldown`
(`CreatePowerPlant` trailing float, object `+0xcc`). AutoCore must send **identity `1.0f`**, not
`0.0f`. A zero multiplier collapses cast-again duration to charge-only (e.g. Tesla Strike 2103:
client ready again after ~900 ms while the server still holds the 14 s `esetCoolDown`). The hotbar
overlay (`i_d_qb_2d_btn_quickbar_cooldown.xml`, `QuickBar_UpdateSkillSlotCooldownGauge` /
`QuickBar_UpdateSlotCooldownOverlay`) reads that local cast-again / category map state via
`Skill_GetCategoryCooldownRemaining`; non-success `SkillStatusEffect` status values (e.g. Recharge
`7`) abort it.

### Ghidra name map (session RE — skill / cooldown / NPC cast)

| Address | Name | Notes |
|---|---|---|
| `00811170` | `Client_RecvSkillStatusEffect` | 0x2031 handler |
| `004d09a0` | `CVOGReaction_CastSkillOnTarget` | packer |
| `00941590` | `Client_RequestCastSkill` | player cast send 0x2030 |
| `00921b50` | `Client_QuickBarActivateSkillSlot` | hotbar activate |
| `0093a3d0` | `Client_StanceOrGadgetActivatePath` | **INFERRED** |
| `00519200` | `Skill_StartCastAgainHeartbeat` | optimistic CD HB |
| `0051e240` | `CVOGHBOKToCastAgain_ctor` | type 8 HB |
| `0051e390` / `0051e3b0` | `CVOGHBOKToCastAgain_OnStart` / `_OnEnd` | set/clear casting |
| `0051aa00` | `Skill_ApplyStatusEffectLocal` | post-status local apply |
| `0051a790` | `Skill_LocalCastValidate` | returns `eSkillResponses` |
| `0051a700` | `Skill_ClearCastBindingAndMaybeRestartCd` | **INFERRED** params |
| `00519150` | `Skill_GetCategoryCooldownRemaining` | hotbar CD source |
| `0052a9b0` | `Vehicle_GetSkillCooldownModifier` | plant+0xcc multiply |
| `005502d0` | `Skill_SetIsCastingFlag` | skill+0x628 |
| `00517b90` | `Skill_ClearActiveCastCounterAndQueueId` | |
| `00518cf0` | `Skill_LookupActiveCastBinding` | |
| `00518df0` | `Skill_InsertCategoryCooldown` | |
| `00518d70` | `Skill_GetCategoryCooldownMap` | lazy map at +0x6c |
| `0051d2f0` | `Skill_CategoryCooldownMap_Insert` | |
| `0051d3b0` | `Skill_QueueDeferredCastId` | |
| `0054b4a0` | `Skill_EvaluateRankedElements` | rank→cooldown/cost |
| `00553390` | `Skill_ReevaluateForCurrentRank` | |
| `005535a0` | `Skill_SetRankAndReevaluate` | |
| `00553480` | `Skill_CopyRuntimeFieldsFromTemplate` | |
| `00553710` | `Skill_InitializeRuntimeObject` | **INFERRED** full params |
| `0054fa20` | `Skill_FormatFailureMessage` | `eSkillResponses` strings |
| `00553130` | `Skill_LocalRangeTargetCheck` | **INFERRED** accuracy |
| `00825520` | `QuickBar_UpdateSkillSlotCooldownGauge` | |
| `00827ab0` | `QuickBar_UpdateSlotCooldownOverlay` | |
| `00829490` | `QuickBar_BuildSkillButtonWidgets` | |
| `00825e00` | `QuickBar_BuildItemButtonWidgets` | |
| `0097dfe0` | `UI_CooldownGaugeWidget_ctor` | |
| `00508200`–`005083b0` | `CVOGHBBase_*` | HB base |
| `005078f0` / `00507950` | `HeartbeatManager_Enqueue` / `_Tick` | |
| `00404aa0` / `0040b150` | `TFID_EqualsObjectId` / `TFID_NotEquals` | |
| `005d1280` | `NPC_TryCastSkillFromSet` | AI cast |
| `0051a980` | `Skill_EnsureLoadedInTree` | |
| `00550300` | `Skill_ResolveTargetList` | |
| `00553650` | `Skill_ValidateTargetForSkill` | |
| `0058d330` | `Skill_GatherTargetsInArea` | **INFERRED** |
| `00402d80` | `SkillSet_GetEntryCount` | |
| `004bb950` | `Object_ResolveFromTFID` | |
| `0050f940` / `00402210` | map lower_bound int/char | |
| `005d1df0` / `005d2360` | map erase / insert int key | |
| `005cced0` | `AI_CheckSlotTimerReady` | **INFERRED** |

### Types / enums / globals (session)

| Symbol | Kind | Notes |
|---|---|---|
| `NPCSkillSetEntry` | struct 0x18 | **INFERRED** skill-set row |
| `TFID_16` | struct 16 | coid + global + pad |
| `SkillRuntime_inferred` | struct | sparse key offsets only |
| `eSkillResponses` | enum u8 | 0 ok … 7 recharge … 14 wrong target |
| `eSkillFlagBits_inferred` | enum | skill+0x614 **INFERRED** |
| `g_dwClientTickMs` | global | `00b041cc` |
| `g_flOne` / `g_flZero` / `g_flMsToSeconds` | globals | |
| `g_abTfidInvalid_*` | globals | `-1,-1,0,0` sentinels |
| `g_flInferredThreatScale` | global | NPC threat **INFERRED** |

### NPC_TryCastSkillFromSet notes

- Struct `NPCSkillSetEntry` (0x18): `nSkillId`, `usPostCastDelayMs`, rank, `flHpRatioMin`/`Max` — **INFERRED** from stride uses.
- this+0x64 = owner object, this+0x9c = post-cast timer map, this+0x08 = cast-chance scalar — **INFERRED**.
- Plate comments on session functions mark **VERIFIED** vs **INFERRED** explicitly.

The WAD is loaded once into `WADLoader.Skills` (4,452 entries in the retail data). Tesla Strike
(`2103`) rank 2 has cost `21 + (2 × 24) = 69`, cooldown (`esetCoolDown=3`) `14000` ms, and charge delay
(`esetCharge=6`) `900` ms. The client begins that authored charge before sending the request.
Because the current server resolves the authoritative effect synchronously, its successful
`SkillStatusEffect` sends `lDelayTime=0` (no charge time remains). A positive delay would create a
second target-bound active-skill heartbeat; on lethal casts that heartbeat can be stranded by the
following `DestroyObject`. The server still enforces the authored 14-second cooldown independently.

Still unsupported for player casts: DoTs/frequency ticks, splash multi-target, chains, summons,
buffs/debuffs, passives, accuracy/resist channels, `CancelSkill` handling, and full power-plant
→ mana sync on login.

Client intent packet layouts:

| Opcode | Packet | Body after opcode |
|---|---|---|
| 0x2030 | `RequestCastSkill` | pad4, target TFID, skill ID i32, target position Vector3 |
| 0x2031 | `SkillStatusEffect` | size, skillId, level, applyPower, status, position, caster TFID, targets… |
| 0x2032 | `CancelSkill` | pad4, target TFID, skill ID i32 |

`CancelSkill` packet reader exists but is not routed yet.
