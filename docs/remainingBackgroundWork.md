# Remaining Background Work — Retail NPC Vehicle Physics (feature-NPC-Retail-Driving)

> **Handoff doc for a fresh agent that HAS live MCP access (Cheat Engine + Ghidra static).**
> Written 2026-07-16 by the background agent that ran tasks A1, B6, B7, B8, C3, E1. This is self-contained:
> you should be able to resume without re-deriving anything. Read it top to bottom once, then start at
> **§9 "Immediate next action"**.

---

## 1. Mission (what this branch does and why)

AutoCore is a server emulator for the game **Auto Assault** (`autoassault.exe`, a 32-bit client). NPC vehicles
currently look wrong (slide, snap, hover, never take ramps or gain air) because the server authors the vehicle
**pose kinematically** and streams it as truth — the client's Havok 2.3 vehicle simulation never runs for
server-owned NPC vehicles.

**Goal of this branch:** a **bit-exact, hand-rolled .NET port** of the client's Havok 2.3 vehicle physics +
drive controller, run **server-side for NPC / server-owned vehicles only** (players stay client-authoritative),
so NPC cars drive for real (grounded stance, ramps, ballistic air, wheel spin, slope stance). Enabled behind
`serverConfig.yaml` (`controllerTier: physics`, OFF by default).

**The current problem being fixed (the "fidelity rework"):** the retail physics library was already ported
(Phases 2–5, committed in `56f2619`, ~20 files under `src/AutoCore.Game/Physics/Vehicle/`, 282 tests), BUT the
NPC controller (`NpcVehiclePhysicsController.cs`) doesn't trust it — it authors XZ + Y kinematically, runs the
sim only for throttle/steer bookkeeping, then **force-overwrites the body back to the authored pose**. Every
vertical constant it uses is self-labeled "server stability; not retail constants." Result: float-above-terrain,
upward drift on turns, weak "10% gravity" on ramps. The sim itself was also **defanged** when it briefly held
authority (suspension applied as COM force, friction r×F disabled, gravity-share load floor, placeholder friction
inputs, speed clamps) because a **guessed inertia axis pairing caused flip explosions**.

**The fix (staged flip):** (1) close the RE ambiguities that made the sim untrustworthy, producing bit-exact
golden fixtures; (2) harden the sim per those findings; (3) rewrite the controller so the **retail sim is
authoritative for NPC pose** (path system demoted to producing driver inputs); the old hybrid stays as a
config-selectable `Kinematic` fallback tier.

---

## 2. Where to work & how to build/test

- **Worktree (do ALL work here):**
  `C:\Users\josh\Documents\GitHub\AutoCore\.worktrees\feature-NPC-Retail-Driving`
  Branch: **`feature-NPC-Retail-Driving`**. Current HEAD: **`1282915b`** (task C3).
  - ⚠️ **Do NOT touch the main checkout** `C:\Users\josh\Documents\GitHub\AutoCore` (it's on `master` with
    unrelated unmerged death/respawn WIP). A shell `cd` for unrelated commands can drift you there — always
    `cd` back into the worktree before git/file ops, and prefer absolute worktree paths.
