# Topic Extraction: Reaction packets

## Executive Summary

Reaction client notification is carried almost entirely by **one server→client opcode**: **`GroupReactionCall` `0x206C`** (client EMSG name `EMSG_Sector_MissionDialog`). The payload is a **bit-packed** stream: an 8-bit entry count, then up to **255** nested entries that reuse the **in-memory type** `LogicStateChangePacket` but **not** its standalone byte-aligned `Write` layout.

On a successful reaction fire, `SectorMap.TriggerReactionsInternal`:

1. Runs domain logic via `Reaction.TriggerIfPossible(activator)`.
2. Builds one nested reaction entry:  
   `new LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, singleClientOnly: false)`.
3. Adds it to a per-batch `GroupReactionCallPacket`.
4. Sends that packet to the activator’s owning character connection via `ResolveCharacter` → `SendReactionPacket`.
5. Optionally builds a second batch for `DoForAllPlayers` and broadcasts it to other characters on the map.
6. Builds a third batch for `DoForConvoy` but **does not send it** (logs and skips; no convoy membership system).
7. Recurses into child reaction lists with depth cap **10**.

**Standalone `LogicStateChange` `0x206B` is defined and has a full `Write` implementation, but has no production send site.** Nested entries inside `0x206C` use `GroupReactionCallPacket.Write`’s bit packing instead.

**C2S control path related to mission dialogs:** `MissionDialogResponse` `0x206D` is received and best-effort parsed; it is **not** the trigger/reaction fire path. An obsolete `MissionDialogPacket` class incorrectly modeled `0x206C` as a dedicated mission-list payload; it is marked `[Obsolete(..., true)]` and throws if used.

**Recipient resolution (current code vs older docs):**

| Source | Claim |
|---|---|
| **Current `SectorMap.cs`** | `ResolveCharacter(activator)` = `GetAsCharacter() ?? GetSuperCharacter(false)`, then null-safe `character?.OwningConnection?.SendGamePacket`. Tests assert vehicle activators deliver `GroupReactionCallPacket`. |
| **`docs/topic-extractions/server-triggers.md` (stale)** | Still describes `activator.GetAsCharacter()?.OwningConnection.SendGamePacket` as live, and calls vehicle delivery **broken**. |
| **Historical / AI-smell pattern** | The pure-`GetAsCharacter()` send was real in earlier fork state; domain handlers already used the dual resolve. Packet path has since been aligned with `Reaction.GetCharacterFromActivator`. |

**Bottom line:** The reaction packet path is now **structurally sound enough to deliver 0x206C for vehicle activators** under unit tests, with remaining gaps around **DoForConvoy**, **255-entry silent truncate**, **unused Variable nested layout vs client flag always-read hypothesis**, **best-effort 0x206D**, **no wire-format unit tests**, and **stale documentation** that still describes the old send bug and old PrepareWritePacket hook.

---

## Scope

### In scope

- `GroupReactionCallPacket` (0x206C): fields, bit-pack order, max 255, send sites.
- Nested vs standalone `LogicStateChangePacket` (0x206B).
- Construction and send plumbing in `SectorMap.TriggerReactionsInternal`.
- Recipient resolution (`GetAsCharacter` / `GetSuperCharacter` / `ResolveCharacter`).
- `DoForAllPlayers` / `DoForConvoy` broadcast behavior (implemented vs stub).
- Obsolete `MissionDialogPacket`; C2S `MissionDialogResponse` 0x206D as related control path only.
- Cross-check of `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md`, `docs/topic-extractions/server-triggers.md` packet sections, and `docs/codeAudit.md` references.

### Out of scope

- Deep dive into every `ReactionType` handler body (only cited where it decides whether a nested entry is emitted).
- Auth/global packet surface.
- Full TriggerManager redesign (entry only: fire → `TriggerReactions`).
- End-to-end mission quest DB model beyond dialog response best-effort accept.

---

## Relevant Files

