# Deferred Work

## Deferred from: code review of story-3.4-dataverse-tools (2026-04-20)

- **Async SDK execution for `IOrganizationService.Execute`.** All three Mode 1 tools wrap a synchronous `_service.Execute(...)` inside an `async` method; the underlying SDK call blocks a thread-pool thread for the round-trip duration. `IOrganizationServiceAsync2` exposes `ExecuteAsync(OrganizationRequest, CancellationToken)` and is implemented by `Microsoft.PowerPlatform.Dataverse.Client.ServiceClient`, but switching requires changing the `IDataverseTool` constructor surface (the tools take `IOrganizationService` for ease of mocking) and the `IDataverseConnectionFactory` return type. Defer until thread-pool exhaustion shows up in load testing — the unit-test mock surface (`Mock<IOrganizationService>`) is significantly simpler than `Mock<IOrganizationServiceAsync2>` and Phase 1 concurrency is bounded at NFR-011 = 3.
- **`RetrieveMetadataChanges` paging not implemented.** `GetTableFieldsTool` and `ListCustomTablesTool` both issue a single `RetrieveMetadataChangesRequest` and read the first page only; the SDK returns a `DeletedMetadataFilters`-style continuation token (`response.ServerVersionStamp`) when the result set exceeds Dataverse's per-call cap (~5000 rows for metadata changes). For a tenant with thousands of custom attributes on one table this would silently truncate. Defer until a real environment surfaces the cap; document by adding a paging branch when `response.ServerVersionStamp != null`.
- **`ListCustomTablesTool` stderr `Console.Error.WriteLine` on fault path was removed.** Story 1.3 deferred entry F-noted "Console.Error.WriteLine bypasses Serilog redaction"; the new fault catch in `ListCustomTablesTool` returns a structured JSON error and does not re-introduce stderr logging. Cross-cutting structured-logging on tool failures is still pending — defer to the observability story alongside the Story 3.1 sanitized-error-with-code work.
- **No orchestrator-level integration test exercising the new tool factory.** `DataverseToolFactory.CreateMode1Tools` returns three `IDataverseTool` instances and `AgentOrchestrator.RunAsync` already accepts `IEnumerable<IDataverseTool>`, but there is no test that wires a factory output through the orchestrator and asserts the tool-use loop dispatches to all three. Story 3.5 will exercise this path end-to-end (`POST /api/document/generate`); adding a synthetic orchestrator integration test before that point would duplicate Story 3.5's coverage.
- **Factory thread-safety not documented at the public API.** `CreateMode1Tools` returns a fresh `IReadOnlyList<IDataverseTool>` per call and each tool stores `_service` in a private field, so concurrent calls with different services produce independent tool instances. The XML doc-comment on `DataverseToolFactory` does not call this out, so a future contributor could be tempted to cache the list across requests (which would re-use the wrong `IOrganizationService`). Add a remarks block when the factory is consumed by Story 3.5's orchestrator wiring.
- **Reflection-based test setup pattern (`SetNonPublic` / `SetLabel`) is not extracted into a shared helper.** Three test files (`GetTableFieldsToolTests`, `GetRelationshipsToolTests`, `ListCustomTablesToolTests`) each carry their own `SetLabel`/`SetNonPublic` reflection helpers because the SDK's `EntityMetadata`/`AttributeMetadata`/`RelationshipMetadataBase` setters are internal. Extracting to a `Tests/Helpers/SdkMetadataBuilder.cs` would dedupe ~40 lines and centralise the brittle "if SDK sealed these properties differently in a major version, here's where to fix it" surface. Defer until a fourth tool-test file lands or a Microsoft Graph SDK update changes the setter visibility.
- **No Unicode / locale-variation tests on `DisplayName` / `Description` / option labels.** All current tests use ASCII labels. The `UserLocalizedLabel` fallback to `LocalizedLabels.FirstOrDefault()` could in principle pick a label in a non-en-US locale when the user-localized variant is null; on a multi-language tenant that selects the wrong language. Defer until a real multi-language tenant is available; add a parameterised test that varies `LanguageCode` then.
- **No test asserting `IsCustomRelationship == false` is filtered out of `GetRelationshipsTool` output.** The defensive filter is implemented but only positive-case tests exist. A regression that drops the `IsCustomRelationship` check would not be caught by the unit suite. Defer — the SDK contract for `IsCustomRelationship` is unambiguous and a regression would surface in Story 3.5's E2E test against a real environment with both custom and OOTB relationships.
- **Prompt-injection defence: no length cap on `tableName` parameter before regex validation.** The `s_logicalNameShape` regex (`^[a-z][a-z0-9_]*$`) bounds the character set but not length; a 100kB string of lowercase letters still passes shape validation and would hit the SDK with a guaranteed-fail query. Defer — Dataverse's logical-name limit is 50 chars; add a `MaxLength(50)` guard when input-validation hardening lands as a cross-cutting story (likely alongside the Story 3.5 request DTO validation pass).

