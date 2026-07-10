# Final fix wave — NPC AI branch review findings

Branch: `feature/npc-ai`  ·  Base HEAD before this wave: `476a1f2`

All three IMPORTANT findings fixed in one pass. TDD: each behavior fix has a test
that was confirmed RED against the un-fixed logic before the fix was restored GREEN.
Quality gate: zero new test failures relative to the known baseline (the two
pre-existing `ReactionBoostTests` failures remain, out of scope).

---

## Finding 1 — Shipped lever config silently enables the per-packet wire diagnostic

**Problem.** `WireDiag: true` in the three shipped `wire-isolation.levers.json` files, combined
with `WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles()` at boot in both `Program.cs`
entry points, turned on the per-packet wire diagnostic on every normal server start — a lock
acquisition, a 48-byte `Convert.ToHexString` allocation per S2C packet, a Network-level log line,
and a 2000-entry retained buffer on the 100ms MainLoop hot path for every game packet and
GhostVehicle pack. This contradicts the code's own documented production defaults
(`WireDiag`: "Off by default"; `WireIsolationLevers`: "Defaults are production-safe … diag off").

**Fix.** Set `"WireDiag": false` in all three JSONs:
- `wire-isolation.levers.json` (repo root)
- `src/AutoCore.Sector/wire-isolation.levers.json`
- `src/AutoCore.Launcher/wire-isolation.levers.json`

The env var (`AUTOCORE_WIRE_DIAG`) and the `wire diag on` console command remain for live
debugging, so no diagnostic capability is lost.

**Test evidence.** No test asserted the shipped file contents (the `WireIsolationLeversTests`
suite uses inline JSON strings), so no test change was needed. Suite stays green.

---

## Finding 2 — Duplicate `CreateVehiclePacket` on scope drop/re-add cycle

**Problem.** `SectorMap.PerformScopeQuery` sent a fresh `CreateVehiclePacket` every time a foreign
global vehicle transitioned from not-ghosted to scoped (gate `!ghost.IsGhostedTo(connection)`).
Nothing sends `DestroyObject` when TNL later kills the ghost, so a guaranteed-ordered create
persists in the client's object table across ghost-kill/re-scope cycles. A player who drives past
the scope-drop radius and returns therefore delivered a **second** in-world create for an object the
client still holds — the duplicate-create / "Invalid Packet" failure class. `GlobalVehicleScopeTests`
only covered re-scope while the vehicle STAYS scoped, never the drop/re-add cycle.

**Fix.** Made the create once-per-connection-per-map-session:
- `TNLConnection` gains a session-scoped `HashSet<long> _globalVehicleCreatesSent` and
  `TryMarkGlobalVehicleCreateSent(coid)` (returns true only the first time a coid is seen).
- `SectorMap.PerformScopeQuery` now gates the create on `TryMarkGlobalVehicleCreateSent(...)`
  instead of `!ghost.IsGhostedTo(...)`. A re-scope after a ghost drop no longer re-creates.
- The set is cleared in `TNLConnection.EnsureGhostsAndScopeAfterMapTransfer` (via
  `ClearGlobalVehicleCreateTracking()`), which runs right after `ResetGhosting()` on a map transfer —
  exactly when the client discards its local object table (`rpcEndGhosting` → `DeleteLocalGhosts`) —
  so the next map session legitimately re-sends creates.

No TNL submodule change: the reset is anchored in AutoCore's own map-transfer path rather than by
making the vendored `GhostConnection.ResetGhosting` virtual.

**Tests (new, in `GlobalVehicleScopeTests`):**
- `PerformScopeQuery_DropThenReScope_DoesNotResendCreate` — scope (1 create), `DetachObject` to
  simulate TNL killing the ghost (client keeps the object), re-scope → still exactly 1 create.
  Confirmed RED against the old `!ghost.IsGhostedTo` gate (produced 2 creates).