| Path | Role |
|---|---|
| `src/AutoCore.Game/Packets/Sector/GroupReactionCallPacket.cs` | S2C 0x206C writer; max 255; bit packing |
| `src/AutoCore.Game/Packets/Sector/LogicStateChangePacket.cs` | Nested entry type + standalone 0x206B layout |
| `src/AutoCore.Game/Packets/Sector/MissionDialogPacket.cs` | Obsolete wrong model of 0x206C |
| `src/AutoCore.Game/Packets/Sector/MissionDialogResponsePacket.cs` | C2S 0x206D best-effort Read |
| `src/AutoCore.Game/Packets/BasePacket.cs` | Opcode + Read/Write virtuals |
| `src/AutoCore.Game/Constants/GameOpcode.cs` | Opcode IDs + MissionDialog alias |
| `src/AutoCore.Game/Map/SectorMap.cs` | `TriggerReactions` / `TriggerReactionsInternal`, `ResolveCharacter`, send helpers |
| `src/AutoCore.Game/Managers/TriggerManager.cs` | Edge fire → `map.TriggerReactions(...)` |
| `src/AutoCore.Game/Entities/Trigger.cs` | Alternate fire path `TriggerIfPossible` → same `TriggerReactions` |
| `src/AutoCore.Game/Entities/Reaction.cs` | `TriggerIfPossible`; `GetCharacterFromActivator`; comments on 0x206C |
| `src/AutoCore.Game/EntityTemplates/ReactionTemplate.cs` | `DoForAllPlayers`, `DoForConvoy`, child `Reactions` |
| `src/AutoCore.Game/Entities/ClonedObjectBase.cs` | Default `GetAsCharacter` null; `GetSuperCharacter` via Owner |
| `src/AutoCore.Game/Entities/Character.cs` | Overrides character resolve; `OwningConnection`; vehicle Owner wiring |
| `src/AutoCore.Game/Entities/Vehicle.cs` | Movement → `CheckTriggersFor(this)` (live activator is vehicle) |
| `src/AutoCore.Game/TNL/TNLConnection.cs` | `SendGamePacket`; C2S dispatch `MissionDialogResponse`; PrepareWrite no longer checks triggers |
| `src/AutoCore.Game/TNL/TNLConnection.Sector.cs` | `HandleMissionDialogResponse` |
| `src/AutoCore.Game/Extensions/BinaryWriterExtensions.cs` | Standalone TFID write (i64 + bool + 7 pad) |
| `src/AutoCore.Game/Extensions/BinaryReaderExtensions.cs` | TFID read used by 0x206D |
| `src/AutoCore.Game/Structures/TFID.cs` | Activator identity carrier |
| `lib/TNL.NET/TNL.NET/Utils/BitStream.cs` | Bit pack primitives used by 0x206C |
| `src/AutoCore.Game.Tests/Map/SectorMapTriggerTests.cs` | Vehicle/character send regression tests |
| `src/AutoCore.Game.Tests/Managers/TriggerManagerTests.cs` | End-to-end fire counts GroupReactionCall sends |
| `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md` | Client RE of 0x206C wire format / opcode mapping |
| `docs/topic-extractions/server-triggers.md` | Prior extraction (packet section partially stale) |
| `docs/codeAudit.md` | Depth-cap note on `TriggerReactionsInternal` only |

---

## Packet / Network Structures (detailed tables)

### Opcode registry

| Enum member | Value | Direction | Status in fork |
|---|---:|---|---|
| `LogicStateChange` | `0x206B` | S→C (defined) | **No production send** |
| `GroupReactionCall` | `0x206C` | S→C | **Primary live reaction notify** |
| `MissionDialog` | `0x206C` | alias | `[Obsolete(..., true)]` — compile error if used |
| `MissionDialogResponse` | `0x206D` | C→S | Best-effort receive handler |
| `Unknown206E` | `0x206E` | — | Obsolete; do not use |

Client name mapping (from `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md`):

- Index `0x6C` → `EMSG_Sector_MissionDialog` → opcode **`0x206C`**
- Index `0x6D` → `EMSG_Sector_MissionDialog_Response` → opcode **`0x206D`**
- Client receive dispatcher at `0x637C20` handles **`0x206C`**; treats most opcodes **`> 0x206C`** as non-dispatched on that path (special-case `0x804D`). Implication used in server comments: sending “dialog” on **`0x206D` as S2C was wrong**.

Framing above payload: `TNLConnection.SendGamePacket` writes **`uint32` opcode** (unless `skipOpcode`), then `packet.Write(writer)`. Default guarantee type is **`RPCGuaranteedOrdered`**. Payloads &gt; 1400 bytes fragment via TNL fragmented RPCs.

---

### 1) `GroupReactionCallPacket` — opcode `0x206C` (S→C)

**Class:** `AutoCore.Game.Packets.Sector.GroupReactionCallPacket`  
**Opcode property:** `GameOpcode.GroupReactionCall`  
**Container:** private `List<LogicStateChangePacket> Packets`  
**Public API:**

| Member | Behavior |
|---|---|
| `Count` | `Packets.Count` |
| `AddPacket(LogicStateChangePacket)` | Append if `Count < 255`; else return **false** (caller **ignores** return value today) |
| `Write(BinaryWriter)` | Builds TNL `BitStream`, copies buffer bytes to writer |