## Deferred from: code review of story-3.3-download-endpoint (2026-04-20)

- **Per-route rate limiting on `/api/download/{token}`.** Story 3.0 wired rate-limiting on credential-accepting endpoints; the download route is currently uncapped. A 32-hex token gives 128 bits of entropy so blind enumeration is infeasible, but defence-in-depth (per-IP partition with a coarse limit) would catch a noisy/abusive client and bound log volume. Defer until a security/observability story consolidates download-side throttling — premature wiring risks accidentally throttling legitimate polling clients.
- **Token in URL leaks via referrer / proxy / access logs.** Putting the 24-hour bearer token in the URL path means it persists in browser history, intermediary access logs, and any HTTP-level telemetry sink. Two mitigations possible: (a) move to `Authorization: Bearer <token>` header, or (b) keep URL but make tokens single-use (delete on first successful retrieve). Both are spec changes — defer to a download-hardening story.
- **Stream large `.docx` via `FileStreamResult` instead of materialising `byte[]`.** Current `IDocumentStore.RetrieveAsync` returns `byte[]`, so the controller cannot stream without changing the store contract and every implementation (`InMemoryDocumentStore`, `BlobDocumentStore` stub). Today's docs are well under a few MB; revisit at Phase 2 when blob-backed storage lands and large environments push report sizes higher.
- **Audit log on every download attempt (success + miss).** No structured log entry currently records who/what/when for a download; both the success path and the TOKEN_EXPIRED path are silent. Useful for forensic analysis of token-leak incidents. Defer to the cross-cutting observability story — adding ad-hoc logging here would diverge from the project's logging conventions.
- **Reconcile PRD FR-040 (HTTP 404) vs story AC-2 (HTTP 200 + structured body).** PRD `docs/prd.md` FR-040 still says "Returns HTTP 404 for an expired or invalid token", but story 3.3 explicitly overrode this to HTTP 200 with `StructuredErrorResponse` (rationale: HTTP client libraries throw on 4xx, and the API-wide error contract uses 200 + body). Not a code defect; the diff correctly follows the story. Open a PRD-edit task to capture the decision so future audits don't flag it again.
- **Constant-time token comparison / timing side-channel.** `IMemoryCache.TryGetValue` is not a constant-time lookup, so a sufficiently sophisticated attacker could in principle measure hit/miss timing. Negligible against 128-bit GUIDs but technically a side-channel; revisit if/when the token surface narrows (e.g., shorter tokens, secret-prefixed tokens).
- **Verify token never leaks into routing/hosting structured logs.** `Microsoft.AspNetCore.Routing` is currently set to `Warning`, so route-template values do not surface at Information level. A future logging-config edit (or a third-party middleware that logs request paths at Information+) could re-introduce the token into log sinks. Add an automated check or a logging-policy doc.

## Deferred from: code review of story-3.2-document-store (2026-04-17)

