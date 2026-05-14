# Story 3.7: Application User Inventory

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a D365 consultant,
I want the Mode 1 document to list all application users registered in the environment with their assigned security roles,
so that I can identify which external integrations are writing to this environment without needing audit log access.

## Acceptance Criteria

1. A new `GetApplicationUsersTool` is added to the Mode 1 tool set (5th tool, alongside `list_custom_tables`, `get_table_fields`, `get_relationships`, `get_organisation_metadata`). The tool's `Name = "get_application_users"`, `Description = "Returns all application (non-human, integration) users registered in the environment along with their assigned security roles."`, and `InputSchema = { "type": "object", "properties": {} }`.
2. The tool queries Dataverse for `SystemUser` records where `isdisabled = false`, `islicensed = false`, and `applicationid` is populated (matches the FR-050 definition of an application user — a non-licensed, application-bound principal used by integrations). Records that are disabled OR that have no `applicationid` are excluded.
3. For each application user, the tool returns: `fullname` (display name), `applicationid` (Guid as string), `internalemailaddress` (if populated), and `roles` (array of role display names). The roles array is populated by a secondary query against `systemuserroles` joined to `role` filtered by `systemuserid`; the role query is per-user, not global, so a single bad user does not poison the whole result.
4. If the per-user role lookup throws (FaultException, timeout, network) the user is still included in the result with `fullname` and `applicationid` populated and `roles = ["(role lookup unavailable)"]` — a single sentinel string entry, not an empty array, so the renderer can distinguish "no roles" from "lookup failed". Exceptions from the role query are swallowed at the per-user boundary; they do NOT propagate to the orchestrator and they do NOT mark the job Failed.
5. Top-level tool failures (initial `SystemUser` query itself fails) follow the existing sibling-tool contract from Story 3.4/3.5: return `{ "error": "Failed to list application users" }` as JSON. `OperationCanceledException` propagates normally so the per-task timeout in `GenerationBackgroundService` still works. If credentials are rejected at tool execution time the existing `CREDENTIAL_REJECTED` mapping in `DocumentGenerateService` covers it — no special handling needed in the tool.
6. The tool is registered as the 5th element returned by `DataverseToolFactory.CreateMode1Tools(service, environmentUrl)`. `DataverseToolFactoryTests` is updated to expect five named tools in the canonical order `[ list_custom_tables, get_table_fields, get_relationships, get_organisation_metadata, get_application_users ]`.
7. The `PromptBuilder.BuildMode1Prompt()` text is amended to instruct Claude to call `get_application_users` exactly once (it has zero inputs and zero per-table loop). The JSON schema Claude returns gains a top-level `applicationUsers` key — array of `{ displayName, applicationId, email, roles[] }` — sibling to the existing `organisation`, `tables`, `fields`, `relationships`, `keyObservations` keys. The prompt explicitly states: "Pass the role array through verbatim — do not redact or summarise role names."
8. `GeneratedDocumentModel` gains a required `IReadOnlyList<ApplicationUserInfo> ApplicationUsers { get; init; }` field at the top level (peer to `Tables`, `Fields`, `Relationships`) so the renderer is not nested under `Summary`. `ApplicationUserInfo` is a new sealed class with `string? DisplayName`, `string? ApplicationId`, `string? Email`, `IReadOnlyList<string> Roles`.
9. `DocxBuilder` renders a new Section 5 **"Application Users (Integration Signals)"**. Per FR-050 this is part of the Technical Reference Layer; until Epic 4 introduces the formal two-layer split the section is appended after the existing Section 4 "Relationship Map". The section opens with the exact prose required by FR-050 and the epic AC: `"Application users are typically used by external integrations. The following application users are registered and may be writing to tables in this environment."`. Below the prose, a three-column table renders one row per user: `Display Name | Application ID | Roles`. The Roles column joins the array entries with a comma and a space; the sentinel `(role lookup unavailable)` is rendered verbatim.
10. If `ApplicationUsers` is empty the section is still rendered (FR-050 + epic AC explicitly forbid omitting it). The post-heading body is the literal sentence `"No application users registered in this environment."` and no breakdown table is emitted. The Section 5 heading is always present whenever `Tables.Count > 0` is true OR `ApplicationUsers.Count > 0` is true; it is omitted only when both are empty (i.e. nothing else in the document either).
11. The prompt requires Claude to invoke `get_application_users` even when zero apps exist; the tool returns an empty list — NOT an error. `DocumentGenerateService` defends against Claude omitting the key by treating a missing/null `applicationUsers` field as `Array.Empty<ApplicationUserInfo>()` rather than failing parse. (Pattern matches existing `parsed.Tables ?? Array.Empty<TableInfo>()` defence in Story 3.5.)
12. Unit tests cover: tool happy-path (returns N populated users with roles); per-user role lookup failure path (one bad user, others succeed, bad user gets `(role lookup unavailable)`); empty environment (returns `{ "applicationUsers": [] }` JSON); cancellation token propagation; `DocxBuilder` renders the section heading + literal prose for both the populated-table and the empty-environment branches; `DataverseToolFactory` returns five named tools in the canonical order; `PromptBuilder` mentions `get_application_users` and the `applicationUsers` output key.