#### Wire format (bit-packed; matches client analysis + server writer)

Bit order evidence:

- Server uses `TNL.Utils.BitStream` (`WriteInt`, `Write`, `WriteFlag`).
- Client RE doc: LSB-first within each byte; multi-byte fields little-endian.

| Order | Field | Width | Source property | Notes |
|---:|---|---:|---|---|
| 1 | `count` | **8 bits** | `Packets.Count & 0xFF` | Max **255** enforced in `AddPacket` |
| *per entry `0..count-1`* | | | | |
| 2 | `entryType` | **8 bits** | `(uint)packet.Type` | `0` = Reaction, `1` = Variable |
| **If `Type == Variable (1)`** | | | | |
| 3a | `variableId` | **16 bits** | `VariableId & 0xFFFF` | Truncated to u16 |
| 4a | `value` | **32 bits** | `Value` float | `stream.Write(float)` → 4 LE bytes as bits |
| **Else if `Type == Reaction (0)`** | | | | |
| 3b | `reactionCoid` | **19 bits** | `(uint)ReactionCoid` | Unsigned width 19; high bits discarded if cast/truncation applies |
| 4b | `activatorCoid` | **64 bits** | `Activator.Coid` | `stream.Write(long)` |
| 5b | `activatorGlobal` | **1 bit** | `Activator.Global` | `WriteFlag` |
| 6b | `singleClientOnly` | **1 bit** | `SingleClientOnly` | `WriteFlag` |
| **Else** | | | | throws `InvalidDataException` |

After packing, server writes:

```text
writer.Write(stream.GetBuffer(), 0, (int)stream.GetBytePosition());
```

`GetBytePosition()` = `(BitNum + 7) >> 3` (byte ceil). Partial last byte is included.

**No byte alignment padding** is written between fields (except natural end-of-buffer ceil).

#### Client parser correspondence (`MISSION_DIALOG_CLIENT_ANALYSIS.md`)

Client handler candidate `0x6374F0` for opcode `0x206C`:

| Wire step | Client names in RE doc | Server mapping (production) |
|---|---|---|
| u8 count | count | entry count |
| u8 entryType | entryType | `LogicStateChangeType` |
| type==1 → u16 + f32 | fieldA_u16, fieldB_f32 | VariableId, Value |
| type!=1 → u19 + u64 | fieldA_u19, fieldC_u64 | ReactionCoid, Activator.Coid |
| flag1, flag2 | RE doc lists flags after type branch (decoded layout marks both as “always”) | Server writes flags **only on Reaction path** |

**Evidence gap / risk:** RE decoded layout marks `flag1`/`flag2` as **always** present on client. Server Variable branch writes **no flags**. Production path currently only constructs **Reaction** entries, so Variable mismatch is latent.

#### Production entry values (always Reaction)

| Field | Value used in `TriggerReactionsInternal` |
|---|---|
| Type | Implicit default `Reaction = 0` (Reaction ctor does **not** assign `Type`) |
| ReactionCoid | `reaction.ObjectId.Coid` |
| Activator | `activator.ObjectId` (vehicle TFID on live volume path) |
| SingleClientOnly | **always `false`** |
| VariableId / Value | unused |

#### Nested vs standalone

| Mode | Opcode | Serialization | Used? |
|---|---|---|---|
| Nested inside GroupReactionCall | outer `0x206C` | BitStream as above | **Yes** |
| Standalone `LogicStateChangePacket.Write` | would be `0x206B` | Byte-aligned + padding (below) | **No send site found** |

---

### 2) Standalone `LogicStateChangePacket` — opcode `0x206B` (S→C, unused)

**Enum:**

```text
LogicStateChangeType.Reaction = 0
LogicStateChangeType.Variable = 1
```

**Constructors:**

| Constructor | Sets Type | Fields |
|---|---|---|
| `(int variableId, float value)` | `Variable` | VariableId, Value |
| `(long reactionCoid, TFID activator, bool singleClientOnly)` | **not set** (default 0 = Reaction) | ReactionCoid, Activator, SingleClientOnly |

**Standalone `Write` layout (byte stream, not BitStream):**

| Order | Bytes | Content |
|---:|---:|---|
| 1 | 1 | `(byte)Type` |
| 2 | 3 | skip / pad (`Position += 3`) — not explicitly zero-filled by this Write |
| **Variable branch** | | |
| 3 | 4 | `VariableId` int32 |
| 4 | 4 | `Value` float |
| 5 | 24 | pad skip |
| **Reaction branch** | | |
| 3 | 8 | `ReactionCoid` int64 |
| 4 | 16 | `WriteTFID(Activator)` = coid i64 + global bool + **7 pad bytes** |
| 5 | 1 | `SingleClientOnly` bool |
| 6 | 7 | pad skip |