- **`BlobDocumentStore` accidental-registration guard conflicts with AC-3.** A developer who later adds `AddSingleton<IDocumentStore, BlobDocumentStore>()` below the Phase 1 line wins the last-registration slot and every request 500s with `NotImplementedException`. A compile-time guard — `[Obsolete("Phase 2+ stub", error: true)]` or `internal` visibility — would block the mistake but require a corresponding attribute removal in `BlobDocumentStore.cs` at Phase 2 swap time, violating AC-3's "single DI registration line change — no other code changes." Deferred: when Phase 2 work starts, either accept the two-line swap (and treat AC-3 as "Program.cs-only change") or drop the guard; do not take both. Risk is bounded in the interim because the stub lives in a single file clearly named as Phase 2+.
- **Expiry test is wall-clock timing.** `RetrieveAsync_AfterTtlElapsed_ReturnsNull` uses a 50 ms TTL + 250 ms `Task.Delay`. On a contended CI runner (CPU-starved xUnit parallel workers, stop-the-world GC pause) the 200 ms slack can slip. `IMemoryCache` in .NET 8 accepts a `TimeProvider` via `MemoryCacheOptions.Clock` — once `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider` is approved as a test dependency (currently not referenced anywhere in the repo), rewrite to advance virtual time across the TTL boundary and eliminate wall-clock entirely. Low priority until CI flake is observed.
- **No `SizeLimit` + no per-entry `Size`.** `AddMemoryCache()` uses default `MemoryCacheOptions` (`SizeLimit == null`), so the cache accepts arbitrarily large/many documents — an OOM vector if the generation pipeline regresses into a loop or an abusive caller slips past rate-limiting. Conversely, if a future change sets `SizeLimit` without also setting `options.Size = documentBytes.Length` inside `StoreAsync`, `MemoryCache` silently refuses the entry: `StoreAsync` returns a token that `RetrieveAsync` will never resolve. Defer both halves to Phase 2 hardening: set `MemoryCacheOptions.SizeLimit` based on realistic .docx volume (e.g. 256 MB) and wire `.SetSize(documentBytes.Length)` on every entry. Spec Dev Notes explicitly waived size limits for Phase 1 under NFR-011 (3 concurrent).

## Deferred from: code review of story-3.1-async-job-infrastructure (2026-04-17)

- **Unbounded `Channel<GenerationTask>` + credential lifetime.** `Channel.CreateUnbounded` never throttles producers and each `GenerationTask` pins a live `EnvironmentCredentials`. Real Story 3.5 pipelines target up to 10 minutes per job, so queue depth × pipeline duration is an in-memory credential residency that isn't bounded. Consider `CreateBounded` with `BoundedChannelFullMode.Wait` and a `JOB_QUEUE_FULL` structured-error path, sized against NFR-011 (3 concurrent). Flag during Story 3.5 when the real pipeline lands and the "held in memory for minutes, not seconds" regime becomes operational.
- **Host-shutdown drain strategy for in-flight Running jobs.** If `stoppingToken` fires mid-pipeline, `OperationCanceledException` re-throws (correctly, to let `BackgroundService` stop cleanly). The `JobRecord` remains in `Running` and is never flushed to `Failed`/`Cancelled`. In-memory store is lost on restart today, but once a persistent store lands the zombies surface on boot. Add a `Cancelled` status + finally-block flush, or an orphan-reaper on startup.
- **`JobRecord` has no `ErrorCode`/`SafeToRetry` fields.** NFR-014 requires structured errors with `code` + `safeToRetry` everywhere. Story 3.1's sanitized `ErrorMessage` preserves NFR-007 but flattens the taxonomy architecture §9 defines (`CREDENTIAL_REJECTED`, `PERMISSION_MISSING`, `GENERATION_TIMEOUT`, …). Story 3.5 must extend `JobRecord` + `JobStatusResponse` with nullable `code`/`safeToRetry` populated from typed pipeline exceptions, without re-introducing raw `ex.Message` echoing.
- **Polling rate-limit for `GET /api/jobs/{jobId}`.** Correctly excluded from Story 3.0's credential-endpoint scope. Unbounded read of an in-memory dictionary is cheap today, but once the store is backed by a cache or database, abusive polling becomes a cost vector. Consider a separate "polling" rate-limit policy in Phase 3, partitioned by jobId (self-limiting — only the owner has the id).
- **`JobStatus` enum → JSON mapping is a `ToString().ToLowerInvariant()`.** Works for current single-word members; breaks readability for future `CredentialFailure` / `Timeout` (would emit `credentialfailure`). Swap for `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` on a serialized enum field when adding new members.
- **`ChannelReader<T>` / `ChannelWriter<T>` DI split.** Producer and consumer currently both take `Channel<GenerationTask>` — either can call `Complete()`. Not load-bearing today; worth tightening when Story 3.5 adds the producer controller.
- **No contract test for `IGenerationPipeline` cancellation honouring.** A Story 3.5 implementation that ignores `cancellationToken` could leave the background service unresponsive to shutdown. Add a contract test alongside the real pipeline.

