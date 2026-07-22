# Mission Regression Catalog

Permanent record of defects found during mission testing hardening.

| ID | Date | Symptom | Root cause | Regression test | Fixed? |
| -- | ---- | ------- | ---------- | --------------- | ------ |
| REG-001 | 2026-07-12 | Replaying `AdvanceOrCompleteObjective` after final complete double-granted mission XP | Complete path did not check whether `quest` was still in `CurrentQuests` before remove/reward | `MissionRewardIdempotencyTests.Complete_ThenReplayStaleAdvance_DoesNotGrantXpTwice` | Yes |
| REG-002 | 2026-07-13 | Null `TNLConnection` in `AdvanceOrCompleteObjective` threw NRE before server state update | Packet/journal sends unguarded | `MissionFaultInjectionTests.Complete_WithNullConnection_StillMutatesServerState` | Yes |
| REG-003 | 2026-07-13 | Mid-sequence deliver turn-in (e.g. mission 3979 GPS unit) removed whole quest; NPC done, journal/next objectives wrong | `TryCompleteDeliverFromDialog` always complete-removed regardless of later sequences | `NpcInteractCoverageGapTests.DeliverTurnIn_WithLaterObjectives_AdvancesSequence_KeepsQuest` | Yes |
| REG-004 | 2026-07-17 | Rogers UseObject: `no dialog missions` / OpenStore fall-through with New Day (554) active | WAD+GLM parallel load raced; `Mission.Read` missed GLM deliver `TargetNPCCBID` when `Mission.NPC=-1` | `MissionRogersUseObjectHeavyRegressionTests.*` + `MissionGlmXmlApplyTests` | Yes |
| REG-005 | 2026-07-17 | Track This (3979): at Gareth after first deliver, dialog OK does not complete; log `activeDeliverCbids=[] remainingDeliverToNpc=1` at `seq1` | Server on intervening AutoComplete patrol; client UseObject still sends first deliver obj **7656**; hint reconcile only advances for later objective ids | `NpcInteractUseObjectTests.Handle*TrackThis*` | Yes |
| REG-006 | 2026-07-18 | Empty mission NPC (Kid Gareth 11155) UseObject opens nearby town store; `Unhandled Opcode: StoreOpen` | `TryHandleMissionDialog` returned false on empty dialog → spatial `VendorStoreService.TryOpen` within 80u | `MissionRogersUseObjectHeavyRegressionTests.EmptyDialogMissionNpc_NearOpenStore_DoesNotOpenStore` | Yes |
| REG-007 | 2026-07-18 | Hide and Seek (3668): dialog turn-in leaves mission active; `/showMissions` still active; re-accept no-ops; hides remain in cargo | `IsCollectTurnInReady` gated on stale `ObjectiveProgress` only; client Collect_Eval uses cargo | `MissionHideAndSeekCollectHeavyRegressionTests.HideAndSeek_TurnInWithHidesButStaleProgress_CompletesAndTakesCargo` | Yes |

## Entry template

```
### REG-NNN — short title
- Discovered: YYYY-MM-DD
- Symptom:
- Root cause:
- Failing test (before fix):
- Fix:
- Permanent test:
```

### REG-001 — Double mission XP on stale AdvanceOrCompleteObjective
- Discovered: 2026-07-12
- Symptom: Completing a mission twice via the same detached `CharacterQuest` reference granted XP twice (64 → 128).
- Root cause: Final complete path always removed (no-op if already gone), re-enqueued Complete, and called `ApplyMissionCompleteRewards` without checking live membership in `CurrentQuests`.
- Failing test (before fix): `MissionRewardIdempotencyTests.Complete_ThenReplayStaleAdvance_DoesNotGrantXpTwice`
- Fix: Early return in `NpcInteractHandler.AdvanceOrCompleteObjective` when `quest` is null or not in `character.CurrentQuests`.
- Permanent test: same method, now green.

### REG-002 — Null connection NRE on AdvanceOrCompleteObjective
- Discovered: 2026-07-13
- Symptom: Completing a mission with `conn == null` threw `NullReferenceException` on `SendGamePacket` before reliable journal skip, risking incomplete client sync and masking disconnect-safe complete.
- Root cause: Packet and journal sends assumed non-null connection; `ForceCompleteMission` already null-checked.
- Failing test (before fix): `MissionFaultInjectionTests.Complete_WithNullConnection_StillMutatesServerState`
- Fix: `conn?.SendGamePacket(...)` and guard `PushJournalMissionList` with `if (conn != null)`.
- Permanent test: same method — server still completes/persists without connection.

### REG-003 — Deliver dialog force-completed multi-sequence missions
- Discovered: 2026-07-13
- Symptom: Turning in a deliver item on sequence 0 of a multi-objective mission (live: mission 3979 / objective 7656, “G.P.S Control Unit”) made the NPC behave as finished while the journal still showed the mission; cargo could be taken without correct credit for later sequences. Server log: `INCOMPLETE[DeliverTurnIn] … later objective sequences exist`.
- Root cause: `TryCompleteDeliverFromDialog` always removed the quest, added `CompletedMissionIds`, and applied full rewards even when a higher objective sequence existed. Kill/patrol/useitem already used `AdvanceOrCompleteObjective`.
- Failing test (before fix): `NpcInteractCoverageGapTests.DeliverTurnIn_WithLaterObjectives_AdvancesSequence_KeepsQuest` (plus cargo / second-dialog cases).
- Fix: Route dialog deliver through parameterized `AdvanceOrCompleteObjective` (`sendCompleteDynamicObjective: false`, `notifyClientRewards: false`, `syncClientImmediately: false`); advance when not final; soft-pedal + delayed force-complete only on final multi-req.
- Permanent tests: mid-seq advance, cargo take/grant next, second dialog does not complete, existing final deliver + patrol+deliver delayed 0x2070.

