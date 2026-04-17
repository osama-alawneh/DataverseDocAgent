# Story 2.2: Permission Pre-Flight Checker — Core Implementation

Status: done

## Story

As a D365 consultant,
I want to call POST /api/security/check with my service account credentials,
so that I can verify the account has exactly the right permissions before connecting it to any mode.

## Acceptance Criteria

1. `POST /api/security/check` accepts a JSON body with: `environmentUrl` (string, required), `tenantId` (string, required), `clientId` (string, required), `clientSecret` (string, required), `targetMode` (string, optional — "mode1"|"mode2"|"mode3"|"all", defaults to "all").
2. When the service account has all required permissions and no extras, the response is HTTP 200 with: `status: "ready"`, `safeToRun: true`, `passed[]` containing all 13 privilege names from PRD Section 5.4, `missing: []`, `extra: []`, and a confirmation `recommendation` string.
3. When required permissions are missing, the response is HTTP 200 with: `status: "blocked"`, `safeToRun: false`, `missing[]` listing each absent privilege as `"Read {EntityName}"` (e.g., "Read PluginAssembly"), and a `recommendation` string with exact remediation steps.
4. When the service account has extra permissions beyond those required, the response is HTTP 200 with: `status: "ready"`, `safeToRun: true`, `extra[]` listing each surplus privilege, and a `recommendation` advising removal with least-privilege rationale.
5. When credentials are invalid (authentication fails against Dataverse), the response is HTTP 200 with: `status: "blocked"`, `safeToRun: false`, `missing: []`, `extra: []`, `passed: []`, and a `recommendation` string explaining the credential failure — the response is not HTTP 401.
6. The endpoint responds within 10 seconds for any valid Dataverse environment (NFR-002).
7. No credential values appear in any Serilog log entry generated during the request lifecycle.

## Tasks / Subtasks

- [x] Define request and response models (AC: 1, 2)
  - [x] Create `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckRequest.cs`
    - Properties: `EnvironmentUrl`, `TenantId`, `ClientId`, `ClientSecret`, `TargetMode`
    - `TargetMode` defaults to `"all"` if not supplied
  - [x] Create `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckResponse.cs`
    - Properties: `Status` (string), `SafeToRun` (bool), `Passed` (string[]), `Missing` (string[]), `Extra` (string[]), `Recommendation` (string)
- [x] Define the required privilege list (AC: 2, 3)
  - [x] Create a static `RequiredPrivileges` class or constant in `SecurityCheckService.cs` listing all 13 privileges from PRD Section 5.4 as `"Read {EntityName}"` strings
  - [x] Annotate: `// F-029, F-030, F-031`
- [x] Implement `SecurityCheckService` (AC: 2–5, 6)
  - [x] Create `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckService.cs`
  - [x] Method: `Task<SecurityCheckResponse> CheckAsync(SecurityCheckRequest request)`
  - [x] Step 1: Attempt to build `EnvironmentCredentials` and call `DataverseConnectionFactory.ConnectAsync()`. On failure, return the blocked/credential-failure response (AC: 5) — catch timing, don't log credential values.
  - [x] Step 2: Query the service account's effective privileges via `RetrieveUserPrivilegesRequest` or by reading the assigned security role's `RolePrivileges`. Map results to privilege name strings.
  - [x] Step 3: Compute `passed[]`, `missing[]`, `extra[]` by comparing retrieved privileges against `RequiredPrivileges`.
  - [x] Step 4: Determine `status`: "blocked" if `missing` is non-empty; "ready" otherwise. Set `safeToRun` accordingly.
  - [x] Step 5: Build `recommendation` string:
    - If blocked: "Cannot run — [N] required permissions are missing. Please add [X] and [Y] to the DataverseDocAgent Reader role and re-run this check."
    - If ready with extras: "Tool can run safely. We recommend removing [X] to minimise risk surface."
    - If clean: "All permissions verified. Safe to run all modes."
- [x] Implement `SecurityCheckController` (AC: 1, 7)
  - [x] Create `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckController.cs`
  - [x] `[ApiController]`, `[Route("api/security")]`
  - [x] `[HttpPost("check")]` action method calls `SecurityCheckService.CheckAsync()` and returns the response
  - [x] Annotate: `// F-029, F-030, F-031 — FR-029, FR-039`
  - [x] Model validation: return `{ error: "...", code: "INVALID_REQUEST", safeToRetry: false }` if required fields are missing
- [x] Integration test with real Dataverse environment (AC: 2–5)
  - [x] Test Case 1: correctly configured DataverseDocAgent Reader role → `status: "ready"`, `missing: []`, `extra: []`
  - [x] Test Case 2: role with one privilege removed → `status: "blocked"`, `missing[]` contains the removed privilege
  - [x] Test Case 3: invalid client secret → `status: "blocked"`, credential failure message in recommendation
  - [x] Record test results in `docs/poc-baseline.md` under a "Permission Checker Test Results" section

### Review Findings