**Important:** This layout is **not** what the client 0x206C handler parses. Nesting relies on `GroupReactionCallPacket.Write`, not this method.

**Send sites:** repository grep finds **no** `SendGamePacket` of a bare `LogicStateChangePacket` and no other writers of opcode `0x206B`.

---

### 3) Obsolete `MissionDialogPacket` (must not use)

- Marked `[Obsolete("...", true)]` — compile-time error if referenced.
- `Opcode` getter throws `NotSupportedException`.
- Incorrect model: TFID creature + up to 8 missions (mission id + 4 item COIDs), bit-packed — **not** the real 0x206C format.
- Comments correctly redirect to `GroupReactionCallPacket` / client analysis doc.

Historical AI mistake documented in `Reaction.HandleGiveMissionDialog`: previously tried S2C “MissionDialog” on **0x206D** (response opcode); client receive path ignores that as S2C.

---

### 4) `MissionDialogResponsePacket` — opcode `0x206D` (C→S, related control path)

| Field | Type | Read order |
|---|---|---|
| `MissionId` | int32 | 1 |
| `MixedVar` | int64 | 2 |
| `MissionGiver` | TFID | 3 via `ReadTFID` = i64 + bool + skip 7 |

Handler: `TNLConnection.HandleMissionDialogResponse` (`TNLConnection.Sector.cs`).

Behavior:

1. Best-effort parse; log on failure.
2. Debug-log fields.
3. If `CurrentCharacter != null` and `MissionId > 0` and quest not already present → add `CharacterQuest(MissionId, 0)`.
4. Send `ConvoyMissionsResponsePacket` with current quest list.

Comments state payload format is **not fully reverse-engineered**. Client analysis lists **MissionDialog_Response pack format as NOT DETERMINED**.

This is **not** how volume triggers fire reactions; it is a possible dialog/accept control path after the client receives 0x206C for a GiveMissionDialog reaction.

---

### TFID on wire (standalone / response paths)

`WriteTFID` / `ReadTFID` (byte-aligned packets):

| Field | Size |
|---|---:|
| Coid | 8 (int64) |
| Global | 1 (bool) |
| padding | 7 zeros |

Nested GroupReactionCall Reaction entries do **not** use this 16-byte TFID blob; they split **coid u64 + global flag** in the bit stream.

---

## Server-Side Flow

### Entry points into packet construction

```text
Vehicle.HandleMovement (after pose update)
  └─ TriggerManager.CheckTriggersFor(vehicle)   // try/catch; not PrepareWritePacket
       └─ on edge-enter / timing-allowed fire:
            map.TriggerReactions(vehicle, trigger.Template.Reactions)

Trigger.TriggerIfPossible(activator)            // alternate API; manager does not call this
  └─ map.TriggerReactions(activator, Template.Reactions)

SectorMap.TriggerReactions(activator, reactions)
  └─ TriggerReactionsInternal(activator, reactions, depth=0)
```

**Note:** `TNLConnection.PrepareWritePacket` explicitly documents that volume triggers are **no longer** checked during write prep (avoids re-entrancy with TransferMap/Delete). `docs/topic-extractions/server-triggers.md` still describes PrepareWrite as the primary hook — **stale**.

### `TriggerReactionsInternal` algorithm (packet-focused)

```text
depth >= 10 → log error, return

clientPacket = new GroupReactionCallPacket()
broadcastPacket = null
convoyPacket = null
childReactionsToTrigger = []

for each reactionCoid in reactions:
  lookup Objects where Key.Coid == reactionCoid && !Key.Global
  if not Reaction → log error, continue
  if reaction.TriggerIfPossible(activator):
    packet = LogicStateChangePacket(reaction.Coid, activator.ObjectId, false)
    clientPacket.AddPacket(packet)                 // ignore bool return
    if Template.DoForAllPlayers:
      broadcastPacket ??= new GroupReactionCallPacket()
      broadcastPacket.AddPacket(packet)            // same instance reference
    if Template.DoForConvoy:
      convoyPacket ??= new GroupReactionCallPacket()
      convoyPacket.AddPacket(packet)
    if Template.Reactions.Count > 0:
      queue child list

SendReactionPacket(ResolveCharacter(activator), clientPacket)

for each child list:
  TriggerReactionsInternal(activator, child, depth+1)   // separate packet(s)

if broadcastPacket != null:
  SendBroadcastToMap(broadcastPacket, exclude: ResolveCharacter(activator))

if convoyPacket != null:
  log "DoForConvoy GroupReactionCall skipped (no convoy membership system)"
  // no send
```

