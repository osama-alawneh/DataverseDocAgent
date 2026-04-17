# Story 3.8: Dataverse Connection Factory Consolidation + Tool CancellationToken Propagation

Status: ready-for-dev

## Story

As a developer,
I want `EnvironmentCredentials`, `DataverseConnectionException`, `IDataverseConnectionFactory`, and `DataverseConnectionFactory` moved into a single shared project referenced by both `DataverseDocAgent.Api` and `DataverseDocAgent.Console`, and `IDataverseTool.ExecuteAsync` extended to accept a `CancellationToken`,
so that the duplicate copies flagged in Epic 2 retro item T3 stop drifting and the tool pipeline becomes cancellable end-to-end (PREP-4 from the Epic 2 retrospective).

> **Dependency gate:** Must complete **before Story 3.4** starts. Story 3.4 introduces new `IDataverseTool` implementations that would inherit the pre-consolidation interface and require rework. Sprint order: 3.0 → 3.1 → 3.2 → 3.3 → **3.8** → 3.4 → 3.5 → 3.6 → 3.7.

## Acceptance Criteria

1. A new project `src/DataverseDocAgent.Shared/DataverseDocAgent.Shared.csproj` exists targeting the same TFM as `Api` and `Console`, with `<Nullable>enable</Nullable>`, no executable entry point, no `Microsoft.AspNetCore.*` references.
2. The following types live in `DataverseDocAgent.Shared` under namespace `DataverseDocAgent.Shared.Dataverse`:
   - `EnvironmentCredentials` — sealed class, 4 required properties (`EnvironmentUrl`, `TenantId`, `ClientId`, `ClientSecret`), `[DebuggerBrowsable(Never)]` on `ClientSecret` preserved.
   - `DataverseConnectionException` — `Exception` subclass, `(string message, Exception? innerException = null)` ctor preserved.
   - `IDataverseConnectionFactory` — with `CancellationToken cancellationToken = default` on `ConnectAsync`.
   - `DataverseConnectionFactory` — canonical Api-side implementation preserved verbatim, including all NFR annotations (NFR-007 credential-logging prohibition comments, NFR-014 typed exception contract).
3. Both `DataverseDocAgent.Api.csproj` and `DataverseDocAgent.Console.csproj` reference `DataverseDocAgent.Shared.csproj` via `<ProjectReference>`. `DataverseDocAgent.Tests.csproj` also references it (transitive via Api is acceptable).
4. The Console project's divergent factory and types are deleted:
   - `src/DataverseDocAgent.Console/Dataverse/DataverseConnectionFactory.cs` deleted.
   - Any Console-only copy of `EnvironmentCredentials` or `DataverseConnectionException` deleted.
   - Console `Program.cs` resolves the shared factory (direct `new DataverseConnectionFactory(...)` or `IServiceCollection` bootstrap) using the shared `IDataverseConnectionFactory` interface.
   - `grep -r "namespace DataverseDocAgent.ConsoleApp" src/` returns zero hits.
5. The Api project's copies are deleted:
   - `src/DataverseDocAgent.Api/Common/EnvironmentCredentials.cs` deleted.
   - `src/DataverseDocAgent.Api/Common/DataverseConnectionException.cs` deleted.
   - `src/DataverseDocAgent.Api/Dataverse/IDataverseConnectionFactory.cs` deleted.
   - `src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs` deleted.
   - All Api consumers (`SecurityCheckService`, `Program.cs` DI registration, any controller, `AgentOrchestrator`, `ListCustomTablesTool`, `ExceptionHandlingMiddleware` if applicable) use `using DataverseDocAgent.Shared.Dataverse;`.
   - DI registration in `src/DataverseDocAgent.Api/Program.cs` still binds `IDataverseConnectionFactory → DataverseConnectionFactory` — now pointing at shared types.
