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
