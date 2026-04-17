# Story 1.4: POC Baseline Measurement and Code Review Gate

Status: done

## Story

As a developer,
I want a documented baseline for connection time and first-response time from the POC run,
so that NFR-001 performance targets can be confirmed or revised, and credential handling is formally verified by code review before Phase 2 begins.

## Acceptance Criteria

1. Timing instrumentation is added to `DataverseDocAgent.Console/Program.cs`: elapsed time for Dataverse connection (from `ConnectAsync` start to `ServiceClient.IsReady = true`) is printed to console. Elapsed time for the full Claude agent loop (from first `Messages.CreateAsync` call to `StopReason == end_turn`) is printed to console. No credential values appear alongside timing output.
2. `docs/poc-baseline.md` is created (stub was created in Story 1.2) and contains: (a) measured connection time from at least one test run, (b) measured Claude loop time from at least one test run, (c) assessment against NFR-001 targets (under 5 minutes for typical environments), (d) any observations about environment size (table count, etc.) of the test environment used.
3. A code review of `EnvironmentCredentials`, `DataverseConnectionFactory`, and `AgentOrchestrator` confirms all three of the following, recorded with pass/fail in `docs/poc-baseline.md`:
   - No credential values (ClientSecret, ClientId, TenantId) appear in any `Console.Write`, `Console.WriteLine`, log call, or exception `Message` string
   - `ClientSecret` is never passed as a raw `string` parameter outside the `EnvironmentCredentials` wrapper
   - `EnvironmentCredentials` is not passed to `JsonSerializer.Serialize()` or any equivalent serialization call
4. Phase 1 exit gate is recorded in `docs/poc-baseline.md` as either PASSED (all three code review items pass, pipeline works end-to-end) or BLOCKED (itemised failures listed).

## Tasks / Subtasks

- [x] Add timing instrumentation to console runner (AC: 1)
  - [x] Wrap `DataverseConnectionFactory.ConnectAsync()` with `Stopwatch` — print elapsed on success
  - [x] Wrap `AgentOrchestrator.RunAsync()` with `Stopwatch` — print elapsed on completion
  - [x] Confirm output contains only timing data and status messages, not credential values
- [x] Run POC against test environment and record measurements (AC: 2)
  - [x] Execute at least two full runs
  - [x] Note: environment URL (sanitized — no credentials), table count returned by `list_custom_tables`, connection time (ms), agent loop time (ms)
  - [x] Record in `docs/poc-baseline.md`
- [x] Assess against NFR-001 (AC: 2)
  - [x] If Mode 1 is projected to call ~5–10 tool types (not just one), extrapolate from single-tool time
  - [x] Mark as: Confirmed / Needs revision / Insufficient data (re-test in Phase 2)
- [x] Conduct credential handling code review (AC: 3)
  - [x] Review `EnvironmentCredentials.cs`: verify `[DebuggerBrowsable(Never)]` on ClientSecret, no unsafe `ToString()`
  - [x] Review `DataverseConnectionFactory.cs`: verify all catch blocks use safe messages, no property access in log calls
  - [x] Review `AgentOrchestrator.cs`: verify `EnvironmentCredentials` not passed to JSON serializer, not included in prompt strings
  - [x] Search codebase for any occurrence of the User Secrets key names (e.g., `ClientSecret`) in string interpolations
  - [x] Record PASS/FAIL per item
- [x] Record Phase 1 exit gate outcome in `docs/poc-baseline.md` (AC: 4)
  - [x] Overall PASSED / BLOCKED
  - [x] If BLOCKED: list each failure and remediation steps

## Dev Notes

- `Stopwatch` is in `System.Diagnostics` — no additional NuGet required.
- For the code review checklist, use a simple Markdown table with columns: Item | Status | Notes.
- The "code review" at this stage is a self-review by the developer. It establishes the pattern that will be enforced by a third-party review in Phase 2 before the first paying customer.
- NFR-001 targets are for the full Mode 1 pipeline (8+ tool types, Claude analysis across multiple sections). The POC only runs one tool. The extrapolation in the baseline should note this explicitly: "POC measures single-tool loop time; full Mode 1 will require additional calibration in Phase 2."
- If connection time is >5 seconds consistently, investigate whether the `ServiceClient` constructor is doing interactive auth (wrong) vs. client credentials (correct).

### Project Structure Notes

Files created or modified:
- `src/DataverseDocAgent.Console/Program.cs` — timing added
- `docs/poc-baseline.md` — primary deliverable of this story

### References

