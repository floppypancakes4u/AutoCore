## Scripts

Reusable automation and reverse-engineering tools live under [`scripts/`](scripts/). See **[`SCRIPTS.md`](SCRIPTS.md)** for a full index (what each script does and how to find it).

**Placement rules:**

* Prefer **reusable** scripts whenever possible: CLI args instead of hardcoded IDs, shared path helpers, and a short plate comment at the top describing purpose and usage.
* Put **all reusable** scripts in **`/scripts`** and list them in **`SCRIPTS.md`** (keep descriptions to two sentences max).
* Put **temporary, one-time** probes and throwaway experiments in **`/tmp`** only. Do not leave one-offs in the repo root, `tmp-map/`, or other ad-hoc folders. Delete or promote them when done—if a one-off proves useful, promote it into `/scripts` with a plate comment and a `SCRIPTS.md` entry.

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
