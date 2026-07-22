# Mission Persistence — Work Log & Handoff

> **Canonical handler behavior:** [missionHandler.md](missionHandler.md) — how grant/progress/advance/complete must work per requirement type. This file remains the **persistence handoff** and open live-bug log.

Handoff for continuing the mission-persistence feature. Read this top to bottom, then
`docs/missionState.md` for the deeper protocol/RE reference. Work is on branch
**`feature/mission-persistence`** (branched from `feature/npc-ai`).

---

## Goals

1. **Persist mission state across client + server restarts.** A returning player must resume the
   exact state they left: every active mission with its current objective and per-objective
   progress, plus the set of completed missions.
2. **No repeating finished missions.** A character who completed a non-repeatable mission must not
   be re-offered it or re-granted it — via NPC dialog **or** map trigger/reaction.
3. **The client must see the correct completed/active state on login** so its journal, NPC offers,
   and prerequisite gating are correct.

The server is authoritative for mission state; it streams state to the client in the
`CreateCharacterExtended` (0x2016) create packet, which the client parses into its active/completed
hash tables.

---

## Current status (TL;DR)

- **Mission acceptance persistence fixed:** the database contained the established
  `character_mission` / `character_mission_completed` tables plus a second, accidentally introduced
  `character_quest` / `character_completed_mission` pair. Acceptance was written to the parallel
  tables, splitting mission history between two schemas. The established tables are now canonical.
- Startup performs an idempotent compatibility merge from the parallel tables. Completed missions
  are unioned first, missing active missions are copied without overwriting canonical progress, and
  completed state removes conflicting active rows. The parallel tables are retained for rollback
  inspection but are no longer read or written.
- Failed queued DB writes are retained for a later mutation/disconnect flush instead of being
  discarded. Background failure does not spin an unbounded retry loop.
- **Active mission client restore fixed from Ghidra evidence:** the old 72-byte serializer was
  incorrect. It wrote sequence/state at `+0x04` and progress/max pairs from `+0x08`; the client
  actually expects mission saved state at `+0x08`, the global active objective id at `+0x30`, and
  four objective state floats at `+0x34`. The create tail now matches the client parser exactly.

- Core persistence is implemented and unit-tested: schema, a queue-backed persistence service,
  mutation hooks, login load, create-packet delivery of completed ids, a reaction re-grant guard,
  and a disconnect flush.
- One confirmed bug fixed: the reaction/trigger `GiveMission` path re-broadcast a `0x206C` on relog
  and re-added completed missions client-side (see "Fixed issues").
- **Open, unresolved:** after turning in "New Day Dawning!" (mission 554) and relogging without
  accepting "Live and Direct", the follow-up mission is not offered. Evidence points to a
  **completion that doesn't survive relog for some flow** (a character is stuck with 554 persisted
  as *active*, never *completed*). Needs live-server evidence to close — diagnostics are in place.
- Build/tests: `AutoCore.Game` and `AutoCore.Game.Tests` compile; full suite is green except **2
  pre-existing Boost baseline failures** (`ReactionBoostTests`, unrelated — ReactionType 24 has no
  handler). 24 mission-persistence tests pass.

---

## What has been implemented

### 1. Database schema — `src/AutoCore.Database/Char/`
- **`Models/CharacterQuestData.cs`** → table `character_mission`, PK `(CharacterCoid, MissionId)`:
  `ActiveObjectiveSequence` (byte), `State` (byte), `ObjectiveProgress` (`byte[]` VARBINARY — packed
  little-endian int32 slots). `ObjectiveMax` is NOT stored; re-derived from the mission template.
- **`Models/CharacterCompletedMissionData.cs`** → table `character_mission_completed`, PK
  `(CharacterCoid, MissionId)`.
- **`CharContext.cs`**: added both `DbSet`s, composite keys in `OnModelCreating`, and
  `EnsureMissionSchema()` (idempotent create + compatibility merge) called from `EnsureCreated()`.
  Pattern mirrors `EnsureInventorySchema()`.

### 2. Persistence service — `src/AutoCore.Game/Managers/`
- **`MissionPersistenceQueue.cs`**: latest-wins `ConcurrentDictionary<(long Coid, int MissionId),
  QuestPersistOp>`. `QuestPersistOp` is `Upsert(seq,state,progressBytes)` or `Complete()`. Has
  `Enqueue`, `Flush`, `Clear`, `RemoveForCharacter(coid)`.
