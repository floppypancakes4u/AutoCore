# Topic Extraction: AutoPatrol packet

## Executive Summary

**`AutoPatrol` (`0x20B3`)** is a **client → server** sector message. The retail client sends it when a **mission patrol waypoint** is close enough and the client-side patrol manager has **auto-patrol enabled**. The payload is a single **TFID** of the resolved world object for that waypoint (16 bytes), with the usual 4-byte pad after the opcode.

| Item | Value |
|------|--------|
| Opcode | `0x20B3` |
| Enum | `GameOpcode.AutoPatrol` |
| Client EMSG name | `EMSG_Sector_AutoPatrol` |
| Direction | **C→S only** |
| Wire size | **0x18** (24 bytes) including opcode |
| S2C receive handler | **None** — not in `Client_PacketDispatch` |
| Fork status | Opcode declared only; **no packet class, no handler, no send** |

**Purpose (from client RE):** progress / notify the server that the local player has come within the configured distance of a patrol objective target. Related mission data types: `ObjectiveRequirementPatrol`, client RTTI `CVOGHBMissionPatrol` / `CVOGObjectiveRequirement_Patrol`.

**Soundness for AutoCore today:** dead surface. The client can emit `0x20B3`; the sector server has no `case` and no parser, so patrol auto-complete over the network is unimplemented.

---

## Scope

### In scope

- Opcode identity and direction
- Full C2S wire layout
- Client send path (`Client_EvalAutoPatrolWaypoint` @ `0x00929EC0`)
- Relationship to mission patrol requirements / waypoint list
- Name-table quirk for `EMSG_Sector_AutoPatrol`
- Fork inventory (what exists vs missing)

### Out of scope

- Full mission-objective engine (kill/collect/etc.)
- Implementing the server handler (extraction + Ghidra only)
- Escort fail/completion patrol distances (related XML fields, different packet path)
- `SetPatrolDistance` reaction type (44) / ghost `PatrolDistance` on vehicles

---

## Relevant Files

| Path | Role |
|------|------|
| `src/AutoCore.Game/Constants/GameOpcode.cs` | `AutoPatrol = 0x20B3` |
| `src/AutoCore.Game/Mission/Requirements/ObjectiveRequirementPatrol.cs` | Patrol objective template (targets, laps, auto-complete/fail distances) |
| `src/AutoCore.Game/Mission/Requirements/ObjectiveRequirement.cs` | `RequirementType.Patrol = 9` |
| `src/AutoCore.Game/Entities/Reaction.cs` | `SetPatrolDistance = 44` (different feature) |
| `src/AutoCore.Game/Packets/Sector/CreateVehiclePacket.cs` | `PatrolDistance` float on create (AI leashing, not this packet) |
| `src/AutoCore.Game/TNL/Ghost/GhostVehicle.cs` | Ghost writes `PatrolDistance` bits |
| `src/AutoCore.Game/TNL/TNLConnection.cs` / `*.Sector.cs` | **No** `AutoPatrol` dispatch case |
| `Documentation/PACKET STRUCTURES.md` | Shared `TFID` / `SMSG_Sector_Base` conventions; **no** AutoPatrol struct yet |
| `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md` | EMSG table base / `0x20xx` name lookup formula |

**Not found:** any `AutoPatrolPacket` class, tests, or production send/receive site in AutoCore.

---

## Packet / Network Structures

### Opcode registry

| Enum member | Value | Direction | Client EMSG string | Client code status |
|-------------|------:|-----------|--------------------|--------------------|
| `FailMission` | `0x20B2` | S→C (and a separate C2S build site) | See name-table note | S2C: `Client_PacketDispatch` → `FUN_0080b100` → `CVOGReaction_FailMission` |
| **`AutoPatrol`** | **`0x20B3`** | **C→S** | `EMSG_Sector_AutoPatrol` | Send only: `Client_EvalAutoPatrolWaypoint` |
| `RequestItemDetails` | `0x20B4` | C→S (client immediate `0x20B4`) | `EMSG_Sector_RequestItemDetails` | Separate item-details path |

Framing: `TNLConnection` / client `Client_SendSectorPacket` (`0x00807460`) writes the buffer as a sector game message (connection vtable `+0x18`). Size argument is **0x18**.

### Wire layout — C2S `AutoPatrol` (`0x20B3`)

Matches the common `SMSG_Sector_Base` pattern: opcode, 4-byte pad, then a 16-byte `TFID` at absolute offset `0x8`.

