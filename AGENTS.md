## Scripts

Reusable automation and reverse-engineering tools live under [`scripts/`](scripts/). See **[`SCRIPTS.md`](SCRIPTS.md)** for a full index (what each script does and how to find it).

**Placement rules:**

* Prefer **reusable** scripts whenever possible: CLI args instead of hardcoded IDs, shared path helpers, and a short plate comment at the top describing purpose and usage.
* Put **all reusable** scripts in **`/scripts`** and list them in **`SCRIPTS.md`** (keep descriptions to two sentences max).
* Put **temporary, one-time** probes and throwaway experiments in **`/tmp`** only. Do not leave one-offs in the repo root, `tmp-map/`, or other ad-hoc folders. Delete or promote them when done—if a one-off proves useful, promote it into `/scripts` with a plate comment and a `SCRIPTS.md` entry.

## Git worktrees and live servers

Agents often work from a **git worktree** (parallel checkout of another branch) instead of the main clone. Detect this early and treat server lifecycle carefully so instances do not conflict.

### Detecting a worktree

Treat the workspace as a worktree (not the primary checkout) if any of these hold:

* `git rev-parse --git-dir` and `git rev-parse --git-common-dir` resolve to **different** paths
* `.git` is a **file** (points at the main repo's worktree metadata) rather than a directory
* The path is under a known worktree root (e.g. `.worktrees/`, `.claude/worktrees/`, or a sibling folder listed by `git worktree list`)

Confirm with `git worktree list` when unsure. Note the current branch and path in your reasoning when live-server steps are involved.

### Building and launching servers from a worktree

The full stack (Auth + Global + Sector) is started via **`AutoCore.Launcher`**:

```text
src/AutoCore.Launcher/bin/Debug/net8.0/AutoCore.Launcher.exe
```

* **Unit/integration tests** (`dotnet test` on test projects): no Launcher required; build only the projects under test.
* **Live server / client repro from a worktree:** you must **build this worktree's** `AutoCore.Launcher` (or the solution that outputs into that Launcher directory) so the binaries you run match the branch under test. Do not assume the main checkout's Launcher is the correct build.
* A running Launcher **locks output DLLs** — stop it before rebuilding that tree's Launcher/solution output.

### Never start Launcher without explicit approval

Default configs share the same ports and databases across checkouts (e.g. Auth `2106`, Communicator `2107`, Global `26880`, Sector `27001`, shared MySQL DBs). **Only one Launcher instance can own those ports at a time.**

**Rules:**

1. **Do not** start, restart, or stop `AutoCore.Launcher` (or Auth/Global/Sector processes) unless the user **explicitly asks** or **approves** after you ask.
2. When live verification is needed from a worktree, **ask first**: confirm whether to stop any existing Launcher (often on the main checkout), build this worktree's Launcher, and start it — or whether the user will run servers themselves.
3. Prefer **ask → wait for yes** over auto-starting. If the user says to launch (or "start the servers", "run Launcher", etc.), then build Launcher in **this** workspace and start only that instance.
4. After work, do not leave an extra Launcher running unless the user wants it; if you started it with approval, offer to stop it when done.

### Practical sequence (only after approval)

1. Stop any existing `AutoCore.Launcher` (user-approved).
2. `dotnet build src/AutoCore.Launcher/AutoCore.Launcher.csproj` (or full `src/AutoCore.sln`) **in this worktree**.
3. Start Launcher **as a background shell task** by running `src/AutoCore.Launcher/bin/Debug/net8.0/AutoCore.Launcher.exe` from this worktree’s output directory (see below).
4. Point the client at this stack; when finished, stop Launcher before switching back to another checkout’s servers.


### How to start Launcher (background task — required)

When starting the server from an agent session (especially a **worktree**), run `AutoCore.Launcher.exe` as a **background** shell command (`background: true` / equivalent long-running task), not via fire-and-forget `Start-Process` that detaches without an agent-owned task id.

**Why:**

* Background tasks keep **stdout/stderr** available so you can confirm boot (ports, Auth/Global/Sector init, client login) and diagnose crashes.
* The agent can **poll or wait on** the task output and **stop** the process cleanly later (`kill` / task terminate) without hunting orphan PIDs.
* Detached `Start-Process` (or a short-lived shell that exits after spawning) often leaves a process that **dies when the agent shell ends**, or leaves no handle to inspect logs — that has already caused “server not running / cannot connect” failures.

**Pattern (after approval + build):**

```powershell
# cwd = this worktree's Launcher output
Set-Location src/AutoCore.Launcher/bin/Debug/net8.0
.\AutoCore.Launcher.exe
# Run that command with background: true so the task stays attached to the session.
```

Then verify before telling the user it is up:

* Task still **running**
* Listening: Auth TCP `2106`/`2107`, Global UDP `26880`, Sector UDP `27001` (and Dev API `27999` if enabled)
* Log lines such as “Listening for clients” / Communicator authenticated

Do **not** claim the server is ready until those checks pass. If the background task exits, read its log and restart only with approval (or if the user already asked to start/keep servers up).

## Ghidra MCP (reverse engineering)

Prefer **decompile** (`decompile_function`, `batch_decompile`) and **`read_memory`** for constants and formulas.

* **Avoid `disassemble_bytes`.** It frequently hangs, times out, or returns incomplete output and is a poor default for RE work.
* Do **not** rely on it for primary analysis. Reconstruct math from decompiler pseudocode + known `DAT_*` / float constants via `read_memory`.
* If disassembly is truly unavoidable (e.g. decompiler is clearly wrong on a tight FPU sequence), keep the request small and treat it as best-effort. The MCP `use_tool` path has **no** documented per-call timeout parameter; there is no reliable way to enforce a 2s cap from the agent tool layer today — so prefer not calling it at all.

## Engineering Standards

All code changes must follow TDD.

Before implementing a feature or fix:

* Write or update a test that fails for the intended behavior.
* Confirm the test fails for the correct reason.
* Implement the smallest production change needed to pass.
* Run the relevant test suite before considering the work complete.

Coverage requirements:

* Any new or modified production code must have meaningful test coverage.
* Target 90% or better coverage for touched files/modules.
* Do not satisfy coverage with shallow tests that only execute code without asserting behavior.
* If 90% coverage is not practical, document why and explain the remaining risk.

Code quality requirements:

* Keep functions small, focused, and single-purpose.
* Avoid broad rewrites unless explicitly requested.
* Keep cyclomatic complexity low; prefer extracting clear helper methods over deeply nested logic.
* Avoid hidden global state, static mutable state, magic numbers, and duplicated logic.
* Prefer explicit error handling over swallowed exceptions.
* Do not add empty `catch` blocks.
* Avoid blocking I/O on hot paths, tick loops, request handlers, packet handlers, or async flows.
* Avoid unbounded loops, unbounded queues, unbounded retries, and background tasks without cancellation.
* Keep domain logic at the correct ownership level; packet handlers and controllers should coordinate, not own core business rules.
* Preserve existing architecture unless there is a clear, tested reason to change it.

Completion requirements:

* New failing test was created first.
* Fix passes the new test.
* Relevant existing tests pass.
* Coverage impact is acceptable.
* Any remaining risks or skipped checks are documented clearly.

## Per-player map instancing (starting areas)

Continents **698 / 707 / 708** (Tierra Roja Dam, Hestia Ark Bay 313, The Wastes) are instanced:
every player entering gets a private `SectorMap` copy, created by
`MapManager.GetMapForCharacter` and disposed synchronously in `SectorMap.LeaveMap` when the
owner leaves (SS-30). The set lives in `AutoCore.Game/Map/InstancedContinents.cs`. Invariants:

* **Every sector host must call `InstancedContinents.EnableForSector()`.** There are two:
  `AutoCore.Sector/Program.cs` (standalone) and
  `AutoCore.Launcher/Bootstrap/DefaultLauncherGameBootstrap.InitializeMapManager` (the Launcher
  hosts Auth/Global/Sector in one process and never runs `Sector/Program.cs`). Miss one and the
  three maps silently fall back to shared — pinned by
  `DefaultLauncherGameBootstrapTests.InitializeMapManager_EnablesStartingAreaInstancing`.
  Standalone `AutoCore.Global/Program.cs` does **not** enable it and must not.
* Instancing is gated at the **call site**, not the flag: Global's new-character path uses
  `GetMap` (shared copy, only seeds `LastTownId`/pose); the sector entry paths use
  `GetMapForCharacter`. That is why the flag being on in the combined Launcher process is safe.
* Relog always lands in a **fresh** instance; persisted character state (position, journal,
  mission-derived world state) replays into it. Nothing instance-scoped is persisted.
* Instances of one continent mint **identical local COIDs**. Any singleton state keyed by COID
  must also key on `SectorMap.InstanceSerial` (see `TriggerManager`, `CharacterMapPresence`,
  `MapPropCorpseDespawn`, `VehicleMapPropRam`). Never compare maps by `ContinentId` to decide
  "same map" — compare references.
* Instance disposal must never write shared `MapData` templates (the `IsActive` restore is
  exclusive to the shared-map `ResetLocalWorldToAuthored`).

## Exception safety and crash resistance

The register of findings, fixes and accepted risk is **[`docs/exception-safety-audit.md`](docs/exception-safety-audit.md)**. Read it before changing an entry point, a loop, a packet handler, or anything that detaches work.

Structured logging / playtest observability (LG register, event catalog, NDJSON dual-write) lives in **[`docs/logging-observability-audit.md`](docs/logging-observability-audit.md)** and **[`docs/logging-event-catalog.md`](docs/logging-event-catalog.md)**. New first-class `GameLog` event names must be added to the catalog (`LogEventCatalogSyncTests` enforces drift).

### Use the shared primitives

`src/AutoCore.Utils/Reliability/` — do not hand-roll these:

| Need | Use |
|---|---|
| Isolate one unit of work at a boundary | `Guard.Run(operation, work)` |
| Isolate each item in a loop | `Guard.ForEach(items, operation, work, describe)` |
| Detach work from the caller | `SafeTask.FireAndForget(task, operation)` |
| Survive transient failures in a loop | `new BackoffPolicy(...)` |
| Process-global last-resort diagnostics | `CrashHandler.Install(subsystem)` (already wired in all four `Main`s) |

### Rules

* **Boundaries, not blankets.** A broad catch belongs at an architectural boundary whose job is to stop process termination — a tick stage, a per-entity loop, a packet handler, a command handler, detached work. Do not wrap ordinary business logic: handle expected failures specifically, close to the operation, and let unexpected ones reach a boundary.
* **Never add an empty `catch`.** If a failure is genuinely ignorable, log it at `Debug` and say why in a comment. The only sanctioned exception-swallowing site in the repo is `Logger.EmergencyReport`, and it is documented as such.
* **Detached work must be observed.** A bare `_ = SomethingAsync()` hides faults until GC. An exception escaping a `ThreadPool` callback or a thread entry point **terminates the process**.
* **Preserve the diagnosis.** Use `Logger.WriteException(type, operation, ex)` — it keeps the type, stack trace and inner-exception chain. `ex.Message` alone throws away everything that makes a production failure findable. Never `throw ex;` (use `throw;`); when wrapping, pass the original as the inner exception.
* **Retries are bounded.** No infinite retry loops. Use `BackoffPolicy`, and dead-letter with an Error log when the budget is exhausted. A bare `continue` on error is a 100%-CPU spin when the failure is persistent.
* **Treat peer and file input as hostile.** Validate lengths, opcodes and enum values *before* they become exceptions. Malformed input should reject the message and log at `Warning` — it is expected input at a boundary, not a server fault.
* **Let the unrecoverable escape.** `OperationCanceledException` (control flow) and `OutOfMemoryException` (unsafe to continue) propagate through `Guard` by design. Startup failures still terminate the process — diagnosably.
* **Clean up with `finally`/`using`.** Pooled buffers (`ArrayPool`), transactions and streams must be released on the failure path too.

### Severity ladder

`LogType.Debug` development detail · `Initialize`/`Network`/`Command` normal state · `Warning` recoverable abnormal condition · `Error` operation failed, process healthy · `Fatal` process or subsystem cannot continue.

Never log passwords, tokens, keys, or other credentials.

### Adding a finding

Continue the **`SS-nn`** series:

1. Allocate the next id and add a row to `docs/exception-safety-audit.md`.
2. Cite the id in the production comment explaining why the boundary exists.
3. Add a tripwire test whose XML doc names the id, then **revert the fix and confirm the test fails**. A guard with no failing test proves nothing.

### Static analysis

`src/Directory.Build.props` + `src/.editorconfig` enable .NET analyzers as **warnings only** — `TreatWarningsAsErrors` is off and `<Nullable>` is untouched, so analysis cannot break the build. Fix what you surface or record it in the audit; do not blanket-suppress. CA1031 (broad catch) is intentionally a *suggestion*, because flagging every deliberate boundary would train people to suppress it.


### Additional Instructions

If present, read [`PROMPT.md`](PROMPT.md) in it's entity. [MANDATORY]