# NPC Vehicle Ghost Recovery Plan

## Purpose

Restore foreign NPC vehicle `GhostVehicle` updates without crashing the retail Auto Assault client. The current production-safe setting sends `CreateVehicle` for nearby NPC vehicles but suppresses their TNL `ObjectInScope`/`GhostVehicle` updates. This keeps the client stable, but does not provide live NPC vehicle movement, state, health, equipment, or lifecycle replication.

This document is the implementation and test plan for replacing that containment with a decoder-compatible ghosting path.

## Confirmed evidence

The affected client is version `0.0.14.117.2007.2.1.11`. Its crash dump is consistent across reproductions:

- Access violation at `autoassault!0x004F5566` (`FUN_004F5560` in the client analysis project).
- The function dereferences the vehicle wheelset pointer at owner offset `0x258` before rendering.
- The pointer is null at the time of the crash.
- The dump identifies the local player vehicle (`coid=18424`, client object `19656`) as the vehicle being rendered.

The wire log established these facts:

1. The local player's `CreateVehicleExtended` is sent during sector entry.
2. After the local-vehicle ghost was removed, no `GhostVehicle` packet for `coid=18424` was emitted before the crash.
3. Foreign global NPC vehicles still received `CreateVehicle`, followed by initial `GhostVehicle` packs and delta packs.
4. Suppressing only foreign `GhostVehicle` updates (`ScopeGlobalVehicleGhost=false`) while leaving foreign `CreateVehicle` enabled prevented the crash in the same map-entry reproduction.

Therefore the active fault is the foreign NPC vehicle ghost stream, or the client state transition it triggers. It is not solved by packing a wheelset onto the local player vehicle ghost.

### Wheelset prerequisite discovered during Phase 1

The debugger capture at `docs/debugger-hits/SUMMARY.json` shows four foreign `CreateVehicle` receives reaching the client's wheelset setup path. At each `EquipFromCreate` and `SetWheelset` breakpoint, the target vehicle's wheelset pointer (`vehicle+0x258`) remained `0x00000000`.

This established a prerequisite that predates the ghost stream: map-spawned NPC vehicles were not equipping their clonebase `DefaultWheelset`, so the server emitted an empty nested `CreateWheelSet` payload. A ghost update then drives the client into a render/tick path that dereferences that null pointer.

The server now equips `CloneBaseVehicle.VehicleSpecific.DefaultWheelset` for both raw-CBID and template NPC vehicle spawns. This is covered by `Spawn_RawVehicleWithDefaultWheelset_EquipsWheelsetForCreateVehicle`. Foreign ghosting remains disabled until a fresh debugger capture verifies that `SetWheelset` assigns a non-null pointer on the client.

## Current containment

`SectorMap.ScopeGlobalVehicleGhost` defaults to `false`.

- Foreign NPC vehicles still receive their guaranteed `CreateVehicle` packet.
- `ObjectInScope` is skipped for those foreign global vehicle ghosts.
- The local player vehicle is also intentionally excluded from ghost registration because `CreateVehicleExtended` constructs it on the client.
- The lever remains available for controlled experiments. Do not enable it in shared or normal play until a milestone below explicitly authorizes it.

The expected functional limitation is static/creation-only NPC vehicles: no reliable TNL-driven movement, health, AI, equipment, or despawn behavior.

## Safety rules

- Do not re-enable the full `ulong.MaxValue` initial mask as an experiment.
- Change one wire block or one mask family per experiment.
- Preserve `CreateVehicle` before every foreign ghost registration; ghosting without an object-table create is invalid.
- Do not interpret a successful connection as success. Each milestone requires a map-entry dwell test, movement through the NPC area, and a clean disconnect/reconnect cycle.
- Keep `WireDiag` on during this recovery work and archive the matching client dump/log pair for every failed experiment.
- If a change crashes the client, immediately restore the last known-safe lever setting before additional analysis.

## Phase 0: establish a reproducible harness

### Goals

Create a deterministic test map/repro with one foreign global vehicle and a known local player vehicle. Make it possible to select individual ghost mask families without modifying unrelated network behavior.

### Work

1. Add a test-only `GhostVehicle` mask profile or a narrowly scoped per-connection diagnostic override. It must be disabled by default.
2. Record, per ghost pack:
   - server coid and global flag;
   - initial/delta status;
   - exact mask;
   - bit count;
   - enabled block names;
   - ordering of the `CreateVehicle` event and ghost registration.