| Offset | Size | Field | Notes |
|-------:|-----:|-------|-------|
| `0x00` | 4 | `Opcode` | `0x20B3` |
| `0x04` | 4 | pad | Alignment; client often leaves residual float bits from a prior distance calc — **server must ignore** |
| `0x08` | 8 | `Coid` | Target object COID (`int64`) |
| `0x10` | 1 | `Global` | TFID global/local flag |
| `0x11` | 7 | pad | Standard TFID padding |

**Total size:** `0x18` (24) bytes.

TFID field layout (same as inventory/docs):

| TFID offset | Size | Meaning |
|------------:|-----:|---------|
| `0x00` | 8 | COID |
| `0x08` | 1 | `bGlobal` |
| `0x09` | 7 | padding |

**Pseudocode (server read after consuming opcode):**

```text
skip 4 bytes          // pad
coid   = ReadInt64()
global = ReadByte() != 0
skip 7 bytes          // TFID pad
```

**Pseudocode (client send, simplified):**

```text
buf[0x00] = 0x20B3
// buf[0x04] left undefined/residual
memcpy(buf+0x08, object+0x160, 16)   // object self-TFID
Client_SendSectorPacket(0x18, buf)
```

### Direction and dispatch evidence

| Check | Result |
|-------|--------|
| `Client_PacketDispatch` (`0x00815710`) case `0x20B3` | **Absent** |
| Client send immediate `mov [..], 0x20B3` | **Present** at `0x0092A087` inside `Client_EvalAutoPatrolWaypoint` |
| Size pushed with send | `0x18` |
| Payload source | 16 bytes at resolved object `+0x160` |

Conclusion: **server receives; client does not apply an AutoPatrol S2C.**

---

## Client reverse engineering (Ghidra)

**Binary:** `autoassault.exe` (Ghidra program open during this work).

### Key symbols (updated in Ghidra)

| Address | Name | Role |
|---------|------|------|
| `0x00929EC0` | `Client_EvalAutoPatrolWaypoint` | Waypoint resolve + optional AutoPatrol send |
| `0x00807460` | `Client_SendSectorPacket` | Generic sector send (`this+0xC78` connection) |
| `0x004BB950` | (resolve helper) | Calls `CVOGReaction_ResolveObjectTarget` with waypoint TFID |
| `0x004BAE70` | `CVOGReaction_ResolveObjectTarget` | TFID → live object |
| `0x00815710` | `Client_PacketDispatch` | S2C switch; no `0x20B3` |
| `0x009D59FC` | `g_szEMSG_Sector_AutoPatrol` | `"EMSG_Sector_AutoPatrol"` |
| `0x00A158A0` | invalid TFID sentinel | `coid = -1`, rest zero |
| `DAT_00d1ad10` | client patrol manager global | Waypoint vector `+0x11C` / `+0x120` |

### Algorithm — `Client_EvalAutoPatrolWaypoint`

Inputs:

- **EAX:** waypoint index  
- **Stack:** patrol manager (`DAT_00d1ad10`), out TFID (16 bytes), out position (3 floats)

Steps:

1. Require local player present (`manager→game+0xE98 != 0`).
2. Bounds-check index against waypoint pointer vector (`+0x11C` begin, `+0x120` end, stride 4).
3. Load waypoint record: dwords `[0..3]` = identity / TFID-like; floats `[4..6]` = cached XYZ.
4. If a “use cache only” flag at `manager+0x8` is set, distance-check cache vs player and return without network send.
5. Else resolve live object via `FUN_004bb950` / `CVOGReaction_ResolveObjectTarget`.
6. Skip if object missing, same as local player vehicle owner, or object “busy” flag (`vtbl+0x198`).
7. Refresh transform (`vtbl+0x144`), copy position from object `+0x80/+0x84/+0x88` into waypoint cache and out-params.
8. **AutoPatrol send gate:**
   - `manager+0x102` (byte) must be non-zero (auto-patrol enabled).
   - Euclidean distance player ↔ waypoint position must be **&lt;** `manager+0x104` (float threshold — maps to mission patrol auto-complete style distance).
9. On gate pass: build `0x18`-byte buffer and call `Client_SendSectorPacket`.

Callers that only **query** waypoints (map markers / nearest target) also invoke this function; the packet fires only on the gate in step 8.

### Related client strings / types

