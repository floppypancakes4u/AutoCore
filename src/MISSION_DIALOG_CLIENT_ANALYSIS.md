# MissionDialog Packet Structure - Client Memory Analysis

## Task Overview

**Objective**: Reverse engineer the exact packet structure that the Auto Assault game client expects for the `MissionDialog` packet (opcode `0x206D`) by analyzing the client's memory and executable code directly.

**Why**: The existing server implementation is speculative/guesswork. We need to determine the true packet format by examining how the client actually parses incoming data.

**End Goal**: Document the exact byte layout (field order, types, sizes, padding) so the server can construct packets the client will correctly parse and display.

---

## Memory Analysis Findings

### 1. Process Information
| Property | Value |
|----------|-------|
| Process Name | `autoassault.exe` |
| PID | 87004 *(example prior session; varies)* |
| Base Address | `0x400000` *(verified live; this run had no base shift)* |
| Code Section | `0x401000` - `0x9c6000` (~5.77 MB) |
| Read-Only Data | `0x9c6000` - `0xaef000` (~1.16 MB) |
| Read-Write Data | `0xaef000` - `0xb00000` |

**Confirmed via MCP attach (NEW)**:
- Attached to live `autoassault.exe` with PID `123216`
- Reported module base address: `0x400000`
- The image regions match the boundaries above exactly.

**Static vs Dynamic address note**:
- When this binary is loaded at `0x400000`, all absolute addresses in this document are effectively **static** for that run.
- If ASLR/rebasing occurs, treat all absolute addresses as **dynamic** and prefer **RVAs** (\( \text{RVA} = \text{VA} - \text{ImageBase} \)).

### 2. Opcode Confirmation
```
Address:     0x6af1a8
Instruction: B9 6D 20 00 00    mov ecx, 0x206D
Next:        3B C1             cmp eax, ecx
Then:        0F 8F A3 05 00 00 jg  +0x5a3
Then:        0F 84 93 05 00 00 je  +0x593  --> jumps to 0x6af74e when opcode matches
```

**Confirmed**: Opcode `0x206D` (decimal 8301) exists in client code.

**Update / likely correction**:
- Based on the EMSG pointer-table index mapping (section 3) and the real opcode dispatcher (section 5.4), **MissionDialog is very likely `0x206C`**, while **MissionDialog_Response is `0x206D`**.
- The `0x206D` constant we initially located appears to be in a **string lookup** path, not necessarily the receive-handler dispatch.

### 3. Message Name Strings
Found in read-only data section:

| Address | String | Purpose |
|---------|--------|---------|
| `0x9d6318` | `EMSG_Sector_MissionDialog` | Server-to-client message |
| `0x9d62f4` | `EMSG_Sector_MissionDialog_Response` | Client-to-server response |

String pointer table located at `0x9d7880` contains pointers to these and other EMSG names.

**Verified table entries (live session)**:
- `0x9d7880` -> `0x9d6318` (`EMSG_Sector_MissionDialog`)
- `0x9d7884` -> `0x9d62f4` (`EMSG_Sector_MissionDialog_Response`)

**Note**: No direct code immediates referencing `0x9d7880` were found yet (e.g., `mov eax, 0x9d7880` / `push 0x9d7880`). This suggests the table is accessed via indirection (absolute memory operand loads like `[addr]`, computed base pointers, or a relocated pointer stored in `.data`).

**NEW - full pointer table base + index mapping (static)**:
- The EMSG pointer table appears to start at **`0x9D76D0`** (RVA `0x5D76D0`) and is a contiguous array of 32-bit pointers into the `.rdata` string pool.
- Within this table:
  - `EMSG_Sector_MissionDialog` is at `0x9D7880`, which is index \( (0x9D7880 - 0x9D76D0) / 4 = 0x6C \) (decimal **108**)
  - `EMSG_Sector_MissionDialog_Response` is at `0x9D7884`, index **0x6D** (decimal **109**)

**Critical hypothesis (strong evidence)**:
- If message opcodes are encoded as \(0x2000 + \text{EMSG\_index}\), then:
  - MissionDialog **opcode would be `0x206C`** (server → client)
  - MissionDialog_Response **opcode would be `0x206D`** (client → server)
