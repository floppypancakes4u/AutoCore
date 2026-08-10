# ID collisions (COID / TFID / `simple_object`)

**Status:** Production failure mode understood and remediated for the
character-select AV class; combat COID-only resolution remediated under the
same SS-31 label. Optional hardening catalogued below.
**Primary label:** SS-31 (exception-safety audit + inventory identity work)
**Live incident date:** 2026-08-09 (account `floppy` / character **Donuts**)
**Client symptom:** `Access violation c0000005` at `autoassault!0x0080A62A`,
`Map: (-1)` (character select)

---

## 1. Executive summary

AutoCore does **not** give characters, vehicles, and inventory items separate
numeric ID spaces. Anything that must survive in the character DB and appear on
the wire as a global object is keyed by a single integer **COID** stored in
`simple_object.Coid` (MySQL / MariaDB `AUTO_INCREMENT`).

Two independent failure modes share the “SS-31” name because they both come
from **numeric COID reuse**:

| Face | Failure | User-visible effect | Remediation |
|------|---------|---------------------|-------------|
| **A. Persist identity clobber** | A second system writes `simple_object` for a COID that already identifies a character or vehicle | Character list ships poison `CreateCharacter` / `CreateVehicle`; retail client AVs at select | Shared DB allocator + overwrite guard + load fail-closed |
| **B. Combat target ambiguity** | Global player COIDs and authored **local** map COIDs share numbers; lookups used COID only | Damage hits the wrong entity; “invincible” vehicles; wrong invuln clears | TFID-exact `(Coid, Global)` resolution (`CombatTargetResolver`, etc.) |

This document focuses on **face A** (the Donuts / character-select crash) and
records how face B relates. It is the operator- and implementer-facing writeup
for “why did inventory wipe my alt?”

---

## 2. How IDs actually work

### 2.1 TFID vs COID

On the wire and in memory, objects are addressed by a **TFID**:

- `Coid` — 64-bit (stored/used as `long`) object id
- `Global` — bool; roughly “persistent / player-owned / cross-map” vs
  “local to this map instance”

Face B bugs happen when code drops `Global` and keys only on `Coid`.

### 2.2 One table owns persistent identity

```text
autocore_char.simple_object
  Coid   PK, identity/sequence
  Type   CloneBaseObjectType byte (Character=20, Vehicle=14, Weapon=12, Item=6, …)
  CBID   clonebase id
  Faction / TeamFaction
```

Related rows **share that primary key** (or point at it):

| Table | Role |
|-------|------|
| `character.Coid` | FK → `simple_object` (character body identity) |
| `vehicle.Coid` | FK → `simple_object` (chassis identity) |
| `character_inventory.ItemCoid` | points at item’s `simple_object` row |
| `character_inventory.CharacterCoid` | owner only — **not** a separate item namespace |
| Vehicle equip columns (`Wheelset`, `Armor`, weapons, …) | each is another `simple_object` COID |

There is **no** `inventory_id` sequence distinct from character IDs. Cargo
ownership is `(CharacterCoid, ItemCoid)`; the item’s type and clonebase live on
`simple_object`.

### 2.3 Two minting authorities (the original design mistake)

| Authority | Intended use | Storage |
|-----------|--------------|---------|
| **`simple_object` DB sequence** | Characters, vehicles, equipment, **persisted** cargo | Insert / identity column |
| **`Map.LocalCoidCounter`** | Map-local props, world loot **spawns**, ephemeral UI slots | In-memory per map; starts near `MapData.HighestCoid + 1` |

Local counters are cheap and correct for objects that never get a durable
`simple_object` row as a **global** identity. They become lethal when a code
path:

1. Takes the next local COID, and  
2. Persists it via `EnsureSimpleObject` / inventory save as if it were a new
   global item.

Local counters are **not** coordinated with the DB sequence. After enough
character creates (or a DB that has advanced the identity high-water mark into
the same numeric band the map is handing out), the two streams **collide**.