6. `IDataverseTool.ExecuteAsync` signature changes from `Task<string> ExecuteAsync(JsonElement input)` to `Task<string> ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default)`. The default value keeps existing callers compilable but all call sites must be updated to pass a real token.
7. `ListCustomTablesTool.ExecuteAsync` accepts the token. Where it calls into synchronous `IOrganizationService.Execute`, a comment documents that SDK-level cancellation is not yet honoured and references the existing deferred item from story 1.3 (F4).
8. The single call site `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs:~106` passes the orchestrator's existing `CancellationToken` through to `tool.ExecuteAsync(inputElement, cancellationToken)` — not `default`.
9. All 68 currently-passing tests continue to pass with no behaviour change.
10. A new unit test verifies `ListCustomTablesTool.ExecuteAsync` accepts a `CancellationToken` parameter and does not throw when a non-default token is passed (even if the SDK call itself cannot observe cancellation).
11. A new unit test verifies Serilog destructuring of the shared `EnvironmentCredentials` via `CredentialDestructuringPolicy` still redacts `ClientSecret`.
12. `dotnet build DataverseDocAgent.sln --no-incremental` produces zero warnings and zero errors.
13. `dotnet test` returns green across the full suite.
14. `grep -r "class DataverseConnectionFactory" src/` returns exactly one file path (the shared project copy).

## Tasks / Subtasks

- [ ] Scaffold shared project (AC: 1, 3)
  - [ ] Create `src/DataverseDocAgent.Shared/DataverseDocAgent.Shared.csproj`
    - `<TargetFramework>net8.0</TargetFramework>` (match Api/Console)
    - `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`
    - `<PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.2.10" />` (version must match current Api reference — pin exactly)
  - [ ] Add project to `DataverseDocAgent.sln` in a `src` solution folder
  - [ ] Add `<ProjectReference>` in `DataverseDocAgent.Api.csproj`
  - [ ] Add `<ProjectReference>` in `DataverseDocAgent.Console.csproj`
  - [ ] Verify `DataverseDocAgent.Tests.csproj` can resolve shared types (add `<ProjectReference>` if needed)
- [ ] Move types into shared project (AC: 2)
  - [ ] Copy `src/DataverseDocAgent.Api/Common/EnvironmentCredentials.cs` → `src/DataverseDocAgent.Shared/Dataverse/EnvironmentCredentials.cs`
    - Update namespace to `DataverseDocAgent.Shared.Dataverse`
    - Preserve `[DebuggerBrowsable(DebuggerBrowsableState.Never)]` on `ClientSecret`
    - Preserve NFR-007 comments
  - [ ] Copy `src/DataverseDocAgent.Api/Common/DataverseConnectionException.cs` → `src/DataverseDocAgent.Shared/Dataverse/DataverseConnectionException.cs`
    - Update namespace
  - [ ] Copy `src/DataverseDocAgent.Api/Dataverse/IDataverseConnectionFactory.cs` → `src/DataverseDocAgent.Shared/Dataverse/IDataverseConnectionFactory.cs`
    - Update namespace
    - Signature: `Task<IOrganizationServiceAsync2> ConnectAsync(EnvironmentCredentials credentials, CancellationToken cancellationToken = default)`
  - [ ] Copy `src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs` → `src/DataverseDocAgent.Shared/Dataverse/DataverseConnectionFactory.cs`
    - Update namespace
    - Preserve all NFR annotations and credential-stripping `catch` blocks verbatim
- [ ] Delete Api-side duplicates (AC: 5)
  - [ ] Delete `src/DataverseDocAgent.Api/Common/EnvironmentCredentials.cs`
  - [ ] Delete `src/DataverseDocAgent.Api/Common/DataverseConnectionException.cs`
  - [ ] Delete `src/DataverseDocAgent.Api/Dataverse/IDataverseConnectionFactory.cs`
  - [ ] Delete `src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs`
  - [ ] Update all `using DataverseDocAgent.Api.Common;` / `using DataverseDocAgent.Api.Dataverse;` references to `using DataverseDocAgent.Shared.Dataverse;`
    - Known touch points: `SecurityCheckService`, `Program.cs` (DI), `AgentOrchestrator`, `ListCustomTablesTool`, `ExceptionHandlingMiddleware` (if it references `EnvironmentCredentials`), `CredentialDestructuringPolicy`, `SecurityCheckController`
- [ ] Delete Console-side duplicates (AC: 4)
  - [ ] Delete `src/DataverseDocAgent.Console/Dataverse/DataverseConnectionFactory.cs`
  - [ ] Delete any Console-only `EnvironmentCredentials.cs` / `DataverseConnectionException.cs` (grep to confirm whether they exist)
  - [ ] Update Console `Program.cs` to import `using DataverseDocAgent.Shared.Dataverse;` and resolve the shared factory
  - [ ] Grep `src/` for `namespace DataverseDocAgent.ConsoleApp` — expect zero hits
