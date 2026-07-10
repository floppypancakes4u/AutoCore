# Stage 10 — Combat AI (vehicles) — Report

**Branch:** feature/npc-ai
**baseSha:** `1cd84c6`  →  **headSha:** `71e1b1c`
**Commit:** `71e1b1c` Add Stage 10 combat AI: NPC vehicle aggro, pursuit, firing, death loot

## What I implemented

### New production code
- **`src/AutoCore.Game/Npc/FactionHostility.cs`** — single choke point `IsHostile(a,b)`.
  Symmetric heuristic: an NPC faction (`>=3`) aggros any distinct real faction (`>=0`);
  player races `0/1/2` never mutual-aggro; `-1` (unset) / `-100` (neutral) never aggro.
- **`src/AutoCore.Game/Npc/NpcCombatAi.cs`** — the combat brain (mirrors `CVOGHBAIDriver_DoLogic`):
  - **IdlePatrol:** throttled aggro scan (`ScanIntervalMs = 500`, staggered by `coid % 500`);
    `Grid.QueryRadius(pos, aggroRange)` where `aggroRange = max(VisionRange, HearingRange)` of the
    driver (vehicle owner) / own clonebase, falling back to `Profile.ValHelpRange` then `50f`;
    field maps only (`!IsTown`); filters corpse/invincible/non-candidate/non-hostile; on a hit:
    `SetTargetObject`, stamp `EngageStartedMs`, `HelpCalled = false`, enter Engage. `ReturningHome`
    steers back toward `HomePosition` and clears within 5u before re-scanning.
  - **Engage:** closes to `weaponRangeMax * 0.8`; after `Profile.ValFleeOrEngageTimerMs` transitions
    to Combat (flee eval is Stage 11).
  - **Combat:** target dead/gone → reset to IdlePatrol + ReturningHome; out of range → cease fire and
    pursue at driver speed; in range → raise the `Firing` bit for the equipped slot (front=1/turret=2/
    rear=4) and reuse `Vehicle.ProcessCombatIfFiring()` (full player pipeline).
  - **Leash:** `dist(home) > max(PatrolDistance, 80)` → drop target, IdlePatrol, ReturningHome, cease fire.
  - **State change:** `SetCombatState` dirties the vehicle `StateMask` (wire byte mirrored onto the
    driver creature's `AiCombatState`) or the creature `StateMask`.
- **`src/AutoCore.Game/Npc/NpcTicker.cs`** — routes every live NPC entity through `NpcCombatAi.Tick`
  each tick before falling back to idle-patrol path movement (skipped while engaged or homing).
  Exposed `ResolveSpeed` / speed-fallback constants as `internal` for reuse.

### Refactors / additions
- **`Vehicle.TrySendDamagePacket`** — now `internal`, takes the victim object, and delivers the
  `DamagePacket` to **both** the attacker's and the victim's owning connections (deduped). Rate-limit
  key is the **attacker vehicle COID** (`source.Coid`); 100 ms throttle and try/catch retained. This
  fixes NPC hits being invisible to victims (NPC attackers have no `OwningConnection`).
  Added `ClearCombatThrottleForTests`.
- **`Vehicle.OnDeath`** — NPC override (`NpcAi != null`): rolls `tVehicleTemplate` loot
  (`LootTableId/LootChance/LootRolls`, `TemplateId>=0 → AssetManager.GetVehicleTemplate`), spawns it,
  leaves the map, and broadcasts `DestroyObject` (mirrors `Creature.OnDeath`). Player vehicles keep base behavior.
- **`LootManager`** — added `GenerateLoot(int lootTableId, byte lootChance, byte lootRolls, byte level)`
  plus `SeedGeneratableItemForTests` / `ResetForTests` seams.
- **`AssetManager`** — added `_testLootTables` + `SetTestLootTables`, checked first in `GetLootTable`,
  cleared by `ClearTestNpcData`.

### Tests (10 new, all green)
- `FactionHostilityTests.IsHostile_MatrixCases`
- `NpcCombatAiTests`: `IdlePatrol_HostilePlayerInVisionRange_AcquiresTargetAndEngages`,
  `IdlePatrol_FriendlyOrNeutral_NoAggro`, `Engage_TimerElapsed_TransitionsToCombat`,
  `Combat_OutOfRange_PursuesTowardTarget_AtDriverSpeed`,
  `Combat_InRange_SetsFiringBitForEquippedWeapon_AndInvokesCombat`,
  `Combat_BeyondPatrolDistance_LeashesHome_ClearsTargetAndFiring`,
  `StateChange_SetsStateAndTargetGhostMasks`,
  `TrySendDamagePacket_NpcAttacker_DeliversToVictimConnection`
- `NpcVehicleDeathTests.NpcVehicleDeath_RollsTemplateLoot_RemovesFromMap_Broadcasts`

## TDD RED/GREEN evidence

**FactionHostility — RED** (`dotnet build AutoCore.Game.Tests`):
```
FactionHostilityTests.cs(20,23): error CS0103: The name 'FactionHostility' does not exist in the current context
```
**GREEN:** `Passed! - Failed: 0, Passed: 1, Total: 1`

**NpcCombatAi — RED** (build):
```
NpcCombatAiTests.cs(58,9): error CS0103: The name 'NpcCombatAi' does not exist in the current context
NpcCombatAiTests.cs(203,17): error CS0117: 'Vehicle' does not contain a definition for 'TrySendDamagePacket'
```
**First run after implementation:** 6/8 passing, 2 failing for identified reasons:
- `IdlePatrol_FriendlyOrNeutral_NoAggro` — my test wrongly used a biomek *player* (faction 2), which
  *is* hostile to an NPC faction; corrected to a same-faction ally + neutral.
- `TrySendDamagePacket_NpcAttacker_DeliversToVictimConnection` — the static per-attacker throttle dict
  leaked across tests (coids repeat across fresh test maps); added `ClearCombatThrottleForTests` in SetUp.
**GREEN:** `Passed! - Failed: 0, Passed: 8, Total: 8`

**NpcVehicleDeath — RED** was implicit (new type/overload); **GREEN:** `Passed! - Failed: 0, Passed: 1, Total: 1`

**Full suite** (`dotnet test src/AutoCore.sln`):
```
AutoCore.Utils.Tests:  Passed! 14
AutoCore.Sector.Tests: Passed!  5
AutoCore.Game.Tests:   Failed!  Failed: 2, Passed: 862, Skipped: 1, Total: 865
  Failed Boost_ActOnActivator_SucceedsWithoutUnhandled
  Failed Boost_EmptyObjects_SucceedsWithoutUnhandled
```
The only 2 failures are the **pre-existing** `ReactionBoost` baseline failures (missing Boost handler,
out of scope). **Zero new failures.**

## Self-review findings
- **No new warnings** — build of my files produced only the repo-wide pre-existing `CS8632`
  (nullable-annotation-context) warnings; nothing new.
- **YAGNI:** deliberately skipped the optional spawn-point respawn stretch (tracked separately).
- **Duplication (accepted):** `SpawnLootOnGround` is intentionally mirrored from `Creature.OnDeath`
  per the brief's "mirror" instruction; kept local to each entity type rather than prematurely
  extracting a shared helper.
- **`[ExcludeFromCodeCoverage]`** removed from `TrySendDamagePacket` now that it is unit-tested;
  the default `dotnet test src/AutoCore.sln` collects no coverage, so no threshold is affected.

## Concerns / notes
- `Combat_InRange_..._AndInvokesCombat` asserts the AI's deterministic contribution (`Firing == 1`
  and `Target` set) rather than landed damage: `ProcessCombatInternal`'s hit roll is seeded partly by
  `Environment.TickCount64`, so asserting a damage number would be flaky. Combat invocation itself is
  structurally guaranteed by the code path.