## Deferred from: code review of story-3.0-rate-limiting (2026-04-17)

- **Reverse-proxy / null-`RemoteIpAddress` partition collapse.** Behind an LB/proxy, every request carries the proxy IP as `RemoteIpAddress`; null-IP request hosts collapse to the literal `"unknown"` bucket. A single noisy tenant denies service to the whole upstream cohort. P3 deployment story must add `Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders` middleware (before `UseRateLimiter`) and/or move partitioning to issued API keys per ADR-009. Null-IP behaviour is now covered by `PartitionedLimiter_NullRemoteIpAddress_CollapsesToUnknownBucket` to lock the current semantics.
- **Sprint-level dependency gate on Story 3.5 → done (AC-11).** No code enforces this. Story 3.5 code-review checklist must refuse `done` if Story 3.0 is not `done`.
- **Window-replenishment / boundary-concurrency test.** `AutoReplenishment = true` on the fixed-window limiter has no direct unit coverage — a regression to `AutoReplenishment = false` would still pass all current tests. Intentionally out of scope for story 3.0 (not in AC-10); revisit if rate-limit regressions surface or when Phase 3 swaps partitioning to API keys.
- **`/api/health` exemption is implicit, not asserted by test.** Endpoint has no `[EnableRateLimiting]` and no `GlobalLimiter` is configured, so it is exempt today. One future global-limiter flip silently throttles health checks. Add a `DisableRateLimiting`-style lock or an assertion test when Phase 3 introduces a global policy.

## Resolved: story 3.8 dataverse-connection-factory consolidation (2026-04-17)

Commit `d40df56` (+ review patches) closes the following previously deferred items by moving `EnvironmentCredentials`, `DataverseConnectionException`, `IDataverseConnectionFactory`, and `DataverseConnectionFactory` into a single shared project `DataverseDocAgent.Shared` (namespace `DataverseDocAgent.Shared.Dataverse`) and by extending `IDataverseTool.ExecuteAsync` with a `CancellationToken` parameter that `AgentOrchestrator` propagates:

- **Story 1.2 — "Duplicate class divergence risk."** Resolved. Single canonical `DataverseConnectionFactory` under Shared; Api, Console, and Tests all reference it. Grep `class DataverseConnectionFactory` in `src/` returns exactly one hit.
- **Story 1.2 — "No CancellationToken support in ConnectAsync."** Resolved. `IDataverseConnectionFactory.ConnectAsync` now takes `CancellationToken cancellationToken = default`; the implementation propagates it to `ServiceClient.ExecuteAsync(WhoAmIRequest, ct)` and rethrows `OperationCanceledException` without wrapping so hosted-service cancellation semantics remain intact.
- **Story 1.3 — "D1: CancellationToken not propagated to IDataverseTool.ExecuteAsync."** Resolved. `IDataverseTool.ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default)`; `AgentOrchestrator` passes its existing `ct` through; `ListCustomTablesTool` observes the token at the pre-SDK boundary via `ThrowIfCancellationRequested()`. (Story 1.3 F4 — SDK-level cancellation — remains open and is called out in `ListCustomTablesTool.cs` with a direct reference to this deferred item.)
- **Story 2.2 — "Console project `DataverseConnectionFactory` does not implement `IDataverseConnectionFactory` and lacks `CancellationToken` parameter."** Resolved. The divergent Console copy and its `DataverseDocAgent.ConsoleApp.*` namespaces are deleted; Console now constructs the shared factory (`new DataverseConnectionFactory()`) and receives the interface contract for free.

The following remain deferred and are not addressed by this story:

- Credentials stored as plain managed strings (story 1.2) — unchanged, .NET language-level limitation.
- `ServiceClient` constructor is synchronous despite async caller (story 1.2) — unchanged, SDK-level limitation.
- No `[JsonIgnore]` / serialization guard attributes on `EnvironmentCredentials` (story 1.2) — unchanged, documentation-only contract.
- `EnvironmentUrl` scheme/format not validated in `Console/Program.cs` (story 1.2) — unchanged.
- Story 1.3 F1/F2/F3/F5/F6 — unchanged, all POC-layer concerns.
- Story 1.3 F4 — `ListCustomTablesTool` wraps the synchronous `IOrganizationService.Execute` — acknowledged in code comment, SDK boundary fix deferred.

## Resolved: T1 middleware body-logging audit (2026-04-17)

Retro item T1 (`ExceptionHandlingMiddleware` body-logging audit) audited in this session via `bmad-code-review`. Findings:

- `ExceptionHandlingMiddleware.InvokeAsync` does **not** read `context.Request.Body` on exception paths — only `_logger.LogError(ex, <literal>)` is called (verified by `ExceptionHandlingMiddlewareTests`, all three cases pass).
- `CredentialDestructuringPolicy` redacts `EnvironmentCredentials` and `SecurityCheckRequest` when destructured via `{@X}` placeholders.
- `UseSerilogRequestLogging()` is **not** registered → no automatic request-property enrichment that could embed bound action arguments.
- `InvalidModelStateResponseFactory` returns validation error messages only, not attempted field values.
- `DataverseConnectionFactory` strips inner SDK exceptions; `SecurityCheckService` outer-catch returns only sanitized recommendation strings.
- **Gap fixed:** `Microsoft.PowerPlatform.Dataverse.Client` and `Anthropic` logger namespaces were not clamped. In Development (`MinimumLevel=Debug`) these SDKs could emit authority URLs / tenant IDs / payload bytes at Info/Debug. Added `.MinimumLevel.Override(..., LogEventLevel.Warning)` for both in `Program.cs`.

T1 removed from deferred list. Remaining 2.4 items retained below.

## Deferred from: code review of story-2.4-setup-guide-privacy (2026-04-17)

- `credentials.TenantId` validated via GUID regex on `SecurityCheckRequest` but silently ignored by `DataverseConnectionFactory.ConnectAsync` — `ServiceClient(Uri, clientId, clientSecret, useUniqueInstance)` overload infers tenant from the environment URL. Pre-existing since story 1.2; field is required in API surface but has no effect on authentication. Either remove from required body or wire through.
- `POST /api/security/check` error responses may echo parts of the credential via SDK exception messages (factory already strips inner exceptions, but controller-level error mapping is unreviewed for secret-adjacent content).
- GDPR categorical "not a Data Processor" claim in `docs/privacy-policy.md:90-92` — grounded in PRD NFR-012 but legally risky; security-role and workflow payloads can embed user display names / email addresses. Hold pending legal review before public launch.

## Deferred from: code review of story-2.3-security-role-artefact (2026-04-16)