### REG-004 — Rogers New Day UseObject empty dialog (GLM/WAD race)
- Discovered: 2026-07-17
- Symptom: UseObject on Rogers (CBID 2477) with New Day (554 / objective 714) active logged `no dialog missions`, then OpenStore miss / no handler. Player could not turn in or get Live and Direct.
- Root cause: `AssetManager.LoadAllData` started WAD and GLM in parallel. `Mission.Read` loads `{name}.xml` for deliver `TargetNPCCBID`; when WAD won the race, mission 554 (`Mission.NPC=-1`) had empty Requirements so `HasDeliverTurnIn` never matched.
- Failing test (before fix): `MissionGlmXmlApplyTests.ApplyGlmXml_AttachesDeliverTarget_EnablesRogersStyleUseObject` (RED on missing `ApplyGlmXml`); heavy suite locks end-to-end shapes.
- Fix: Load GLM before WAD; `Mission.ApplyGlmXml` / `ReapplyMissingMissionGlmXml` backfill.
- Permanent tests: `MissionRogersUseObjectHeavyRegressionTests` (20 cases), `NpcInteractUseObjectTests` NPC=-1 cases, `MissionGlmXmlApplyTests`.

### REG-005 — Track This Gareth turn-in stuck on intervening patrol
- Discovered: 2026-07-17
- Symptom: Standing at Kid Gareth (11155) on Track This (3979) after first GPS deliver; dialog OK logs `deliver not completed … seq=1 activeDeliverCbids=[] remainingDeliverToNpc=1` and resyncs as already-active.
- Root cause: Mission shape is deliver → AutoComplete patrol → deliver. After first turn-in server is on patrol; client UseObject still sends objective **7656** (seq0). `TryReconcileClientObjectiveHint` only forward-syncs when the hint sequence is *later* than active, so seq2 never became active and `TryCompleteDeliverFromDialog` failed.
- Failing test (before fix): `HandleUseObject_TrackThisStaleFirstDeliverHint_AdvancesPastPatrolToFinalDeliver`, `HandleMissionDialogResponse_TrackThisAtGarethDuringPatrol_CompletesFinalDeliver`.
- Fix: `TryReconcileAtDeliverNpcDestination` advances through AutoComplete-only patrol objectives to the next deliver at the interacted NPC (UseObject + dialog response). Kill/use-item gaps are not skipped.
- Permanent tests: same methods + `HandleMissionDialogResponse_DuringPatrolWithKillBetween_DoesNotSkipToDeliver`.

### REG-006 — Empty mission NPC UseObject opens nearby OpenStore
- Discovered: 2026-07-18
- Symptom: UseObject on Kid Gareth (CBID 11155) with no current give/receive logged `has no dialog missions`, then opened a nearby town store (OpenStore reaction 5445 / store 4810) and the client sent unused `StoreOpen` (`0x2024`).
- Root cause: `TryHandleMissionDialog` returned false on empty dialog; `ObjectUseManager` fell through to spatial `VendorStoreService.TryOpen` (80u player-to-store).
- Failing test (before fix): `EmptyDialogMissionNpc_NearOpenStore_DoesNotOpenStore` (RED: GroupReactionCall emitted).
- Fix: In-range `IsMissionGiverCbid` NPCs with an empty dialog list consume UseObject (`ConsumeEmptyMissionNpcInteract`) so TriggerEvents / VendorStore / facilities do not run.
- Permanent tests: `EmptyDialogMissionNpc_NearOpenStore_DoesNotOpenStore`, `EmptyDialogMissionNpc_UseObject_NoDialogNoCrash` (nearby store placed).

### REG-007 — Hide and Seek collect turn-in blocked by stale ObjectiveProgress
- Discovered: 2026-07-18
- Symptom: After collecting 2 Alligrake Hides and dialog OK at Jake Detroit (2545), log shows `deliver not completed … activeDeliverCbids=[]` then `already active — resync`; `/showMissions` still lists 3668 active; re-accept no-ops; hides remain in cargo.
- Root cause: Client Collect_Eval uses inventory count; `IsCollectTurnInReady` only checked `ObjectiveProgress`, which can stay 0 if pickup sync missed. Collect turn-in failed; grant path hit already-active.
- Failing test (before fix): `HideAndSeek_TurnInWithHidesButStaleProgress_CompletesAndTakesCargo`.
- Fix: `IsCollectTurnInReady` recounts cargo via `MissionCollectProgress.SyncQuestProgressFromInventory` before gating; pickup sync no longer continent-gates progress updates.
- Permanent tests: same method + `IsCollectTurnInReady_RecountsFromInventoryWhenProgressStale`.