- **Build/test (MSTest, .NET):**
  `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj` (run from the worktree root). Full suite
  takes several minutes — use a 10-min timeout. While iterating, filter: `--filter <TestClassName>`.
  - A running AutoCore Launcher / Visual Studio locks `AutoCore.Launcher` DLLs and breaks the build — test the
    `AutoCore.Game.Tests` project individually (that's what all commands above do).
- **Test gate = ZERO NEW failures vs the recorded baseline.**
  **Baseline (at 56f2619): 2905 total, 1 pre-existing failure =**
  `AutoCore.Game.Tests.Managers.DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll`
  (inherited from master, NOT caused by this branch — do not chase it). The old "2 ReactionBoost failures" from
  earlier memory are FIXED on this base; ignore that stale note.
- **TNL.NET gotcha (only relevant if the worktree submodule is missing):** copy `lib/TNL.NET/` from the main
  tree into the worktree and `rm` the nested `.git` file. The worktree here is already built and working, so
  this shouldn't be needed.

---

## 3. Reference files you MUST read

| File | What it is |
|------|-----------|
| `~/.claude/plans/crystalline-plotting-crayon.md` | **THE PLAN.** Full task-by-task spec (Phases A–F), global constraints, dependency graph. Authoritative. Absolute path: `C:\Users\josh\.claude\plans\crystalline-plotting-crayon.md`. |
| `.superpowers/sdd/progress.md` (in worktree) | **THE LEDGER.** One line per completed task with commit SHAs + findings. **Trust the ledger + `git log` over any recollection.** gitignored local scratch. |
| `.superpowers/sdd/task-*-brief.md` and `task-*-report.md` | Per-task briefs (extracted plan text) and implementer/review reports. gitignored local scratch, same machine. |
| `docs/reconstruction/physics/README.md` | Index of the 14 Phase-0 RE evidence files + the "Open ambiguities" list the B-tasks resolve. |
| `docs/reconstruction/physics/verified/fn_*.md` | Per-function decompile evidence (addresses, struct offsets). |
| `docs/NPCDriving.md` | Original client-RE reference for the whole vehicle stack. |

---

## 4. How this work is being executed (subagent-driven development)

Follow the same process (skill: `superpowers:subagent-driven-development`):
1. Extract the task's brief (the plan text) to a file, dispatch a **fresh implementer subagent** per task with
   the brief + context + a report-file path. Specify the model explicitly (sonnet for most; the cheapest tier
   for mechanical transcription).
2. Implementer does TDD, commits, self-reviews, writes a report file, returns a <15-line status.
3. Generate a review package (`git diff` for the task's commit range to a file) and dispatch a **task reviewer
   subagent** (spec compliance + code quality). Scrutinize named risks (e.g. circular oracles, passing-by-weakness).
4. Dispatch a fix subagent for Critical/Important findings; re-review the fix diff.
5. Append a one-line completion note to the ledger (`Task N: complete (commits X..Y, review ...)`), mark the
   task done. Roll up Minor findings for the final review.
- Helper scripts: `C:\Users\josh\.claude\plugins\cache\claude-plugins-official\superpowers\6.1.1\skills\subagent-driven-development\scripts\` has `task-brief` and `review-package`. **NOTE:** `task-brief` only matches numeric task headings; our tasks are letter-prefixed (B1, C4…). Extract briefs with this awk instead:
  ```bash
  awk -v n="C4" '/^```/{f=!f} !f && /^#+[ \t]+Task[ \t]+[A-F]?[0-9]+/{t=($0 ~ ("^#+[ \t]+Task[ \t]+" n "([^0-9A-Za-z]|$)"))} t' \
    ~/.claude/plans/crystalline-plotting-crayon.md > .superpowers/sdd/task-C4-brief.md
  ```
- Do NOT run two implementer subagents in parallel in this same worktree (they race on git commits).

---

## 5. Completed work (committed; do not redo)

Commit chain on the branch after the base `56f2619`:

| SHA | Task | What it established |
|-----|------|--------------------|
| `16386d5c` | A1 | Baseline recorded; `tmp/` gitignored. |
| `0bf5b2c5` + `d6ceb4b5` | B6 | **Aero** `hkDefaultAerodynamics::update` `0x64dae0`: `aero_goldens.json` 8/8 bit-exact vs `HkVehicleAerodynamics.ComputeForce`. Confirmed `body+0x2c` = inverse-mass (emulation register readback) and front/up axis assignment (disassembly). Fx/Fy/Fz goldens are decompile-derived (emulator couldn't read the memory-resident output) → **live CE capture of Fx/Fy/Fz at `0x64dae0` RET is a nice-to-have follow-up.** |
| `91bc1197` | B7 | **Steering** `hkDefaultSteering_update` `0x64f840`: `steering_goldens.json` 8/8 genuinely emulation-captured bit-exact (used a `wheelCount=0` early-exit to read the computed angle from XMM0 at RET). **DEVIATION FOUND:** port guard `HkVehicleSteering.cs:127 (forwardSpeed > 0f)` returns a non-NaN identity where retail computes `0/0 = NaN` (bits `0x7fc00000`) on the degenerate `FullSpeedLimit=0, speed=0` input. That vector is `[Ignore]`d — **decide fidelity-vs-safety in the C-phase.** |
| `54ad0ea5` | B8 | **Brake** — see §6, this is a plan-changing finding. |
| `ab25d78c` + `78d9280b` | E1 | **Retail-parity characterization suite** `RetailParityTests.cs` — see §6/§8. 5 active pass + 3 `[Ignore]`d contracts that become the real gates after C-phase. |
| `1282915b` | C3 | **Anti-sink** rewritten to retail form in `VehicleActionSim.ApplyAntiSink`: scan min wheel `CurrentLength`; if `< 0`, lift chassis `PosY -= min` (position-only, **preserves LinVelY**). Old contact-vs-hardpoint penetration heuristic was provably dead code for upright bodies; deleted. Implemented from **static** decompile evidence with a "B2 debugger-confirm pending" caveat in the code comment. |
| `a41c2eb7` | **B4 + C1** | **Inertia axis pairing RESOLVED by live CE capture** and fixed. Live rb `0x3b0c3940`: body-frame principal inv-inertia at `rb+0xe0` = `(1/4500,1/4500,1/1500,1/1500)`, mass `1500`; chassis basis (live `vehicleData=*(fw+0x10)`) front=+Z, up=+Y, side=−X; DB def (`0x2c97e6e0`) `RVInertiaRoll=1/Pitch=3/Yaw=3`. → `mass*RVInertia` maps **X←Pitch, Y←Yaw, Z←Roll** (forward/Z = low roll inertia). `CreateBody` had **Roll↔Pitch swapped**; fixed. Also proves asset tensor = `mass*RVInertia`, and `rb+0x2c=1/1500` corroborates B2 mass-normalization. Evidence → `0.2-mass-inertia.md §2.1`. |
| `a65849ab` | **C6** | **ReGround** primitive `VehiclePhysicsInstance.ReGround(query)`: cast down from `PosY+10`, snap to hit, zero lin/ang vel + force/torque, `SetDriveAxes(0)`, reset wheel state (mirrors `airStabilization 0x598320` §3.2/§3.3). Wires the formerly-unused `ComputeReGroundCastStartY`/`ResolveReGroundPositionY`. New const `ReGroundCastMaxDistance`. The D-phase spawn/teleport/out-of-world primitive. |
| `6308e4fb` | **B2** | **Suspension** — confirmed `gScale=1/(RB+0x2c)=mass` (fresh `0x64de50` decompile + B4 live `RB+0x2c=invMass`); hardpoint impulse via `postTick` `applyPointImpulse` at `wheel+0x20` (not COM) → C2; anti-sink `0x598650` step 3 = position-only Y lift, no velocity write → confirms C3 (caveat removed). Decompile-derived `suspension_goldens.json` (8 vectors) + `SuspensionOracleTests.cs` (3 pass; 7 bit-exact vs port, 1 pins the `MaxSuspensionForce=80` clamp deviation C2 removes). Doc `0.4-suspension.md`. |
| `f89826a5` | **B1** | **Friction solver goldens** — live CE, 5 driving scenarios (rest/launch/cruise/turn/slide) captured at the solver-call return `0x64c9b2` (setup `fw+0x1fc`, cb, out `fw+0x2cc` off ESP). `frictionSolver_goldens.json` + `FrictionSolverOracleTests.cs` (3 active pass + 1 `[Ignore]` C4 target). RESOLVED: long/lat binding (`out[0]`/`out[2]` zero under grip, active only saturated); setup block is static per-vehicle (16-entry friction LUT + softness); `circleProjection 0x6c3f90` decompiled (iterative ellipse projection). Doc `0.3-friction-solver.md` "Live capture". |
| `e15897d9` | **B3** | **`wheel+0x88` = per-wheel drive-torque CONTACT GATE** (live CE, fresh spawn): `1.0` grounded (bit-exact, all wheels, ~135k samples), `0.0` airborne (preUpdate no-contact branch). Rewritten each `preUpdate` (store `0x64D2F7`); framework vtbl `+0x24`=`0x51e900` is an empty `ret 0xC` no-op. `postTick`: `drivePack += wheels+0x28[i] * wheel+0x88 / axleCount`. Torque ratio is separate (retail `calcWheelTorque`→`wheels+0x28[i]`); port folds `tRatio` into `DriveScale` as interim until `calcWheelTorque` ported (C5). Corrected wheel walk: `wheel[i]=*(container+0x80)+i*0xC0`. Pow half already static (`fn_00598040_uprightPow.md`). Doc `fn_wheel_driveScale_0x88.md`. |

Existing oracle fixtures in `src/AutoCore.Game.Tests/Physics/oracles/`: `aero_goldens.json`, `steering_goldens.json`,
`torqueCurve2D_goldens.json`, `driveController_goldens.json` (+ their `*OracleTests.cs`).

---

## 6. KEY FINDINGS that change the plan (read these carefully)

1. **BRAKE IS TICKED, not vestigial (B8).** The prior belief ("no service-brake torque") was a vtable-dispatch
   false negative. Verified chain (Ghidra byte-checked): `applyAction 0x598650` → `tickSubsystems 0x636a60`
   dispatches 7 children via vtbl+0x14; slot **`fw+0x24`** → **`hkDefaultBrake_update 0x64e6f0`** (vtable
   `0x9e4cb8 +0x14`). Pedal = **reverse component of the throttle axis** via `hkDefaultAnalogDriverInput_calcStatus
   0x5fe520`. Output torque `brake+0x10[i]` is consumed by `postTickApplyForces 0x64bc70` into the friction-solver
   input; the lock flag `brake+0x1c[i]` is consumed by `preUpdate 0x64cf20` to zero wheel spin.
   → **New task C8** was added (port the real brake update; after C4). **The D-phase driver must NOT double-apply
   reverse-throttle deceleration** — the old "no service brake" assumption is dead; check `VehicleDriveController`
   / engine reverse handling for overlap.

2. **Friction longitudinal term cancels ABSOLUTE chassis speed, not wheel-relative slip (E1).** In
   `VehicleActionSim.cs:427`, `slipLong = body.LinVelX*fwdX + …` is the absolute chassis velocity along forward,
   fed unconditionally into the solver → **any seeded speed decays to ~0.03 m/s within ~0.6 s regardless of
   throttle.** This is the **root cause of the historical "crawl"** and is exactly what **C4** must fix.

3. **Steering NaN deviation (B7)** — see §5. Decide in C-phase.

4. **Damper false-compression on sloped normals (E1).** Closing-speed uses `dot(chassisLinVel, contactNormal)`,
   so a tilted normal at speed reads as false compression and saturates `MaxSuspensionForce=80` — relevant to
   **C2/C3**.

5. **Sim deviations from retail already documented in `VehicleActionSim.cs`** (the C-phase targets): suspension
   applied as **COM force** instead of hardpoint point-impulses (~line 276); friction axle impulses applied **at
   COM with r×F disabled** ("flip explosions under unit mass" — line ~565, ~612 — almost certainly the guessed
   inertia pairing, which **C1/B4** fixes); a **gravity-share normal-load floor** (~line 482); `MuSlope=0,
   MuMax=mu0` placeholder friction-curve inputs; `MaxSuspensionForce=80` clamp; linear/angular speed clamps in
   `HkRigidBody.Integrate`.

6. **Retail has NO soft-pull, NO ramp-lip special case, NO launch-Vy** — air emerges naturally from per-wheel
   raycast misses. Never reintroduce heuristic vertical terms.

---

## 7. The debugger situation → Cheat Engine (why, and current state)

**Ghidra's debugger is unusable in this environment** — don't try it:
- The raw pybag backend (`python -m debugger`, port 8099) does **memory reads** fine but its `set_breakpoint`/
  `trace_function` fail "Module not in address map" (empty map; it mislabels the main module as `vorbisfile`),
  **and attaching FREEZES the game** (dbgeng stop-mode).
- The Ghidra-trace backend (`_2` tools, `static_to_dynamic`, port 8089) reports "Debugger not active, could not
  auto-start Debugger tool" — even `debugger_launch_offers` fails.

**Pivot (user-directed): use the Cheat Engine MCP bridge** — CE attaches with hardware breakpoints / DBVM and
does **not** freeze the game.
- Installed: `git clone https://github.com/miscusi-peek/cheatengine-mcp-bridge` →
  `C:\Users\josh\Documents\GitHub\cheatengine-mcp-bridge`. Server code reviewed = clean FastMCP stdio + named-pipe
  via pywin32. **Leave `CE_MCP_ALLOW_SHELL` UNSET** (its `run_command`/`shell_execute` tools are arbitrary-code-exec).
- MCP server registered as **`cheatengine`** in `C:\Users\josh\Documents\GitHub\AutoCore\src\.mcp.json` (reuses
  the hermes venv Python 3.11 that already has `mcp`+`pywin32`; `py_compile` OK).
- CE is installed at `C:\Program Files\Cheat Engine\cheatengine-x86_64.exe`.

**⚠️ THE BLOCKER that ended the background session:** this session runs as a **background job**, and background
jobs **cannot open the interactive `/mcp` approval UI**. Project-scoped `.mcp.json` servers need a **one-time
interactive approval** before their tools load, so the `cheatengine` tools never became available here (ToolSearch
for `ping`/`open_process`/`read_integer` found nothing). `/mcp enable` said "already enabled"; `/mcp` said "Can't
open MCP settings in a background session." **This is why the work is being handed to an interactive agent.**

> **UPDATE 2026-07-16 (resume session) — root cause was deeper + two working paths found:**
> 1. **The registration was in the wrong file.** Claude Code reads project `.mcp.json` from the **project root**
>    (`AutoCore\.mcp.json`), NOT `src\.mcp.json`, so the `cheatengine` server was never even *discovered* —
>    approval was never the whole story. Fixed by registering at **user scope** (like `ghidra-mcp`):
>    `claude mcp add --scope user cheatengine -e CE_MCP_TIMEOUT=60 -- "<hermes-venv-python>" "<repo>\MCP_Server\mcp_cheatengine.py"`.
>    That writes `~/.claude.json` and connects on the **next** session start (`claude mcp list` shows ✔ Connected).
>    `src\.mcp.json` left as-is (harmless).
> 2. **You don't have to wait for a session restart.** The CE Lua bridge is a plain named pipe
>    (`\\.\pipe\CE_MCP_Bridge_v99`). Talk to it directly *right now* with `tmp/re/ce_client.py` (gitignored) —
>    see **`tmp/re/CE_API_NOTES.md`** for the framing protocol, method list, attach steps, sanity constants, and
>    the breakpoint/XMM-capture patterns. This is what the resume session used to run the B-tasks without a restart.
> **Confirmed live this session:** attached PID 48600, sanity constants read (`0x9cc798`=`FF FF EF 41`,
> `0xaf3388`=`00 00 A0 41`), disassembly at `0x6c4450` = `55 8B EC …`. 1:1 static map holds.

**What the receiving (interactive) agent needs to confirm first:**
- The `cheatengine` MCP server is connected and its tools are visible (search ToolSearch for `open_process`,
  `read_memory`, `set_breakpoint`, `dissect_structure`, `get_process_list`, `read_pointer_chain`, `aob_scan`).
  If not, approve it via `/mcp` in the interactive terminal (persists to `.claude/settings.local.json`, e.g. an
  `enabledMcpjsonServers` entry), then reconnect.
- **CE-side setup (may already be done):** CE running; **Settings → Extra → "Query memory region routines"
  DISABLED** (README CAUTION: prevents `CLOCK_WATCHDOG_TIMEOUT` BSODs); the Lua bridge
  `C:\Users\josh\Documents\GitHub\cheatengine-mcp-bridge\MCP_Server\ce_mcp_bridge.lua` loaded (CE log shows
  `[MCP v12.0.0] MCP Server Listening on: CE_MCP_Bridge_v99`); CE attached to `autoassault.exe` via CE's
  Open Process (or the bridge's `open_process` tool). As of writing: CE was running (PID 86492) with the Lua
  bridge loaded and "waiting for connection"; the game `autoassault.exe` was PID 48600 earlier but **the PID may
  have changed** — re-check with `get_process_list`.

---

## 8. Cheat Engine usage guide (critical mechanics for the RE captures)

- **Address mapping is 1:1.** `autoassault.exe` is a fixed-base image at **`0x00400000`**, identical to the
  Ghidra image base, with no ASLR on the main module (verified across two PIDs). So a Ghidra address like
  `0x6c4450` **is** the live runtime address — read/breakpoint it directly. (Sanity-check any CE read against
  known constants: `0x9cc798` = `0x41EFFFFF` (float 29.9999998, substep cap); `0xaf3388` = `0x41A00000`
  (float 20.0, steering speed factor). These were confirmed live.)
- **Attach:** `open_process` to `autoassault.exe` (or by PID from `get_process_list`). Use `ping` to confirm
  `{"success": true, ... "CE MCP Bridge Active"}`.
- **Breakpoints:** use **hardware** breakpoints (`set_breakpoint` / `set_data_breakpoint`). For an every-frame
  function like the friction solver, CE's hardware BP + `dissect_structure`/`read_memory` at the hit lets you
  snapshot the input structs, then continue — **without the full freeze** Ghidra caused. For per-wheel writer
  discovery use a **data/access breakpoint** on the target field.
- **CE tools you'll lean on:** `read_memory`, `read_integer`, `read_float`(if present)/interpret hex,
  `read_pointer_chain` (`[[base+0x10]+0x20]`), `dissect_structure` (auto field types), `get_rtti_classname`
  (identify C++ objects), `disassemble`/`analyze_function`, `set_breakpoint`/`set_data_breakpoint`,
  `find_references`/`find_call_references`, `pause_process`/`unpause_process`.
- **Capture floats as raw little-endian hex** for bit-exact goldens (see §10).
- **Finding the live player vehicle object** (needed for B2/B4 struct reads): easiest is to set a breakpoint at
  the subsystem tick (`0x6c4450` friction, or `0x64de50` suspension, or `applyAction 0x598650`) while driving —
  the `this`/param pointers at the hit ARE the live vehicle/solver structs. Alternatively pointer-scan from a
  known global. Drive the player vehicle so its state is distinguishable from idle NPCs.

---

## 9. Immediate next action (start here)

**Fastest path to CE (no session restart needed):** use the direct named-pipe client
`tmp/re/ce_client.py` (gitignored) — see `tmp/re/CE_API_NOTES.md`. It speaks the CE Lua bridge's
framed JSON-RPC directly, so you can drive breakpoints/reads even if the `cheatengine` MCP tools
aren't loaded this session. (The MCP server is now registered at **user scope** in `~/.claude.json`
and loads on the next start; ToolSearch `open_process`/`ping` confirms it.)

1. Attach: `open_process {"process_id_or_name": "autoassault.exe"}` (PID was 48600), then `ping`.
2. Verify the 1:1 map (`0x9cc798`→`41EFFFFF`, `0xaf3388`→`41A00000`) — both re-confirmed 2026-07-16.
3. **B3 ✓, B4 ✓, C1 ✓, C6 ✓ are DONE** (see §5). Resume the remaining captures — all now **unblocked**
   (CE live). Note the wheel-array walk is `wheel[i]=*(container+0x80)+i*0xC0`, `container=*(fw+0xc)`,
   `fw`=ECX at `postTick 0x64bc70` / `applyAction 0x598650`:
   - **B5** — airStab recovery: needs a **flip/crash** (user drives into it). The only remaining B-task.
   Then C2 (hardpoint impulse + remove `MaxSuspensionForce` clamp, per B2) → C4 (un-`[Ignore]` the B1
   `PortSolve` + E1 at-speed tests as it lands) → C5 → C8(after C4), D1→D2→D3, C7(last), F.
   Full graph: **A ✓ → B1✓,B2✓,B3✓,B4✓,B5 → C1✓→C2→C3✓→C4→C5→C6✓→C8 → E1 ✓ → D1→D2→D3 → C7 → F.**
   B1 capture harness (reusable): Lua peak-latch handler installed via `evaluate_lua` — see
   `tmp/re/b1c.py` and `CE_API_NOTES.md`; snapshots solver structs synchronously at a BP.

Branch HEAD is now `a65849b`.

---

## 10. Oracle / golden-fixture pattern (use for every B-task fixture)

Match the current best pattern (from B6/B7 — NOT the older decimal-only torqueCurve2D one):
- Fixture JSON `src/AutoCore.Game.Tests/Physics/oracles/<name>_goldens.json`: each vector has `name`, `inputs`,
  `expected`; **every float stored as decimal AND a `*Hex` raw little-endian float32 hex string.**
- Oracle test `*OracleTests.cs`: parse each `*Hex` (`Convert.FromHexString` → `BitConverter.ToSingle`), assert
  (a) hex↔decimal **self-consistency** and (b) the C# port's computed output equals the hex-decoded expected
  **bit-exact** via `BitConverter.SingleToInt32Bits` (no float tolerance). Use reflection to resolve the port
  method and `Assert.Inconclusive` if the type is absent, like the existing oracle tests.
- **Avoid circular oracles:** expected values must come from the retail function (emulation register readback, or
  hand-derived from the *disassembly*), NEVER computed by the C# port under test.
- If a vector fails against the current (unhardened) port, that's a **success** (you found a real deviation):
  mark it `[Ignore("unblocked by C4")]` (or the relevant C-task) and document the deviation — do NOT change the
  port in a B-task.
- With CE you can now get the **genuinely runtime values** the static-only approach couldn't (per-frame solver
  in/out, constructed inertia tensor). Prefer live CE capture for those; keep static emulation for pure-math kernels.

---

## 11. Remaining tasks — full spec (in dependency order)

The plan (`crystalline-plotting-crayon.md`) has the authoritative detail; this is the actionable summary with the
RE targets inlined so you don't have to cross-reference.

### RE captures (Phase B) — via Cheat Engine live + Ghidra static

**B1 — Friction solver (highest impact: crawl/slide/turn-drift).** Targets: `hkVehicleFrictionSolver::solve
0x6c4450` (~2.9 KB), aggregation `postTickApplyForces 0x64bc70`, `circleProjection` helper (near `0x6c3f90`).
Resolve: which jacobian row / out-slot is **longitudinal** (→ wheel `+0x94`) vs **lateral** (→ wheel `+0xa0`);
softness composition (`setup+0x44/+0x54/+0x94`); circleProjection L2 vs component-wise renorm; `driveMax` field
identity; `cb+0xa0` lateral cutoff. Struct map: `param_2` = per-axle setup block `fw+0x1fc` (2×0x64);
`param_3` = chassis solver body `cb+0x20..0x128` (jacobian rows, invMass diag `+0xe0`, inv-inertia `+0x100`);
`param_4` = out block `fw+0x2cc` (2×0x1c). Wheel stride 0xC0: `+0x28` torques, `+0x88` drive scale, `+0x30`
contact-normal basis. Scenarios (one golden each): rest→full-throttle launch; mid-speed straight; steady turn;
handbrake slide; a front-drive vs rear-drive vehicle (binds `driveMax`). Deliver `frictionSolver_goldens.json`
(+ `postTick_goldens.json`) + `FrictionSolverOracleTests.cs` (`[Ignore]` until C4) + update
`docs/reconstruction/physics/0.3-friction-solver.md`. Commit `re(physics): friction solver goldens + doc (B1)`.

**B2 — Suspension gScale + hardpoint impulse + anti-sink confirm (float-height symptom).** Target
`hkDefaultSuspension::update 0x64de50`. Confirm `gScale = 1/RB[+0x2c]` (read `RB+0x2c`; is it 1/assetMass?);
component arrays `fw+0x28` restLen, `+0x44` strength, `+0x50/+0x5c` comp/ext damping; `wheel+0xB0` currentLength,
`+0xB4` closingSpeed; output `fw+0x28→+0x34`. Confirm the `0x64bc70` suspension apply is `F·dt·n̂` at **hardpoint**
`wheel+0x20` via `applyPointImpulse` (NOT COM). Confirm retail anti-sink (`0x598650` step 5) writes `pos.y` with
**no LinVelY zeroing** (validates C3). Deliver `suspension_goldens.json` + `SuspensionOracleTests.cs` + update
`0.4-suspension.md`. Feeds C2 + confirms C3.

**B3 — calcWheelTorque pow + wheel+0x88 writer (crawl magnitude).** Target `calcWheelTorque 0x598040`.
`disassemble` to find the `_CIpow` site; capture the x87 operands (expected base = |upDot|, exp = 4.0 — verify).
Per-wheel torque writes `wheels+0x28[i]`. Use a **data breakpoint on `WHEEL0+0x88`** during vehicle creation
(`buildFramework 0x5fd390` / wheels builder `0x5fcce0`) to find the drive-scale writer → decompile it → replaces
the provisional `TorqueRatio` mapping. Deliver `calcWheelTorque_goldens.json` + update `engine-torque-spec.md`
and `setup-field-mapping.md`. Feeds C5.

**B4 — DONE (commit `a41c2eb7`, live CE).** Result: front=+Z/up=+Y/side=−X; body-frame tensor
`(Ix=4500,Iy=4500,Iz=1500)` = `mass(1500)*(Pitch=3,Yaw=3,Roll=1)` → **X←Pitch, Y←Yaw, Z←Roll**;
`CreateBody` had Roll↔Pitch swapped, now fixed (C1 also done). COM stays modifier-only. Original spec
(retained for reference — *turn-drift; root cause of the flip-explosion that made r×F be disabled*):
After construction (`vehicleDataInit 0x5fc620` / `buildFramework 0x5fd390` tail), read the constructed chassis RB
**inverse-inertia tensor + COM** from live memory and compare per-axis to the vehicle's DB `RVInertia{Roll,Pitch,Yaw}`
to prove the pairing currently guessed in `VehiclePhysicsInstance.CreateBody` (guess: X=Roll, Y=Yaw, Z=Pitch) and
whether `CenterOfMassModifier` lands in the RB. Cross-check `RB+0x2c` vs B2. Update `0.2-mass-inertia.md`
(+ optional `inertiaPairing_goldens.json`). **Prerequisite for C1 → C2/C4.** This is a one-shot struct read — very
doable with CE `dissect_structure` on the RB pointer captured at a tick breakpoint.

**B5 — airStabilization recovery + re-ground (feeds C6 spawn/teleport recovery).** Target `airStabilization
0x598320`. Drive a car into a flip/crash; capture recovery gating (6400 ms collision window), the `pos.y + 10`
(`DAT_00a110d8`) raise, and the `CVOGMap_CastTerrainHeight 0x4cfe60` result write; note whether recovery zeroes
velocity / resets drive-axes. Deliver `airStab_goldens.json` + update `avd-airstab-spec.md`. Feeds C6.
→ **FULL SELF-CONTAINED CAPTURE PLAN: [`B5-airstab-capture-plan.md`](reconstruction/physics/B5-airstab-capture-plan.md)**
(all addresses/offsets/constants, the crash-recovery latch method, the open in-window-impulse item, and the
deliverables — everything the next agent needs). Requires the user to drive a crash; low priority.

**ASSET-MASS EXTRACTION — load real chassis mass/COM/inertia from `physics.glm` (NEW, high value).**
Supersedes the "mass=1.0" constraint. The server already has all client files + `GLMLoader`
(`Managers/Asset/GLMLoader.cs`), and `SimpleObjectSpecific.PhysicsName` (clonebase UTF-16 off 65) names the
physics asset. GLM format (from GLMLoader): trailing int32 → `CHNK` header (`opts[0]=66`, `opts[1]=76` LE)
→ `strTableOff/strTableSize/entryCount`; null-separated name table; 18-byte entries
(`Offset,Size,RealSize,ModifiedTime:int32, Scheme:int16, +4 skip`). **`Size != RealSize` ⇒ compressed**
(check `Scheme`; GLMLoader.GetStream currently reads raw `Size` bytes — may need a decompressor). **TODO:**
(1) open `physics.glm`, list entries, confirm a vehicle `PhysicsName` resolves to an entry; (2) parse the
serialized **Havok 2.3 `hkpRigidBody`** for `chassisMass`/`centerOfMass`/`inertiaTensor` (reflection type
strings `chassisMass`@0x9e7344, `inertiaTensor`@0x9dc6c8, `centerOfMass`@0x9dc6b8 exist in the client;
Havok packfiles usually embed type names — search the blob); (3) validate against B4/B1 live reads (mass
`1500`, inertia `[4500,4500,1500]` / `[90000,4500,6000]`, and `I = mass×RVInertia`); (4) wire into
`HkVehicleData.FromVehicleSpecific` (replace `UnitMass`), keeping unit-mass as the parse-failure fallback.
Feeds C1 (real inertia), C2/C4 (real force magnitudes), and F.

### Sim hardening (Phase C) — TDD: oracle/parity test red → change → green. All in `src/AutoCore.Game/Physics/Vehicle/`.

**C1 — Inertia pairing + COM (from B4).** Fix `VehiclePhysicsInstance.CreateBody` axis mapping + COM offset.
Test `VehiclePhysicsInstanceTests.cs`. **Do first — C2/C4 need correct r×F response.**

**C2 — Suspension → retail hardpoint point-impulses (from B2).** Replace the COM `ApplyForce` (pass 2, ~line 276
of `VehicleActionSim.cs`) with per-wheel `body.ApplyPointImpulse(F·dt·n̂, hardpointWorld)`. Remove/gate the
`MaxSuspensionForce=80` clamp (final removal blessed by the parity suite). Files `HkVehicleSuspension.cs`,
`VehicleActionSim.cs`, `HkPhysicsConstants.cs`; tests `VehicleActionSimTests.cs`, `VehiclePhysicsStabilityTests.cs`.

**C3 — DONE** (anti-sink). B2 will confirm the static evidence it was built on.

**C4 — Friction solver retail-exact (from B1) — biggest fidelity lever.** In `HkVehicleFrictionSolver.Solve`:
full `J·M⁻¹·Jᵀ` with softness, coupled 2×2, exact `circleProjection`, `cb+0xa0` lateral cutoff, verified `driveMax`.
In `VehicleActionSim.TryApplyFriction`: **remove the gravity-share load floor** (~line 482; retail |N| = aggregated
suspension impulse), feed **real `mu0/MuSlope/MuMax`** from the wheel friction table (drop placeholders), and
**re-enable r×F** by applying axle impulses at the per-axle averaged contact points (undo the COM-only stub ~line
612). **This fixes the absolute-speed slip-cancel (§6.2).** Tests `HkVehicleFrictionSolverTests.cs`,
`FrictionSolverOracleTests.cs`. Un-`[Ignore]` the E1 at-speed turn/downhill parity tests once green.

**C5 — Engine pow + drive-scale (from B3).** Correct the `pow` operand order in `HkVehicleEngine.cs` and the
`wheel+0x88` mapping in the `HkVehicleData` builder (+ `HkWheelSetup.cs`). Tests updated. Also decide the B7
steering-NaN fidelity-vs-safety guard here (or in C4/steering touch-up).

**C6 — Re-ground recovery primitive (from B5).** New `VehiclePhysicsInstance.ReGround(IVehicleCollisionQuery)`:
cast down from `PosY + 10`, write hit Y, zero lin/ang velocity, clear wheel state. Wire the existing
`HkVehicleAirStabilization.ComputeReGroundCastStartY`/`ResolveReGroundPositionY` helpers (currently unused). Test
`VehiclePhysicsInstanceTests.cs`. **This is the D-phase spawn/teleport/out-of-world primitive.** (Debugger-light —
core is static-derivable; B5 refines exact side-effects.)

**C8 — Port the ticked brake (from B8; AFTER C4).** Emulation/CE goldens for `hkDefaultBrake_update 0x64e6f0`;
port the update into `HkVehicleBrake.cs`; wire pedal-from-reverse-throttle, torque into the friction input path,
and lock-flag wheel-spin zeroing in the sim's preUpdate stage, matching retail tick order. **Ensure the D-phase
driver does not also apply reverse-throttle deceleration** (double-decel). Tests + `brake_goldens.json`.
Commit `fix(physics): port ticked hkDefaultBrake update (B8 finding)`.

**C7 — Retire server stability clamps (LAST, after the parity suite is green).** Remove/gate `MaxLinearSpeed`/
`MaxAngularSpeed` clamps in `HkRigidBody.Integrate`; delete/convert `Integrate_ClampsLinearAndAngularSpeed` and
`SuspensionForce_IsClamped`.

### E1 — DONE (parity suite). It is the acceptance contract for the C-phase.
`RetailParityTests.cs`: 5 active pass + **3 `[Ignore]`d contracts that must be un-ignored as C-tasks land**:
`ConstantRadiusTurn_AtSpeed_...` (unblock C4), `Downhill_..._AtSpeed_...` (C4), `RampExit_GenuineLiftoffAtLip_...`
(C2/C3/C4). The suite has an **independent analytic ballistic oracle** (verified to catch a 0.1× gravity error at
frame 2) — use it as the ramp/gravity gate.

### Authority flip (Phase D) — after C1–C6 (+ E green). Depends on the whole C-phase.

**D1 — Rewrite `NpcVehiclePhysicsControllerTests.cs` for sim authority.** Keep 4 (create/wait-hold/no-clonebase/
extract-basis), rewrite 2 (path-cruise / velocity-aligns with loosened tolerances asserting sim progress), DELETE
the 12 `IntegrateVertical_*` tests, add `Apply_PublishesSimPoseVerbatim`, `Apply_SpawnSeatsOnTerrain`,
`Apply_RecoversWhenBodyFallsOutOfWorld`, `Apply_DivergenceFromPath_TeleportsAndReGrounds`. Delete the 3 SoftPull*
+ ResolveRideHeight stability tests.

**D2 — Rewrite `NpcVehiclePhysicsController.cs` (572 → ~250 lines).** Delete `IntegrateVertical`, `TrySampleFrontRear`,
pitch-from-probes, the authored-pose write + **force-restore block (lines ~198–226)**, the 3 `SoftPull*` helpers,
`ResolveFootprint`/`ResolveRideHeight`/`PitchFromQuaternion`. New flow: guards + wait-hold → recovery (first-create
→ ReGround; non-finite / `PosY < supportY−50` / `|body.Pos − hard.NewPosition| > ResyncDriftThreshold` → SetPose
+ ReGround) → aim (`NpcVehicleDriveController.ResolveLookAheadAim`) → axes (`VehicleDriveController.ComputeAxes`,
the retail `0x4fc650` port, as the ONLY driver) → `inst.Step(...)` → **publish sim pose verbatim** (pos/rot/vel/
angVel from `inst.Body`; throttle/steer/sharp = axes). Delete the non-retail constants block in
`HkPhysicsConstants.cs` (`PathAirborneClearance`, `PathMaxStickSurfaceDrop`, `PathRampLipFrontDrop`,
`PathProbeHalfLength`, `PathSoftPull*`, `PathFallbackRideHeight`). Keep `TerrainCastWorldDownDot`.

**D3 — Lifecycle wiring.** `Vehicle.ClearPhysicsInstance()` currently has **ZERO call sites** — hook it into
teleport/`SetPosition`, respawn, and death paths (test: teleport → next `Apply` recreates + ReGrounds). Verify
`NpcTicker` wait-hold ghost-dirty branch and `ForeignNpcDriverWire`/ghost packing still behave.

### F — Verification & finish.
Per-task gate every time (zero new failures vs baseline). Then flip `serverConfig.yaml` →
`npcVehiclePhysics: {enabled: true, controllerTier: physics}` (rollback = `kinematic`), rebuild the Launcher from
the worktree, and run the **live Launcher checklist** (each run needs user approval): flat cruise, banked/constant-
radius turn, ramp (natural launch at lip), cliff drop, curb/step (anti-sink), high-speed S-curve — A/B vs
`kinematic`. Whole-branch code review. Update `docs/reconstruction/physics/README.md` Phase-7 row. Update the
project memory file `retail-npc-driving-branch.md`.

---

## 12. Open Minor roll-ups (fix during the final review, not blocking)

- `docs/reconstruction/physics/verified/fn_0064dae0_aero.md` ~line 238 still says "1e-4f tolerance" (stale after B6
  went bit-exact).
- B7: numpy cross-check transcript not preserved; the steering guard-divergence doc note isn't exhaustive.
- B8: consumption-site excerpts (`0x64bc70`/`0x64cf20`/`0x5fe520`) weren't independently re-verified.
- E1: `RetailParityTests.cs:363` has a dangling pointer to the gitignored `task-E1-report.md` — inline the value or drop it.
- C3: `hardX/Y/Z` scratch spans (`VehicleActionSim.cs:197-199, 212-214`) are now write-only dead stores — remove.
- B6 follow-up: live-CE capture of aero `Fx/Fy/Fz` at `0x64dae0` RET to replace the decompile-derived goldens.

---

## 13. Non-negotiable constraints (from the plan's Global Constraints)

- Physics tier stays **opt-in** (`controllerTier` OFF by default; instant rollback = `kinematic`).
- **TDD** for production physics changes. Zero new test failures vs baseline (§2).
- **No Launcher start/stop and no game-attach without user approval.** (CE attach was user-directed; keep it that way.)
- ~~Mass model locked: mass = 1.0, inertia = RVInertia (asset scalar mass is unavailable server-side).~~
  **REOPENED 2026-07-16:** the premise is wrong — the server has all client files + a GLM reader, and the
  real mass/COM/inertia are loadable from `physics.glm`. See the **Asset-mass extraction** task below and
  the CORRECTION box in `0.2-mass-inertia.md`. Unit mass is now the graceful *fallback*, not the target.
- Retail has **no soft-pull / ramp-lip / launch-Vy** — never reintroduce heuristic vertical terms.
- **Controller model (clarified 2026-07-16):** only the **path controller** is in scope. It should
  **continuously steer toward the next waypoint** (producing driver *inputs* — steer/throttle) and let the
  hardened physics sim move the car — i.e. **physics-based path-following, NOT snap-to-path**. This is the
  D-phase intent: the path system is demoted to an input source; `inst.Body` pose is authoritative. The old
  kinematic hybrid (`NpcVehiclePhysicsController` force-restoring the body to an authored pose) is what D2 replaces.
- Ghidra is READ-ONLY except bookmarks/comments (no renames/retypes). Do not attach Ghidra's debugger to the game.
- Keep `CE_MCP_ALLOW_SHELL` unset.