- This aligns with the concrete dispatcher we found, which has an explicit handler for `0x206C` (see section 5.4).

### 3.1 Message-name lookup function (NEW - VERIFIED)
We located the concrete opcode → message-name mapping used by the client for logging/debug:

- **Function**: `0x59E210` *(static, inside `autoassault.exe`)*  
- **Behavior**:
  - For opcodes in the range **`0x2000 <= opcode < 0x20C7`**, it returns:
    - `namePtr = *(uint32*)(0x9CF6D0 + opcode * 4)`
  - This is a clever layout where `0x9CF6D0 + 0x2000*4 = 0x9D76D0`, i.e. the **EMSG pointer table for the 0x20xx opcodes**.

**Verified mapping (live, static addresses)**:
- `*(uint32*)(0x9CF6D0 + 0x206C*4) = 0x9D6318` → `"EMSG_Sector_MissionDialog"`
- `*(uint32*)(0x9CF6D0 + 0x206D*4) = 0x9D62F4` → `"EMSG_Sector_MissionDialog_Response"`

**Implication (now confirmed, not just hypothesis)**:
- `MissionDialog` **is `0x206C`** (server → client)
- `MissionDialog_Response` **is `0x206D`** (client → server)

### 3.2 `0x80xx` opcode name behavior in the same lookup (NEW - VERIFIED)
The same lookup function (`0x59E210`) has a separate branch for `0x80xx` opcodes that does **not** return a per-opcode `EMSG_*` string; it returns a fixed placeholder string.

**Verified (live, static addresses)**:
- **Code bytes at `0x59E210` (first branch)**:
  - `3D 00 80 00 00` → `cmp eax, 0x8000`
  - `72 0D` → `jb <skip>`
  - `3D 58 80 00 00` → `cmp eax, 0x8058`
  - `77 06` → `ja <skip>`
  - `B8 D4 7A 9D 00` → `mov eax, 0x9D7AD4`
  - `C3` → `ret`
- **String at `0x9D7AD4`**: `"EMSG_Global_Xxxx"` (null-terminated)

**Implication (verified)**:
- For opcodes in **`0x8000..0x8058`**, the client’s *logging/debug name lookup* resolves to `"EMSG_Global_Xxxx"` rather than a specific message name.

### 3.3 Missions-related debug/UI string anchors (NEW - VERIFIED)
While searching for convoy/missions-related entry points, we found mission debug strings in `.rdata` with concrete code xrefs.

**Verified (live, static addresses)**:
- `0xA4A96C`: `"New Missions:"` (null-terminated)
- There is a concrete code xref that pushes this literal at `0x8AE5A0` via `68 6C A9 A4 00` (`push 0xA4A96C`)

### 3.4 Convoy-related IPC/message name strings (NEW - VERIFIED)
We confirmed the presence of convoy-related `VSIPC_*` name strings in the client’s `.rdata` (exact role in opcode dispatch not yet confirmed).

**Verified (live, static addresses)**:
- `0x9D7454`: `"VSIPC_GlobalConvoyRefresh"`
- `0x9D7508`: `"VSIPC_GlobalConvoyFullInfo"`

### 4. TNL Network Library Infrastructure
The client uses Torque Network Library (TNL). Found RTTI type information:

| Address | Class | Purpose |
|---------|-------|---------|
| `0xb00b54` | `.?AVBitStream@TNL@@` | Bit-level stream reading/writing |
| `0xb00b70` | `.?AVByteBuffer@TNL@@` | Raw byte buffer container |
| `0xb016d8` | `.?AVNetEvent@TNL@@` | Base network event class |
| `0xb016f0` | `.?AVRPCEvent@TNL@@` | RPC event base class |

### 5. RPC Message Handler Classes
Found at `0xaf37c0` region:

```
RPC_TNLConnection_rpcMsgGuaranteed@@@TNL@@
RPC_TNLConnection_rpcMsgGuaranteedOrdered@@@TNL@@
RPC_TNLConnection_rpcMsgNonGuaranteed@@@TNL@@
RPC_TNLConnection_rpcMsgGuaranteedFragmented@@@TNL@@
RPC_TNLConnection_rpcMsgGuaranteedOrderedFragmented@@@TNL@@
RPC_TNLConnection_rpcMsgNonGuaranteedFragmented@@@TNL@@
```