### Recipient resolution

```text
ResolveCharacter(activator):
  null activator → null
  else → activator.GetAsCharacter() ?? activator.GetSuperCharacter(false)

GetAsCharacter:
  Character → this
  others (incl. Vehicle) → null (base)

GetSuperCharacter(includeSummons):
  Character → this
  base → Owner?.GetSuperCharacter(...)
  Vehicle Owner set by Character.LoadCurrentVehicle / SetCurrentVehicleForTests
```

`SendReactionPacket`:

```text
if packet null or Count == 0 → return
character?.OwningConnection?.SendGamePacket(packet)
```

Null-safe at every step: no NRE when character missing or connection null.

`SendBroadcastToMap`:

```text
foreach Character in Objects.Values
  skip excludeCharacter by ReferenceEquals
  character.OwningConnection?.SendGamePacket(packet)
```

Does **not** walk vehicles; only `Character` instances currently in the map object dictionary.

### Reaction success gate

A nested entry is added **only if** `reaction.TriggerIfPossible` returns true.

Many types return true with “client-side only” comments (Activate/Deactivate/Enable/Disable, waypoints, text, etc.) — meaning the **packet is the intended authority** for client apply.

`GiveMissionDialog` returns true after logging; actual dialog UI depends on client receiving **0x206C** with the reaction coid and looking up clonebase/template data (server comments + RE doc).

Unhandled reaction types: log error, **still return true** → still emit nested entry.

### Max depth vs max entries

| Limit | Value | Scope |
|---|---:|---|
| Nested reaction depth | 10 | Recursive `TriggerReactionsInternal` calls; each depth builds its **own** GroupReactionCall |
| Entries per GroupReactionCall | 255 | Single batch at one depth; further `AddPacket` returns false and is ignored |

A wide sibling list &gt; 255 successful reactions in one list can **silently drop** extras for client notify while still running domain handlers for those reactions (handler runs before AddPacket; overflow only loses packet inclusion).

### Activator identity on the wire

Live volume path uses **vehicle** as activator:

- Nested entry Activator TFID = **vehicle** ObjectId (not character).
- Packet is **sent to character connection** after resolve.
- Client semantics of activator TFID are not fully documented in-repo beyond RE field positions.

---

## State and Persistence

### Packet path itself

- **No DB persistence** of GroupReactionCall / LogicStateChange traffic.
- Packets are ephemeral S2C notifications.
- Empty batches are not sent (`Count == 0`).

### Related runtime state (not packet storage)

| State | Owner | Relation to packets |
|---|---|---|
| Trigger edge latch / fire counts | `TriggerManager` | Determines **when** packets are built |
| Map `Objects` / `Reactions` | `SectorMap` | COID lookup for reaction entities |
| Character `CurrentQuests` | `Character` | Updated by **0x206D** handler best-effort; not by 0x206C send |
| `OwningConnection` | `Character` | Required for delivery |

### Template flags affecting packaging

From `ReactionTemplate` map deserialize:

| Flag | Affects packets? |
|---|---|
| `DoForAllPlayers` | Extra GroupReactionCall broadcast to other map characters |
| `DoForConvoy` | Extra packet **built**, **not sent** |
| Child `Reactions` list | Nested recursive batches |
| Mission lists on GiveMissionDialog | Logged server-side; client expected to use reaction coid / clonebase |

---

## Responsibility Boundary Review

| Concern | Current owner | Appropriate? | Notes |
|---|---|---|---|
| Decide reaction success / domain effects | `Reaction.TriggerIfPossible` | Yes | Handlers mixed quality (out of scope) |
| Build nested entry list | `SectorMap.TriggerReactionsInternal` | Yes | Map owns local reaction COID lookup |
| Resolve which connection receives S2C | `SectorMap.ResolveCharacter` | Yes (now) | Mirrors `Reaction.GetCharacterFromActivator` |
| Serialize 0x206C | `GroupReactionCallPacket` | Yes | Bit packing colocated with client RE comments |
| Standalone 0x206B layout | `LogicStateChangePacket.Write` | Dead path | Risk of future misuse if someone sends it for “reactions” |
| C2S dialog response | `TNLConnection` + `MissionDialogResponsePacket` | Partial | Best-effort; format unverified |
| Obsolete MissionDialog class | retained + Obsolete(true) | OK as guardrail | Prevents regressing to wrong payload |
| Broadcast all players | `SendBroadcastToMap` | Partial | Characters only; excludes activator |
| Broadcast convoy | stub log | Incomplete | Flag loaded from map data |