```text
  simple_object sequence ──► 18943, 18944, … 18950 (Donuts), 18951 (vehicle), …
  Map.LocalCoidCounter   ──► … 18949 (world loot), 18950 (next) …
                                      │
                                      ▼
                         pickup allocates cargo coid 18950
                         EnsureSimpleObject(18950, Weapon, 12853)
                         overwrites Donuts’ identity row
```

---

## 3. Face A — persistent identity clobber (character-select AV)

### 3.1 Mechanism

1. Character **C** is created; DB assigns `simple_object` row  
   `Coid=C`, `Type=Character (20)`, `CBID=<body>`.
2. Some inventory path mints item COID **C** from `Map.LocalCoidCounter`
   (or any non-DB source that can equal C).
3. `InventoryPersistence.EnsureSimpleObject` finds the existing row and
   **updates** `Type` / `CBID` to the item’s values (historical behavior:
   unconditional overwrite).
4. `character` row for C still exists (name, account, vehicle FK, …).
5. On login, Global builds the character list:
   - `Character.LoadFromDB` loads `character` + `SimpleObjectBase`.
   - `LoadCloneBase(SimpleObjectBase.CBID)` attaches a **weapon/item**
     clonebase to a Character entity.
   - `WriteToPacket` → `CreateCharacter` / `CreateVehicle` with corrupt shape.
6. Retail client parses the create stream at character select (`Map=-1`) and
   access-violates at **`0x0080A62A`**.

Corruption is **permanent** until the DB row is repaired or the character is
removed. Relogging alone does not heal it.

### 3.2 Live incident: Donuts (2026-08-09)

From `log-sector.txt` (account id **8**, name **floppy**):

| Time | Event |
|------|--------|
| ~20:31 | Floppy picks up world item CBID **12853** (`weap_m-g_frn_01_guajardo-tricannon-qstrwd`); claim logs `cargo coid=18950` |
| Earlier same evening | Donuts (coid **18950**) still loaded and entered world normally |
| 20:40 / 20:44 | Pre-guard builds still `Loaded character Coid 18950 … Donuts` and `WriteToPacket` → client AV at select |
| **20:52** | Post-guard build: skip instead of wire poison |

Log lines (post-fix):

```text
Character.LoadFromDB: coid=18950 simple_object.Type=12 cbid=12853
  (expected Character=20) — skipping corrupt identity (SS-31; client would AV at 0x0080A62A)
SendCharacter: skipped coid=18950 account=8 — LoadCharacterForSelection failed
  (missing vehicle or simple_object type/cbid corruption)
```

DB state at diagnosis:

- `character` 18950 **Donuts**, `ActiveVehicleCoid=18951`, account 8  
- `simple_object` 18950: **Type=12 (Weapon), CBID=12853** (not Character)  
- `character_inventory` row: Floppy (`CharacterCoid=1`) owned `ItemCoid=18950`  
- Vehicle graph 18951–18956 still present until hard-delete  

**Cleanup performed:** hard-delete of Donuts and vehicle/equip rows; **kept**
`simple_object` 18950 and Floppy’s inventory row (that weapon is legitimate
cargo after the clobber). Account 8 left with **Floppy** (1) and **NotBrolic**
(18943).

Sibling observation the same night: world loot was also **spawned** on COIDs
that matched character ids (e.g. armor spawn at 18950, weapon spawn at 18943).
This was a `InventoryCoidCounter.SyncFromCargo` consequence, not a separate
bug: on login, `SyncFromCargo` re-seeded `Map.LocalCoidCounter` from the
character's **maximum cargo COID**, which could itself already sit past the
live `simple_object` high-water mark — so the very next map-local spawn landed
on a COID the DB sequence considered live. `SyncFromCargo` and
`InventoryCoidCounter` are **retired** as of this pass (deleted outright — see
§4.1 and §8); map-local spawn now only ever advances from
`MapData.HighestCoid`, never from a character's cargo contents.