Also found `FunctorDecl` template instantiations - TNL's mechanism for binding RPC calls to handler functions.

### 5.1 RPC Registration / Binding Xrefs (NEW - IMPORTANT)
We located concrete **code references** that push the RPC name strings (from `.rdata`) and call into what appears to be TNL's RPC registration/binding logic. This is likely the missing bridge to the real RPC receive path and dispatch mechanism.

**RPC name strings (live session)**:
- `0x9d80a8`: `RPC_TNLConnection_rpcMsgGuaranteed`
- Nearby contiguous strings include `...GuaranteedOrdered`, `...NonGuaranteed`, `...GuaranteedFragmented`, etc.

**Code site containing literal xrefs (live session)**:
- At/near `0x9c0e87` in `autoassault.exe` read-only data, we observed the following code bytes (decoded highlights annotated):

```
6A 00                push 0
6A 02                push 2
6A 01                push 1
68 A8 80 9D 00       push 0x9d80a8      ; "RPC_TNLConnection_rpcMsgGuaranteed"
B9 08 4F B0 00       mov  ecx, 0xb04f08  ; likely 'this' / global connection class rep
E8 FB 14 BE FF       call <rel32>        ; registration-ish call
68 D0 3D 9C 00       push 0x9c3dd0       ; likely functor/callback or related thunk
E8 37 8A AC FF       call <rel32>
59                   pop  ecx
C3                   ret
```

**Why this matters**:
- This proves there *is* a place where the client binds named RPCs to callable code (or to "functor" thunks).
- Tracing the two `call <rel32>` targets here should lead to the real RPC dispatch path (and ultimately to the handler for opcode `0x206D`).

### 5.2 RPC Globals + VTables (NEW - IMPORTANT)
During live analysis we confirmed that the `mov ecx, 0xB04Fxx` values used in the registration stubs are **real global objects** (likely instances of the various `RPC_TNLConnection_rpcMsg*` handler classes). Their first DWORD is a **vtable pointer** into `.rdata`, which gives us a concrete starting point for Phase 1 (finding the virtual `process()` / `checkIncoming()` path).

**RPC name strings (live session, confirmed)**:
- `0x9d80a8`: `RPC_TNLConnection_rpcMsgGuaranteed`
- `0x9d80cc`: `RPC_TNLConnection_rpcMsgGuaranteedOrdered`
- `0x9d80f8`: `RPC_TNLConnection_rpcMsgNonGuaranteed`
- `0x9d8120`: `RPC_TNLConnection_rpcMsgGuaranteedFragmented`

**Global instances observed in code (live session)**:
- `0xB04F08` → vtable `0x9D7BE8`
- `0xB04F40` → vtable `0x9D7BF4`
- `0xB04F78` → vtable `0x9D7C00`
- `0xB04FB0` → vtable `0x9D7C20`

**Vtable inspection (high level)**:
- Vtables in the `0x9D7Bxx` region contain multiple pointers into code around `0x5A27xx` / `0x5A2Bxx` (candidate virtual implementations).
- Adjacent strings observed in the same region include `Process` and `CheckIncoming`, suggesting we are very close to the real “incoming RPC → dispatch” entry points.

### 5.3 Incoming Message Debug Strings (NEW)
We located the following debug strings in `.rdata` that appear to correspond to the **receive path**:

| Address | String |
|---------|--------|
| `0x9d7d78` | `received %d msg %d (%d bytes) [%s]` |
| `0x9d7d9c` | `TNLConnection::InsertMessage` |

**Concrete code xref found (live session)**:
- `push 0x9d7d9c` (`"TNLConnection::InsertMessage"`) occurs in `autoassault.exe` code at `0x5a0146`.
- Following this function should lead directly into the real message receive/insert path, which is a promising route to the opcode dispatch.