**Separation issue:** `LogicStateChangePacket` dual role (nested bit fields vs standalone byte Write) is confusing. Nested path never calls nested object’s `Write`; GroupReactionCall re-reads properties. Easy for maintainers to “fix” the wrong serializer.

---

## Engineering Concerns

Prioritized for the **packet path only**:

1. **Documentation drift (High for maintainers)**  
   - `server-triggers.md` still claims vehicle send is broken (`GetAsCharacter` only) and PrepareWritePacket is the check hook.  
   - **Current code + tests contradict both.**  
   - Risk: future “fixes” reintroduce wrong patterns or waste work.

2. **DoForConvoy never delivered (Medium)**  
   - Flag loaded from map; convoy packet built; send skipped with debug log.  
   - Multiplayer/convoy-shared reactions desync vs map authors’ intent.

3. **Silent 255-entry truncate (Medium)**  
   - `AddPacket` returns false; callers ignore.  
   - Server may apply more reactions than client is told about in that batch.

4. **Reaction COID only 19 bits on wire (Medium/Low depending on COID ranges)**  
   - `WriteInt((uint)ReactionCoid, 19)` truncates.  
   - Local map reaction COIDs typically small; large/global COIDs would corrupt client lookup. Lookup already requires `!Global`.

5. **Variable nested format vs client flags (Low today / High if Variables used)**  
   - Production never adds Variable entries.  
   - If Variable type is used later without flags, may desync bitstream mid-packet.

6. **Reaction ctor omits explicit `Type = Reaction` (Low)**  
   - Relies on enum default 0. Works, but fragile if enum order changes.

7. **No wire-format unit tests for GroupReactionCall bit layout (Medium)**  
   - Tests cover send routing (vehicle/character/empty/no connection), not bit dumps vs client RE.  
   - Regression risk if BitStream usage changes.

8. **DoForAllPlayers broadcast scope (Low–Medium)**  
   - Only `OfType<Character>()` on map Objects.  
   - Correct if characters always enter map; fails if some players are represented only as vehicles without Character on Objects.

9. **Shared `LogicStateChangePacket` instance in client + broadcast lists (Low)**  
   - Same reference added twice; fine while immutable after build; mutate-after would double-affect.

10. **MissionDialogResponse unverified (Medium for dialogs)**  
    - Accept path may add wrong quests if client layout differs.  
    - Related control path, not volume trigger packaging.

11. **Obsolete wrong MissionDialog class still in tree (Low)**  
    - Guarded by Obsolete(true); good.  
    - Comments in GroupReactionCall still point at `src/MISSION_DIALOG_CLIENT_ANALYSIS.md` while file lives under `Documentation/`.

12. **AI-generated historical mistakes still visible as comments/code fossils**  
    - Wrong opcode 0x206D as S2C MissionDialog.  
    - MissionDialogPacket invented structure.  
    - Earlier GetAsCharacter-only send (fixed in current SectorMap).  
    - server-triggers extraction not refreshed after fixes.

---

## Crash / Stability Risks

| Risk | Evidence | Severity |
|---|---|---|
| NRE on missing connection | **Mitigated:** `character?.OwningConnection?.SendGamePacket` | Low (was Medium when only one `?.` on GetAsCharacter) |
| NRE / throw on null activator resolve | `ResolveCharacter` null-checks; SendReactionPacket null-safe | Low |
| Exception in reaction handler during movement | `Vehicle.HandleMovement` wraps `CheckTriggersFor` in try/catch | Medium for gameplay; write path protected |
| Infinite reaction chain | Depth max 10 + error log | Mitigated |
| Client ignore / desync if wrong opcode used | Documented: &gt;0x206C mostly not dispatched S2C | Historical; obsolete path blocked |
| Empty GroupReactionCall | Not sent when Count==0 | OK |
| Fragmentation of large 0x206C | SendGamePacket fragments &gt;1400 | Supported; rare for reaction batches |
| Dictionary mutation during fire | TriggerManager snapshots trigger list; reaction Delete may still mutate Objects during nested fires | Medium (map domain, not serializer) |
| Unknown LogicStateChangeType | throws on Write | Low (only 0/1 used) |

---

## Comparison to Expected Behavior

Sources: current server code, unit tests, `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md`, packet sections of `docs/topic-extractions/server-triggers.md` (noted where stale), `docs/codeAudit.md` (depth only).

