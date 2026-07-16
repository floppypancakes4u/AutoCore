# Task B5 — airStabilization recovery: live-capture plan (handoff for a later agent)

> **Priority: LOW.** Everything else in the B/C RE campaign can proceed without this. B5 is the last
> open **B**-task; it refines the already-shipped C6 `ReGround` primitive and feeds the D-phase
> spawn/teleport/recovery wiring. Written 2026-07-16 after B1–B4/B6–B8 landed. This doc is
> **self-contained** — all addresses, offsets, constants, the capture method, and the deliverables are
> inline so you don't have to re-derive anything.

---

## 1. What B5 is, in one paragraph

`VehicleAction_airStabilization` (**`0x598320`**) is the client's **post-collision recovery** for
vehicles: after a crash/flip, for a 6400 ms window it applies a self-righting corrective impulse, and
the moment that window expires it **zeroes the vehicle's velocity, clears the drive axes, and snaps the
chassis back onto the terrain**. C6 already ported the *recovery* half as
`VehiclePhysicsInstance.ReGround(query)` from **static** decompile evidence. **B5's job is to
drive a car into a real crash/flip and capture the runtime behaviour**, to (a) confirm C6's assumed
side-effects bit-exactly and (b) pin the one genuinely-unresolved piece — the **in-window corrective
impulse** whose math lives in an entity virtual method that was never decompiled.

**Why it matters for the branch:** NPC vehicles that flip/fall out of the world must recover exactly
like the client did (snap upright onto terrain, kill momentum) or they'll drift/tumble forever. C6 is
the primitive; B5 makes it faithful and adds the in-window nudge if the port needs it.

---

## 2. What is ALREADY RESOLVED (static) — read these first

The authoritative per-function record is
**`docs/reconstruction/physics/verified/fn_00598320_airStab.md`** (fully decompiled, constants
`read_memory`-verified). Also **`docs/reconstruction/physics/avd-airstab-spec.md`** (the port spec to
update). Summary of what they establish — **do not re-derive**:

**Control flow (`0x598320`, `__thiscall`, `this` = VehicleAction, `param_2` = input/dt block):**
```
entity = *(VA + 0x44)
if (entity+0x103 != 0)               return;   // forced-stop / dead
if (disabledFlag[+0x7e] != 0)        return;   // *( *(*(entity+4)+4) + 0xa8 + entity ) + 0x7e
delta = g_dwClientTickMs - *(u32*)(entity + 0x14)          // entity+0x14 = last-collision stamp (ms)
if (delta < 0x1900) {                          // ===== IN COLLISION WINDOW (6400 ms) =====
    *(u8*)(VA + 0x1c) = 1;                      // in-collision flag
    if (speed > DAT_009d54a8) {                 // ~1.19e-7, i.e. "moving"
        // pack pose/vel/quat, call entity vtbl[+0x3c] to BUILD a corrective impulse,
        // if it returns ok → CVOGPhysics_ApplyImpulseVector (phys vtbl +0x50), then return
    }
} else if (*(u8*)(VA + 0x1c) != 0) {           // ===== RECOVERY (window just expired) =====
    *(u8*)(VA + 0x1c) = 0;
    // reset 3 stabilizer slots (entity+0x260, i=0..0xC step 4 → FUN_0056a260(slot,0))
    // phys vtbl[+0x50](&zeroVec)   → zero LINEAR velocity
    // phys vtbl[+0x54](&zeroVec)   → zero ANGULAR velocity
    // VehicleEntity_SetDriveAxes(0) @ 0x4fbec0   → clear throttle/steer/handbrake
    // RE-GROUND: yStart = rb[+0xb4] + 10.0; h = CVOGMap_CastTerrainHeight(rb[+0xb0], rb[+0xb8]);
    //            pos = (rb[+0xb0], h, rb[+0xb8]); phys vtbl[+0x40](&pos)  → set position
}
```

**Key addresses / helpers:**