**18274/18275 vs 18950/18951 — two different pairs, don't conflate them.**
`18950` (Donuts) / `18951` (her vehicle) are the **live** Donuts-incident
COIDs above — the actual corrupted rows recovered from `log-sector.txt` and
the production DB. `18274` (character) / `18275` (vehicle) are a **synthetic**
canonical example pair that exists only in test code and doc comments (see
`InventoryRuntime.AllocateItemCoid`'s doc comment, `Ss31CollisionIntegrationTests`,
`SimpleObjectOverwriteGuardTests`, `CharacterSelectionManagerTests`) — chosen to
pin the same collision *shape* (a character/vehicle pair at N/N+1, then a
Weapon at CBID 12853 — the same weapon CBID as the real incident — landing on
the character COID) without depending on live-incident-specific data. If a
test failure references 18274/18275, it is the guard/regression pin, not a
second real incident.

**Shadow0712 (2026-08-10) — second live face-A incident, un-triaged.** A
second character hit the same character-select AV symptom the following day,
surfaced by the **first run** of `scripts/check-id-collisions.ps1`:

- `character` 18935 **Shadow0712**, `AccountId=13`, `Deleted=0`
- `simple_object` 18935: **Type=4 (QuestObject), CBID=20046** (expected
  Type=Character (20); original character CBID unknown — clobbered)

Unlike Donuts, this incident has not yet been walked through
`log-sector.txt` / DB inspection to confirm how the clobber happened, or
whether it predates or postdates this pass's fixes landing on the server the
account was on. Recorded here as an open item, needing §6.4 treatment — it is
**not** folded into the "fixed" claims in §10 until triaged.

### 3.3 Why “character IDs and inventory IDs should be different” feels right

Conceptually they should be. This codebase and the retail-shaped schema use
**one** COID universe for anything that is a `simple_object`. Separating them
would mean a new table or a reserved high-bit / range split and client-visible
TFID rules — a large design change. The practical fix is: **one minting
authority for every COID that lands in `simple_object` as a durable global**,
plus refuse cross-category overwrites.

---

## 4. What we fixed (face A)

Commits on the combat cherry branch (representative; see git history for
full stack):

| Commit | Change |
|--------|--------|
| `42463f903` | **Allocator:** `InventoryRuntime.AllocateItemCoid` → insert Type=0 placeholder in `simple_object`, return generated COID. **Guard:** `EnsureSimpleObject` throws on overwrite of Character/Vehicle rows. Tests: `InventoryRuntimeTests`, `SimpleObjectOverwriteGuardTests`. |
| `7c1eefdd4` | **Load fail-closed:** `Character.LoadFromDB` / `Vehicle.LoadFromDB` refuse wrong `simple_object.Type`; equipment `SimpleObject.LoadFromDB` refuses char/vehicle rows; `SendCharacter` skips null loads + error log. Tests: `CharacterSelectionManagerTests` SS-31 cases. |
| `14948c030` | **Stragglers:** `MissionCargoService.AllocateInventoryCoid` and `MissionUseItemProgress` grant path use `InventoryRuntime.AllocatePersistentCoid` instead of `Map.LocalCoidCounter++`. |

### 4.1 Shared persistent allocator

```csharp
// InventoryRuntime — production path
AllocateItemCoid() => AllocatePersistentCoid(); // default: AllocateFromSimpleObjectSequence

// Inserts SimpleObjectData { Type = 0, CBID = 0 }, SaveChanges, returns row.Coid
```

Call sites that **must** use this (persisted inventory):

- World item **pickup** → cargo claim (`TNLConnection.Sector` / loot claim path)
- Loot → inventory COID when claiming into bags  
- Vendor **buy**  
- Chat / admin **addItem**  
- Mission cargo grants / use-item completed-item grants  

A placeholder row occupies the sequence slot so no concurrent character create
can receive the same COID before the item row is filled in.

### 4.2 Overwrite guard

`EnsureSimpleObjectInternal` (simplified):

- If row exists and `existing.Type` is Character or Vehicle and differs from
  the requested item type → **`InvalidOperationException`** (loud SS-31).