| Expected | Current fork | Match? |
|---|---|---|
| Client applies reactions / mission dialog via **0x206C** bit-packed GroupReactionCall | Built and sent via `GroupReactionCallPacket` | **Yes** (structure + opcode) |
| Wire: count u8 + type-specific bit fields | Server Write matches RE for Reaction entries | **Yes** for Reaction path |
| Vehicle volume activator still notifies owning player | `ResolveCharacter` + tests | **Yes in current code** (stale docs say No) |
| Standalone LogicStateChange 0x206B for single changes | Layout exists; never sent | **Unused** |
| Mission dialog is **not** a separate S2C mission-list packet | Obsolete MissionDialogPacket blocked; GiveMissionDialog relies on reaction coid in 0x206C | **Yes** |
| DoForAllPlayers notifies others | `SendBroadcastToMap` implemented | **Yes** (characters on map) |
| DoForConvoy notifies convoy | Built, not sent | **No** |
| Client C2S MissionDialog_Response handled | Best-effort 0x206D | **Partial** (format unconfirmed) |
| Max 255 entries per group message | Enforced in AddPacket | **Yes** (overflow silent) |
| Nested child reactions still notify | Recursion with new packets | **Yes** |
| Depth safety | Max 10 | **Yes** (codeAudit aligns) |

### Client comparison detail (0x206C)

Confirmed by RE doc and mirrored by server:

- Opcode 0x206C = MissionDialog EMSG name, but payload is **group logic/reaction entries**, not mission ID list.
- Client looks up reaction by coid (server comments; RE field semantics for flags still incomplete).
- Server always sets `SingleClientOnly=false` and activator Global flag from `activator.ObjectId.Global`.
- RE still lists open questions: semantic meaning of flags / fields; full TNL framing; 0x206D pack format.

---

## Questions for the User

1. Should **activator TFID** on the wire remain the **vehicle** ObjectId for player-driven volume triggers, or must it be the **character** TFID for some client reaction types?
2. Is **`SingleClientOnly=true`** ever required by content, or is always-false correct for this emulator’s maps?
3. Should **`DoForConvoy`** block release of multiplayer content, or can convoy broadcast wait until a membership system exists?
4. Are map reaction COIDs guaranteed to fit in **19 bits**, or is sign-extension / large COID possible from tools?
5. For **Variable** logic-state entries: does the client always read the two flags after Variable fields? Should the server ever emit Variable nested entries?
6. Is **0x206B standalone** ever observed from original servers / client codepaths, or can it be treated as dead forever?
7. For **0x206D**, do you have captures or a preferred next RE target to freeze MissionId/MixedVar/MissionGiver layout?
8. Should documentation debt (`server-triggers.md` packet/send claims) be refreshed as part of the next fix issue, or left as historical?

---

## Recommended Follow-Up Fix Issues

1. **RP-01: Refresh stale trigger/packet documentation**  
   - Severity: Medium (process)  
   - Description: Update `docs/topic-extractions/server-triggers.md` packet/send sections to match `ResolveCharacter`, `SendBroadcastToMap`, movement-hook checks, and tests. Align comment path to `Documentation/MISSION_DIALOG_CLIENT_ANALYSIS.md`.  
   - TDD: N/A (docs).  
   - Files: `docs/topic-extractions/server-triggers.md`, packet comments.

2. **RP-02: Wire-format unit tests for GroupReactionCall**  
   - Severity: Medium  
   - Description: Golden-vector tests for Reaction entry bit packing (count, u19 coid, u64 activator, two flags) and multi-entry packing; assert max 255 returns false and that callers either split or log.  
   - TDD: Write tests first against current `GroupReactionCallPacket.Write`.  
   - Files: new tests under `AutoCore.Game.Tests/Packets/`, possibly `GroupReactionCallPacket.cs`.

3. **RP-03: Honor AddPacket overflow (split or log)**  
   - Severity: Medium  
   - Description: If a batch exceeds 255, send multiple GroupReactionCall packets or log error when entries drop; never silently lose client notify after successful domain apply.  
   - TDD: 256 successful reactions → 2 sends or explicit failure behavior.  
   - Files: `SectorMap.cs`, tests.

4. **RP-04: Implement or formally defer DoForConvoy send**  
   - Severity: Medium  
   - Description: Either broadcast to convoy members when system exists, or document map flag as unsupported and stop building unused packets.  
   - TDD: With fake convoy membership, all members receive; activator exclusion rules defined.  
   - Files: `SectorMap.cs`, convoy subsystem when available.

