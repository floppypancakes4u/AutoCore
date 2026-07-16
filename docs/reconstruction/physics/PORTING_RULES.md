# Porting rules — Ghidra is authoritative

Bit-exact NPC vehicle physics. Phase 0 evidence under this folder is a **map**, not a substitute
for re-reading the binary when implementing C#.

## Mandatory RE gate (every subsystem module)

Before writing production code for a module, the agent **must**:

1. **`decompile_function` / `batch_decompile`** the primary client function(s) for that module
   (addresses in `README.md` evidence index). Prefer decompiler pseudocode.
2. **`read_memory`** every float/int constant used in the formula (`DAT_*` / plate symbols).
   Confirm LE float32 / int values; do not trust renamed Ghidra symbols alone.
3. **Reconcile** decompile + memory against the matching `*.md` evidence file.
   - Match → implement from the verified formula.
   - Conflict → **binary wins**. Update the evidence file with the correction and a short note.
4. **Do not** use `disassemble_bytes` as the primary tool (hang-prone); only if decompile is clearly wrong on a tight FPU sequence, and keep the window tiny.
5. **`emulate_function`** when the function is pure enough (e.g. LUT lookup with controlled memory).
   If pointer-heavy and emulation is impractical, document why and use hand-derived goldens from the verified decompile (as with `torqueCurve2D`).

## Implementation rules

- Port the **actual algorithm** (casts, clamp order, sign, which axis, which wheel index test).
- Cite primary address(es) in the C# file header comment.
- Tests must encode golden vectors from RE (or emulation), not invented “reasonable” values.
- Unknowns (e.g. `_CIpow` upright falloff base/exp) stay flagged — do not invent curves.

## Collision geometry

Retail cast against **all** Havok bodies. Server v1 may use terrain heightfield + an
`IVehicleCollisionQuery` that can later add world geometry. Do not hard-code “terrain only”
into the math; inject the query interface.

## Quality over speed

Parallel agents are allowed and preferred for independent modules, but each agent must complete
the RE gate above. Unverified ports are rejected.

## Program

Ghidra program name: **`autoassault.exe`** (image base `0x400000`). Always pass `program` when
calling MCP tools if multiple binaries are open.