- [ ] Propagate CancellationToken through IDataverseTool (AC: 6, 7, 8)
  - [ ] Edit `src/DataverseDocAgent.Api/Agent/Tools/IDataverseTool.cs`
    - Change `Task<string> ExecuteAsync(JsonElement input);` → `Task<string> ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default);`
  - [ ] Edit `src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs`
    - Update `ExecuteAsync` signature to accept `CancellationToken cancellationToken`
    - Add comment near the synchronous `IOrganizationService.Execute` call: `// F4 (story 1.3 deferred) — SDK does not expose async/cancellable Execute overload in v1.2.10. Token accepted for pipeline symmetry; not observed at SDK boundary.`
  - [ ] Edit `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs` (~line 106)
    - Change `tool.ExecuteAsync(inputElement)` → `tool.ExecuteAsync(inputElement, cancellationToken)`
    - Confirm the method's existing `CancellationToken` parameter is plumbed through
- [ ] Write new unit tests (AC: 10, 11)
  - [ ] `tests/DataverseDocAgent.Tests/ListCustomTablesToolTests.cs`
    - Test: `ExecuteAsync_WithCancellationToken_DoesNotThrowWhenTokenPassed`
    - Pass a `new CancellationTokenSource().Token` — assert the method accepts it without compile-time or runtime rejection
  - [ ] `tests/DataverseDocAgent.Tests/CredentialDestructuringPolicyTests.cs` (create or extend existing)
    - Test: `Destructure_SharedEnvironmentCredentials_RedactsClientSecret`
    - Build an `EnvironmentCredentials` from `DataverseDocAgent.Shared.Dataverse` namespace, invoke the policy, assert `ClientSecret` is redacted
- [ ] Verify build and tests (AC: 9, 12, 13, 14)
  - [ ] `dotnet build DataverseDocAgent.sln --no-incremental` — zero warnings, zero errors
  - [ ] `dotnet test` — all tests green (69+ after the two new tests)
  - [ ] `grep -r "class DataverseConnectionFactory" src/` — exactly one file
  - [ ] `grep -r "DataverseDocAgent.Api.Common.EnvironmentCredentials" src/` — zero hits

## Dev Notes

