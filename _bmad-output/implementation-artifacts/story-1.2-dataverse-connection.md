# Story 1.2: Dataverse Connection and Credential In-Memory Handling

Status: done

## Story

As a developer,
I want a `DataverseConnectionFactory` that authenticates to Dataverse using client credentials held exclusively in memory,
so that I can prove the credential handling contract before any API surface is exposed.

## Acceptance Criteria

1. `EnvironmentCredentials` is a `sealed` C# class in `Common/` with properties: `EnvironmentUrl` (string), `TenantId` (string), `ClientId` (string), `ClientSecret` (string). `ClientSecret` has `[System.Diagnostics.DebuggerBrowsable(DebuggerBrowsableState.Never)]` applied. The class does not override `ToString()` in a way that would expose any property value.
2. `DataverseConnectionFactory.ConnectAsync(EnvironmentCredentials credentials)` returns a `ServiceClient` and executes a lightweight test query (e.g., `RetrieveOrganizationRequest`) to confirm connectivity.
3. If credentials are invalid, `ConnectAsync` throws a typed `DataverseConnectionException` and does not retry or log any credential value.
4. A console test runner in `DataverseDocAgent.Console/Program.cs` loads credentials from User Secrets, calls `ConnectAsync`, and prints either "Connected successfully" or the exception message — never the credential values.
5. No credential value (`ClientSecret`, `ClientId`, `TenantId`) appears in any console output or exception message at any point during the test run.
6. A code review checklist item is documented in `docs/poc-baseline.md` confirming: (a) no credential logging, (b) `ClientSecret` is not passed as a raw string outside the `EnvironmentCredentials` wrapper, (c) `EnvironmentCredentials` is not serialized via `JsonSerializer` or `ToString()`.

## Tasks / Subtasks

- [x] Implement `EnvironmentCredentials` (AC: 1)
  - [x] Create `src/DataverseDocAgent.Api/Common/EnvironmentCredentials.cs`
  - [x] Sealed class, all properties `init`-only
  - [x] Apply `[DebuggerBrowsable(DebuggerBrowsableState.Never)]` to `ClientSecret`
  - [x] Do NOT override `ToString()` — default behavior is safe (does not enumerate properties)
- [x] Implement `DataverseConnectionException` (AC: 3)
  - [x] Create `src/DataverseDocAgent.Api/Common/DataverseConnectionException.cs`
  - [x] Constructor accepts a `message` and optional inner exception — no credential parameters
- [x] Implement `DataverseConnectionFactory` (AC: 2, 3)
  - [x] Create `src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs`
  - [x] `ConnectAsync(EnvironmentCredentials)` builds `ServiceClient` using `clientId`, `clientSecret`, `tenantId`, `environmentUrl`
  - [x] Executes `WhoAmIRequest` (lightweight connectivity test) to confirm the connection is live
  - [x] On `ServiceClient` construction failure or query failure, catch and throw `DataverseConnectionException` with a safe message (e.g., "Failed to connect to Dataverse environment. Verify credentials and environment URL.")
  - [x] Log only timing and success/failure — never log credential property values
- [x] Wire up console test runner (AC: 4, 5)
  - [x] In `DataverseDocAgent.Console/Program.cs`, read credentials from `IConfiguration` (User Secrets)
  - [x] Construct `EnvironmentCredentials`
  - [x] Call `DataverseConnectionFactory.ConnectAsync(credentials)`
  - [x] Print elapsed time and "Connected successfully" or `exception.Message` (safe message only)
- [x] Create `docs/poc-baseline.md` stub with credential code review checklist (AC: 6)
  - [x] Section: "Credential Handling Code Review" with three checkboxes (to be completed in Story 1.4)

## Dev Notes