- `PerformScopeQuery_AfterMapTransfer_ResendsCreateForNewMapSession` — after a
  `ResetGhosting` + `EnsureGhostsAndScopeAfterMapTransfer` cycle the create IS re-sent (count 2),
  proving the session reset works.
- Existing `PerformScopeQuery_FirstGlobalNpcVehicle_SendsCreateBeforeRegisteringGhost` (stay-scoped
  re-scope, still 1 create) and the three lever tests remain green.

---

## Finding 3 — Delta owner-gating recomputed from current owner instead of what the client received

**Problem.** In `GhostVehicle.PackUpdate`, on non-initial packs `clientHasOwner = wouldPackOwner`
(owner != null *right now*), not whether the `CurrentOwner` block was actually written in THIS
ghost's initial pack. The documented live-A/B lever workflow (`wire set EnableOwnerWire false`
while ghosts are created, then GM/StateMask/AttributeMask deltas after flipping back to true) then
writes owner-applied bytes (GM nibble → owner+0x12A, AI state → owner+0x127, attribute payload) to a
client whose vehicle has no owner object — the exact null-owner access violation (0x005F8FED) the
method's own comment warns about.

**Fix.** Latched the fact on the ghost instance:
- New field `GhostVehicle._ownerSentAtInitial`, assigned `= packOwner` inside the `isInitial` branch.
- Delta gating now reads `clientHasOwner = isInitial ? packOwner : _ownerSentAtInitial`, so a delta
  only writes owner-applied bytes if the owner block was really on THIS ghost's initial wire, never
  recomputed from live lever/owner state.

**Tests (new, in `GhostVehicleWireTests`):**
- `PackDelta_AfterOwnerWireOffInitial_SuppressesGmEvenAfterLeverFlipsBackOn` — initial with
  `EnableOwnerWire=false`, flip back on, GM delta → GM flag stays false. Confirmed RED against the
  old `wouldPackOwner` gate (wrote the GM nibble).
- `PackDelta_AfterOwnerSentAtInitial_PacksGm` — initial with owner on → GM delta packs correctly.

**Updated existing delta-only tests** to reflect the corrected contract (owner block must have been
sent at initial before a delta may reference it): a preceding `PackInitial(vehicle)` was added to
`PackUpdate_StateMask_WritesDriverCombatState`,
`PackUpdate_NonInitial_CreatureDriver_AiStateRequiresOwnerPresent`,
`PackUpdate_NonInitial_AttributeMask_RequiresCharacterOwner`, and
`PackUpdate_NonInitial_GmMask_CharacterOwner`. All the negative (no-owner → suppressed) tests were
already correct and stay green.

**Residual note (see Concerns).** The latch is per-ghost-instance, and one `GhostVehicle` instance is
shared across connections in this TNL model. This fully removes the documented single-connection
foot-gun and is strictly safer than the previous code in every case; a purely exotic
multi-connection-simultaneous-lever-toggle edge remains, matching the reviewer's own "latch on the
ghost instance" prescription (a per-connection set was rejected to avoid leaking dead-connection
references on long-lived NPC ghosts).

---

## Test evidence (full)

- Covering tests (`GhostVehicleWire*`, `GlobalVehicleScope`, `MapTransferGhosting`,
  `NpcVehicleSafety`): 65/65 green.
- Full solution (`dotnet test src/AutoCore.sln`): Utils 14/14, Sector 5/5,
  Game 876 passed / 1 skipped / **2 failed** — both the pre-existing, out-of-scope
  `ReactionBoostTests.Boost_ActOnActivator_SucceedsWithoutUnhandled` and
  `ReactionBoostTests.Boost_EmptyObjects_SucceedsWithoutUnhandled` (missing Boost handler).
  Zero new failures relative to the `2435f8e` baseline.
- Each behavior fix verified RED-before-GREEN by temporarily reverting the fix and observing the new
  test fail, then restoring.