- No CI/automated validation for zip integrity — no CI infrastructure exists yet; hand-rolled solution XML has no automated check that it remains importable after edits.
- Three independent sources of truth for privilege list (README table, PRD Section 5.4, customizations.xml) — inherent to having documentation alongside implementation. SecurityCheckService.RequiredPrivileges is the authoritative code list.
- `setup-guide.md` forward reference in artefacts/README.md is a dead link until Story 2.4 creates the file. README notes this explicitly.
- Privilege name risk if Microsoft renames internal `prv*` privilege names in a future Dataverse platform update — MapPrivilegeName() is case-insensitive but has no fuzzy matching or version-aware mapping.

## Deferred from: code review of story-2.2-permission-checker (2026-04-16)

- No rate limiting on `POST /api/security/check` — endpoint accepts raw credentials and connects to external Dataverse environments; usable as credential validation oracle or outbound proxy. Cross-cutting concern; address at API-level with `Microsoft.AspNetCore.RateLimiting` middleware.
- `TargetMode` not validated against allowed values ("mode1"|"mode2"|"mode3"|"all") — story explicitly says accept-and-ignore for MVP since all modes share identical privileges.
- `"degraded"` status from PRD Section 5.5 / FR-029 not implemented — story scope only covers `"ready"` and `"blocked"`. Document as known gap; implement when mode-specific privilege requirements diverge.
- ~~Console project `DataverseConnectionFactory` does not implement `IDataverseConnectionFactory` and lacks `CancellationToken` parameter~~ — resolved by story 3.8; see "Resolved: story 3.8" section above.
- `RetrieveMultipleAsync` does not handle paging and `ConditionOperator.In` may hit Dataverse query size limits with very large privilege sets — theoretical for standard security roles (100-300 privileges). Monitor in integration testing.

## Deferred from: code review of story-2.1-api-project-setup (2026-04-16)

- Health endpoint `AllowAnonymous` not annotated — auth middleware not yet registered; when added, `/api/health` must be explicitly excluded or it silently requires auth. Annotate with `AllowAnonymous` equivalent when auth is wired up.
- Silent failure in `AgentOrchestrator` — tool exceptions caught and returned as error strings with no `_logger.LogError()` call; failures invisible in structured logs. Pre-existing from story 1.x.
- `Console.Error.WriteLine` in `AgentOrchestrator` and `ListCustomTablesTool` — bypasses Serilog credential redaction and structured logging. Pre-existing from story 1.x.
- NFR-015 annotations missing on pre-existing tool/service files (`AgentOrchestrator.cs`, `IDataverseTool.cs`, `ListCustomTablesTool.cs`). Pre-existing; address in Phase 2 hardening sweep.
- `DataverseConnectionFactory` uses `// F-034 — ... (NFR-007)` instead of a direct `// NFR-xxx` annotation. Non-conforming per NFR-015. Pre-existing.

## Deferred from: code review of story-1.1-project-scaffold (2026-04-13)

- `appsettings.Development.json` committed-and-ignored creates operational confusion. Both AC 4 (file exists) and AC 5 (in .gitignore) are literally met, but the pattern invites surprise. Document onboarding pattern in a future story.
- Empty `Anthropic:ApiKey` / `Dataverse:TestCredentials` keys are committed in `appsettings.json` and invite developers to paste real credentials there instead of using user-secrets. Add an onboarding doc.
- Console project sets `<UserSecretsId>` but has no `Microsoft.Extensions.Configuration.*` packages — there is no way to actually load secrets. Add `Microsoft.Extensions.Configuration`, `.Json`, `.UserSecrets`, `.Binder` in Story 1.2 when the Phase 1 POC needs config loading.
- Console project has no `appsettings.json`. Add in Story 1.2 when first config binding is needed.
- No `Directory.Build.props` / `Directory.Packages.props` (Central Package Management). Add when a third project lands to prevent NuGet version drift between Console and Api.
- No `tests/` solution folder slot in `DataverseDocAgent.sln`. Add when the first test project arrives so VS doesn't render inconsistently.
- `UserSecretsId` only auto-loads when `EnvironmentName == Development`. Document for any future story that introduces `Staging` / `Production` environments — secrets will silently stop loading.
- `Nullable enable` + `dnlib` (no NRT annotations) will require `!` workarounds in Story 2.x plugin scan code. Known paper-cut.
- Project namespace `DataverseDocAgent.Console` collides with `System.Console`. Any future code in that assembly that uses `Console.WriteLine` needs `global::System.Console` or a using alias. Footgun for Story 1.2+.
- `appsettings.Development.json` "commented examples" task wording is impractical — JSON has no native comment syntax. Revisit AC wording in epic-1 retrospective.