**NEW - VERIFIED DISASSEMBLY CONTEXT (static)**:
- The function starting near `0x5A0120` logs `"TNLConnection::InsertMessage"`, then calls the opcode dispatcher at `0x637C20`.
- It later calls `0x59E210` to translate opcodes into `"EMSG_*"` name strings for logging (`"received %d msg %d (%d bytes) [%s]"`).

### 5.4 Opcode Dispatch Reality Check (NEW - IMPORTANT)
We identified a real opcode dispatcher that handles multiple `0x20xx` messages, and confirmed that **it does NOT dispatch `0x206D`** (it treats `> 0x206C` as a no-op except for one special case).

**Dispatcher function (static)**:
- `autoassault.exe` code: `0x637C20` (RVA `0x237C20`)
- Behavior (from disassembly):
  - `if (opcode > 0x206C) goto 0x637D0B`
  - `if (opcode == 0x206C) call 0x6374F0`
  - `if (opcode == 0x2005) call 0x637990`
  - `if (opcode == 0x2023) call 0x636F00`
  - otherwise: common path at `0x637D12` (stores message pointer + refcount bookkeeping)

**> 0x206C branch (static)**:
- `autoassault.exe` code: `0x637D0B` (RVA `0x237D0B`)
- Behavior:
  - Only special-cases `opcode == 0x804D` → calls `0x637750`
  - Otherwise, it performs **no handler dispatch** (just pointer store / refcount cleanup) and returns

**Implication**:
- `0x206D` exists in the binary, but it is **not handled by this `0x20xx` dispatcher**. The real MissionDialog processing likely occurs in a different layer (e.g., TNLWrapper/queue-based message decoding, or a different dispatch table that does not embed the literal `0x206D` constant).

**NEW - VERIFIED**:
- `0x637C20` is the **real receive-dispatch used by `TNLConnection::InsertMessage`** (call site observed at `0x5A019A`).
- It **does** dispatch `opcode == 0x206C` into the handler at `0x6374F0`.
- It **does not** dispatch `0x206D` (consistent with `0x206D` being a *client→server* response message, not a receive message in this path).

**Handlers referenced by this dispatcher (all static)**:
- `0x6374F0` (RVA `0x2374F0`): handler invoked for opcode `0x206C`
- `0x637990` (RVA `0x237990`): handler invoked for opcode `0x2005`
- `0x636F00` (RVA `0x236F00`): handler invoked for opcode `0x2023`
- `0x637750` (RVA `0x237750`): handler invoked for opcode `0x804D`

> **Static vs Dynamic note**: “static” here means the address lies inside the `autoassault.exe` image mapping (code in `0x401000-0x9C6000`, rdata in `0x9C6000-0xAEF000`, etc.). Absolute addresses may still shift if the module is rebased/ASLR’d; the RVA is stable.

### 5.5 TNLWrapper Entry Point Xref (NEW)
We located a direct code xref to the wrapper message-queue layer via the debug string `TNLWrapper::AddMessageToQueue`.

**String (static)**:
- `0x9D7E00` (RVA `0x5D7E00`): `TNLWrapper::AddMessageToQueue`

**Code xref (static)**:
- `0x5A05B0` (RVA `0x1A05B0`): `push 0x9D7E00` followed by a call into shared logging / wrapper code.

**Why this matters**:
- This is a strong anchor into the layer that likely maps incoming wire data to `EMSG_Sector_*` names and message-specific parsers (including MissionDialog), even though `0x206D` is not dispatched by the `0x637C20` `0x20xx` opcode switch.

**NEW - VERIFIED (static)**:
- `TNLWrapper::AddMessageToQueue` is implemented starting near `0x5A05B0`.
- It logs and guards send behavior using adjacent `.rdata` strings:
  - `0x9D7DBC`: `"broadcast sent %d size %d."` (used to log **opcode** and **payload size**)
  - `0x9D7DD8`: `"m_interface null when sending a message"`
- In the send-log call site (`0x5A0652`), the wrapper reads:
  - `opcode = *(uint32*)(msg + 0x10)`
  - `size  = <local ebx>` (computed earlier in the wrapper)

This gives us a concrete place to validate outgoing opcodes at runtime (and is a promising hook point for finishing `0x206D`’s pack format).

