# Story 3.1: Async Job Infrastructure (Job Store + Background Service)

Status: ready-for-dev

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

- [ ] Define job model types (AC: 1)
  - [ ] Create `src/DataverseDocAgent.Api/Jobs/IJobStore.cs`
    - Interface with `CreateJob()`, `UpdateStatus()`, `GetJob()`
  - [ ] Create `src/DataverseDocAgent.Api/Jobs/JobStatus.cs`
    - Enum: `Queued`, `Running`, `Ready`, `Failed`
  - [ ] Create `src/DataverseDocAgent.Api/Jobs/JobRecord.cs`
    - Record: `string JobId`, `JobStatus Status`, `string? DownloadToken`, `string? ErrorMessage`
  - [ ] Create `src/DataverseDocAgent.Api/Jobs/GenerationTask.cs`
    - Record: `string JobId`, `EnvironmentCredentials Credentials` (held in memory, not serialized)
- [ ] Implement `InMemoryJobStore` (AC: 2)
  - [ ] Create `src/DataverseDocAgent.Api/Jobs/InMemoryJobStore.cs`
  - [ ] `ConcurrentDictionary<string, JobRecord>` as backing store
  - [ ] `CreateJob()`: `Guid.NewGuid().ToString()`, adds a `JobRecord` with `Status = Queued`
  - [ ] `UpdateStatus()`: replaces the `JobRecord` for the given jobId with updated fields
  - [ ] `GetJob()`: returns `null` if jobId not found
- [ ] Implement `GenerationBackgroundService` stub (AC: 3, 5, 6)
  - [ ] Create `src/DataverseDocAgent.Api/Jobs/GenerationBackgroundService.cs`
  - [ ] Constructor: inject `Channel<GenerationTask>`, `IJobStore`, `ILogger<GenerationBackgroundService>`
  - [ ] `ExecuteAsync(CancellationToken stoppingToken)`: loop on `await _channel.Reader.ReadAsync(stoppingToken)`
  - [ ] For each task:
    - Call `_jobStore.UpdateStatus(task.JobId, JobStatus.Running, null, null)`
    - Invoke pipeline stub (placeholder for Story 3.5 — for now, simulate with `await Task.Delay(100)`)
    - On success: call `_jobStore.UpdateStatus(task.JobId, JobStatus.Ready, downloadToken, null)`
    - On any exception: call `_jobStore.UpdateStatus(task.JobId, JobStatus.Failed, null, ex.Message)`
    - In both cases: ensure credentials are not retained after the task completes
  - [ ] Wrap task processing in try/catch so one failure doesn't kill the background service loop
- [ ] Implement `GET /api/jobs/{jobId}` endpoint (AC: 4)
  - [ ] Create `src/DataverseDocAgent.Api/Features/DocumentGenerate/JobStatusController.cs`
  - [ ] `[HttpGet("/api/jobs/{jobId}")]`
  - [ ] Retrieve `JobRecord` from `IJobStore`
  - [ ] If null: return `new { error = "Job not found", code = "JOB_NOT_FOUND", safeToRetry = false }` as HTTP 200
  - [ ] If found: return `new { jobId, status = record.Status.ToString().ToLower(), downloadToken = record.DownloadToken, error = record.ErrorMessage }`
- [ ] Register services in DI (AC: 1–3)
  - [ ] In `Program.cs`:
    - `builder.Services.AddSingleton<IJobStore, InMemoryJobStore>()`
    - `builder.Services.AddSingleton(Channel.CreateUnbounded<GenerationTask>())`
    - `builder.Services.AddHostedService<GenerationBackgroundService>()`
- [ ] Manual integration test (AC: 4, 5, 6)
  - [ ] Start the API, POST a stub request, GET the job status, observe `Queued → Running → Ready` transitions

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

### Completion Notes List

### File List