- [Source: docs/prd.md#7-non-functional-requirements — NFR-001] — Mode 1 generation time targets
- [Source: docs/prd.md#9-build-roadmap — P1] — Phase 1 exit gate: "Pipeline works end-to-end. Credential handling passes code review."
- [Source: docs/prd.md#functional-requirements — FR-034, NFR-007] — credential handling guarantee

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- Anthropic API credits were unavailable during initial run attempt; resolved mid-session. All measurements taken once credits were confirmed active.

### Completion Notes List

- **AC-1:** Timing instrumentation added to `Program.cs` using `System.Diagnostics.Stopwatch`. Connection time printed after `ConnectAsync` success; agent loop time printed after `RunAsync` returns. No credential values appear in any timing output.
- **AC-2:** Two full runs executed on 2026-04-15. Run 1: connection 1320 ms, loop 44265 ms. Run 2: connection 1317 ms, loop 29446 ms. Environment contains ~400+ custom tables (7 `doc_` business tables + platform/system tables). Recorded in `docs/poc-baseline.md`.
- **AC-2c NFR-001:** Connection confirmed well within budget. Single-tool loop 30–44 s. Full Mode 1 extrapolation marked "Insufficient data — re-test in Phase 2". Note added that NFR-001 target may need revision for large environments.
- **AC-3:** Code review of `EnvironmentCredentials.cs`, `DataverseConnectionFactory.cs`, `AgentOrchestrator.cs` — all 3 items PASS. No credential values in any output/log/exception string. `ClientSecret` only at SDK auth boundary. No `JsonSerializer.Serialize` on credentials object.
- **AC-4:** Phase 1 exit gate recorded as **PASSED** in `docs/poc-baseline.md`.

### File List

- `src/DataverseDocAgent.Console/Program.cs` — timing instrumentation added (Stopwatch wrapping ConnectAsync and RunAsync)
- `docs/poc-baseline.md` — completed with measurements, NFR-001 assessment, code review results, Phase 1 exit gate

### Review Findings

- [x] [Review][Patch] connectSw measurement caveat — document in poc-baseline.md that the 1,319 ms connection figure includes the WhoAmIRequest verification ping inside ConnectAsync (not just auth handshake). [docs/poc-baseline.md] ← resolved D1-B ✓ applied
- [x] [Review][Patch] agentSw measurement caveat — document in poc-baseline.md that the agent loop figure includes pre-loop setup (BuildSdkTools, list construction) before the first Anthropic API call; not purely Claude generation time. [docs/poc-baseline.md] ← resolved D2-B ✓ applied
- [x] [Review][Defer] SDK ex.Message risk — deferred to Phase 2 hardening; POC-only scope — deferred, pre-existing ← resolved D3-C
- [x] [Review][Patch] NFR-001 gate — change from PASSED to CONDITIONAL PASS with explicit risk item noting linear-scaling assumption, multi-tool-per-iteration behavior, and MaxIterations cap. [docs/poc-baseline.md] ← resolved D4-A ✓ applied
- [x] [Review][Patch] connectSw leaks running stopwatch if ConnectAsync throws any exception other than DataverseConnectionException — fixed: moved connectSw.Stop() into finally block. [`Program.cs`] ✓ applied
- [x] [Review][Patch] MaxIterations sentinel return is indistinguishable from a successful response in Program.cs — fixed: sentinel check added after RunAsync; prints WARNING label if iteration limit was hit. [`Program.cs`] ✓ applied
- [x] [Review][Patch] Agent loop catch swallows all exceptions with process exit code 0 — fixed: Environment.Exit(1) added to agent loop catch block; agentSw.Stop() moved to finally. [`Program.cs`] ✓ applied
- [x] [Review][Patch] Program.cs omitted from credential review scope in poc-baseline.md — fixed: Program.cs added to reviewed files list. [`docs/poc-baseline.md`] ✓ applied
- [x] [Review][Patch] No commit SHA pinning the credential review to a specific code state — fixed: commit SHA c77914f added alongside review date. [`docs/poc-baseline.md`] ✓ applied
- [x] [Review][Patch] poc-baseline.md does not specify which EnvironmentCredentials copy was reviewed — fixed: fully-qualified type names used; Api copy specified. [`docs/poc-baseline.md`] ✓ applied
- [x] [Review][Defer] "Both runs completed successfully" claim backed only by absence of exception — not by return-value or sentinel check; claim is technically accurate since sentinel case did not occur in the recorded runs; defer until sentinel handling is implemented — deferred, pre-existing

#### Round 2 Review Findings

- [x] [Review][Decision] Environment.Exit(1) inside using(serviceClient) — resolved D1-A: replaced with agentFailed flag; Environment.Exit(1) moved to after using block; agentSw stopped explicitly in both paths; timing printed on failure path too. [`Program.cs`] ✓ applied
- [x] [Review][Decision] "CONDITIONAL PASS" is not a spec-permitted AC 4 outcome — resolved D2-A: changed to PASSED; NFR-001 risk preserved as prominent prose risk note. [`docs/poc-baseline.md`] ✓ applied
- [x] [Review][Patch] agentSw timing degraded by round-1 patch — fixed: agentSw.Stop() called immediately after RunAsync returns, before any Console output; timing printed first so measurement excludes response I/O overhead. [`Program.cs`] ✓ applied
- [x] [Review][Patch] Sentinel check uses magic string prefix with no shared constant — fixed: extracted as AgentOrchestrator.MaxIterationsSentinel (public const); Program.cs uses string.Equals against the constant. [`Program.cs`, `AgentOrchestrator.cs`] ✓ applied
- [x] [Review][Patch] Exit code inconsistency — fixed: connection failure now calls Environment.Exit(1) instead of return; finally block calls connectSw.Stop() as no-op safety net. [`Program.cs`] ✓ applied