| Addr | Symbol | Role |
|------|--------|------|
| `0x598320` | `VehicleAction_airStabilization` | the function |
| `0x598650` | `VehicleAction_applyAction` | sole caller (unconditional, after `calcWheelTorque`, xref `0x599227`) |
| `0x4cfe60` | `CVOGMap_CastTerrainHeight` | down-cast for re-ground Y (args: worldX, worldZ; startY on stack) |
| `0x4fbec0` | `VehicleEntity_SetDriveAxes` | clears `entity+0x614/618/61c` on recovery |
| `0x40d260` | `CVOGPhysics_ApplyImpulseVector` | applies the in-window impulse (phys vtbl `+0x50`) |
| `0x56a260` | `FUN_0056a260` | reset one stabilizer slot |
| `0x53e0b0` | `FUN_0053e0b0` | velocity getter (speed gate) |
| `0x404c90` | pos-ptr getter | returns `rb+0xb0` (or entity fallback) |
| `0x404a20` | quat-ptr getter | returns `rb+0x30` (or entity fallback) |
| **entity vtbl `+0x3c`** | (unnamed) | **BUILDS the in-window corrective impulse — NOT yet decompiled (the open piece)** |

**Constants (`read_memory`-verified):** `0x1900` = **6400** (window ms, vs `g_dwClientTickMs`);
`DAT_00a110d8` @ `0xa110d8` = **10.0** (re-ground Y raise); `DAT_009d54a8` @ `0x9d54a8` =
**1.1920929e-7** (speed epsilon); `DAT_00b04eb0` @ `0xb04eb0` = **zero vec4** (the lin/ang-vel clear arg).

**Rigid-body offsets** (`fn_offsets_rigidbody.md`): `rb = *(*(entity+8)+0x3c)`; `+0x30` quat(xyzw),
`+0x40` linVel(xyzw), `+0x50` angVel(xyzw), `+0xb0` pos(xyzw). Physics-object vtbl (via `*(phys+0x3c)`):
`+0x40` set position, `+0x50` set/apply linear, `+0x54` set angular.

**What C6 already ported from this** (`VehiclePhysicsInstance.ReGround`, commit `a65849b`): cast from
`PosY + ReGroundYRaise(10)`, snap to hit Y, **zero lin+ang velocity**, zero force/torque, **SetDriveAxes(0)**
(throttle/steer/handbrake), reset per-wheel state. C6 does **not** implement the in-window corrective
impulse (see §3.5).

---

## 3. What B5 must CAPTURE / RESOLVE (needs a live crash — this is the whole point of B5)

The static decompile is solid on *structure*; a runtime crash confirms the *dynamics* and resolves the
one true unknown. Capture each of these:

**3.1 Recovery actually zeroes lin+ang velocity.** Watch `rb+0x40` (linVel) and `rb+0x50` (angVel) on
the frame where `VA+0x1c` transitions `1 → 0`. Confirm both become `~0` immediately after. (C6 assumes
this — confirm bit-exactly, i.e. exactly `0.0`, not a scaled damp.)

**3.2 The re-ground Y value.** Capture the `CVOGMap_CastTerrainHeight` **input** (`startY = rb+0xb4+10`,
worldX=`rb+0xb0`, worldZ=`rb+0xb8`) and **output** (`h`), and confirm the written `pos.y == h` (the
terrain height), **not** `startY`. (C6's `ResolveReGroundPositionY` returns the hit Y — confirm.)

**3.3 SetDriveAxes(0) fires on recovery.** Confirm `entity+0x614/618/61c` are cleared on the recovery
frame.

**3.4 Stabilizer slots reset.** Confirm the 3 slots at `entity+0x260` are reset (mostly informational —
the server has no analogue; note it and move on).

**3.5 THE IN-WINDOW CORRECTIVE IMPULSE (the genuinely-open item).** During `delta < 0x1900` with the car
moving, `airStabilization` calls **`entity_vtbl[+0x3c]`** to build an impulse, then applies it via
`CVOGPhysics_ApplyImpulseVector`. **That builder was never decompiled** — its math (the self-righting /
velocity-coupled nudge that keeps a mid-air/crashing car from tumbling) is unknown. Resolve it by EITHER:
- **Static:** identify the entity's C++ class (`get_rtti_classname` on `entity`, or from
  `CVOGVehicle` vtable), read its vtable, resolve slot `+0x3c`'s function address, and decompile it in
  Ghidra. Inputs passed are: `*param_2` (dt block), `&pos`(`local_20`), `&linVel`(`local_50`),
  `&angVel`(`local_30`), plus stack `&quat`, `1.0f`, `0`, `1`. Output = the impulse applied at `+0x50`.