5. **RP-05: Explicit Type assignment + Variable layout decision**  
   - Severity: Low–Medium  
   - Description: Set `Type = Reaction` in reaction constructor; if Variable nested is needed, match client flag reads; if not, assert Type is Reaction in GroupReactionCall.Write.  
   - TDD: Constructor Type asserts; optional Variable bit layout vectors.  
   - Files: `LogicStateChangePacket.cs`, `GroupReactionCallPacket.cs`.

6. **RP-06: Confirm DoForAllPlayers recipient set**  
   - Severity: Low–Medium  
   - Description: Verify all connected players on a map are always present as `Character` in `Objects`; if not, resolve via vehicles’ super-characters.  
   - TDD: Two players on map, DoForAllPlayers reaction, both non-activator and activator rules.  
   - Files: `SectorMap.SendBroadcastToMap`, tests.

7. **RP-07: Freeze MissionDialogResponse 0x206D format**  
   - Severity: Medium for dialogs  
   - Description: RE or capture-based confirmation of C2S fields; replace best-effort accept with validated handler.  
   - TDD: Parse vectors; reject undersized buffers.  
   - Files: `MissionDialogResponsePacket.cs`, `TNLConnection.Sector.cs`, docs.

8. **RP-08: Remove or quarantine dead 0x206B / MissionDialogPacket if product policy allows**  
   - Severity: Low  
   - Description: Keep Obsolete guards or delete dead types after confirming no external references; avoid dual serializers for the same conceptual entry.  
   - TDD: Solution builds; no reflection send of 0x206B.  
   - Files: packet types, opcode comments.

---

## Appendix A — Critical code evidence (absolute paths)

### GroupReactionCall bit pack + max 255

`C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Packets\Sector\GroupReactionCallPacket.cs`

- `AddPacket` hard-stops at 255.
- `Write`: count 8 bits; type 8 bits; Variable u16+f32; Reaction u19+i64+2 flags.

### Nested construction + send + broadcast + convoy stub

`C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Map\SectorMap.cs` (`TriggerReactionsInternal`, `ResolveCharacter`, `SendReactionPacket`, `SendBroadcastToMap`)

### Standalone LogicStateChange (unused)

`C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Packets\Sector\LogicStateChangePacket.cs`

### Opcodes

`C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Constants\GameOpcode.cs` (`0x206B`–`0x206D`)

### Character resolve defaults / overrides

- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\ClonedObjectBase.cs` — base `GetAsCharacter` null; `GetSuperCharacter` via Owner  
- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\Character.cs` — overrides; `SetCurrentVehicleForTests` sets vehicle Owner  
- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\Reaction.cs` — `GetCharacterFromActivator` same dual resolve  

### Live fire path (vehicle activator)

- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Entities\Vehicle.cs` — `CheckTriggersFor(this)` after movement  
- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\Managers\TriggerManager.cs` — `TriggerReactions`  
- `C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\TNL\TNLConnection.cs` — PrepareWrite no longer checks triggers; `SendGamePacket` framing  

### C2S dialog response

`C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\TNL\TNLConnection.Sector.cs` — `HandleMissionDialogResponse`

### Regression tests (vehicle send)

`C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game.Tests\Map\SectorMapTriggerTests.cs`

### Client RE

`C:\Users\josh\Documents\GitHub\AutoCore\Documentation\MISSION_DIALOG_CLIENT_ANALYSIS.md`

### Stale prior extraction (packet claims)

`C:\Users\josh\Documents\GitHub\AutoCore\docs\topic-extractions\server-triggers.md` — still documents GetAsCharacter-only send and TODO broadcasts for both flags; **do not treat as current truth without re-reading SectorMap**.

---

## Appendix B — Historical vs current recipient bug (explicit)

**Pattern described in seed critical evidence / older docs:**

```text
activator.GetAsCharacter()?.OwningConnection.SendGamePacket(clientPacket)
```

- Vehicle activator → `GetAsCharacter()` null → **no packet**.
- Also historically risked NRE if character existed but `OwningConnection` was null (`?.` only on GetAsCharacter).

**Current production path:**

```text
SendReactionPacket(ResolveCharacter(activator), clientPacket)
// ResolveCharacter = GetAsCharacter() ?? GetSuperCharacter(false)
// Send = character?.OwningConnection?.SendGamePacket(packet) after Count!=0
```

Unit test `TriggerReactions_VehicleActivator_SendsGroupReactionCallToOwningCharacter` encodes the intended fixed behavior.

**Report stance:** Audit records **both** the historical failure mode (still cited in stale docs and original critical evidence) and the **as-of-code** fixed behavior. Do not re-open RP-vehicle-send as “broken in production code” without re-verification against `SectorMap.cs`.

---

*Extraction complete. Production code was not modified. Only this report file was written.*