- `Microsoft.PowerPlatform.Dataverse.Client` `ServiceClient` is constructed with a connection string or with individual parameters. Use the overload: `new ServiceClient(new Uri(environmentUrl), clientId, clientSecret, true)` for daemon/client credentials flow.
- The `tenantId` is implicitly used by the `ServiceClient` via the authority URL derived from the environment URL. Confirm that the SDK version in use handles multi-tenant client credentials correctly.
- The `ServiceClient.IsReady` property returns true only when the connection is authenticated. Check this before issuing the test query.
- `EnvironmentCredentials` should also be created in `DataverseDocAgent.Console` or shared — since Console doesn't reference Api, consider creating it in a shared location or duplicating the simple class for now. Keep it simple for POC; proper sharing via a shared project is Phase 2 work.
- User Secrets key names: `Dataverse:EnvironmentUrl`, `Dataverse:TenantId`, `Dataverse:ClientId`, `Dataverse:ClientSecret`. These match the placeholder `appsettings.json` sections from Story 1.1.

### Project Structure Notes

Files created or modified:
- `src/DataverseDocAgent.Api/Common/EnvironmentCredentials.cs` — used by all subsequent stories
- `src/DataverseDocAgent.Api/Common/DataverseConnectionException.cs`
- `src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs`
- `src/DataverseDocAgent.Console/Program.cs` — test runner
- `docs/poc-baseline.md` — stub created here, completed in Story 1.4

### References