- **`MissionPersistence.cs`** (`Singleton`): mirrors `ExplorationManager`. Background ThreadPool
  flush on enqueue (`AutoFlushOnEnqueue`), DI seams for tests (`PersistQuestRow`, `DeleteAllRows`),
  `ResetPersistenceForTests`. Public API:
  - `OnQuestChanged(character, quest)` → enqueue Upsert.
  - `OnMissionCompleted(coid, missionId)` → enqueue Complete (delete quest row + insert completed row).
  - `FlushPending()` → drain (background + disconnect + tests).
  - `DeleteAllForCharacter(coid)` → drop pending + delete both tables' rows for the character.
  - `PackProgress`/`UnpackProgress` (int[] ↔ byte[]).
  - EF paths guard on `string.IsNullOrEmpty(CharContext.ConnectionString)` (no-op offline/tests) and
    are `[ExcludeFromCodeCoverage]`.

### 3. Mutation hooks (enqueue on every in-memory change)
- `Entities/Reaction.cs` — `HandleGiveMission` (after `CurrentQuests.Add`) and
  `HandleSetActiveObjective` (after sequence set) → `OnQuestChanged`.
- `Managers/NpcInteractHandler.cs` — `GrantMission` → `OnQuestChanged`;
  `AdvanceOrCompleteObjective` advance branch → `OnQuestChanged`, complete branch (`~:1090`, after
  `CompletedMissionIds.Add`) → `OnMissionCompleted`; `TryCompleteDeliverFromDialog` (`~:803`) →
  `OnMissionCompleted`.
- `Managers/MissionKillProgress.cs` — partial-progress branch → `OnQuestChanged`.
- **Verified these are the only completion sites**: grep for `CompletedMissionIds.Add` /
  `CurrentQuests.Remove` returns exactly the two `NpcInteractHandler` sites (both hooked) plus the
  load in `Character.Missions.cs`.

### 4. Login load — `src/AutoCore.Game/Entities/Character.Missions.cs` (new partial)
- `LoadMissions(CharContext context)`: reads canonical `character_mission` → `CurrentQuests` (calls
  `PopulateFromAssets()` to fill `ObjectiveMax`, then overlays stored `ObjectiveProgress`,
  `ActiveObjectiveSequence`, `State`); reads `character_mission_completed` → `CompletedMissionIds`.
  Logs loaded counts.
- `SetMissionsForTests(...)` seam.
- Called from `Character.cs` `LoadFromDB` right after `LoadExplorations`, inside
  `if (!isInCharacterSelection)`. Sector entry uses `GetOrLoadCharacter` →
  `LoadFromDB(..., isInCharacterSelection: false)`, so `LoadMissions` runs on login.

### 5. Create-packet delivery — `Packets/Sector/CreateCharacterExtendedPacket.cs` + `Character.cs`
- Added `List<int> CompletedMissionIds` to the packet; `Write` emits the completed id array
  (replaced the old zero-fill at the completed-quests slot).
- `Character.WriteToPacket` sets `NumCompletedQuests = CompletedMissionIds.Count` and passes the ids
  (was hard-coded `0`). Also logs current + completed ids being sent.
- **Verified byte layout** with a unit test (`CreatePacket_AbsoluteOffsets_MatchClientLayout`): with
  a 4-byte uint opcode prefix, `NumCompletedQuests`@`0x1A8`, `NumCurrentQuests`@`0x1AC`,
  FirstTimeFlags@`0x8EC` (known-good anchor), and the completed id lands at tail start `0x1358`
  (skills=0). Matches client `CVOGCharacter_ApplyCreateFromPacket` @`0x00535174`.
- Ghidra verification of the current-mission record loop established the 72-byte layout:
  `missionId:i32`, reserved `i32`, ten saved-mission `i32` values, active global `objectiveId:i32`,
  four saved-objective `float` slots, reserved `i32`. Progress is normalized to `0..1` and written
  to the objective requirements' authored `FirstStateSlot` values.

### 6. Disconnect flush — `TNL/TNLConnection.cs`
- `EndCharacterSession` calls `MissionPersistence.Instance.FlushPending()` alongside the existing
  world-state persist, so a fast logout can't outrun the background flush.

### 7. Diagnostic chat commands — `Chat/ChatCommandService.cs`
- **`/showMissions`** — prints server-side `CompletedMissionIds` and `CurrentQuests`
  (mission id, active seq, progress/max). The definitive check for server state after relog.
