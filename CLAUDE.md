# CLAUDE.md

**[`AGENTS.md`](AGENTS.md) is the single source of truth for how to work in this repository.**
Read it. This file is a short pointer plus the things that are expensive to get wrong, kept
deliberately thin so the two cannot drift.

## Do not skip

**Never start `AutoCore.Launcher` (or Auth/Global/Sector) without explicit approval.**
Every checkout shares the same ports and MySQL databases — Auth `2106`, Communicator `2107`,
Global `26880`, Sector `27001` — so only one instance can run at a time and starting one can
break whatever the user already has running. Ask first, every time. Details and the approved
start sequence are in AGENTS.md → *Git worktrees and live servers*.

**TDD is required.** Write the failing test first, confirm it fails for the right reason, then
make the smallest change that passes. Target ≥90% coverage on files you touch; document the gap
if that is impractical.

**Exception safety has a register.** Before changing an entry point, a loop, a packet handler, or
anything that detaches work, read [`docs/exception-safety-audit.md`](docs/exception-safety-audit.md)
and follow AGENTS.md → *Exception safety and crash resistance*. The short version:

- Use `Guard`, `SafeTask`, `BackoffPolicy` from `src/AutoCore.Utils/Reliability/` — do not hand-roll.
- Broad catches belong at boundaries, not around ordinary logic. Never add an empty `catch`.
- Log with `Logger.WriteException(...)`, not `ex.Message` — keep the stack trace and inner chain.
- Detached work must be observed; retries must be bounded; never log credentials.
- New findings continue the `SS-nn` series and need a tripwire test that fails when the fix is reverted.

**Avoid `disassemble_bytes` in Ghidra MCP.** It hangs and returns incomplete output. Prefer
`decompile_function` / `batch_decompile` and `read_memory`.

## Build and test

```powershell
dotnet build src/AutoCore.sln
dotnet test  src/AutoCore.sln

# One project, filtered
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj --filter "FullyQualifiedName~Mission"
```

MSTest, no mocking library — hand-written fakes in `Fakes/` folders. Analyzers run as warnings
only; the build cannot fail on analysis.

Known flake: `LauncherShutdownTests.Shutdown_DoesNotBindPorts` compares a machine-wide TCP
listener count and can fail when test assemblies run in parallel. It passes in isolation.

## Where things go

- Reusable scripts → `scripts/`, indexed in [`SCRIPTS.md`](SCRIPTS.md).
- One-off probes → `tmp/` only. Never the repo root.
- Docs index → [`docs/TOC.md`](docs/TOC.md).

## Additional Instructions

If present, read [`PROMPT.md`](PROMPT.md) in it's entity. [MANDATORY]