### 5.6 Wrapper “group message” receive/logging strings (NEW - VERIFIED)
We found additional wrapper-layer debug strings (adjacent to the existing `TNLWrapper::AddMessageToQueue` anchor) that appear to correspond to *group message* queueing and fragment reassembly.

**Verified strings (live, static addresses)**:
- `0x9D7E20`: `TNLWrapper::AddGroupMessageToQueue`
- `0x9D7E44`: `Group %d message %d (%d bytes)`
- Nearby (same `.rdata` block): fragment receive/reassembly/drop strings, e.g. `Received fragment %d:%d/%d (%d fragments).`

**Verified code xref (live, static)**:
- `push 0x9D7E44` occurs in `autoassault.exe` code at `0x5A113E`.

**Additional verified detail (live, static)**:
- In the same logging sequence (at/near `0x5A112F`), the wrapper loads the message opcode as:
  - `opcode = *(uint32*)(msg + 0x10)` (observed as `8B 46 10` prior to pushing args for the `"Group %d message %d (%d bytes)"` log call)

### 6. Opcode Switch Statement Analysis

**Location**: `0x6a5f30` - `0x6b1bc8` (approximately 48KB of code)

**Structure discovered**:
- Massive switch statement comparing opcodes
- Each case: `mov eax, <string_address>; jmp <common_return>`
- Returns string pointers (debug/error message names)
- **NOT the actual packet handler dispatch**

**Jump Table Found**:
```
Address: 0x6b1bcc
Format:  Array of 32-bit code addresses
Sample entries:
  [0] = 0x6a5f30
  [1] = 0x6a5f3a
  [2] = 0x6a5f44
  ...
```

Instruction at `0x6a5f20`:
```
83 F8 09              cmp eax, 9
0F 87 69 BC 00 00     ja  +0xbc69
FF 24 85 CC 1B 6B 00  jmp dword ptr [eax*4 + 0x6b1bcc]
```

This is an indexed jump using the jump table, but it's part of the string-lookup function, not the actual message handler dispatch.

### 7. Mission Data Structure Fields
Found debug format strings at `0xa86be0` showing mission definition fields:

```
LevelMax
intReqMissionID1, intReqMissionID2, intReqMissionID3, intReqMissionID4
bitIsRepeatable
cbidItem1, cbidItem2, cbidItem3, cbidItem4
rlItemValue1, rlItemValue2, rlItemValue3, rlItemValue4
bitAutoAssign
bActiveObjectiveOverride
IDContinent
intAchievement
IDDiscipline
intDisciplineValue
IDRewardDiscipline
intRewardDisciplineValue
intRewardUnassignedDisciplinePoints
intRequirementEventID
bitIsKit1, bitIsKit2, bitIsKit3, bitIsKit4
cbidItemTemplate1...
```

**Note**: These are mission DEFINITION fields from game data files, not necessarily the packet structure fields. However, they indicate what data types the client understands for missions (int, bit flags, cbid references).

---

## What Has NOT Been Determined Yet

1. **MissionDialog_Response (`0x206D`, client → server) pack/write structure** (field order, widths, bit packing)
2. **Semantic meaning** of `fieldA/fieldB/fieldC/flag1/flag2` in `MissionDialog` (`0x206C`) entries
3. **End-to-end wire framing** above the `0x206C` payload (TNL/RPC layer headers, reliability channel details), if the server needs to emulate client framing precisely rather than only the opcode+payload layer
4. Any additional validation rules the client applies after parsing (range checks, enum constraints) that could cause it to discard/ignore malformed payloads
5. **ConvoyMissionsResponse (`0x8010`, server → client) packet structure** (field order, widths, bit packing) — *not yet located/verified*

---

## Analysis Obstacles Encountered

1. **Indirection**: The opcode switch found returns debug strings, not handler functions. The actual dispatch mechanism is separate and not yet located.

2. **TNL Abstraction**: TNL uses templates (FunctorDecl, NetClassRepInstance) that make static analysis difficult without runtime tracing.

3. **No Obvious Handler Table**: Unlike simpler implementations, there's no direct opcode-to-function-pointer array found yet.

4. **Large Code Size**: ~5.77MB of executable code makes exhaustive searching time-consuming.