- **`/clearAllMissions`** — wipes the character's active + completed missions from memory and DB
  (and drops pending writes). Escape hatch for characters stuck in a bad persisted state. Client
  journal resets on next relog.

---

## Fixed issues

### Reaction `GiveMission` re-broadcast on relog (FIXED)
`Reaction.HandleGiveMission` previously returned `true` when it *declined* to grant (already-active,
or completed-non-repeatable). `SectorMap.TriggerReactions` (`SectorMap.cs:541-545`) packs a reaction
into the client `GroupReactionCall` (0x206C) **iff `TriggerIfPossible` returns true**. On relog the
map `PerPlayerLoad` trigger re-fires `GiveMission`, so the server broadcast `GiveMission(554)` to the
client, which re-added the completed mission as **active** client-side while the server kept it
completed-only. Symptom: a finished mission showed as active and un-turn-in-able, and the follow-up
was offered anyway.

**Fix:** both decline branches now `return false` (mirrors `NpcInteractHandler.CanOfferMission`,
`:547-585`). Only a genuine grant returns `true`. Covered by
`ReactionGiveMission_CompletedNonRepeatable_NotReGrantedAndNotSentToClient`,
`ReactionGiveMission_ActiveMission_NotReSentToClient`,
`ReactionGiveMission_CompletedRepeatable_IsReGrantedAndSent`.

### Rogers UseObject / New Day deliver (FIXED)
UseObject on Rogers (CBID **2477**) logged `no dialog missions` then fell through to OpenStore even
with mission **554** (New Day Dawning) active. Retail shape: `Mission.NPC = -1` (no giver); turn-in
is deliver-only via GLM `TargetNPCCBID=2477` on objective **714**. Live and Direct (**3032**) is
given by the same NPC after turn-in.

**Root cause:** `AssetManager.LoadAllData` started WAD and GLM in parallel. `Mission.Read` pulls
`{name}.xml` from GLM during WAD load; when WAD won the race, objective **Requirements** stayed
empty, so `HasDeliverTurnIn` never matched Rogers.

**Fix:**
1. Load GLM to completion before starting WAD.
2. `Mission.ApplyGlmXml` / `MissionObjective.ApplyGlmXml` can re-attach requirements; after both
   loaders succeed, `ReapplyMissingMissionGlmXml` backfills any mission still missing Requirement
   children.

**Tests:** `HandleUseObject_NpcMinusOneDeliverOnly_OpensTurnInDialog`,
`HandleUseObject_AfterNpcMinusOneTurnIn_OffersFollowUpFromSameNpc`,
`MissionGlmXmlApplyTests.ApplyGlmXml_AttachesDeliverTarget_EnablesRogersStyleUseObject`.

### Mission NPC vehicles looked invulnerable (FIXED)
`FactionDirty` combat spawns (Final Exam Gunny template **580**, fam `OriginalFaction=22`) never
copied `OriginalFaction` onto `ObjectTemplate.Faction`. Override applied Human **0**, so
`WeaponFireTargetAcquisition` rejected hits as same-faction. Fix: fam read + Create path wire
`Faction` from `OriginalFaction`; spawn override falls back to `OriginalFaction` when Faction is
unset/0. Details: `docs/NPC.md` §15.3. Tests in `SpawnPointTemplateSpawnTests` /
`WeaponFireTargetAcquisitionTests`.

### Target NPC frozen, then deleted (FIXED)
With `ScopeGlobalVehicleGhost=false` (code default / crash isolation), combat targets still got
`CreateVehicle` but never `ObjectInScope`. No pose or HealthMask → looks immobile and undamageable;
client later drops the stale object. Fix: pathing / AI foreign vehicles still ghost after the
create-hold even when the lever is off (`SectorMap.PerformScopeQuery`). See `docs/ghostPlan.md`
(containment exception) and `docs/nullWheels.md` (lever table). Tests:
`PerformScopeQuery_GhostLeverOff_PathVehicleStillGhostsAfterHold`,
`PerformScopeQuery_GhostLeverOff_AiVehicleWithoutPathStillGhostsAfterHold`.