- Type=0 placeholders and same-category updates still fill/update as before.
- New COIDs insert with explicit PK via raw SQL (identity column still owns
  sequence for pure inserts without PK).

### 4.3 Character-select fail-closed

Even if a row was already corrupted (or a future bug races the guard):

1. `Character.LoadFromDB`: require `SimpleObjectBase`; if `Type` is set and
   not `Character` (20), log and return false. (`Type==0` allowed as legacy
   placeholder-ish rows.)
2. `Vehicle.LoadFromDB`: same for `Vehicle` (14).
3. `ObjectManager.LoadCharacterForSelection` returns null.
4. `CharacterSelectionManager.SendCharacter` logs and **sends no**
   CreateCharacter/CreateVehicle for that COID.
5. Other characters on the account still list; client stays alive.

Operators should treat skip logs as **data damage**, not “ignore forever.”

### 4.4 What still uses `Map.LocalCoidCounter` (by design)

| Use | Why local is OK |
|-----|-----------------|
| World loot **spawn** on the ground | Ephemeral map object; pickup allocates a **new** persistent COID |
| Vendor browse **display** slot COIDs | `StoreSlotIdentity` mints from a dedicated offset range (`CoidBase = 0x6000_0000`, always `Global=true`) — clear of low-positive player/vehicle `simple_object` COIDs and of `MapNpcIdentity`'s `0x5000_0000+` range; the buy path still allocates a fresh persistent COID via `AllocateItemCoid` |
| NPC / prop / reaction local spawns | Map-local TFIDs (`Global=false`) |

**Rule of thumb:** if the COID will be written to `simple_object` or
`character_inventory.ItemCoid` as durable global state, it must come from
`InventoryRuntime.AllocatePersistentCoid` (or the character/vehicle create
path that inserts `simple_object` first). If it only exists on the current
map and dies with the instance, local counter is fine.

---

## 5. Face B — combat / map COID collision (related SS-31)

After a character DB wipe, global identity COIDs restart near 1 while maps
still author local objects with small COIDs. Lookups that did
“find by COID, prefer local” made:

- Player shots hit map props  
- Acquisition dedupe drop one of two entities  
- Invulnerability flags clear on the wrong object  

**Remediation (commit family `f701e3c33` and related):** resolve targets by
full **TFID** `(Coid, Global)` via `CombatTargetResolver`, hard-target latch,
weapon acquisition, skills, reactions, and sector packet handlers. See
`docs/exception-safety-audit.md` SS-31 row for the combat register entry.

Face A and face B are **not** fixed by the same code, but both require
engineers to treat bare COIDs as ambiguous.

---

## 5.5 Face C — client-side bare-COID keying (map-transfer corruption)

**Live incident date:** 2026-08-10 (account `floppy`, warps into Ground Zero
661 and Hestia maps). **Symptom:** on map transfer the client arrives broken —
incomplete vehicle, wrong position, unresponsive controls — plus phantom
"pickable" cargo (client spams `ItemPickup` for its own cargo coid; server
logs `HandleItemPickupPacket: Item <coid> is not a SimpleObject`).

### Mechanism

The server resolves targets TFID-exact (face B fix), but the **retail client
keys parts of its object table by bare COID**. When a persistent global the
client must bind at transfer time — the character, the vehicle, its equipped
items, or cargo/locker items — has a coid that numerically equals an
**authored local object** on the destination map, the create-stream corrupts
the client's bindings. The server cannot patch this; the only defense is
keeping persistent coids numerically disjoint from authored map coids.

Post-wipe, the `simple_object` sequence started at 1, so characters (coid 1),
vehicles (2), equipment (3–7), and — after the face-A shared allocator —
every new item (~18.1k–19k band) all landed inside the authored map-object
range (observed ~1..45k). Evidence trail: cargo item 18942 vs an authored
non-SimpleObject object with coid 18942 on map 661 (pickup spam since
2026-08-09 19:05, pre-dating the hardening merge); repeated broken 707↔661
warps on both pre- and post-merge builds.

### Remediation (2026-08-10)

