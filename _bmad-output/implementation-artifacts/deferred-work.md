# Deferred Work

## Deferred from: code review of story-2.3-security-role-artefact (2026-04-16)

- No CI/automated validation for zip integrity — no CI infrastructure exists yet; hand-rolled solution XML has no automated check that it remains importable after edits.
- Three independent sources of truth for privilege list (README table, PRD Section 5.4, customizations.xml) — inherent to having documentation alongside implementation. SecurityCheckService.RequiredPrivileges is the authoritative code list.
- `setup-guide.md` forward reference in artefacts/README.md is a dead link until Story 2.4 creates the file. README notes this explicitly.
- Privilege name risk if Microsoft renames internal `prv*` privilege names in a future Dataverse platform update — MapPrivilegeName() is case-insensitive but has no fuzzy matching or version-aware mapping.

## Deferred from: code review of story-2.2-permission-checker (2026-04-16)

- No rate limiting on `POST /api/security/check` — endpoint accepts raw credentials and connects to external Dataverse environments; usable as credential validation oracle or outbound proxy. Cross-cutting concern; address at API-level with `Microsoft.AspNetCore.RateLimiting` middleware.
- `TargetMode` not validated against allowed values ("mode1"|"mode2"|"mode3"|"all") — story explicitly says accept-and-ignore for MVP since all modes share identical privileges.
- `"degraded"` status from PRD Section 5.5 / FR-029 not implemented — story scope only covers `"ready"` and `"blocked"`. Document as known gap; implement when mode-specific privilege requirements diverge.
- Console project `DataverseConnectionFactory` does not implement `IDataverseConnectionFactory` and lacks `CancellationToken` parameter — two implementations will drift. Pre-existing divergence; consolidate via shared project in Phase 2.
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

- D1: CancellationToken not propagated to IDataverseTool.ExecuteAsync — no cancellable host context until GenerationBackgroundService in Story 3.1; adding now has zero practical effect in a single-run console POC. Revisit when Story 3.1 lands.
- F1: ExtractText returns only first TextContent block — multi-block Claude responses silently drop remaining content. POC scope limitation; revisit when response aggregation matters.
- F2: FindTool uses StringComparison.Ordinal — a case mismatch from Anthropic API would silently return unknown-tool error. API always returns exact registered names; harden in Phase 2.
- F3: dnlib referenced in DataverseDocAgent.Api.csproj with no visible usage in story 1.3 — planned for future stories; audit and remove if still unused after epic 3.
- F4: ListCustomTablesTool.ExecuteAsync wraps synchronous IOrganizationService.Execute in Task.FromResult — blocks thread pool thread. Pre-existing SDK limitation (matches story-1.2 deferred). Revisit when IOrganizationServiceAsync2 is adopted.
- F5: Null UserLocalizedLabel produces null JSON fields silently — handled by WhenWritingNull; acceptable for POC. Document expected nulls for Claude consumers.
- F6: ToJsonElement converts null block.Input to empty object `{}` — theoretical gap for no-param tool where block.Input should never be null. Revisit if param-bearing tools are added.

## Deferred from: code review of story-1.2-dataverse-connection (2026-04-14)

- Credentials stored as plain managed strings — `ClientSecret` and all credentials are heap-allocated `string` objects; not zeroed on scope exit. `SecureString` deprecated in .NET 6+. No practical replacement until OS-level secure memory APIs. Known language limitation.
- ServiceClient constructor is synchronous despite async caller — `new ServiceClient(Uri, string, string, bool)` performs OAuth token acquisition synchronously, blocking a thread pool thread. No async constructor overload exists in `Microsoft.PowerPlatform.Dataverse.Client` v1.2.10.
- Duplicate class divergence risk — `EnvironmentCredentials`, `DataverseConnectionException`, `DataverseConnectionFactory` are copy-pasted across Api and Console with no shared project. Phase 2 shared project extraction planned per Dev Notes.
- No serialization guard attributes on EnvironmentCredentials — "must never be serialized" contract is documentation-only; no `[JsonIgnore]` or similar type-system enforcement. Address in Phase 2 hardening.
- No CancellationToken support in ConnectAsync — hung Dataverse endpoint or slow token acquisition cannot be cancelled. Explicitly out of POC scope.
- EnvironmentUrl scheme/format not validated in Program.cs — non-HTTPS or malformed URLs produce a generic error with no diagnostic hint. Acceptable for POC test runner; validate in Phase 2 API layer.