- [Source: docs/prd.md#52-credential-handling-architecture] — 7-stage credential flow
- [Source: docs/prd.md#functional-requirements — FR-034] — credential in-memory guarantee
- [Source: _bmad-output/planning-artifacts/architecture.md#6-credential-handling-contract] — EnvironmentCredentials class contract
- [Source: docs/prd.md#7-non-functional-requirements — NFR-007] — credential persistence prohibition

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `RetrieveOrganizationRequest` (from Dev Notes) was not resolvable via `Microsoft.Xrm.Sdk.Messages` in the included SDK assemblies. Switched to `WhoAmIRequest` from `Microsoft.Crm.Sdk.Messages` — it is the canonical lightweight connectivity test for Dataverse and satisfies AC 2.
- Console project intentionally does not reference Api (per Dev Notes architecture constraint). All three Dataverse classes (`EnvironmentCredentials`, `DataverseConnectionException`, `DataverseConnectionFactory`) are duplicated under `DataverseDocAgent.ConsoleApp.*` namespaces for POC. Shared project extraction is Phase 2 work.
- Added `Microsoft.Extensions.Configuration.UserSecrets` (v8.0.1) to Console project — deferred from Story 1.1 review.

### Completion Notes List

- `EnvironmentCredentials` (sealed, `init`-only, `[DebuggerBrowsable(Never)]` on ClientSecret) created in both Api and Console. Default `ToString()` not overridden — safe by default. AC 1 ✅
- `DataverseConnectionException` (sealed, safe messages only, no credential parameters) created in both projects. AC 3 ✅
- `DataverseConnectionFactory.ConnectAsync` builds `ServiceClient`, checks `IsReady`, executes `WhoAmIRequest`, logs only elapsed time — no credential values in any log or exception message. AC 2, 3 ✅
- `Console/Program.cs` reads all four credentials from User Secrets, guards against missing values, calls `ConnectAsync`, prints "Connected successfully (Nms)" or the safe `DataverseConnectionException.Message` — credential values never printed. AC 4, 5 ✅
- `docs/poc-baseline.md` stub created with three credential-handling review checkboxes (to be signed off in Story 1.4). AC 6 ✅
- `dotnet build` → 0 errors, 0 warnings (both projects).

### File List

- `src/DataverseDocAgent.Api/Common/EnvironmentCredentials.cs`
- `src/DataverseDocAgent.Api/Common/DataverseConnectionException.cs`
- `src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs`
- `src/DataverseDocAgent.Console/Common/EnvironmentCredentials.cs`
- `src/DataverseDocAgent.Console/Common/DataverseConnectionException.cs`
- `src/DataverseDocAgent.Console/Dataverse/DataverseConnectionFactory.cs`
- `src/DataverseDocAgent.Console/DataverseDocAgent.Console.csproj`
- `src/DataverseDocAgent.Console/Program.cs`
- `docs/poc-baseline.md`

### Review Findings

- [x] [Review][Decision→Patch] Inner exception chain strips SDK credential fragments — resolved: Option A (strip inner exceptions). Both factories now discard inner exceptions; `DataverseConnectionException` carries only the safe diagnostic message. [Api+Console DataverseConnectionFactory.cs]
- [x] [Review][Patch] ServiceClient not disposed on failure paths — `client.Dispose()` called before rethrowing in both the `IsReady = false` branch and the `WhoAmIRequest` catch block in both factory copies. [Api+Console DataverseConnectionFactory.cs]
- [x] [Review][Patch] Remove Console.WriteLine and internal Stopwatch from DataverseConnectionFactory — both removed from both factory copies; `System.Diagnostics` using removed. Program.cs retains timing and output ownership. [Api+Console DataverseConnectionFactory.cs]
- [x] [Review][Patch] TenantId added to validation guard in Program.cs — guard now checks all four credential fields for blank/whitespace. [Console Program.cs:20–23]
- [x] [Review][Patch] XML safety-contract comments added to Console duplicate classes — both `Console/Common/EnvironmentCredentials.cs` and `Console/Common/DataverseConnectionException.cs` now carry explicit `SECURITY CONTRACT` summary comments matching the intent of the Api versions.
- [x] [Review][Defer] Credentials stored as plain managed strings — `ClientSecret` (and all credentials) are held as `string` references; the GC may copy them to new heap addresses and they are not zeroed on scope exit. True credential isolation would require pinned byte arrays or OS-level secure memory. `SecureString` is deprecated in .NET 6+ with no practical replacement. Deferring — known language limitation, out of POC scope. [Api+Console EnvironmentCredentials.cs]
- [x] [Review][Defer] ServiceClient constructor is synchronous despite async caller — `new ServiceClient(Uri, string, string, bool)` performs OAuth token acquisition synchronously, blocking the calling thread pool thread. Wrapping in `Task.Run` or using a different async overload would mitigate this. Deferring — SDK constraint; no async overload is available in v1.2.10. [Api+Console DataverseConnectionFactory.cs:25]
- [x] [Review][Defer] Duplicate class divergence risk — `EnvironmentCredentials`, `DataverseConnectionException`, `DataverseConnectionFactory` are copy-pasted between Api and Console with no shared abstraction or behavioral parity test. A fix in one will silently diverge from the other. Phase 2 shared project planned per Dev Notes. Deferring — explicitly Phase 2 work.
- [x] [Review][Defer] No serialization guard attributes on EnvironmentCredentials — the "must never be serialized" contract is documentation-only; `[JsonIgnore]` and similar guard attributes are absent. Phase 2 hardening. Deferring. [Api+Console EnvironmentCredentials.cs]
- [x] [Review][Defer] No CancellationToken support in ConnectAsync — a hung Dataverse endpoint or slow token acquisition cannot be cancelled. Explicitly out of POC scope per Dev Notes (no retries, no cancellation). Deferring. [Api+Console DataverseConnectionFactory.cs:18]
- [x] [Review][Defer] EnvironmentUrl scheme and format not validated — non-HTTPS or malformed URLs produce the generic error message with no specific diagnostic. Produces correct behaviour (safe error) but poor UX. Deferring — acceptable for POC test runner; validate in Phase 2 API layer. [Console Program.cs:20]

### Change Log

- 2026-04-14: Story 1.2 implemented — `EnvironmentCredentials`, `DataverseConnectionException`, `DataverseConnectionFactory` in Api and Console; Console test runner using User Secrets; `docs/poc-baseline.md` stub. Build: 0 errors, 0 warnings.
- 2026-04-14: Code review findings appended — 1 decision-needed, 4 patches, 6 deferred, 10 dismissed.
- 2026-04-14: All 5 patches applied — inner exceptions stripped, ServiceClient disposed on failure paths, factory Console.WriteLine/Stopwatch removed, TenantId added to guard, XML safety contracts added to Console duplicates. Build: 0 errors, 0 warnings. Story marked done.