## Deferred from: code review of story-1.4-poc-baseline.md (2026-04-15)

- W1: "Both runs completed successfully end-to-end" — claim is backed only by absence of exception, not by return-value or sentinel check. Sentinel case (MaxIterations hit) did not occur in the recorded runs so the claim is technically accurate. Defer until sentinel handling is implemented in a future story.
- D3: SDK ex.Message risk — Program.cs catch blocks print ex.Message; no assessment of whether Anthropic SDK or DataverseConnectionException can embed credential-adjacent values in messages. Deferred to Phase 2 hardening; POC-only scope.

## Deferred from: code review of story-1.3-claude-agent-poc (2026-04-15)

- ~~D1: CancellationToken not propagated to IDataverseTool.ExecuteAsync~~ — resolved by story 3.8; see "Resolved: story 3.8" section above.
- F1: ExtractText returns only first TextContent block — multi-block Claude responses silently drop remaining content. POC scope limitation; revisit when response aggregation matters.
- F2: FindTool uses StringComparison.Ordinal — a case mismatch from Anthropic API would silently return unknown-tool error. API always returns exact registered names; harden in Phase 2.
- F3: dnlib referenced in DataverseDocAgent.Api.csproj with no visible usage in story 1.3 — planned for future stories; audit and remove if still unused after epic 3.
- F4: ListCustomTablesTool.ExecuteAsync wraps synchronous IOrganizationService.Execute in Task.FromResult — blocks thread pool thread. Pre-existing SDK limitation (matches story-1.2 deferred). Revisit when IOrganizationServiceAsync2 is adopted.
- F5: Null UserLocalizedLabel produces null JSON fields silently — handled by WhenWritingNull; acceptable for POC. Document expected nulls for Claude consumers.
- F6: ToJsonElement converts null block.Input to empty object `{}` — theoretical gap for no-param tool where block.Input should never be null. Revisit if param-bearing tools are added.

## Deferred from: code review of story-1.2-dataverse-connection (2026-04-14)

- Credentials stored as plain managed strings — `ClientSecret` and all credentials are heap-allocated `string` objects; not zeroed on scope exit. `SecureString` deprecated in .NET 6+. No practical replacement until OS-level secure memory APIs. Known language limitation.
- ServiceClient constructor is synchronous despite async caller — `new ServiceClient(Uri, string, string, bool)` performs OAuth token acquisition synchronously, blocking a thread pool thread. No async constructor overload exists in `Microsoft.PowerPlatform.Dataverse.Client` v1.2.10.
- ~~Duplicate class divergence risk~~ — resolved by story 3.8; see "Resolved: story 3.8" section above.
- No serialization guard attributes on EnvironmentCredentials — "must never be serialized" contract is documentation-only; no `[JsonIgnore]` or similar type-system enforcement. Address in Phase 2 hardening.
- ~~No CancellationToken support in ConnectAsync~~ — resolved by story 3.8; see "Resolved: story 3.8" section above.
- EnvironmentUrl scheme/format not validated in Program.cs — non-HTTPS or malformed URLs produce a generic error with no diagnostic hint. Acceptable for POC test runner; validate in Phase 2 API layer.