## Tasks / Subtasks

- [x] Implement `GetApplicationUsersTool` (AC: 1, 2, 3, 4, 5)
  - [x] Create `src/DataverseDocAgent.Api/Agent/Tools/GetApplicationUsersTool.cs`
  - [x] Constructor takes `IOrganizationService` only — same shape as siblings; no environment URL needed.
  - [x] First query: `QueryExpression` against `systemuser` with `ColumnSet("fullname", "applicationid", "internalemailaddress")` and filter `isdisabled = false AND islicensed = false AND applicationid != null`. Use `_service.RetrieveMultiple(query)` synchronously (matches Story 3.4 / 3.5 pattern; sync-SDK limitation already in deferred-work.md).
  - [x] For each result `Entity`, build a per-user role list via a second `QueryExpression` against `systemuserroles` linked to `role`: link-entity from `systemuserroles.roleid → role.roleid`, filter `systemuserroles.systemuserid = <currentUserId>`, columns `role.name`. Try/catch around this per-user query — on any `FaultException<OrganizationServiceFault>`, `TimeoutException`, `CommunicationException`, or `HttpRequestException`, set the user's role list to `[ "(role lookup unavailable)" ]` and continue.
  - [x] `OperationCanceledException` from either query MUST propagate (do NOT swallow inside the per-user catch; use `when` clauses to exclude OCE — matches `ListCustomTablesTool` precedent).
  - [x] Outer try/catch on the initial `systemuser` query returns the structured `{ "error": "Failed to list application users" }` JSON for SDK faults / timeouts / WCF channel faults (matches sibling tools).
  - [x] Annotate file header: `// F-055 — FR-050 — Integration Signal Detection: App User Inventory (Story 3.7)`. Add `// NFR-007` inline at the per-user role-lookup catch block to pin the no-message-leak intent.
- [x] Wire the tool into `DataverseToolFactory` (AC: 6)
  - [x] Append `new GetApplicationUsersTool(service)` as the 5th element of the array returned by `CreateMode1Tools`.
  - [x] Update `DataverseToolFactoryTests.CreateMode1Tools_ReturnsExactlyFourNamedTools` → rename to `_ReturnsExactlyFiveNamedTools` and assert the new tool name in the trailing slot.
- [x] Extend `PromptBuilder.BuildMode1Prompt()` (AC: 7)
  - [x] Add step `5.` "Call `get_application_users` once to retrieve every application user (non-human integration principal) registered in the environment."
  - [x] Add `applicationUsers` key to the prescribed JSON shape with the four fields per AC-7.
  - [x] Add the "Pass the role array through verbatim" rule alongside the existing rules block.
  - [x] Update `PromptBuilderTests` to assert `get_application_users` and the `applicationUsers` output key are mentioned.
- [x] Extend the document model (AC: 8)
  - [x] Add `ApplicationUserInfo` sealed class in `src/DataverseDocAgent.Api/Documents/GeneratedDocumentModel.cs`.
  - [x] Add `required IReadOnlyList<ApplicationUserInfo> ApplicationUsers { get; init; }` at the top level of `GeneratedDocumentModel` (peer to `Tables`/`Fields`/`Relationships`, not nested under `Summary`).
  - [x] Update Story 3.5 `BuildSampleModel` in `DocxBuilderTests` to set the new field — flag this as the breaking-test fix in the PR.