- **Shared project scope is narrow on purpose.** Only Dataverse connection plumbing moves in this story. `AgentOrchestrator`, tools, Serilog config, `StructuredErrorResponse`, and middleware stay where they are. Pulling more into shared is out of scope — the retro item T3 is specifically about the duplicated connection factory.
- **No `Microsoft.AspNetCore.*` in shared.** The shared project must not reference ASP.NET Core. If a type being moved incidentally depends on `IHttpContextAccessor` or similar, stop and flag — that is a signal the boundary is wrong.
- **Namespace choice `DataverseDocAgent.Shared.Dataverse`.** Flat under `Shared.` is preferred over nested `Shared.Common.` or `Shared.Dataverse.Connection.` — keeps imports short. If later shared types cluster by concern (e.g., `Shared.Jobs`), the Dataverse ones stay put.
- **CancellationToken on `IDataverseTool.ExecuteAsync` — default value kept.** Using `CancellationToken cancellationToken = default` rather than a breaking-no-default change means future tools that forget to pass the token compile, but the orchestrator call site is updated explicitly. This matches the PREP-4 note: surface the parameter now so new Story 3.4 tools cannot ship without it.
- **SDK cancellation limitation.** `IOrganizationService.Execute` is synchronous in the current `Microsoft.PowerPlatform.Dataverse.Client` package. The token is accepted for pipeline symmetry; it won't cancel an in-flight SDK call until `IOrganizationServiceAsync2` adoption lands. The comment in `ListCustomTablesTool` must say this explicitly — do not imply cancellation works.
- **`CredentialDestructuringPolicy` rewrite risk.** The policy likely matches `EnvironmentCredentials` by type reference, not string name. After the namespace move, recompile-and-test confirms whether the policy still attaches. If Serilog matches by fully-qualified type name internally, the new test in AC 11 catches any regression.
- **`Program.cs` DI line.** The line `builder.Services.AddScoped<IDataverseConnectionFactory, DataverseConnectionFactory>();` keeps the same shape — only the `using` statement at the top of `Program.cs` changes.
- **Console bootstrap option.** Console currently constructs the factory directly (no DI). Simplest migration: `using DataverseDocAgent.Shared.Dataverse;` and `new DataverseConnectionFactory(logger)`. DI in Console is out of scope unless trivial.
- **Test project reference.** If the tests currently reference types via `DataverseDocAgent.Api.Common`, update the `using` statements. Transitive through `<ProjectReference Include="...Api.csproj" />` should still resolve shared types since Api references Shared.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Shared/DataverseDocAgent.Shared.csproj`
- `src/DataverseDocAgent.Shared/Dataverse/EnvironmentCredentials.cs`
- `src/DataverseDocAgent.Shared/Dataverse/DataverseConnectionException.cs`
- `src/DataverseDocAgent.Shared/Dataverse/IDataverseConnectionFactory.cs`
- `src/DataverseDocAgent.Shared/Dataverse/DataverseConnectionFactory.cs`
- `tests/DataverseDocAgent.Tests/ListCustomTablesToolTests.cs`
- `tests/DataverseDocAgent.Tests/CredentialDestructuringPolicyTests.cs` (or extension of existing)

Files deleted:
- `src/DataverseDocAgent.Api/Common/EnvironmentCredentials.cs`
- `src/DataverseDocAgent.Api/Common/DataverseConnectionException.cs`
- `src/DataverseDocAgent.Api/Dataverse/IDataverseConnectionFactory.cs`
- `src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs`
- `src/DataverseDocAgent.Console/Dataverse/DataverseConnectionFactory.cs`
- Any Console-only `EnvironmentCredentials.cs` / `DataverseConnectionException.cs` (confirmed by grep)

Files edited:
- `DataverseDocAgent.sln` — add `DataverseDocAgent.Shared` project
- `src/DataverseDocAgent.Api/DataverseDocAgent.Api.csproj` — add `<ProjectReference>`
- `src/DataverseDocAgent.Console/DataverseDocAgent.Console.csproj` — add `<ProjectReference>`
- `src/DataverseDocAgent.Api/Program.cs` — update `using` + DI registration
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckService.cs` — update `using`
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckController.cs` — update `using` (if references the types)
- `src/DataverseDocAgent.Api/Common/CredentialDestructuringPolicy.cs` — update `using` / type references
- `src/DataverseDocAgent.Api/Middleware/ExceptionHandlingMiddleware.cs` — update `using` (if references the types)
- `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs` — pass `cancellationToken` to `tool.ExecuteAsync`
- `src/DataverseDocAgent.Api/Agent/Tools/IDataverseTool.cs` — add `CancellationToken` parameter
- `src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs` — accept token + SDK-limitation comment
- `src/DataverseDocAgent.Console/Program.cs` — update `using` + factory construction
- `tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj` — add `<ProjectReference>` to Shared (if needed)

### References

- Epic 2 retrospective: `_bmad-output/implementation-artifacts/epic-2-retro-2026-04-17.md`
  - T3 — DataverseConnectionFactory duplication between Api and Console
  - PREP-4 — CancellationToken propagation through `IDataverseTool.ExecuteAsync`
- Story 1.2 deferred: `_bmad-output/implementation-artifacts/deferred-work.md` — "Duplicate class divergence risk" and "No CancellationToken support in ConnectAsync"
- Story 1.3 deferred: F4 — `ListCustomTablesTool.ExecuteAsync` wraps synchronous `IOrganizationService.Execute` in `Task.FromResult`
- Epic 3 plan: `_bmad-output/planning-artifacts/epics.md` Story 3.8 entry

## Out of Scope

- Adopting `IOrganizationServiceAsync2` to honour `CancellationToken` at the SDK boundary. Tracked separately; this story only surfaces the token.
- Introducing DI to the Console project beyond what consolidation requires.
- Adding Central Package Management (`Directory.Packages.props`). Deferred item from story 1.1; not required for a three-project solution but revisit if a fourth project version-drifts.
- Moving `AgentOrchestrator`, tools, `StructuredErrorResponse`, Serilog config, or middleware into shared. Out of scope for T3.

## Definition of Done

- [ ] All ACs met.
- [ ] Build clean (zero warnings, zero errors).
- [ ] All tests green (existing 68 + new 2 = 70 minimum).
- [ ] Grep checks pass: one `DataverseConnectionFactory` file; zero `DataverseDocAgent.ConsoleApp` namespace hits; zero `DataverseDocAgent.Api.Common.EnvironmentCredentials` references.
- [ ] Story 3.4 can be started without further consolidation work.
- [ ] `deferred-work.md` updated to close T3 and PREP-4 entries.
