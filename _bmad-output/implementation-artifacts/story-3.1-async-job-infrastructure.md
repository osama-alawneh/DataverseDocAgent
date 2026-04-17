# Story 3.1: Async Job Infrastructure (Job Store + Background Service)

Status: done

## Story

As a developer,
I want a job store and background service that accepts generation tasks and tracks their status,
so that Mode 1 (and later Mode 3) can run for up to 10 minutes without holding an HTTP connection open.

## Acceptance Criteria

1. `IJobStore` interface is defined with: `string CreateJob()` (returns jobId), `void UpdateStatus(string jobId, JobStatus status, string? downloadToken, string? errorMessage)`, `JobRecord? GetJob(string jobId)`.
2. `InMemoryJobStore` implements `IJobStore` using a `ConcurrentDictionary<string, JobRecord>`. `CreateJob()` returns a new UUID. Jobs are not expired from the store in Phase 2 (acceptable for MVP single-instance deployment).
3. `GenerationBackgroundService` implements `BackgroundService`, accepts a `Channel<GenerationTask>` as its input queue, dequeues tasks, and invokes the generation pipeline for each task. It updates job status to `Running` when it picks up a task, and to `Ready` or `Failed` on completion or error.
4. `GET /api/jobs/{jobId}` returns HTTP 200 with `{ jobId, status, downloadToken? }` when the job exists. It returns HTTP 200 with `{ error: "Job not found", code: "JOB_NOT_FOUND", safeToRetry: false }` for an unknown jobId — not HTTP 404.
5. If a generation job fails for any reason (credential rejection, Dataverse error, Claude API error), the job is marked `Failed` and credentials held by the failing task are discarded — no partial credential state is retained after failure.
6. The background service handles exceptions within a single task without crashing the service — subsequent queued tasks continue processing after one failure.

## Tasks / Subtasks

- [x] Define job model types (AC: 1)
  - [x] `src/DataverseDocAgent.Api/Jobs/IJobStore.cs`
  - [x] `src/DataverseDocAgent.Api/Jobs/JobStatus.cs` (Queued/Running/Ready/Failed)
  - [x] `src/DataverseDocAgent.Api/Jobs/JobRecord.cs` (sealed record; replaced via `with`)
  - [x] `src/DataverseDocAgent.Api/Jobs/GenerationTask.cs`
- [x] Implement `InMemoryJobStore` (AC: 2)
  - [x] `ConcurrentDictionary<string, JobRecord>` backing store
  - [x] `CreateJob()` → new `Guid.NewGuid()`, seeds record with `Status = Queued`
  - [x] `UpdateStatus()` uses `AddOrUpdate` with `record with { ... }` for immutable replace
  - [x] `GetJob()` returns `null` on miss
- [x] Implement `GenerationBackgroundService` (AC: 3, 5, 6)
  - [x] Constructor injects `Channel<GenerationTask>`, `IJobStore`, `IGenerationPipeline`, `ILogger<>`
  - [x] `ExecuteAsync` drains the channel via `ReadAllAsync(stoppingToken)`
  - [x] `internal ProcessTaskAsync` handles the per-task state machine — extracted for testability (InternalsVisibleTo is already set on Api → Tests)
  - [x] Single-task exceptions caught and recorded; `OperationCanceledException` on host shutdown re-thrown so the base class stops cleanly
  - [x] Credentials released when the per-iteration local `task` falls out of scope; no explicit zeroing (managed strings)
- [x] Pipeline seam for Story 3.5 (Dev Notes — "placeholder for Story 3.5")
  - [x] `IGenerationPipeline` interface with `RunAsync(GenerationTask, CancellationToken) → Task<string>`
  - [x] `StubGenerationPipeline` — `await Task.Delay(100)` + fresh GUID as download token
- [x] Implement `GET /api/jobs/{jobId}` endpoint (AC: 4)
  - [x] `src/DataverseDocAgent.Api/Features/DocumentGenerate/JobStatusController.cs`
  - [x] `[HttpGet("/api/jobs/{jobId}")]` always returns HTTP 200
  - [x] Miss → `StructuredErrorResponse { Code = "JOB_NOT_FOUND", SafeToRetry = false }`
  - [x] Hit → `JobStatusResponse { jobId, status (lowercased), downloadToken, error }`