3. Add an integration-style server test proving the order is `CreateVehicle` then `ObjectInScope` for a foreign vehicle.
4. Add a regression test proving that the safe default does not register a foreign vehicle ghost.

### Exit criteria

- A test can request a single initial mask profile for a single foreign vehicle.
- Logs identify every block included in that profile.
- Existing create-before-ghost ordering tests remain green.

## Phase 1: reverse-engineer the client initial decoder

### Goals

Document the exact field order, widths, condition flags, and object-lifetime prerequisites of `VehicleNet_UnpackGhostVehicle` (`0x005F7720`) for an initial update and a delta update.

### Work

1. In Ghidra, trace every `BitStream_read*` call from the initial branch through the first render/update scheduling point.
2. For each conditional block, record:
   - preceding mask/flag predicate;
   - bits consumed;
   - destination object and offset;
   - assumptions about owner, wheelset, hardpoint, and object-table entries;
   - whether it is valid on initial, delta, or both.
3. Trace the call chain into `FUN_004F5560` and identify every client code path that writes or clears the wheelset pointer.
4. Compare `CreateVehicle` and `CreateVehicleExtended` decoding with ghost initialization to establish which fields must never be duplicated by the ghost.
5. Save the findings in a dedicated decoder table under `docs/topic-extractions/` or this document.

### Initial decoder ledger (current)

| Wire area | Current server behavior | Client evidence / status |
| --- | --- | --- |
| `CreateVehicle` nested wheelset | NPC spawn now embeds clonebase default wheelset when defined. | Required prerequisite; pending live debugger confirmation of a non-null `+0x258` assignment. |
| Initial common body | Packed unconditionally by `GhostVehicle.PackUpdate`. | Field order must be traced before enabling a minimal profile. |
| Path | Optional, 18-bit path id plus fields. | Bit width observed; client lifetime semantics remain unverified. |
| Template/spawn | Optional initial fields. | Not eligible for first re-enable profile. |
| Owner/driver | Optional initial block; later GM/AI fields depend on it. | Null-owner accesses were observed at a separate client fault site; keep out of first profile. |
| Equipment/armor | Suppressed on initial; allowed only on deltas. | Do not include until client object resolution is documented. |
| Health/state/position | Mask-driven after initial body. | Position is the first candidate after wheelset construction is verified. |

### Exit criteria

- Every byte/bit emitted by `GhostVehicle.PackUpdate` has a matching documented client read.
- The reason a foreign NPC update affects the rendered local vehicle is either proven or narrowed to a specific shared client state path.
- No proposed wire block is enabled based solely on guessed alignment.

## Phase 2: minimal foreign initial ghost

### Goal

Find the smallest initial `GhostVehicle` payload that attaches safely to a foreign `CreateVehicle` object.

### Candidate sequence

Try exactly one candidate per build/repro, in this order:

1. Header/identity only, if the client contract permits an otherwise empty initial update.
2. Header plus required initial body with every optional block false.
3. Add pose/position only.
4. Add health only.
5. Add a single documented required NPC field at a time.

Do not include owner, path, template/spawn, AI, hardpoints, armor, or skills until their dedicated phase.

### Tests before implementation

- A bitstream unit test for each candidate with assertions for every flag and field width.
- A test that ensures initial equipment flags are false.
- A test that ensures local player vehicles cannot use this foreign-NPC profile.

### Exit criteria

- Five consecutive sector entries and at least one reconnect complete without an AV or `Invalid Packet`.
- A foreign NPC vehicle appears and remains stable while stationary.

### Phase 2 evidence (2026-07-11)

The first live minimal-profile run succeeded without a client crash. `WireDiag` recorded multiple foreign vehicle initial packs with:

- effective mask `0x2` (`PositionMask`);
- `726` bits;
- `path=0`, `owner=0`, `tmpl=0`, `spawn=0`, `equip=0`;
- `profile=minimal` and original source mask `FFFFFFFFFFFFFFFF`.

The client remained responsive while later deltas arrived. This is one successful entry; retain the minimal profile until the required repeated-entry/reconnect validation is complete.
- The wire capture matches the documented initial decoder table.

## Phase 3: movement and lifecycle deltas

### Goal

Enable safe ongoing replication without resending the risky initial state.