### Track This — cannot turn in to Gareth (FIXED)
Mission **3979** is deliver → AutoComplete patrol → deliver (same NPC **11155**). After the first
Gareth dialog the server is on patrol `seq1`, but UseObject still sends objective **7656** (first
deliver). Reconcile only advanced for *later* objective ids, so dialog hit
`activeDeliverCbids=[]` / `remainingDeliverToNpc=1` and status-resync'd. Fix:
`TryReconcileAtDeliverNpcDestination` skips AutoComplete-only patrol gaps to the next deliver at
that NPC (UseObject + dialog response). Does not skip kills. Tests:
`HandleUseObject_TrackThisStaleFirstDeliverHint_AdvancesPastPatrolToFinalDeliver`,
`HandleMissionDialogResponse_TrackThisAtGarethDuringPatrol_CompletesFinalDeliver`. Catalog REG-005.

### Empty mission NPC opens nearby store (FIXED)
UseObject on Kid Gareth (CBID **11155**) with nothing to give/receive logged
`has no dialog missions`, then spatial OpenStore opened a nearby town store (e.g. reaction
**5445** / store **4810**) and the client sent unused `StoreOpen` (`0x2024`). Fix:
`TryHandleMissionDialog` **consumes** UseObject for in-range `IsMissionGiverCbid` NPCs with an
empty dialog list (no fallthrough to TriggerEvents / VendorStore / facilities). Tests:
`EmptyDialogMissionNpc_NearOpenStore_DoesNotOpenStore`,
`EmptyDialogMissionNpc_UseObject_NoDialogNoCrash`. Catalog REG-006.

---

## Open issue — follow-up mission not offered after relog

### Symptom (reported by user)
Turn in "New Day Dawning!" (mission **554**) at NPC "Rogers", relog **without** accepting "Live and
Direct", then after relog: cannot see/accept Live and Direct; interacting with Rogers does not offer
it.

### Established facts
- The client does **not** filter offered missions — `Client_RecvNpcMissionDialog` @`0x00815070`
  displays whatever the server sends. So "not offered" is a **server-side** decision in
  `NpcInteractHandler.GetOfferableMissions` → `CanOfferMission`, gated on `CompletedMissionIds`
  containing 554 (Live and Direct's presumed prerequisite — note WAD **3032** has empty
  `ReqMissionId`; continent **707** / race gates still apply).
- Historical parallel-table DB snapshot (MariaDB `autocore_char`) during investigation:
  - `character_quest`: coid **18381** → mission **554 active**, seq 0, zero progress.
  - `character_completed_mission`: coids **18325**, **18423** → **554 completed**.
- Character 18381's state (554 persisted as *active*, never *completed*) exactly reproduces the
  symptom and is the signature of a **completion that didn't persist** — after which the login
  re-grant re-adds 554 as a fresh active quest, and `LoadMissions` reloads it active every relog,
  keeping the character stuck.

### Leading hypothesis
For some completion flow, the `Complete` op does not end up as the last persisted state for
`(coid, 554)` — the row stays `active`. Static analysis did **not** find a stray `OnQuestChanged`
after `OnMissionCompleted`, and the queue is latest-wins with a disconnect flush, so the current
(fixed) code *should* persist completion. The stuck character is likely **corrupted state from
earlier buggy builds** (pre-`return false`, or before completion hooks existed), perpetuated by
reload + re-grant. This needs confirmation with a **clean run on the rebuilt server**.

### Alternative to rule out
If a clean run shows 554 correctly completed after relog but Live and Direct is still not offered,
then Live and Direct has prerequisites beyond just 554 (or a continent/level/NPC gate mismatch) —
pull its mission definition and check `ReqMissionId`, `Continent`, `ReqLevelMin/Max`, `NPC`.

**Note:** If UseObject on Rogers still logs `no dialog missions` *before* turn-in with 554 active,
rebuild so the GLM-before-WAD fix is loaded; that path is separate from this relog-offer issue.

---

## How to reproduce / verify (next agent: do this first)

Requires a running stack (Auth + Global + Sector via `AutoCore.Launcher`) and the retail client.
The running `AutoCore.Launcher` process **locks the build output DLLs** — stop it before rebuilding.

1. Stop the server; `dotnet build src/AutoCore.sln` (or at least `AutoCore.Game`); restart.
2. On the affected character: **`/clearAllMissions`**, then relog for a clean slate.
3. Acquire "New Day Dawning!", turn it in at Rogers, do NOT accept "Live and Direct".
4. **`/showMissions`** → expect `Completed (1): 554`.
5. **Relog**, then **`/showMissions`** → expect `Completed (1): 554` still.
6. Talk to Rogers → Live and Direct should be offered.