- [x] Parse the new section in `DocumentGenerateService` (AC: 11)
  - [x] Extend the internal `AgentJsonModel` DTO with `List<ApplicationUserInfo>? ApplicationUsers { get; set; }`.
  - [x] In `RunPipelineAsync`, pass `(IReadOnlyList<ApplicationUserInfo>?)parsed.ApplicationUsers ?? Array.Empty<ApplicationUserInfo>()` into the model build. Defence-in-depth: a Claude response that drops the `applicationUsers` key is rendered as the "no users" section, not as an AI_ERROR.
- [x] Render Section 5 in `DocxBuilder` (AC: 9, 10)
  - [x] New private helper `AppendApplicationUsersSection(body, applicationUsers)`.
  - [x] Skip rendering only when `model.Tables.Count == 0 && model.ApplicationUsers.Count == 0` (AC-10 "always present unless the document is otherwise empty").
  - [x] H1 heading exactly `"5. Application Users (Integration Signals)"`.
  - [x] Always render the FR-050 literal prose paragraph (single sentence per AC-9).
  - [x] If `applicationUsers.Count == 0`: render the literal sentence `"No application users registered in this environment."` and return.
  - [x] Otherwise: render a `BuildTable` with headers `[ "Display Name", "Application ID", "Roles" ]` and one row per user. Roles joined with `", "`; empty roles list renders as the literal `"(no roles assigned)"` so the cell is never blank.
  - [x] Call the helper at the end of `AppendRelationshipMapSection`'s sibling slot inside `Build`, after `AppendRelationshipMapSection(body, model.Tables, model.Relationships)`.
- [x] Tests (AC: 12)
  - [x] Create `tests/DataverseDocAgent.Tests/GetApplicationUsersToolTests.cs` covering the four scenarios in AC-12 against a mocked `IOrganizationService`. Mock `_service.RetrieveMultiple` to return a controlled `EntityCollection`; for per-user role queries match by linked-entity filter so different mocked responses can be returned per user.
  - [x] Extend `tests/DataverseDocAgent.Tests/DataverseToolFactoryTests.cs` per AC-6.
  - [x] Extend `tests/DataverseDocAgent.Tests/PromptBuilderTests.cs` per AC-7.
  - [x] Extend `tests/DataverseDocAgent.Tests/DocxBuilderTests.cs` with two new tests: populated section + empty-list section. Both assert the literal FR-050 prose and (for populated) the three-column table.
  - [x] Extend `tests/DataverseDocAgent.Tests/DocumentGenerateServiceTests.cs` to cover the missing-key defence (AC-11).

## Dev Notes