### Work

1. Enable position/rotation delta only.
2. Confirm one moving NPC vehicle can move, leave scope, and re-enter scope without a duplicate `CreateVehicle`.
3. Test ghost kill/re-scope behavior and map transfer. The client object table and the server's create-tracking set must agree.
4. Add health/max-health deltas only after pose deltas are stable.

### Tests before implementation

- Position delta serialization test with exact float order.
- Drop/re-scope test proving `CreateVehicle` is not duplicated within one map session.
- Map-transfer test proving the create tracking resets after the client discards its object table.

### Exit criteria

- NPC vehicle position updates visibly work.
- Scope transitions and map transfers remain crash-free.
- No duplicate create or invalid-packet logs occur.

## Phase 4: optional blocks, one family at a time

Each family requires its own failing test, implementation, targeted test run, and live repro before moving to the next family.

| Order | Block family | Primary risk to prove |
| --- | --- | --- |
| 1 | Path | Exact optional path flag and 18-bit path identifier contract. |
| 2 | Template and spawn owner | Initial-only object/template assumptions. |
| 3 | Current owner / driver | Owner object must exist before the ghost decoder dereferences it. |
| 4 | AI state and GM | Valid only when the appropriate client owner/driver state exists. |
| 5 | Health-related state | Initial vs delta ownership and update timing. |
| 6 | Wheelset and other hardpoints | Client object resolution and render-pointer lifetime. |
| 7 | Armor and skills | Nested payload lengths and client-side prerequisites. |

For each family:

1. Prove the exact client read in the decoder table.
2. Add a failing bitstream test covering present and absent values.
3. Implement the smallest packing change.
4. Run the affected test suite.
5. Enable the profile only for the controlled NPC vehicle.
6. Run the live map-entry, dwell, movement, re-scope, and reconnect checks.
7. Keep or revert the family based on recorded evidence.

### Phase 4 results to date

- Path: enabled successfully with the minimal pose profile. Live initial packs expanded from 726 to 810 bits (`path=1/1`) with no client crash.
- Template/spawn: enabled successfully after path. Live initial packs expanded to 850 bits (`tmpl=1/1`, `spawn=1/1`) with no client crash.
- Owner/driver: **failed**. Enabling the initial owner block reproduced the `0x004F5566` null-wheelset access violation. The server immediately rolled the owner lever back to `false`; path/template/spawn/pose remain enabled and stable.

The owner failure means the current owner payload changes client vehicle construction or lifetime in a way that again leaves the wheelset null. Do not retry the same block. The next investigation must trace the client owner branch and compare the server's creature-owner bytes with the retail packet contract before attempting a narrower owner profile.

## Phase 5: restore default ghosting

Only after all required block families are proven should the global default be restored:

1. Enable the validated profile for all foreign global vehicles behind the existing lever.
2. Run the full targeted network/ghost test suite and a multi-map manual smoke pass.
3. Enable `ScopeGlobalVehicleGhost` by default.
4. Keep the lever and detailed diagnostics for one release cycle as a rollback mechanism.
5. Update this document with the validated mask profile and any intentionally unsupported fields.

## Required test matrix

| Scenario | Expected result |
| --- | --- |
| Sector entry with one nearby NPC vehicle | No client crash; foreign vehicle created once. |
| Stationary foreign NPC | Initial state displays correctly. |
| Moving foreign NPC | Pose deltas display correctly. |
| NPC leaves and re-enters range | Ghost may re-scope; `CreateVehicle` is not duplicated in same map session. |
| Map transfer | Client discards old objects; create is resent once in the new session. |
| Local player vehicle present | No local `GhostVehicle` registration or wheelset crash. |
| Reconnect | Fresh object table receives create before any ghost. |
| Each optional family enabled | No AV, no invalid packet, and expected visible state update. |

## Definition of done

Foreign NPC vehicle ghosting is considered restored only when all of the following hold:

- The client has completed repeated map-entry, movement, re-scope, transfer, and reconnect runs with no `0x004F5566` access violation or `Invalid Packet` disconnect.
- The final wire profile is documented against the client decoder.
- Unit and integration tests cover initial and delta bitstreams, create ordering, scope transitions, and local/foreign vehicle separation.
- The full targeted suite passes; any unrelated suite failures are documented separately.
- The global ghost lever is enabled by default only after the above evidence is captured.