5. **Locating `0x8010` handler remains unverified**:
   - A global byte-pattern search for `10 80 00 00` (little-endian `0x8010`) returns many occurrences in memory, but none have been confirmed as the **opcode field at `msg + 0x10`** for a concrete message object.
   - No direct `cmp eax, 0x8010` / `cmp ax, 0x8010` instruction immediates were found in `autoassault.exe` code; this suggests `0x8010` dispatch is likely **table-driven** or occurs in a different layer than the obvious opcode switches.

---

## Future Analysis Plan

### Phase 1: Locate RPC Handler Entry Point
- Find vtable for `RPC_TNLConnection_rpcMsgGuaranteedOrdered` class
- Locate the `process()` or `execute()` virtual function
- This is where incoming RPC data first arrives

### Phase 2: Trace Data Flow
- From RPC handler, trace how ByteBuffer data is accessed
- Find where opcode is extracted from the buffer
- Identify the switch/dispatch that routes to specific handlers

### Phase 3: Finish MissionDialog_Response (`0x206D`) write format
- Use the `TNLWrapper::AddMessageToQueue` anchor (`0x5A05B0`) to identify the outbound message construction path
- Find the `pack()`/writer routine that fills the bitstream for opcode `0x206D`
- Document every `writeBits`/`writeInt`/`writeFlag` call and widths, mirroring what we did for `0x206C`

### Phase 4: Analyze Packet Parsing
- In the handler function, document each buffer read operation:
  - Read offset
  - Data type (byte, word, dword, qword, float, string)
  - Field purpose (if determinable)
- Note any conditional parsing based on flags/counts

---

## Packet Parsing (Concrete - NEW)

### MissionDialog Handler Candidate: opcode `0x206C` (static)
**Entry point**: `autoassault.exe` `0x6374F0` (RVA `0x2374F0`) — **verified** invoked directly by `0x637C20` when `opcode == 0x206C`.

**Bitstream reader helpers used (static)**:
- `0x42B3A0`: initializes a bitstream-like reader from a buffer pointer + length (observed args: `[arg+0x0C]`, `[arg+0x10]`)
- `0x42B670`: reads a fixed number of bits into a caller-provided output buffer (used with `8`, `0x10`, `0x20`, `0x40`)
- `0x42B8B0`: reads an integer field (used after pushing `0x13` hex = 19 decimal, indicating a **19-bit integer** read)

### Parsed Payload Shape (wire-level)
The handler reads a **count**, then decodes a repeated per-entry structure from a bit-level stream (**bit-packed**, no byte-alignment between fields; flags are single bits that immediately follow the prior field).

**Bit ordering / endianness (VERIFIED via disassembly of `0x42B670` and `0x42B8B0`)**:
- Extraction uses `shr dl, cl` where `cl = (bitIndex & 7)` → **LSB-first within each byte**.
- `0x42B670` assembles output bytes by combining right-shifted current byte with left-shifted next byte → multi-byte fields come out **little-endian**.
- `0x42B8B0(widthBits)` calls `0x42B670(widthBits)` then masks: `eax &= ((1<<widthBits)-1)` (for `widthBits != 32`) → **unsigned** width-limited integer.

**1) Count**
- Read **8 bits** into a byte `count` (stored at stack `+0x13`).

**2) Repeated entries (`count` times)**
For each entry:
- **Read 8 bits**: `entryType` (byte)
- **If `entryType == 1`**:
  - Read **16 bits**: `fieldA_u16`
  - Read **32 bits**: `fieldB_f32` (stored via `movss`, so IEEE-754 float)
- **Else (`entryType != 1`)**:
  - Read **19 bits**: `fieldA_u19` (**unsigned**, via `0x42B8B0(0x13)` - note: 0x13 hex = 19 decimal)
  - Read **64 bits**: `fieldC_u64` (read into two dwords; stored as 8 bytes)
- **Read 1 bit**: `flag1` (bit extracted directly from the bit buffer, advancing bit position by +1)
- **Read 1 bit**: `flag2` (same, advancing by +1)