**Re-base the persistent coid sequence out of the authored band**:
`simple_object AUTO_INCREMENT = 134217728` (0x0800_0000). The full numeric
map is now:

| Range | Owner |
|-------|-------|
| ~1 .. ~65k | Authored map objects (local) + legacy pre-rebase globals |
| 0x0800_0000 + | Persistent globals (`simple_object` sequence) |
| 0x5000_0000 + | `MapNpcIdentity` (NPC/wheelset wire TFIDs) |
| 0x6000_0000 + | `StoreSlotIdentity` (vendor display slots) |

`scripts/wipeplayers.ps1` now applies this rebase after truncation (TRUNCATE
resets AUTO_INCREMENT to 1) and hard-fails if the sequence is below the base.
Existing sub-base rows were removed by the 2026-08-10 wipe (accounts kept).
**Any future wipe or restore that leaves the sequence low reintroduces this
face** — `scripts/check-id-collisions.ps1` checks the sequence floor.

---

## 6. Diagnostics

### 6.1 Logs to grep

```text
SS-31
simple_object.Type
SendCharacter: skipped
EnsureSimpleObject: coid
refusing to overwrite
client would AV at 0x0080A62A
AllocateItemCoid
```

Typical healthy list load:

```text
Character.LoadFromDB: Loaded character Coid … Name from DB: '…'
```

Corrupt identity (after fail-closed):

```text
Character.LoadFromDB: coid=… simple_object.Type=… cbid=… (expected Character=20) — skipping …
SendCharacter: skipped coid=… account=…
```

### 6.2 SQL health checks

Characters whose identity row is not a character type:

```sql
SELECT c.Coid, c.Name, c.AccountId, c.Deleted, s.Type, s.CBID
FROM `character` c
JOIN simple_object s ON s.Coid = c.Coid
WHERE s.Type NOT IN (0, 20);   -- 20 = CloneBaseObjectType.Character
```

Vehicles:

```sql
SELECT v.Coid, v.Name, v.CharacterCoid, s.Type, s.CBID
FROM vehicle v
JOIN simple_object s ON s.Coid = v.Coid
WHERE s.Type NOT IN (0, 14);   -- 14 = Vehicle
```

Inventory items pointing at character/vehicle identity COIDs:

```sql
SELECT i.Id, i.CharacterCoid, i.ItemCoid, i.Cbid, i.Type AS inv_type,
       s.Type AS so_type, s.CBID
FROM character_inventory i
JOIN simple_object s ON s.Coid = i.ItemCoid
WHERE s.Type IN (20, 14);
```

Orphan placeholder rows (`Type=0` rows the allocator reserved but that were
never filled in by a persist — i.e. a leaked reservation, not referenced by
any character, vehicle, inventory item, or vehicle equip slot):

```sql
SELECT s.Coid
FROM simple_object s
WHERE s.Type = 0
  AND NOT EXISTS (SELECT 1 FROM `character` c WHERE c.Coid = s.Coid)
  AND NOT EXISTS (SELECT 1 FROM vehicle v WHERE v.Coid = s.Coid)
  AND NOT EXISTS (SELECT 1 FROM character_inventory i WHERE i.ItemCoid = s.Coid)
  AND NOT EXISTS (
        SELECT 1 FROM vehicle v2
        WHERE s.Coid IN (v2.Ornament, v2.RaceItem, v2.PowerPlant, v2.Wheelset,
                          v2.Armor, v2.MeleeWeapon, v2.Front, v2.Turret, v2.Rear));
```

This is the same query family `scripts/check-id-collisions.ps1` runs (see
§7.4 / SCRIPTS.md) — a leaked placeholder is harmless to gameplay (nothing
points at it) but signals a code path that allocates a persistent COID and
then fails to persist without cleaning up the reservation.

### 6.3 Client crash fingerprint

| Field | Face A select crash |
|-------|---------------------|
| Exception | `c0000005` |
| Address | `autoassault!0x0080A62A` (build-dependent; this title version) |
| Map | `(-1)` |
| Timing | Immediately after auth when character list arrives |

