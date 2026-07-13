# Mission System Invariants

Each invariant is either **tested**, **partial**, **gap**, or **n/a** for AutoCore as implemented.

| ID | Invariant | Status | Tests / notes |
| -- | --------- | ------ | ------------- |
| I01 | Mission cannot complete before required objectives are satisfied | **partial** | Multi-req one-shot complete characterized: `ObjectiveProgressContractTests.MultiRequirement_*` |
| I02 | Mission cannot be rewarded more than once per completion identity | **tested** | `MissionRewardIdempotencyTests.Complete_ThenReplayStaleAdvance_DoesNotGrantXpTwice` (REG-001) |
| I03 | Terminal completed missions do not return to active unless repeatable | **tested** | Transition + `GiveMission_RepeatableCompleted_AllowsRegrant` + non-rep regrant decline |
| I04 | Abandoned missions do not continue advancing | **partial** | Admin clear; no formal abandon enum |
| I05 | Failed missions cannot complete | **gap** | FailMission STUB — `FailMission_Stub_DoesNotClearQuest_OrComplete` |
| I06 | Expired missions cannot accept/reward | **n/a** | No expiry engine |
| I07 | Trigger does not execute against ineligible mission conditions | **partial** | MissionStateTriggerReevalTests |
| I08 | Reaction/trigger activation limits honored | **tested** | `FireTriggerReactions_ActivationCountOne_*`, ActivationCountZero |
| I09 | Duplicate events do not reopen completed missions | **tested** | Kill after complete scenarios; REG-001 stale advance |
| I10 | Reordered independent progress stays bounded | **partial** | Seeded kill property loop |
| I11 | Invalid events do not crash or corrupt | **tested** | `UseObject_MalformedPackets_*`, null guards, REG-002 |
| I12 | Failed persistence does not silently drop intent | **tested** | Queue failed persist + `PersistFailure_RetainsPending_*` |
| I13 | Progress cannot exceed defined bounds | **tested** | `KillProgress_NeverExceedsNumToKill_*`, kill clamp |
| I14 | Pack/unpack tolerates short/odd blobs | **tested** | `PackUnpack_TruncatedAndOddBytes_Safe` |
| I15 | Rewards atomic or compensatable | **partial** | Try/catch in ApplyMissionCompleteRewards |
| I16 | Completion and reward delivery do not diverge silently | **partial** | Complete always calls Apply; failures logged |
| I17 | Retry does not duplicate committed side effects | **tested** | REG-001 membership guard |
| I18 | Player A events never mutate Player B missions | **tested** | Isolation kill + volume latch isolation |
| I19 | Mission template never mutated as instance state | **tested** | `Template_NotMutated_ByGrantAndComplete` |
| I20 | Shared mission state synchronized | **n/a** | No group missions |
| I21 | Trigger leave clears latch; no leak of cascade depth | **tested** | LeaveVolume re-fire; ClearAllForTests; cascade depth test |
| I22 | Completed missions stop receiving kill progress | **tested** | Post-complete kill scenarios |
| I23 | Serialization round-trip preserves semantics | **tested** | PackUnpack property + CharacterQuest write |
| I24 | Time-based trigger checks accept injectable nowMs | **tested** | CheckTriggersFor(nowMs) in cascade tests |
| I25 | Random generation reproducible from seed | **n/a** | No mission RNG service |
| I26 | Unknown types fail safely | **partial** | Requirement XML fuzz; unknown reaction logs |
| I27 | Null/corrupt/missing template data handled | **tested** | FaultInjection load/missing template; unpack |
| I28 | Server authoritative over transitions and rewards | **tested** | Server lists + complete paths |
| I29 | Client-provided mission state validated | **partial** | Dialog resolve + malformed packets |
| I30 | Cascade depth bounded | **tested** | `CascadeDepth_DeepActivateChain_DoesNotCrash`, self-activate once |
| I31 | Complete path removes quest; stale refs ignored | **tested** | REG-001 |
| I32 | Disconnect mid-complete still mutates server state | **tested** | REG-002 `Complete_WithNullConnection_*` |

## Characterization of known incomplete production behavior

1. `ReactionType.FailMission` — no quest mutation  
2. Multi-requirement objectives — single Advance satisfies all  
3. `CompleteCount > 1` ignored on Advance  
4. Multi-waypoint / sequential / multi-lap patrol incomplete  
5. Many UI reactions client-only via 0x206C  
6. `MissionKillProgress` credits only the **first** matching active quest per kill event  

Link: `IncompleteHandlerLog` call sites in `NpcInteractHandler` and `Reaction`.