**Notes**:
- The two flags are read via explicit bit-tests against a moving bit index (no byte-align), so they are *bit-packed immediately after the preceding field*.
- The in-memory decoded entry size is `0x28` bytes and the handler allocates `count * 0x28 + 1` bytes for a contiguous block, then copies the decoded entry structs into it.

### Verified per-entry decoded layout (in-memory, `0x28` bytes)
During parsing, the handler writes each entry into a `0x28`-byte struct. The first byte is the `entryType`, and most numeric fields are stored as 32-bit values:

Offset | Size | Meaning | When
------:|-----:|---------|-----
`0x00` | 1 | `entryType` | always
`0x01` | 3 | padding/unknown | always
`0x04` | 4 | `fieldA` (u16 widened to u32, or u19 as u32) | always
`0x08` | 4 | `fieldB_f32` | only if `entryType == 1` (else set to 0)
`0x0C` | 8 | `fieldC_u64` (little-endian, split into two dwords) | only if `entryType != 1`
`0x14` | 1 | `flag1` (0/1) | always
`0x15` | 7 | padding/unknown | always
`0x1C` | 1 | `flag2` (0/1) | always
`0x1D` | 0x0B | padding/unknown | always

This layout is the **decoded/queued representation** the client builds internally after reading the wire payload.

### Working Hypothesis
Given the tight mapping between:
- EMSG index `0x6C` ⇄ opcode `0x206C`, and
- the existence of a concrete handler for `0x206C`,

this handler is **confirmed** to be the **MissionDialog** receive parser (`EMSG_Sector_MissionDialog`).

### Phase 5: Document Structure
- Create byte-offset diagram of complete packet
- Identify any variable-length sections
- Document alignment/padding bytes

### Alternative Approaches to Try

1. **Runtime Analysis**: If static analysis continues to be blocked, consider:
   - Setting memory read breakpoints on opcode value
   - Watching for ByteBuffer access patterns
   - Tracing call stack when 0x206D is processed

2. **Cross-Reference Response Packet**:
   - The client SENDS `MissionDialogResponse` packets
   - Finding how the client WRITES that packet may reveal structure patterns
   - Similar fields likely exist in both directions

3. **Compare Similar Opcodes**:
   - Analyze nearby opcodes (0x206B LogicStateChange, 0x206C GroupReactionCall)
   - If those handlers can be found, the pattern may reveal MissionDialog's location

---

## Technical Notes

### Memory Scanning Results
- Opcode 0x206D (as int32 value 8301) found at exactly one location in code section: `0x6af1a9`
- 58 total occurrences of value 8301 in process memory (most in DLLs/heap)
- No occurrences found in data section `0x9c6000`-`0xb00000` as standalone value

### Address Calculations
```
Opcode comparison:  0x6af1a8  (mov ecx, 0x206D)
                    0x6af1ad  (cmp eax, ecx)
Conditional jump:   0x6af1b5  (je +0x593)
Jump target:        0x6af1b5 + 6 + 0x593 = 0x6af74e
```

At `0x6af74e`:
```
B8 1C 16 9F 00    mov eax, 0x9f161c  ; loads a string address
E9 70 24 00 00    jmp +0x2470        ; jumps to common return
```

The string at `0x9f161c` is `"ERROR_DS_ROOT_MUST_BE_NC"` - a Windows error code string, confirming this switch is for debug/error string lookup, not handler dispatch.

---

## Current Status

| Item | Status |
|------|--------|
| Process attached | Complete |
| Opcode location confirmed | Complete |
| TNL infrastructure mapped | Complete |
| Message strings located | Complete |
| RPC class names identified | Complete |
| RPC registration xrefs found (code pushes RPC name strings) | **NEW - Complete** |
| Actual handler function (`MissionDialog` / `0x206C`) | **COMPLETE (verified)** |
| Packet structure (wire-level payload for `0x206C`) | **COMPLETE (verified)** |
| Packet structure (`MissionDialog_Response` / `0x206D`) | **NOT DETERMINED** |

**Next Action**: Determine the `MissionDialog_Response` (`0x206D`, client→server) writer/parser to finish the bidirectional mission-dialog protocol, and identify the semantics of `fieldA/fieldB/fieldC/flags`.

---

*Document generated from live memory analysis session*
*Last updated during active investigation*