If Map is a real continent id, look at sector enter / CreateVehicle / ghost
paths instead — not this doc’s primary incident.

### 6.4 Repair vs delete

| Option | When |
|--------|------|
| **Skip forever** | Acceptable only as temporary; list omits the char |
| **Soft-delete** (`character.Deleted=1`) | Hides from list; leaves corrupt `simple_object` |
| **Hard-delete** | Preferred for unrecoverable alts: child tables, vehicle + equip SO rows, character row; **do not** delete a `simple_object` still referenced as another player’s `ItemCoid` |
| **Surgical repair** | Only if original Character CBID / Type known **and** vehicle graph intact; rare after item overwrite |

---

## 7. Suggested stricter guards (not all implemented)

Current stack has moved well past the Donuts-class minimum since this pass:
5 of the 13 items below are now **implemented**. What remains is genuine
defense-in-depth and regression insurance, not a gap that leaves the known
incident class open.

### 7.1 Allocator / architecture

1. **IMPLEMENTED** — Single choke point enforcement: `CoidAllocationArchitectureTests`
   (`src/AutoCore.Game.Tests/Architecture/CoidAllocationArchitectureTests.cs`)
   source-scans every `.cs` file under `src/` and fails if `LocalCoidCounter`
   appears outside a 10-entry allowlist (map-spawn, vendor-display-slot
   instance, and comment-only mentions), or if `SyncFromCargo` /
   `InventoryCoidCounter` appear anywhere in production source at all — this
   is a permanent tripwire, not a one-time grep.
2. **Post-allocate assert** — after minting a persistent COID, assert it is
   not already a `character` or `vehicle` PK (should be impossible with
   placeholder insert; tripwire if someone bypasses the API). *Not done —
   the placeholder-insert allocator (item 4) already makes this structurally
   unreachable; would only catch a bypass of the allocator itself, which
   item 1's arch test already prevents at review time.*
3. **ObjectManager check on claim** — if a newly allocated cargo COID is
   already a live global in `ObjectManager`, abort claim and log fatal-class
   error. *Not done.*

### 7.2 Persist layer

4. **IMPLEMENTED** — No cross-type rewrite except placeholder:
   `InventoryPersistence.EnsureSimpleObjectInternal` now refuses any
   `existing.Type != requestedType` unless `existing.Type == 0` (allocator
   placeholder) **or** it is a same-Type/CBID identity refresh, throwing
   `InvalidOperationException` otherwise. This is no longer Character/Vehicle-
   specific — the guard is type-agnostic, so a Weapon→Armor clobber between
   two item paths is refused the same way a Weapon→Character clobber is.
5. **Explicit insert vs update** — placeholder fill is UPDATE; true new items
   never UPDATE an unknown pre-existing row. *Not done as a separate
   mechanism — item 4's guard achieves the same outcome by refusing the
   unknown-row case outright rather than by insert/update code-path
   separation.*
6. **DB constraints (heavy)** — optional trigger or app-level check that
   `character.Coid` implies `simple_object.Type IN (0,20)`, etc. *Considered,
   declined — see below.*

### 7.3 Load / wire

7. **IMPLEMENTED** — Clonebase kind check: `Character.LoadFromDB` and
   `Vehicle.LoadFromDB` verify, immediately after `LoadCloneBase`, that the
   clonebase's kind matches the entity class being loaded, in addition to the
   pre-existing null-simple-object / wrong-`Type` / `CBID <= 0` checks; all
   four checks are fail-closed (skip the row, do not wire-poison the client).
   `SimpleObject.LoadFromDB` carries the matching null/type/CBID checks
   (clonebase-kind is Character/Vehicle-only, since plain simple objects have
   no clonebase-kind distinction to check).
8. **Char create self-test** — immediately after create, re-read SO type and
   fail the create transaction if mismatched. *Not done.*

### 7.4 Operations

9. **Scheduled SQL scan** (queries in §6.2) → Discord/log alert. *Deferred —
   see "Considered, declined" below; `scripts/check-id-collisions.ps1`
   already covers manual/on-demand detection of the same conditions.*