- **SystemUser read permission gap.** PRD §5.4 (the permission checker's Exact Permissions table) does NOT list `SystemUser` as a required read privilege. In practice, every system role granted to the service account includes `prvReadUser` implicitly via the platform's Basic User membership — but story 3.7 is the first story to depend on it. Two options:
  1. Add `SystemUser | Read | "List application users for integration signal detection" | Never` to the PRD §5.4 table in the same commit that ships this story, and update the security-check service's required-privilege list to match. This is the recommended path because the privilege is genuinely required and the privacy/permission contract should reflect reality.
  2. Defer the doc update and let the dev pick it up. NOT recommended — it leaves a gap between FR-050 and the documented permission scope, which the senior consultant audience will notice immediately.
  Prefer option 1. Treat this as part of the story's definition of done.
- **`isdisabled = false` filter.** FR-050 specifies `islicensed = false AND applicationid populated`. The epic AC says the same. Adding `isdisabled = false` is a defensive extension — disabled app users are stale registrations and should not be flagged as live integration signals. Documented inline on the filter.
- **Per-user role-lookup defence.** AC-4 mandates a sentinel array `[ "(role lookup unavailable)" ]` on failure, NOT an empty array, because the renderer must distinguish a user with no roles assigned (legitimate state) from a user whose role query failed (transient lookup error). The renderer rule in AC-9 / AC-10 (empty roles → `"(no roles assigned)"`) reinforces the distinction. NFR-007 applies: the catch block must NOT log the SDK exception's `.Message` — only the user's id + a fixed string.
- **Why no global `systemuserroles` join.** A single `LinkEntity` query joining `systemuser`, `systemuserroles`, and `role` would be cheaper round-trip-wise but couples role-lookup success to systemuser-fetch success. The per-user query loop matches the existing pattern in `GetTableFieldsTool` / `GetRelationshipsTool` and isolates per-user lookup failures cleanly. Performance impact is bounded: typical environments have <20 application users.
- **Synchronous SDK limitation carries over.** All four existing Mode 1 tools wrap `IOrganizationService.Execute` / `RetrieveMultiple` synchronously. Story 3.7 follows the same pattern. The deferred-work item on `IOrganizationServiceAsync2` adoption already covers this entire family.
- **Section placement.** Per FR-050 the section belongs in the Technical Reference Layer. Epic 4 introduces the formal Executive / Technical Reference split (ADR-007). Until then, appending after Section 4 is structurally consistent — Sections 1–4 in this MVP are entirely Technical Reference content except for Section 1 (Executive Summary). When Epic 4 lands, the Section 5 helper can be tagged with `DocumentLayer.TechnicalReference` without changing its rendering logic.
- **Prompt churn risk.** Story 3.5 stabilised the prompt JSON shape. Adding `applicationUsers` is a backward-compatible addition (Claude returning the old 5-key shape is silently coerced to empty users via AC-11). No DocumentGenerateService test from Story 3.5 needs to be deleted; the existing `ParseAgentJson` tests still cover the four-key shape.
- **Iteration ceiling unchanged.** Adding one more single-call tool (zero-input, no per-table loop) adds at most one orchestrator round-trip. The `Mode1MaxIterations = 200` ceiling set in Story 3.5 has ample headroom.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Api/Agent/Tools/GetApplicationUsersTool.cs` — `// F-055 — FR-050`
- `tests/DataverseDocAgent.Tests/GetApplicationUsersToolTests.cs`

Files modified:
- `src/DataverseDocAgent.Api/Agent/Tools/DataverseToolFactory.cs` — registers 5th tool
- `src/DataverseDocAgent.Api/Agent/PromptBuilder.cs` — step 5 + new JSON key + role-passthrough rule
- `src/DataverseDocAgent.Api/Documents/GeneratedDocumentModel.cs` — adds `ApplicationUserInfo` + `ApplicationUsers` slot
- `src/DataverseDocAgent.Api/Documents/DocxBuilder.cs` — new Section 5 helper + call site in `Build`
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs` — `AgentJsonModel.ApplicationUsers` + safe-coalesce in `RunPipelineAsync`
- `tests/DataverseDocAgent.Tests/DataverseToolFactoryTests.cs` — five-tool assertion
- `tests/DataverseDocAgent.Tests/PromptBuilderTests.cs` — new prompt-shape assertions
- `tests/DataverseDocAgent.Tests/DocxBuilderTests.cs` — populated + empty Section 5 + `BuildSampleModel` update
- `tests/DataverseDocAgent.Tests/DocumentGenerateServiceTests.cs` — missing-key defence test
- `docs/prd.md` — §5.4 row `SystemUser | Read | ...` (per Dev Notes recommendation)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — story 3-7 → review on commit

No new csproj packages required.

### References

- [Source: docs/prd.md#functional-requirements — FR-050 (lines 1187-1197)] — full FR text and AC for app user inventory
- [Source: docs/prd.md#user-flows — flow step 9 (line 183)] — "Application users listed as integration signals"
- [Source: docs/prd.md#executive-and-technical-layers (line 1135)] — Technical Reference Layer composition includes FR-050
- [Source: docs/prd.md#permission-table — §5.4 (lines 405-416)] — **gap**: SystemUser read is not listed; this story is the first to require it
- [Source: docs/prd.md#feature-table — F-055 (line 319)] — Mode 1 P2 priority Medium
- [Source: _bmad-output/planning-artifacts/epics.md#story-37 (lines 668-700)] — story epic source with full BDD ACs
- [Source: src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs] — precedent for per-row sub-query with try/catch isolation (the fields-per-table pattern is structurally identical to roles-per-user)
- [Source: src/DataverseDocAgent.Api/Agent/Tools/DataverseToolFactory.cs] — central registration point; the 5th-tool addition is the smallest possible diff there
- [Source: src/DataverseDocAgent.Api/Agent/PromptBuilder.cs] — prescriptive JSON shape that Claude returns; needs `applicationUsers` key and step-5 instruction
- [Source: src/DataverseDocAgent.Api/Documents/DocxBuilder.cs#Build] — host method for Section 5 call-site
- [Source: src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs#RunPipelineAsync] — adds the safe-coalesce parsing of the new key (FR-050 + AC-11)

## Dev Agent Record

### Agent Model Used

claude-opus-4-7

### Debug Log References

- 2026-05-14 dev — first `dotnet test` pass after fixture changes failed `Build_ApplicationUsersSection_EmptyList_PopulatedEnvironment_RendersHeadingAndFallbackProse` because `BuildModelWithApplicationUsers` hard-coded `Tables = Array.Empty<TableInfo>()` while exercising `tableCount > 0`. `AppendApplicationUsersSection` (correctly) gates on `model.Tables.Count`, not the summary counter, so the section was suppressed. Fix: helper now synthesises a placeholder `TableInfo` per `tableCount` so the AC-10 "populated environment" branch is reached. All 247 tests pass after the fix.

### Completion Notes List

- Implemented `GetApplicationUsersTool` as the 5th Mode 1 tool. JSON output keys (`displayName`, `applicationId`, `email`, `roles`) deliberately mirror the Claude-output shape so AC-7's "pass the role array through verbatim" rule is a no-op rename rather than a per-key remap.
- Per-user role isolation: try/catch around the secondary `QueryExpression` swallows only `FaultException<OrganizationServiceFault>` / `TimeoutException` / `CommunicationException` / `HttpRequestException`; `OperationCanceledException` propagates via `when (cancellationToken.IsCancellationRequested)` filter so the orchestrator's cancellation contract is intact.
- Sentinel `(role lookup unavailable)` exposed via `GetApplicationUsersTool.RoleLookupUnavailableSentinel` `public const string` so both the analyzer-style consumer (tests) and any future renderer can reference the literal without drift.
- `GeneratedDocumentModel.ApplicationUsers` is `required` at the top level — peer to `Tables` / `Fields` / `Relationships`, NOT nested under `Summary`. Story 3.6's `ExecutiveSummary.PrefixSummary` precedent influenced this choice but Section 5 is rendered post-Section-4 (Technical Reference Layer per FR-050 / ADR-007), so a top-level slot keeps the executive-summary boundary clean.
- `DocumentGenerateService.AgentJsonModel.ApplicationUsers` is optional (nullable). A backwards-compatible four-key Claude response from Story 3.5 deserialises with `ApplicationUsers == null` and the safe-coalesce (`?.Where(u => u is not null).ToList() ?? Array.Empty<ApplicationUserInfo>()`) renders the empty Section 5 instead of raising `AI_ERROR`. Mirrors Story 3.6 P2 null-entry filter at the parse boundary.
- `DocxBuilder.AppendApplicationUsersSection` is suppressed only when both `model.Tables.Count == 0` AND `model.ApplicationUsers.Count == 0` (AC-10 "nothing else in the document either"). Headers always present whenever either collection is non-empty; the literal "No application users registered…" sentence covers the empty-list branch.
- PRD §5.4 row added (`SystemUser | Read | ... | Never`) per story Dev Notes recommended option 1. **Note:** `SecurityCheckService.RequiredPrivileges` was NOT updated in this commit — adding `Read SystemUser` would require updating 6+ test count assertions (12 → 13 across `RequiredPrivileges_HasExactly12Entries`, `ComputePrivilegeSets_*`, etc.) plus a new `[InlineData("prvReadSystemUser", "Read SystemUser")]` row. The handoff prompt explicitly capped diversions to "a single inline-data row addition" and instructed STOP-and-defer otherwise. The mismatch between PRD §5.4 (now 13 rows) and `SecurityCheckService.RequiredPrivileges` (still 12 entries) is intentionally deferred to a follow-up commit and documented in `deferred-work.md`.
- Tests: 247 pass (was 230 pre-story). New: 9 `GetApplicationUsersToolTests` cases + 4 `DocxBuilderTests` Section-5 branches + 1 `PromptBuilderTests` assertion + 1 `DataverseToolFactoryTests` rename (with trailing-slot pin) + 2 `DocumentGenerateServiceTests` (missing-key defence + null-entry filter).

### File List

- `src/DataverseDocAgent.Api/Agent/Tools/GetApplicationUsersTool.cs` (new)
- `src/DataverseDocAgent.Api/Agent/Tools/DataverseToolFactory.cs` (modified — 5th tool registered, XML doc reflects new count)
- `src/DataverseDocAgent.Api/Agent/PromptBuilder.cs` (modified — step 5 + `applicationUsers` JSON key + role-passthrough rule)
- `src/DataverseDocAgent.Api/Documents/GeneratedDocumentModel.cs` (modified — `ApplicationUserInfo` + required `ApplicationUsers` top-level slot)
- `src/DataverseDocAgent.Api/Documents/DocxBuilder.cs` (modified — `AppendApplicationUsersSection` helper + call-site in `Build`)
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs` (modified — `AgentJsonModel.ApplicationUsers` + safe-coalesce + null-entry filter in `RunPipelineAsync`)
- `tests/DataverseDocAgent.Tests/GetApplicationUsersToolTests.cs` (new)
- `tests/DataverseDocAgent.Tests/DataverseToolFactoryTests.cs` (modified — renamed test asserts 5 named tools in canonical order)
- `tests/DataverseDocAgent.Tests/PromptBuilderTests.cs` (modified — 5th tool + `applicationUsers` key + verbatim-passthrough rule)
- `tests/DataverseDocAgent.Tests/DocxBuilderTests.cs` (modified — 4 Section-5 branches + `BuildSampleModel` + empty-environment fixtures updated for required init)
- `tests/DataverseDocAgent.Tests/DocumentGenerateServiceTests.cs` (modified — missing-key defence + null-entry filter tests)
- `docs/prd.md` (modified — §5.4 `SystemUser | Read | ... | Never` row added per Dev Notes option 1)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (modified — story 3-7 → review)
- `_bmad-output/implementation-artifacts/story-3.7-application-user-inventory.md` (modified — task checkboxes, Dev Agent Record, status)

### Change Log

| Date       | Change                                                                       |
|------------|------------------------------------------------------------------------------|
| 2026-05-14 | Story 3.7 implemented — `GetApplicationUsersTool` + Section 5 (Application Users — Integration Signals); 247 tests pass. PRD §5.4 SystemUser row added; SecurityCheckService alignment deferred per handoff cap. |
| 2026-05-14 | Code review patches P1–P16 applied; 272 tests pass. Story → done.            |

### Review Findings

Adversarial review run on 2026-05-14 with three parallel subagents (Blind Hunter — diff-only, Edge Case Hunter — diff + read access, Acceptance Auditor — diff + spec + PRD). Severity rubric: HIGH = wrong output / crash / broken invariant; MED = edge case / coverage gap; LOW = nit. Patches applied below; everything else captured in `_bmad-output/implementation-artifacts/deferred-work.md` under the heading "Deferred from: code review of story-3.7 (2026-05-14)".

#### Patches applied

- [x] **P1** — `GetApplicationUsersTool.ExecuteAsync` defensively skips null `Entity` elements in both the outer user enumeration and the inner role-result enumeration. SDK contract does not enforce non-null entries; without the guard a flaky payload would NRE and the outer catch would convert the whole environment into the "Failed to list application users" error contract. (`src/DataverseDocAgent.Api/Agent/Tools/GetApplicationUsersTool.cs`)
- [x] **P2** — `GetApplicationUsersTool.ExecuteAsync` outer catch broadened from the narrow `FaultException | TimeoutException | CommunicationException | HttpRequestException` filter to `catch (Exception) when (!cancellationToken.IsCancellationRequested)`. Sibling-tool contract demands the agent loop receive a tool result for ANY non-cancellation failure — an unexpected exception type (e.g. `InvalidOperationException` from a malformed `Entity` attribute cast) would otherwise crash the Mode 1 loop. NFR-007 still holds: no exception details are echoed; fixed error text only. (`src/DataverseDocAgent.Api/Agent/Tools/GetApplicationUsersTool.cs`)
- [x] **P3** — `GetApplicationUsersTool.FetchRolesForUser` per-user catch broadened identically. AC-4's per-user isolation requirement applies to ANY exception type, not only the four originally listed — a single unknown SDK shape (NRE, InvalidCast, ArgumentException) on one user must not abort enumeration of every remaining user. (`src/DataverseDocAgent.Api/Agent/Tools/GetApplicationUsersTool.cs`)
- [x] **P4** — `ExtractApplicationId` gains an `EntityReference` arm (some SDK paths surface `applicationid` this way) and treats `Guid.Empty` as null. Fallback returns null instead of `raw.ToString()` (which would emit `"Microsoft.Xrm.Sdk.EntityReference"` for unknown runtime types). (`src/DataverseDocAgent.Api/Agent/Tools/GetApplicationUsersTool.cs`)
- [x] **P5** — `FetchRolesForUser` filters whitespace-only role names at the tool boundary via `!string.IsNullOrWhiteSpace(name)` instead of the laxer `is { Length: > 0 }`. Whitespace role rows would otherwise render as `, ,` in the Word cell. (`src/DataverseDocAgent.Api/Agent/Tools/GetApplicationUsersTool.cs`)
- [x] **P6** — `DocxBuilder` Section 5 routes role-list rendering through a new internal helper `FormatRolesCell(IReadOnlyList<string>?)` that filters null / whitespace entries, dedupes (preserves first-occurrence order), and trims. Prevents misleading cells like `"Reader, , Reader"` from a flaky Claude payload. (`src/DataverseDocAgent.Api/Documents/DocxBuilder.cs`)
- [x] **P7** — `FormatRolesCell` collapses a roles array containing the `(role lookup unavailable)` sentinel to the sentinel alone. Claude prompt drift could produce a mixed `["(role lookup unavailable)", "Reader"]` array; rendering that verbatim is semantically meaningless (either the lookup failed or it did not). Sentinel-only enforcement is locked by a dedicated DocxBuilder test. (`src/DataverseDocAgent.Api/Documents/DocxBuilder.cs`)
- [x] **P8** — `ApplicationUserInfo.Roles` declared nullable (`IReadOnlyList<string>?`) to honestly type the System.Text.Json round-trip: a JSON `"roles": null` payload stomps the init expression, so a non-nullable slot was a future-NRE hazard. The renderer (`FormatRolesCell`) accepts the nullable type and emits `"(no roles assigned)"` for null/empty inputs. (`src/DataverseDocAgent.Api/Documents/GeneratedDocumentModel.cs`)
- [x] **P9** — Tests now reference `GetApplicationUsersTool.RoleLookupUnavailableSentinel` directly instead of redeclaring the literal — defeats drift if the sentinel string is ever renamed. The duplicated test-local constant was removed. (`tests/DataverseDocAgent.Tests/DocxBuilderTests.cs`)
- [x] **P10** — New `ExecuteAsync_PerUserRoleLookupFault_AllFaultTypes_SurfaceSentinel` theory covers all six fault types (FaultException, TimeoutException, CommunicationException, HttpRequestException, InvalidOperationException, ArgumentException). Locks the broadened P3 catch against regression. (`tests/DataverseDocAgent.Tests/GetApplicationUsersToolTests.cs`)
- [x] **P11** — New `ExecuteAsync_OuterFault_AllFaultTypes_ReturnStructuredError` theory covers all five outer-fault types. Locks the broadened P2 catch. (`tests/DataverseDocAgent.Tests/GetApplicationUsersToolTests.cs`)
- [x] **P12** — New `ExecuteAsync_RetrieveMultipleReturnsNull_ReturnsEmptyArray` pins the `result?.Entities is null` guard. (`tests/DataverseDocAgent.Tests/GetApplicationUsersToolTests.cs`)
- [x] **P13** — New `ExecuteAsync_CancellationBetweenUsers_PropagatesOCE` test exercises mid-iteration token cancellation (one user's role query callback cancels the CTS; the next loop iteration must short-circuit). The pre-existing `_AlreadyCancelledToken_Throws` only covered pre-loop cancellation. (`tests/DataverseDocAgent.Tests/GetApplicationUsersToolTests.cs`)
- [x] **P14** — Three new tests pin `ExtractApplicationId` branches: string input, EntityReference input, and `Guid.Empty` (collapses to null → JSON key dropped). (`tests/DataverseDocAgent.Tests/GetApplicationUsersToolTests.cs`)
- [x] **P15** — `DataverseToolFactoryTests` reflection-binding test comment updated from "all three" to "all five" — pre-existing wording bug exposed by Story 3.7's 5-tool registration. (`tests/DataverseDocAgent.Tests/DataverseToolFactoryTests.cs`)
- [x] **P16** — `DocxBuilder` class-level XML doc adds Section 5 to the section list, matching the per-method banner. (`src/DataverseDocAgent.Api/Documents/DocxBuilder.cs`)

Tests after patches: 272 pass (was 247 after Phase A; +25 from coverage-gap patches and new branch tests).

#### Deferred (see `deferred-work.md` — "Deferred from: code review of story-3.7 (2026-05-14)")

- [x] **D-CR1** — `SecurityCheckService.RequiredPrivileges` alignment with PRD §5.4's new `SystemUser | Read` row (already deferred at Phase A dev time; carried into review deferral set).
- [x] **D-CR2** — Duplicate `ApplicationUser` entries from Claude (same `applicationId`) — no dedupe at service-layer or renderer. Deferred until a real environment surfaces it.
- [x] **D-CR3** — Non-deterministic role ordering across SDK retries — no `AddOrder` on the role query. Deferred until "regenerate-and-diff" workflow lands.
- [x] **D-CR4** — Unicode / very-long display-name and email coverage gap in tests. Deferred until a real multi-language tenant surfaces a defect.
- [x] **D-CR5** — Cross-section layout test (Tables.Count == 0 AND ApplicationUsers.Count > 0) — Sections 2/3/4 emit "no tables" placeholders, Section 5 emits the populated table. Combined-layout assertion deferred.
- [x] **D-CR6** — Per-user `FetchApplicationUsers` cannot honour mid-call cancellation — synchronous SDK limitation already deferred at the family level (Story 3.4 / 3.5 deferred entries on `IOrganizationServiceAsync2`).
- [x] **D-CR7** — Redundant `systemuserid` column in the initial `ColumnSet` (SDK populates `Entity.Id` automatically). Cosmetic; deferred.
- [x] **D-CR8** — `SecurityCheckService.cs:13` comment still reads "All 12 required privileges" — paired with D-CR1 fix.

#### Dismissed

- AC-3 wording mixes Dataverse SDK attribute names (`fullname` / `applicationid` / `internalemailaddress`) with JSON keys, while implementation emits the Claude-output shape (`displayName` / `applicationId` / `email`) per AC-7. Deliberate per Completion Notes; resolved by AC-12 tests. No change.
- `EntityCollection.Entities` cast to `IReadOnlyList<Entity>` — `DataCollection<T>` inherits from `Collection<T>` which implements `IReadOnlyList<T>` on .NET 6+. Safe.
- `s_inputSchema` static `JsonElement` from literal JSON — `JsonSerializer.Deserialize<JsonElement>` on the valid literal cannot return null. No defect.
- Heading numbering ("5. Application Users …") — story spec mandates the literal; document layout is consistent. No change.
- `internalemailaddress` semantics for app users — Dataverse stores synthesised addresses; acceptable per AC-3. No change.
- Tool's `ExecuteAsync` is sync-wrapped in `Task.FromResult` — matches Story 3.4 / 3.5 family pattern; sync-SDK limitation is already family-deferred.
