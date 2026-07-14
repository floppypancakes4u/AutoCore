# Mission Regression Catalog

Permanent record of defects found during mission testing hardening.

| ID | Date | Symptom | Root cause | Regression test | Fixed? |
| -- | ---- | ------- | ---------- | --------------- | ------ |
| REG-001 | 2026-07-12 | Replaying `AdvanceOrCompleteObjective` after final complete double-granted mission XP | Complete path did not check whether `quest` was still in `CurrentQuests` before remove/reward | `MissionRewardIdempotencyTests.Complete_ThenReplayStaleAdvance_DoesNotGrantXpTwice` | Yes |
| REG-002 | 2026-07-13 | Null `TNLConnection` in `AdvanceOrCompleteObjective` threw NRE before server state update | Packet/journal sends unguarded | `MissionFaultInjectionTests.Complete_WithNullConnection_StillMutatesServerState` | Yes |
| REG-003 | 2026-07-13 | Mid-sequence deliver turn-in (e.g. mission 3979 GPS unit) removed whole quest; NPC done, journal/next objectives wrong | `TryCompleteDeliverFromDialog` always complete-removed regardless of later sequences | `NpcInteractCoverageGapTests.DeliverTurnIn_WithLaterObjectives_AdvancesSequence_KeepsQuest` | Yes |

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