10. **IMPLEMENTED** — Metric: `CharacterSelectionManager.CorruptIdentitySkipCount`
    and `InventoryPersistence.Ss31OverwriteRefusedCount` are live counters,
    surfaced in `SectorServer.CollectHealthMetrics` as `Ss31SelectSkips` and
    `Ss31OverwriteRefusals` in the periodic health-summary log line.
11. **One-shot cleanup tool** — soft-delete or quarantine characters failing
    the type join. *Not done — the Donuts cleanup in §3.2 was a manual
    one-off; no repeatable tool exists yet.*

### 7.5 Tests

12. **IMPLEMENTED** — Integration test: `Ss31CollisionIntegrationTests`
    (`src/AutoCore.Game.Tests/Inventory/Ss31CollisionIntegrationTests.cs`)
    seeds a character/vehicle pair at coids 18274/18275, forces the map-local
    counter onto the character's own coid, and asserts the shared allocator
    never returns that coid and the persist path never touches the character
    row — plus a companion case asserting `EnsureSimpleObject` throws (rather
    than clobbers) if something ever did try to write item data at the
    character's coid.
13. Red/green already present for overwrite guard and list skip; keep them
    on the default CI filter. *Ongoing practice, not a one-time item — see
    the expanded test list in exception-safety-audit.md's SS-31 row.*

**Highest value / lowest cost:** (4) placeholder-only type fill + (1) CI
grep/arch rule on map-counter → inventory persist — **both now implemented**.

### Considered, declined

- **Range-split COID namespaces** (e.g. characters in one numeric band,
  inventory items in another) — would eliminate the collision class
  structurally, but the ranges are **client-visible** (COIDs travel on the
  wire as TFIDs) and every consumer of raw COID values, save data, and log
  correlation across the existing live DB would need to be audited or
  migrated. Too disruptive relative to the single-allocator fix already
  landed; declined for this pass.
- **DB constraints / triggers** (item 6) — a trigger enforcing
  `character.Coid` ⇒ `simple_object.Type IN (0,20)` would catch violations
  that bypass the C# layer entirely (e.g. a manual SQL fix-up), but adds
  operational weight (trigger maintenance, migration risk, harder-to-debug
  failures at the DB layer) for a case the application-layer guard (item 4)
  and the architecture test (item 1) already close off from the code side.
  Declined as heavy for the marginal coverage gained.
- **Scheduled SQL scan + Discord/log alerting** (item 9) — deferred, not
  declined outright. `scripts/check-id-collisions.ps1` (see §6.2, SCRIPTS.md)
  already runs the same detection queries on demand; wiring it to a schedule
  and an alert channel is straightforward follow-up work but out of scope
  for this pass.

---

## 8. Code map (face A)

| Area | Path |
|------|------|
| Persistent allocate | `src/AutoCore.Game/Inventory/InventoryRuntime.cs` |
| Persist + overwrite guard | `src/AutoCore.Game/Inventory/InventoryPersistence.cs` |
| Leak pre-validation | `src/AutoCore.Game/Inventory/InventoryManager.cs` (`CanAcceptAnyOfCbid`, `TryPersistEquip`) |
| Vendor display slot COID | `src/AutoCore.Game/Map/StoreSlotIdentity.cs` (dedicated `0x6000_0000+` range; replaces the retired `InventoryCoidCounter.SyncFromCargo` map-local resync) |
| Pickup claim COID | `src/AutoCore.Game/TNL/TNLConnection.Sector.cs` (`HandleItemPickupPacket`) |
| Loot inventory COID | `src/AutoCore.Game/Managers/LootManager.cs` |
| Vendor buy COID | `src/AutoCore.Game/Managers/VendorStoreService.cs` |
| Mission grants | `src/AutoCore.Game/Mission/MissionCargoService.cs`, `Managers/MissionUseItemProgress.cs` |
| Char/vehicle load guards | `src/AutoCore.Game/Entities/Character.cs`, `Vehicle.cs`, `SimpleObject.cs` |
| Character list | `src/AutoCore.Game/Managers/CharacterSelectionManager.cs` |
| Selection load entry | `src/AutoCore.Game/Managers/ObjectManager.cs` `LoadCharacterForSelection` |
| Health metrics | `src/AutoCore.Sector/Network/SectorServer.cs` (`CollectHealthMetrics` → `Ss31SelectSkips`, `Ss31OverwriteRefusals`) |
| Architecture tripwire | `src/AutoCore.Game.Tests/Architecture/CoidAllocationArchitectureTests.cs` |
| Type enum | `src/AutoCore.Game/Constants/ClonebaseObjectType.cs` |
| Tests | `SimpleObjectOverwriteGuardTests`, `InventoryRuntimeTests`, `CharacterSelectionManagerTests`, `Ss31CollisionIntegrationTests`, `CoidAllocationArchitectureTests`, `VendorStoreSlotIdentityTests`, `InventoryPlaceholderLeakTests`, `InventoryEquipPersistRollbackTests` |