- `FactionHostility` uses the documented heuristic (NPC.md Risk 2 pending RE); it is the single choke
  point, so a future RE refinement is localized to one method.
- Foot-creature NPCs can enter Engage/Combat via the shared scan but only pursue (the firing pipeline
  is `Vehicle`-only); this stage's scope is vehicle combat.

## Fix round (review findings)

### Finding 1 [IMPORTANT] — Verbatim destroy-broadcast duplication (Vehicle.OnDeath)
The NPC-vehicle destroy broadcast in `Vehicle.OnDeath` was a third hand-rolled copy of the
`foreach (Character) SendGamePacket(DestroyObjectPacket)` loop already implemented by the base helper
`GraphicsObject.BroadcastDestroy(SectorMap, TFID)`; `Creature.OnDeath` had a second copy. The Vehicle
copy was strictly worse — it swallowed send exceptions with a bare `catch {}` (dropping the
`LogType.Error` logging the helper provides).

Changes:
- `src/AutoCore.Game/Entities/GraphicsObject.cs`: changed `BroadcastDestroy` from `private static` to
  `protected static` so subclasses can reuse it.
- `src/AutoCore.Game/Entities/Vehicle.cs`: deleted the inline `foreach`/`try-catch` loop in `OnDeath`
  and replaced it with `BroadcastDestroy(map, vehicleObjectId);` — restores the dropped error logging
  and the shared `ForceNetworkHelperFailureForTests` path.
- `src/AutoCore.Game/Entities/Creature.cs`: deleted the inline `foreach` loop in `OnDeath` and replaced
  it with `BroadcastDestroy(map, creatureObjectId);`, collapsing the duplication to a single site.

Test added (regression guard for the refactored path):
- `src/AutoCore.Game.Tests/NpcAi/NpcVehicleDeathTests.cs`:
  `NpcVehicleDeath_BroadcastSendFailure_DoesNotThrow` drives `Vehicle.OnDeath` under
  `GraphicsObject.ForceNetworkHelperFailureForTests = true` (which only the shared helper honors) and
  asserts the vehicle is still marked a corpse and still leaves the map — proving the death broadcast
  now routes through `BroadcastDestroy` and a failed send does not abort death handling.

Tests run:
- Targeted: `dotnet test src/AutoCore.sln --filter "FullyQualifiedName~NpcVehicleDeathTests|FullyQualifiedName~GraphicsObjectCoverageTests|FullyQualifiedName~Creature"`
  → `Passed! - Failed: 0, Passed: 38, Skipped: 0, Total: 38`.
- Full suite: `dotnet test src/AutoCore.sln`
  → Utils 14/14 passed, Sector 5/5 passed, Game 863 passed / 2 failed / 1 skipped. The 2 failures are
  the pre-existing baseline `ReactionBoostTests.Boost_ActOnActivator_SucceedsWithoutUnhandled` and
  `Boost_EmptyObjects_SucceedsWithoutUnhandled` (missing Boost reaction handler, out of scope). Zero new
  failures relative to baseline.
