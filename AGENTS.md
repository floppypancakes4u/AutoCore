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
* `.git` is a **file** (points at the main repo’s worktree metadata) rather than a directory
* The path is under a known worktree root (e.g. `.worktrees/`, `.claude/worktrees/`, or a sibling folder listed by `git worktree list`)

Confirm with `git worktree list` when unsure. Note the current branch and path in your reasoning when live-server steps are involved.

### Building and launching servers from a worktree

The full stack (Auth + Global + Sector) is started via **`AutoCore.Launcher`**:

```text
src/AutoCore.Launcher/bin/Debug/net8.0/AutoCore.Launcher.exe
```

* **Unit/integration tests** (`dotnet test` on test projects): no Launcher required; build only the projects under test.
* **Live server / client repro from a worktree:** you must **build this worktree’s** `AutoCore.Launcher` (or the solution that outputs into that Launcher directory) so the binaries you run match the branch under test. Do not assume the main checkout’s Launcher is the correct build.
* A running Launcher **locks output DLLs** — stop it before rebuilding that tree’s Launcher/solution output.

### Never start Launcher without explicit approval

Default configs share the same ports and databases across checkouts (e.g. Auth `2106`, Communicator `2107`, Global `26880`, Sector `27001`, shared MySQL DBs). **Only one Launcher instance can own those ports at a time.**

**Rules:**

1. **Do not** start, restart, or stop `AutoCore.Launcher` (or Auth/Global/Sector processes) unless the user **explicitly asks** or **approves** after you ask.
2. When live verification is needed from a worktree, **ask first**: confirm whether to stop any existing Launcher (often on the main checkout), build this worktree’s Launcher, and start it — or whether the user will run servers themselves.
3. Prefer **ask → wait for yes** over auto-starting. If the user says to launch (or “start the servers”, “run Launcher”, etc.), then build Launcher in **this** workspace and start only that instance.
4. After work, do not leave an extra Launcher running unless the user wants it; if you started it with approval, offer to stop it when done.

### Practical sequence (only after approval)

1. Stop any existing `AutoCore.Launcher` (user-approved).
2. `dotnet build src/AutoCore.Launcher/AutoCore.Launcher.csproj` (or full `src/AutoCore.sln`) **in this worktree**.
3. Start Launcher **as a background shell task** from this worktree’s output directory (see below).
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