| Address | String / type |
|---------|----------------|
| `0x009E0478` | `"Patrol mission without any patrol points."` |
| (nearby) | `"Follow waypoints."` |
| `0x009E056C` | mission XML dump `type="patrol"` |
| `0x00AF5158` | `.?AVCVOGHBMissionPatrol@@` |
| `0x00AF4A74` | `.?AVCVOGObjectiveRequirement_Patrol@@` |

### EMSG name-table note

Name lookup (`0x0059E210`): for `0x2000 ≤ opcode < 0x20C7`,

```text
name = *(char**)(0x9CF6D0 + opcode * 4)
```

Verified neighbors:

| Opcode | Table slot | String pointer target |
|-------:|------------|------------------------|
| `0x20B1` | `0x9D7994` | `EMSG_Sector_FailMission` |
| `0x20B2` | `0x9D7998` | **`EMSG_Sector_AutoPatrol`** |
| `0x20B3` | `0x9D799C` | `EMSG_Sector_RequestItemDetails` |

**Code immediates disagree with the late name-table slots by one** in this band (e.g. client **sends** AutoPatrol as **`0x20B3`**, RequestItemDetails as **`0x20B4`**, UpdateFirstTimeFlags as **`0x20B1`** — matching `GameOpcode` and live send sites). Trust **code immediates + `GameOpcode`** for wire IDs; treat the EMSG table as debug labels that are skewed near the end of the `0x20xx` range. MissionDialog `0x206C` still matches the table (skew is not global).

---

## Fork status / gaps

| Layer | Status |
|-------|--------|
| `GameOpcode.AutoPatrol` | Present (`0x20B3`) |
| Packet class | **Missing** |
| Sector C2S handler | **Missing** |
| Mission patrol runtime (client notify → objective progress) | **Missing** |
| `ObjectiveRequirementPatrol` load from mission XML | Present (data only) |
| Unit tests | **None** |

`ObjectiveRequirementPatrol` already loads:

- `AutoComplete` / `AutoCompleteDistance`
- `AutoFail` / `AutoFailDistance`
- `GenericTargetCOID[]`, `Laps`, `Sequential`, `ContinentCBID`

Those fields are the natural server-side counterparts to the client manager flags/threshold that gate `0x20B3`, but nothing wires them to a packet handler today.

### Contrast — not this packet

| Feature | Relation |
|---------|----------|
| Vehicle / spawn `PatrolDistance` | AI leash radius on create/ghost; not `0x20B3` |
| `ReactionType.SetPatrolDistance` (44) | Map reaction; separate |
| `FailMission` `0x20B2` | Mission fail notify; same size class (`0x18`) but different fields |
| Escort `FailPatrolDistance` / `CompletionPatrolDistance` | Escort requirement XML; not AutoPatrol |

---

## Suggested server implementation (not done)

When implementing later (TDD):

1. Add `AutoPatrolPacket` (or C2S reader) for `GameOpcode.AutoPatrol` with pad + `WriteTFID`/`Read` of target TFID.
2. Register `case GameOpcode.AutoPatrol` on sector `TNLConnection`.
3. Validate: character has an active patrol objective that lists the target COID; distance optional if client is already gated.
4. Advance patrol slot / lap / complete objective; consider S2C `ObjectiveState` / `CompleteDynamicObjective` if those paths are adopted.
5. Tests: wire layout, reject unknown COID, happy-path progress.

---

## Ghidra documentation performed

During this topic extraction the open `autoassault.exe` database was updated:

- Renamed `FUN_00929EC0` → `Client_EvalAutoPatrolWaypoint` (plate comment with layout + algorithm)
- Renamed `FUN_00807460` → `Client_SendSectorPacket` (plate comment)
- EOL / decompiler comments on send site `0x0092A058`–`0x0092A08F`
- Labeled string `g_szEMSG_Sector_AutoPatrol` @ `0x009D59FC`
- Extended `Client_PacketDispatch` plate comment noting `0x20B3` is C2S-only
- Program saved

---

## Open questions

1. Exact semantics of `manager+0x102` / `+0x104` vs mission XML `AutoComplete` / `AutoCompleteDistance` (almost certainly related; not binary-proven field-for-field).
2. Whether server should ack with `ObjectiveState` (`0x2071`) or rely solely on other mission UI packets.
3. Whether AutoPatrol can fire for escort completion COIDs or only `RequirementType.Patrol` targets.
4. Whether pad at `+0x04` is ever meaningful (evidence says residual only).