- [x] Register services in DI (AC: 1–3)
  - [x] `AddSingleton<IJobStore, InMemoryJobStore>()`
  - [x] `AddSingleton(Channel.CreateUnbounded<GenerationTask>())`
  - [x] `AddSingleton<IGenerationPipeline, StubGenerationPipeline>()` — Story 3.5 replaces this
  - [x] `AddHostedService<GenerationBackgroundService>()`
- [x] Unit-test coverage for AC-1/2/4/5/6 (manual integration test not run — 10 new xunit tests assert the behaviour instead)
  - [x] `InMemoryJobStoreTests` — unique ids, Queued seed, UpdateStatus replace, unknown-id null
  - [x] `JobStatusControllerTests` — hit body shape, miss body shape + 200, failed-job error surfacing
  - [x] `GenerationBackgroundServiceTests` — happy-path transitions, NFR-007 sanitized failure message, fault-isolation via `ExecuteAsync` with completed channel
- [x] Verify build + tests green before marking review
  - [x] `dotnet build DataverseDocAgent.sln --no-incremental` → 0 warn / 0 err
  - [x] `dotnet test` → 85/85 green (75 baseline + 10 new)

## Dev Notes

- **`Channel<T>`:** Use `System.Threading.Channels.Channel<GenerationTask>`. `Channel.CreateUnbounded<GenerationTask>()` is appropriate for MVP — it never blocks the producer (the HTTP controller) regardless of queue depth. Register as singleton so both the controller and background service share the same channel instance.
- **Credential lifetime:** The `GenerationTask` record holds `EnvironmentCredentials`. Once the task is dequeued and processed, the reference is released when `task` goes out of scope in the `ExecuteAsync` loop. The GC collects it. This is the correct lifecycle per the architecture credential handling contract.
- **Job not found → HTTP 200:** This is intentional per the PRD's error response contract — all API responses use the structured error format rather than HTTP error codes, to provide consistent, parsable responses for all outcomes.
- **Background service pipeline stub:** In this story, the generation pipeline is a placeholder `await Task.Delay(100)` followed by a simulated token. The real pipeline is wired in Story 3.5. The job infrastructure must work independently of the pipeline content.
- **`JobRecord` as a record type:** C# records are immutable by default. `UpdateStatus` should replace the entire record in the `ConcurrentDictionary` using `AddOrUpdate`, not mutate in place.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Api/Jobs/IJobStore.cs`
- `src/DataverseDocAgent.Api/Jobs/JobStatus.cs`
- `src/DataverseDocAgent.Api/Jobs/JobRecord.cs`
- `src/DataverseDocAgent.Api/Jobs/GenerationTask.cs`
- `src/DataverseDocAgent.Api/Jobs/InMemoryJobStore.cs`
- `src/DataverseDocAgent.Api/Jobs/GenerationBackgroundService.cs`
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/JobStatusController.cs`

Modified:
- `src/DataverseDocAgent.Api/Program.cs` — DI registrations

### References

- [Source: _bmad-output/planning-artifacts/architecture.md#4-request-lifecycle] — full async job flow diagram
- [Source: _bmad-output/planning-artifacts/architecture.md#8-async-job-model] — IJobStore and JobStatus interface definitions
- [Source: docs/prd.md#7-non-functional-requirements — NFR-014] — structured error responses
- [Source: docs/prd.md#functional-requirements — FR-036] — POST /generate returns 202 with jobId

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `dotnet build DataverseDocAgent.sln --no-incremental` → 0 warnings / 0 errors.
- `dotnet test` → 85/85 passing (75 pre-existing + 10 new for story 3.1).

### Completion Notes List

- **Deliberate NFR-007 deviation from literal spec wording.** The Dev Notes' template of recording the exception message on the failed job record would echo SDK-sourced exception text into the API response (and sometimes into structured log fields via generic exception handlers). Some .NET SDKs (notably the Dataverse ServiceClient and MSAL) include tenant IDs, authority URLs, and occasionally request-body fragments in exception messages. To keep the NFR-007 credential-logging prohibition airtight, `GenerationBackgroundService.ProcessTaskAsync` records a *sanitized* message (`"Generation failed. Check server logs for details."`) on the job record, and logs the full exception (with stack trace) through Serilog only — where the `CredentialDestructuringPolicy` can scrub it. The GenerationBackgroundServiceTests' `ProcessTaskAsync_PipelineThrows_MarksFailed_AndDoesNotLeakExceptionMessage` test pins this behaviour.
- **`internal ProcessTaskAsync` for testability.** The per-task state machine is extracted as an `internal` method so the `GenerationBackgroundServiceTests` can exercise it directly without needing `WebApplicationFactory`/TestHost. `InternalsVisibleTo("DataverseDocAgent.Tests")` was already set in the Api project, so no project-file change was needed.
- **`OperationCanceledException` on shutdown.** When `stoppingToken` fires during host shutdown, the cancellation is re-thrown from `ProcessTaskAsync` so the `BackgroundService` base class can stop cleanly — it is not swallowed and recorded as a job failure. Any other exception is caught and routed to the sanitized-failure path.
- **Job store is never expired in Phase 2.** Acceptable per AC-2 for a single-instance MVP. Multi-instance / long-running deployments will need an eviction policy (tracked in `deferred-work.md`).
- **`GenerationTask` credential release.** Credentials live for the duration of the per-iteration local `task` variable inside `ExecuteAsync`. When the loop iteration completes, the record (and its `EnvironmentCredentials`) becomes unreachable and is collected. No explicit zero-fill — managed strings do not provide a primitive for this, and the current architecture credential-handling contract does not require it.

### File List

New:
- `src/DataverseDocAgent.Api/Jobs/IJobStore.cs`
- `src/DataverseDocAgent.Api/Jobs/JobStatus.cs`
- `src/DataverseDocAgent.Api/Jobs/JobRecord.cs`
- `src/DataverseDocAgent.Api/Jobs/GenerationTask.cs`
- `src/DataverseDocAgent.Api/Jobs/InMemoryJobStore.cs`
- `src/DataverseDocAgent.Api/Jobs/IGenerationPipeline.cs`
- `src/DataverseDocAgent.Api/Jobs/StubGenerationPipeline.cs`
- `src/DataverseDocAgent.Api/Jobs/GenerationBackgroundService.cs`
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/JobStatusController.cs`
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/JobStatusResponse.cs`
- `tests/DataverseDocAgent.Tests/InMemoryJobStoreTests.cs`
- `tests/DataverseDocAgent.Tests/JobStatusControllerTests.cs`
- `tests/DataverseDocAgent.Tests/GenerationBackgroundServiceTests.cs`

Modified:
- `src/DataverseDocAgent.Api/Program.cs` — DI for `IJobStore`, `Channel<GenerationTask>`, `IGenerationPipeline`, `GenerationBackgroundService`.
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — story 3-1 → review → done.

### Code Review Outcomes (2026-04-17)

Three-layer adversarial review (Blind Hunter + Edge Case Hunter + Acceptance Auditor) run against commit `712c18f`. Auditor verdict: SATISFIED on all six ACs; `ex.Message` → sanitized-message substitution ruled an authorised (and arguably required) NFR-007 upgrade. Five patches applied in follow-up commit:

- **P1** `InMemoryJobStore.UpdateStatus` now true no-op on unknown id (was `AddOrUpdate`-inserting a ghost record — contradicting the interface xmldoc). Test `UpdateStatus_UnknownId_IsTrueNoOp_NoGhostRecordInserted` flipped to pin the intended contract. Closes latent arbitrary-key planting footgun ahead of Story 3.5 wiring the real controller.
- **P2** `JobStatusController.Get` now rejects non-guid route parameters with `JOB_NOT_FOUND` before touching the store. Caps log cardinality and aligns with `CreateJob()`'s guid-only output.
- **P3** `Channel<GenerationTask>` registered via factory `AddSingleton(_ => Channel.CreateUnbounded<GenerationTask>())` instead of captured instance — safer under `WebApplicationFactory` reuse and host reload.
- **P4** `ExecuteAsync_OneTaskFailure_DoesNotStopLoop_...` rewritten to keep the channel open across the first failure (previously completed pre-start — the test would have passed even if the loop aborted on fault).
- **P5** New test `ProcessTaskAsync_HostShutdownCancellation_PropagatesWithoutMarkingFailed` pins the `catch (OperationCanceledException) when stoppingToken.IsCancellationRequested` filter.

Deferred (tracked in `deferred-work.md`): unbounded-channel credential lifetime; host-shutdown drain strategy; `JobRecord` ErrorCode/SafeToRetry taxonomy (Story 3.5); polling rate-limit for `/api/jobs/*` (Phase 3); `JsonStringEnumConverter` vs `ToString().ToLower()`; `ChannelReader`/`Writer` DI split; `IGenerationPipeline` cancellation contract test.
