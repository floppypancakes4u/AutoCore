# System: Missions / objectives

**ID:** SYS-MISSION  
**Priority:** 7  
**Status:** complete (static) with residuals  
**Updated:** 2026-07-15

## Scope

Give mission, complete/advance objective, dynamic objective packets, auto-missions.

## Entry points

| Symbol | Address | Role |
|--------|---------|------|
| CVOGReaction_GiveMission | 0x005327C0 | Grant mission + first objective + unlock |
| CVOGReaction_CompleteObjective | 0x00533F90 | Advance or finish; XP/credits/rewards on final |
| CVOGMission_AddActiveObjective | 0x00531B00 | Insert active objective |
| Client_RecvCompleteDynamicObjective | 0x0080ff00 | S2C 0x2070 force complete |
| Client_RecvObjectiveState | 0x00809460 | Objective state update |

## Character layout (mission-related)

| Offset | Role | Confidence |
|--------|------|------------|
| +0x540 | active missions hash | high |
| +0x548 | active objectives hash | high |
| +0x538 / +0x53c | completed / alt completed | probable |
| param_1[0x152] | active objectives (CompleteObjective) | high |
| param_1[0x14e] | mission hash for complete path | high |
| +0x720 area | money (Add credits on final) | high |

## CompleteObjective branches

- **Advance** if objective index < last: AddActiveObjective next, unlock, skill/attrib points only
- **Final**: XP (Mission_ComputeObjectiveXp + round bias), credits, medals, inventory rewards (local only), UI audio mission_complete_3/5
- Dialog turn-in runs CompleteObjective **locally** before server ack — do not also send 0x2070

## Evidence

- Ghidra decompile this pass
- docs/missionState.md prior RE

## Confidence

- GiveMission insert path: **high**
- Full CompleteObjective reward matrix: **probable**
- Runtime: **blocked** UF-002

## Residuals (not eligible high-pri — see WORK_QUEUE Residual table)

| ID | Residual | Class |
|----|----------|-------|
| WQ-MISSION-r1 | Full packet layouts for all objective S2C variants beyond 0x2070 | optional depth |
| WQ-RT-01 / UF-002 | Runtime dual-run | blocked |

GiveMission / CompleteObjective high-pri path is complete (static).