- **Live:** breakpoint just after the `CVOGPhysics_ApplyImpulseVector` call (or read the impulse struct
  the builder fills) during an active crash window, and capture input pose/vel/quat → output impulse for
  a few frames to characterise it.
- **Decide for the port:** does the server NPC recovery need the in-window impulse at all, or is the
  window-expiry ReGround (already in C6) sufficient? Retail applies the nudge *every frame* of the
  window; C6 only handles the expiry snap. If NPC cars visibly tumble during the 6.4 s window, port the
  nudge; otherwise document that C6's expiry-snap is the accepted server simplification.

**3.6 `entity+0x14` last-collision stamp.** Confirm what writes it (the collide/contact path) so the
window start is understood; the server needs an equivalent "last collision tick" to gate recovery. This
is mostly a wiring note for the D-phase (`ForeignNpcDriverWire` / collision events), not a physics golden.

---

## 4. EXACTLY how to capture (method, proven this session)

**Prereqs (the CE constraint):** the `cheatengine` MCP is registered at user scope; you can also drive
the bridge directly via the named pipe with **`tmp/re/ce_client.py`** (see **`tmp/re/CE_API_NOTES.md`**
for the full protocol, attach steps, and sanity constants). **ASK THE USER before using Cheat Engine**
(they requested this), and **the user must drive the car into a crash/flip** — you cannot produce the
collision state yourself.

**Attach & verify** (per CE_API_NOTES): `open_process {"process_id_or_name":"autoassault.exe"}`, `ping`,
verify the 1:1 static map (`0x9cc798`→`FF FF EF 41`, `0xaf3388`→`00 00 A0 41`). Base `0x400000`,
addr == Ghidra addr.

**The capture harness** — reuse the **Lua synchronous-snapshot pattern** from B1
(`tmp/re/b1c.py` + the `evaluate_lua` handler). The airStab recovery frame is a **one-shot**
(fires once, when `VA+0x1c` flips `1→0`), so you need a **latch**, not a single arm:

```
Install an execute BP at 0x598320 (airStabilization entry). In the Lua callback (ECX = VA this):
  entity = readInteger(ECX + 0x44)
  rb     = readInteger(readInteger(entity + 8) + 0x3c)
  flag   = readInteger(entity + 0x14)          -- last-collision stamp
  vaflag = readByte(ECX + 0x1c)                -- in-collision flag (pre-update)
  -- snapshot pre-state EVERY hit into a ring: rb+0x40 linVel, rb+0x50 angVel, rb+0xb0 pos, rb+0x30 quat
  -- LATCH: when this hit is the recovery frame, capture a "recovery" record.
  --   You cannot see the 1->0 transition at ENTRY (the function does it). Instead:
  --   detect "was in window last hit (vaflag==1) AND delta>=0x1900 now" — read g_dwClientTickMs and
  --   entity+0x14 to compute delta = clientTick - stamp; if vaflag==1 and delta>=0x1900 → THIS call
  --   will run the recovery branch. Snapshot pre-state now, and set a second BP at the recovery
  --   tail (after the SetDriveAxes/re-ground, ~0x59863b set-position call) to snapshot post-state.
```
- `g_dwClientTickMs` is a global; find it from the `delta` computation in the decompile (the load feeding
  `sub` against `entity+0x14`). Alternatively, just **ring-buffer every airStab hit's rb velocity+pos**
  and, after the user crashes, find the frame where linVel/angVel drop to exactly 0 and pos.y jumps —
  that's the recovery frame (same peak-latch idea as B1's slide capture).