- [x] [Review][Patch] EnvironmentUrl: HTTPS-only regex + GUID validation on TenantId/ClientId [SecurityCheckRequest.cs]
- [x] [Review][Patch] Credential-failure-path test for CheckAsync via mocked IDataverseConnectionFactory [SecurityCheckServiceTests.cs]
- [x] [Review][Patch] Unhandled exceptions in Steps 2-4: wrap using(client) in try/catch for OperationCanceledException, InvalidOperationException, Exception [SecurityCheckService.cs]
- [x] [Review][Patch] CancellationToken passed to ExecuteAsync + WhoAmIRequest in ConnectAsync [SecurityCheckService.cs, DataverseConnectionFactory.cs]
- [x] [Review][Patch] SecurityCheckRequest added to CredentialDestructuringPolicy [CredentialDestructuringPolicy.cs]
- [x] [Review][Patch] GUID regex on TenantId/ClientId rejects whitespace; HTTPS regex on EnvironmentUrl rejects whitespace; MinLength(1) on ClientSecret [SecurityCheckRequest.cs]
- [x] [Review][Patch] MapPrivilegeName returns fallback for unrecognized actions (prvPublish → "PublishEntity") — extra[] no longer undercounts [SecurityCheckService.cs]
- [x] [Review][Patch] ComputePrivilegeSets parameter changed to IReadOnlyList — no double-enumeration risk [SecurityCheckService.cs]
- [x] [Review][Patch] Added "prvRead" boundary test, unrecognized-action fallback tests, improved case-insensitive value assertions [SecurityCheckServiceTests.cs]
- [x] [Review][Defer] No rate limiting on credential-accepting endpoint — credential validation oracle risk — deferred, cross-cutting concern for API-level story
- [x] [Review][Defer] TargetMode not validated against allowed values — deferred, story explicitly says accept-and-ignore for MVP
- [x] [Review][Defer] "degraded" status from PRD Section 5.5/FR-029 not implemented — deferred, not in story scope
- [x] [Review][Defer] Console project DataverseConnectionFactory diverges from API version — deferred, pre-existing
- [x] [Review][Defer] RetrieveMultipleAsync paging + ConditionOperator.In query size limits — deferred, theoretical for privilege-size sets

## Dev Notes

- **Privilege retrieval approach:** The most reliable way to check effective privileges for an application user is `RetrieveUserPrivilegesRequest` with the application user's `systemuserid`. This returns all privileges granted through any assigned role. Alternatively, retrieve the roles assigned to the application user and then query `RolePrivilege` for each role — but this misses role inheritance.
- **Application user GUID lookup:** The application user's `systemuserid` can be retrieved by querying `systemuser` where `applicationid = '{clientId}'`.
- **Privilege name matching:** Dataverse privilege names follow the pattern `prv{Action}{EntityName}` (e.g., `prvReadPluginAssembly`). Map these to the human-readable `"Read PluginAssembly"` format in `RequiredPrivileges`.
- **`targetMode` filtering:** For MVP, all three modes require identical privileges (PRD Section 5.4 note: "All three modes require identical permissions"). The `targetMode` parameter is accepted and stored but the same privilege list applies to all. Document this in the response — "All modes share the same required permissions."
- **HTTP 200 for auth failures:** The API always returns HTTP 200 (the check succeeded — it just determined the credentials are bad). HTTP 401/403 from the API itself would be confusing and incorrect.
- **Timeout:** Wrap the entire `CheckAsync` in a `CancellationToken` with 9-second timeout to ensure the 10-second NFR-002 target is met.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckRequest.cs`
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckResponse.cs`
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckService.cs` — `// F-029, F-030, F-031`
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckController.cs` — `// FR-039`

### References

- [Source: docs/prd.md#55-permission-checker-endpoint-specification] — full endpoint contract with example responses
- [Source: docs/prd.md#54-exact-permissions-required] — all 13 required privileges
- [Source: docs/prd.md#functional-requirements — FR-029, FR-030, FR-031, FR-039] — ACs in full
- [Source: docs/prd.md#7-non-functional-requirements — NFR-002] — 10-second response time
- [Source: _bmad-output/planning-artifacts/architecture.md#9-error-response-standard] — structured error format

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

None.

### Completion Notes List

- Introduced `IDataverseConnectionFactory` interface to enable unit testing without live Dataverse connection. `DataverseConnectionFactory` implements it; DI registered in Program.cs.
- Privilege retrieval: two-step — `RetrieveUserPrivilegesRequest` for GUIDs, then batch `privilege` entity query for names. `RolePrivilege` (SDK type) does not carry name; GUID-to-name lookup is required.
- `MapPrivilegeName` maps `prv{Action}{Entity}` format to `"Action Entity"` string. `AppendTo` ordered before `Append` to prevent partial match.
- Model validation uses ASP.NET Core `[Required]` annotations + `InvalidModelStateResponseFactory` → returns `StructuredErrorResponse` with HTTP 400 and code `INVALID_REQUEST`.
- AC-5 (credential failure): catches `DataverseConnectionException`, logs warning without credential values, returns HTTP 200 with `status: "blocked"`.
- AC-7 (no credentials in logs): service logs only safe messages; `CredentialDestructuringPolicy` already active in Serilog pipeline.
- Integration tests (TC-1, TC-2, TC-3) are manual — require live environment. Results recorded in `docs/poc-baseline.md`. 51 unit tests added, all pass.
- 9-second `CancellationTokenSource` inner timeout applied to `CheckAsync` to meet NFR-002 (10-second response target).

### File List

- `src/DataverseDocAgent.Api/Dataverse/IDataverseConnectionFactory.cs` (new)
- `src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs` (modified — implements IDataverseConnectionFactory, CT param)
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckRequest.cs` (new)
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckResponse.cs` (new)
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckService.cs` (new)
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckController.cs` (new)
- `src/DataverseDocAgent.Api/Program.cs` (modified — DI registration, InvalidModelStateResponseFactory)
- `tests/DataverseDocAgent.Tests/SecurityCheckServiceTests.cs` (new — 51 tests)
- `docs/poc-baseline.md` (modified — Permission Checker Test Results section added)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (modified — 2.2 → review)