**Decision point:** if step 5 shows `Completed (0): none`, completion is not surviving relog — read
the server log lines to find the failing boundary:
- `Mission: persisted <Upsert|Complete> coid=… mission=…` (actual DB writes)
- `Character.LoadMissions: coid=… loaded … active […], … completed […]` (relog load)
- `Character.WriteToPacket: coid=… sending … current quests […], … completed […]` (create packet)

These three lines localize the drop to persist / load / send respectively.

### Inspecting the DB directly
MariaDB client: `C:/Program Files/MariaDB 12.1/bin/mysql.exe`. Connection (from
`appsettings.sector.json`): `Server=localhost;Port=3306;Uid=root;Password=admin123!;Database=autocore_char`.
`character` is a reserved word — backtick it.
```
mysql -h localhost -P 3306 -u root -padmin123! autocore_char \
  -e "SELECT * FROM character_mission; SELECT * FROM character_mission_completed;"
```

---

## Suggested next steps

1. Run the clean repro above and capture `/showMissions` (before + after relog) and the
   three server log lines. This is the missing evidence to close the open issue.
2. If completion is lost on a specific flow, trace that flow's completion site and confirm
   `OnMissionCompleted` fires and `Mission: persisted Complete` is logged before the relog. Check
   whether the relog re-grant (`FireOnLoadPlayerMissions` → `GiveMission`) is writing a fresh Upsert
   because `CompletedMissionIds` was empty at that instant (ordering of `LoadMissions` vs the
   re-grant — `LoadMissions` runs in `LoadFromDB`, before `SendLocalPlayerCreatePackets` /
   `FireOnLoadPlayerMissions` in `TNLConnection.Sector.cs:170-190`, so this should be safe).
3. If completion persists but Live and Direct still isn't offered, dump its mission definition and
   verify `CanOfferMission` gates (`NpcInteractHandler.cs:547-585`).
4. Consider whether completion should flush **synchronously** for extra safety (currently async
   queue + background + disconnect flush; robust but eventually-consistent).
5. Remaining protocol gaps (see `docs/missionState.md` §9): rewards not applied on completion; no
   fail/abandon lifecycle; `MissionDialogResponse 0x206D` client→server write format undetermined.

---

## Key files

| Area | File |
|---|---|
| Schema | `src/AutoCore.Database/Char/Models/CharacterQuestData.cs`, `CharacterCompletedMissionData.cs`, `CharContext.cs` |
| Persistence | `src/AutoCore.Game/Managers/MissionPersistence.cs`, `MissionPersistenceQueue.cs` |
| Load | `src/AutoCore.Game/Entities/Character.Missions.cs`, `Character.cs` (`LoadFromDB`, `WriteToPacket`) |
| Create packet | `src/AutoCore.Game/Packets/Sector/CreateCharacterExtendedPacket.cs` |
| Mutation/lifecycle | `src/AutoCore.Game/Entities/Reaction.cs`, `Managers/NpcInteractHandler.cs`, `MissionKillProgress.cs` |
| Disconnect flush | `src/AutoCore.Game/TNL/TNLConnection.cs` (`EndCharacterSession`) |
| Diagnostics | `src/AutoCore.Game/Chat/ChatCommandService.cs` |
| Tests | `src/AutoCore.Game.Tests/Managers/MissionPersistenceTests.cs` (19 tests) |
| Deep reference | `docs/missionState.md` (protocol, client RE, packet offsets, open questions) |

### Reference model to imitate
The whole design mirrors the **exploration persistence** system: `ExplorationManager.cs` +
`ExplorationPersistenceQueue.cs` + `Character.Exploration.cs` + the `LoadFromDB` → `LoadExplorations`
hook. When in doubt about a pattern (queue, DI seam, load, test structure), copy exploration.

---

## Build / test notes

- Build individual projects, not the full solution, while the server is running — the
  `AutoCore.Launcher` process locks output DLLs (`MSB3021`/`MSB3027` copy errors are the lock, not
  compile errors). Stop the Launcher to build the solution.
- `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj` → expect 2 failures
  (`Boost_ActOnActivator_SucceedsWithoutUnhandled`, `Boost_EmptyObjects_SucceedsWithoutUnhandled`) —
  **pre-existing baseline**, ReactionType 24 (Boost) has no handler in `Reaction.cs`. Unrelated to
  mission work.
- Nothing is committed yet on `feature/mission-persistence`; changes are in the working tree.