**Retired this pass:** `InventoryCoidCounter` and its `SyncFromCargo` login
resync were deleted outright — they were the mechanism behind the sibling
world-spawn collisions in §3.2. Vendor display slots now mint from
`StoreSlotIdentity` instead.

Face B: `CombatTargetResolver`, `WeaponFireTargetAcquisition`, sector firing /
resend handlers, `SkillService.ResolveTarget` — see exception-safety audit.

---

## 9. Related docs

- [Exception-safety audit (SS-nn)](exception-safety-audit.md) — SS-31 combat
  register entry; process for tripwire-first fixes  
- [Crash report notes](crashreport.md) — client AV patterns  
- [Networking](networking.md) — CreateCharacter / inventory packet layout  
- [Inventory cargo wire RE](inventory-cargo-wire-re.md) — client cargo binding  

---

## 10. Bottom line

| Question | Answer |
|----------|--------|
| Is the Donuts / select-AV class fixed? | **Yes**, with shared DB allocation, a type-agnostic overwrite refusal, fail-closed load guards (including clonebase-kind), leak pre-validation, `TryPersistEquip` rollback, and retirement of `SyncFromCargo`/`InventoryCoidCounter` — deployed on this pass. The second incident (Shadow0712, §3.2) was resolved by the 2026-08-10 player wipe (face C remediation) — its corrupt rows no longer exist. |
| Is the map-transfer client corruption fixed (face C)? | **Yes for all post-wipe data**: the 2026-08-10 wipe removed every sub-floor persistent coid and re-based the `simple_object` sequence to 0x0800_0000, so persistent globals can no longer numerically collide with authored map objects (§5.5). `wipeplayers.ps1` enforces the rebase on every future wipe; `check-id-collisions.ps1` queries 5–6 detect regression. |
| Are character and inventory IDs separate? | **No.** One `simple_object` sequence. Design intent of the fix is single minting authority, not dual namespaces — range-split namespaces were considered and declined (§7, "Considered, declined") as too disruptive to client-visible COIDs. |
| Can it happen again? | Not via the known pickup/vendor/mission/addItem/equip paths. A **new** bypass of the allocator is now caught at review/CI time by `CoidAllocationArchitectureTests` (§7.1 item 1), and any cross-type overwrite that did slip through is refused by `EnsureSimpleObjectInternal` (§7.2 item 4) rather than silently corrupting the row. |
| Must we implement §7? | **Partially done.** 5 of 13 items are implemented (1, 4, 7, 10, 12 — architecture tripwire, placeholder-only overwrite guard, clonebase-kind load check, `Ss31SelectSkips`/`Ss31OverwriteRefusals` metrics, end-to-end integration pin). The remaining 8 are optional defense-in-depth (post-allocate assert, `ObjectManager` claim check, explicit insert/update split, DB constraints, char create self-test, scheduled scan/alerting, one-shot cleanup tool) — not required for playability, useful for faster detection of the next foot-gun. |