- For the **in-window impulse (3.5)**: set an execute BP at the `CVOGPhysics_ApplyImpulseVector` call
  site inside airStab (find it in the decompile, ~the `+0x50` vtbl call in the `delta<0x1900` branch),
  read the impulse vector argument + the packed pose/vel/quat locals at the hit.
- **Coordinate with the user** like B1: install the latch, tell them "crash/flip the car now, then say
  done", read the latched recovery frame + any in-window impulse samples. Repeat for a couple of crash
  types (roll-over, nose-dive, out-of-world fall) to cover cases.
- **Clean up**: remove every BP (`debug_removeBreakpoint`, or the bridge's `clear_all_breakpoints` for
  bridge-tracked ones — note custom `evaluate_lua` BPs need explicit `debug_removeBreakpoint`).

---

## 5. Deliverables (Definition of Done for B5)

1. **`src/AutoCore.Game.Tests/Physics/oracles/airStab_goldens.json`** — captured recovery frames
   (pre/post lin+ang vel, pre pos + post pos, terrain-cast in/out) + any in-window impulse samples, with
   raw LE-float32 hex per the oracle pattern (`CE_API_NOTES.md` §"Float → golden hex").
2. **`src/AutoCore.Game.Tests/Physics/oracles/AirStabOracleTests.cs`** — assert fixture
   self-consistency (hex↔decimal) and the retail contract: recovery zeroes lin+ang vel, writes terrain-Y,
   clears drive axes. Compare against `VehiclePhysicsInstance.ReGround` where it maps; `[Ignore]` any part
   that needs an in-window-impulse port not yet built.
3. **Update `docs/reconstruction/physics/avd-airstab-spec.md`** and add a "Task B5 live capture" section
   to **`fn_00598320_airStab.md`** with the confirmed dynamics + the resolved (or decompiled) in-window
   impulse math.
4. **Feed C6:** confirm/adjust `ReGround`'s side-effects to match the capture; if the in-window impulse is
   needed for the server, add it (new method / extend `HkVehicleAirStabilization`) with its own TDD.
   If not needed, document C6's expiry-snap as the accepted simplification.
5. Per-task gate: **zero new test failures** vs the baseline (1 pre-existing:
   `DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll`).
6. Ledger line in `.superpowers/sdd/progress.md`; flip the row in
   `docs/remainingBackgroundWork.md` §5/§9; mark the memory `retail-npc-driving-branch.md`.

---

## 6. Non-negotiables / gotchas

- **Work in the worktree** `.worktrees/feature-NPC-Retail-Driving` (branch `feature-NPC-Retail-Driving`).
- **ASK before using Cheat Engine**; the **user must drive the crash** — this task cannot be done idle.
- Ghidra is READ-ONLY (bookmarks/comments only); do **not** attach Ghidra's debugger to the game.
- Keep `CE_MCP_ALLOW_SHELL` unset.
- The window is **6400 ms**, not "6400 ticks / 1.07 s" — old plates are wrong (see `fn_00598320_airStab.md` §8).
- `DAT_00a110d8 = 10.0` is the **re-ground Y raise only**, NOT an AVD/angular-damping term (common misread).
- Continuous AVD (`hkAngularVelocityDamper_update 0x64d810`) is a **separate** mechanism — not part of B5.

---

## 7. Pointers

- Static record: `docs/reconstruction/physics/verified/fn_00598320_airStab.md` (authoritative).
- Port spec to update: `docs/reconstruction/physics/avd-airstab-spec.md`.
- Already-built primitive: `HkVehicleAirStabilization` + `VehiclePhysicsInstance.ReGround` (C6, `a65849b`).
- Capture tooling: `tmp/re/ce_client.py`, `tmp/re/CE_API_NOTES.md`, `tmp/re/b1c.py` (Lua latch pattern).
- Overall campaign state / dependency graph: `docs/remainingBackgroundWork.md`.
