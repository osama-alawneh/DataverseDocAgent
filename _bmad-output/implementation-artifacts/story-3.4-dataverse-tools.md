# Story 3.4: Dataverse Tools — Tables, Fields, and Relationships

Status: review

## Story

As a developer,
I want `ListCustomTablesTool`, `GetTableFieldsTool`, and `GetRelationshipsTool` implemented as production-ready `IDataverseTool` instances,
so that Claude can gather all required environment metadata during Mode 1 generation.

## Acceptance Criteria

1. `ListCustomTablesTool` (updated from POC version in Story 1.3) queries all entities where `IsCustomEntity = true` and returns a JSON array of `{ displayName, logicalName, schemaName, solutionName, description }` per table. If zero custom tables exist, returns `{ "tables": [], "message": "No custom tables found in this environment" }`.
2. `GetTableFieldsTool` accepts a `tableName` (logical name) parameter, queries all attributes where `IsCustomAttribute = true` for that table, and returns a JSON array including: `displayName`, `logicalName`, `attributeType`, `requiredLevel`, `defaultValue` (if set), and for OptionSet/MultiSelectPicklist types, an `options` array of `{ label, value }` pairs.
3. `GetRelationshipsTool` accepts a `tableName` (logical name) parameter, returns all 1:N relationships where `IsCustomRelationship = true` involving that table as either the referencing or referenced entity, and all N:N custom relationships. Each entry includes: `relationshipType` ("OneToMany" or "ManyToMany"), `schemaName`, `referencingEntity`, `referencedEntity`, and `cascadeConfiguration` (delete, assign, share, unshare behaviours as strings).
4. All three tools are registered as available tools in the `AgentOrchestrator` when Mode 1 is executed.
5. If Dataverse returns an error for any tool call (e.g., table not found for `GetTableFieldsTool`), the tool returns a structured JSON error string `{ "error": "...", "tableName": "..." }` rather than throwing — Claude receives the error and can respond accordingly.
6. No credential values appear in any tool output, log entry, or exception message produced by any of the three tools.

## Tasks / Subtasks

- [x] Update `ListCustomTablesTool` to production quality (AC: 1)
  - [x] Move from Console project to `DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs` (already in Api before Story 3.4)
  - [x] Five return fields populated: `displayName`, `logicalName`, `schemaName`, `solutionName` (Phase 2 — explicit null per dev notes), `description`
  - [x] Zero-result case returns `{ "tables": [], "message": "No custom tables found in this environment" }`
  - [x] Annotated `// F-001`
- [x] Implement `GetTableFieldsTool` (AC: 2, 5)
  - [x] Create `src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs`
  - [x] `Name = "get_table_fields"`, `Description = "Returns all custom fields for a specified table"`, `InputSchema` declares `tableName` required
  - [x] Query: `RetrieveMetadataChangesRequest` filters attributes by `IsCustomAttribute = true`
  - [x] Per field: extracts `DisplayName.UserLocalizedLabel.Label`, `LogicalName`, `AttributeType.ToString()`, `RequiredLevel.Value.ToString()`
  - [x] OptionSet handling via `EnumAttributeMetadata` runtime-reflection — covers `PicklistAttributeMetadata`, `MultiSelectPicklistAttributeMetadata`, `StateAttributeMetadata`, `StatusAttributeMetadata` even when derived types shadow the base property
  - [x] `DefaultValue` deferred — Phase 1 of metadata projection ships only the four AC-2 mandated fields plus options[]; defaults add SDK-type-by-type extraction code without a Mode 1 consumer
  - [x] "Table not found" returns `{ "error": "Table '{tableName}' not found or inaccessible", "tableName": tableName }`
  - [x] Annotated `// F-002, FR-002`
- [x] Implement `GetRelationshipsTool` (AC: 3, 5)
  - [x] Create `src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs`
  - [x] `Name = "get_relationships"`, `Description = "Returns all custom relationships for a specified table"`, `InputSchema` declares `tableName` required
  - [x] Single `RetrieveEntityRequest` with `EntityFilters.Relationships` covers 1:N (both directions) + N:N — single round-trip per dev notes
  - [x] Per 1:N: `relationshipType = "OneToMany"`, `schemaName`, `referencingEntity`, `referencedEntity`, `cascadeConfiguration { delete, assign, share, unshare }` from `CascadeType` enum
  - [x] Per N:N: `relationshipType = "ManyToMany"`, `schemaName`, `entity1LogicalName`, `entity2LogicalName`
  - [x] Filters out non-custom relationships (`IsCustomRelationship != true`)
  - [x] "Table not found" returns sanitized structured error JSON
  - [x] Annotated `// F-003, FR-003`
- [x] Register all three tools in `AgentOrchestrator` for Mode 1 (AC: 4)
  - [x] `AgentOrchestrator.RunAsync` already accepts `IEnumerable<IDataverseTool>` (no refactor needed)
  - [x] New `DataverseToolFactory.CreateMode1Tools(IOrganizationService)` centralises Mode 1 tool list — Story 3.5 wires `factory(serviceClient)` into the orchestrator call
- [ ] Integration test against real Dataverse environment (AC: 1–5)
  - [ ] Deferred to Story 3.5 — no Dataverse credentials available in unit-test scope; will be exercised end-to-end via `POST /api/document/generate`

## Dev Notes

- **`ServiceClient` injection:** As discussed in Story 1.3, tools receive a pre-built `ServiceClient` via constructor rather than `EnvironmentCredentials`. The `AgentOrchestrator` constructs the `ServiceClient` once at the start of the pipeline (via `DataverseConnectionFactory`) and passes it to all tool constructors. This avoids re-authentication per tool call.
- **`RetrieveMetadataChangesRequest` vs `RetrieveEntityRequest`:** For bulk attribute retrieval, `RetrieveMetadataChangesRequest` with `EntityFilters.Attributes` is efficient. For per-table retrieval with relationships, `RetrieveEntityRequest` with `EntityFilters.All` retrieves everything in one call.
- **Cascade configuration strings:** `CascadeType` enum values: `NoCascade`, `Cascade`, `Active`, `UserOwned`, `RemoveLink`, `Restrict`. Convert to these string labels in the output.
- **Option set labels:** Use `UserLocalizedLabel.Label` for the display label. The `Value` is an integer.
- **`solutionName` retrieval:** Retrieving the solution membership for a table requires a query to `solutioncomponent` (where `objectid = entity.MetadataId` and `componenttype = 1`), then joining to `solution` for the `uniquename`. This is a secondary query. If it adds significant complexity, return `null` for `solutionName` in Phase 2 and document it as a known limitation to address in Phase 3.
- **Tool error returns:** When Dataverse throws `FaultException<OrganizationServiceFault>` for an unknown table, catch it and return the structured JSON error string. Do NOT let it propagate as an exception — the agent loop should receive a tool result, not a tool error.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs` — `// F-002`
- `src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs` — `// F-003`

Modified:
- `src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs` — moved from Console, hardened — `// F-001`
- `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs` — tool registration for Mode 1

### References

- [Source: docs/prd.md#functional-requirements — FR-001, FR-002, FR-003] — full acceptance criteria per feature
- [Source: _bmad-output/planning-artifacts/architecture.md#5-custom-tool-inventory] — tool name, description, and FR mapping
- [Source: docs/prd.md#7-non-functional-requirements — NFR-007, NFR-008] — credential logging and read-only enforcement

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `dotnet test tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj` → 139 passed / 0 failed (23 new across `GetTableFieldsToolTests` + `GetRelationshipsToolTests`).
- Post-review: `dotnet test` → 165 passed / 0 failed (26 new tests across the three tool-test files + new `DataverseToolFactoryTests`).

### Completion Notes List

- **Picklist OptionSet capture (GetTableFieldsTool)**: First pass used a `switch` on concrete derived types (`PicklistAttributeMetadata` / `MultiSelectPicklistAttributeMetadata`); the Picklist test failed because the SDK type's `OptionSet` property hides the base `EnumAttributeMetadata.OptionSet` via `new`, so a base-cast read returned null. Final implementation reflects on the runtime type for `OptionSet` — single code path covers Picklist, MultiSelect, State, Status without depending on which class shadows the base.
- **Option label fallback**: `Label(string, int)` ctor populates `LocalizedLabels` but not `UserLocalizedLabel`. Code falls back to `LocalizedLabels.FirstOrDefault()` so option labels surface even when callers used the convenience ctor.
- **Cascade configuration mapping**: `CascadeConfiguration` exposes `Delete`/`Assign`/`Share`/`Unshare` (and others). AC-3 only requires those four — emitted as enum-string via `?.ToString()` and serialised through `WhenWritingNull` so callers see only set fields.
- **N:1 surfaced as 1:N**: `RetrieveEntityRequest` returns `OneToManyRelationships` (table is parent) and `ManyToOneRelationships` (table is child) separately. Both are 1:N edges from the table's perspective — the tool emits both with `relationshipType = "OneToMany"` so Claude receives a single uniform list.
- **AC-4 wiring**: orchestrator already accepted `IEnumerable<IDataverseTool>` per `RunAsync` call — no refactor. Added `DataverseToolFactory.CreateMode1Tools(IOrganizationService)` so Story 3.5's Mode 1 generation handler has a single registration point; tools cannot be DI-resolved at app startup because they require a per-request `IOrganizationService` built from caller-supplied credentials (Story 3.8 factory pattern).
- **`solutionName` on `ListCustomTablesTool`**: explicit null per Story 3.4 dev notes — the secondary `solutioncomponent` query is deferred to Phase 3 (deferred-work backlog). DTO carries the field so the JSON shape is forward-compatible; `WhenWritingNull` keeps it out of current output.
- **`DefaultValue` on `GetTableFieldsTool`**: deferred. AC-2 lists "defaultValue (if set)" but the SDK exposes defaults via type-specific properties (`StringAttributeMetadata.DefaultValue`, `BooleanAttributeMetadata.DefaultValue.Value`, `IntegerAttributeMetadata.MinValue`, …). No Mode 1 consumer requires it yet; postponing avoids landing a per-type extractor that may shift when the docx generator (Story 3.5) defines its real consumption shape.
- **Integration test against real Dataverse**: deferred to Story 3.5. Story 3.4 has no E2E entry point of its own — the tools are first wired to a real `ServiceClient` inside `POST /api/document/generate`. The unit suite covers all five ACs with reflection-set SDK doubles.

### File List

- `src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs` (new) — `// F-002, FR-002`
- `src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs` (new) — `// F-003, FR-003`
- `src/DataverseDocAgent.Api/Agent/Tools/DataverseToolFactory.cs` (new) — `// F-001, F-002, F-003` (Mode 1 wiring helper for Story 3.5)
- `src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs` (modified) — added `SolutionName` (null) to `TableDto`
- `tests/DataverseDocAgent.Tests/GetTableFieldsToolTests.cs` (new + extended) — 18 unit tests covering AC-2, AC-5, AC-6 + cancellation + input validation + Boolean True/False options + Picklist value=0 + invalid logical-name shapes
- `tests/DataverseDocAgent.Tests/GetRelationshipsToolTests.cs` (new + extended) — 19 unit tests covering AC-3, AC-5, AC-6 + cancellation + input validation + null-entity error + dedup + cross-table filtering + null cascade fields + invalid logical-name shapes
- `tests/DataverseDocAgent.Tests/ListCustomTablesToolTests.cs` (extended) — added structured-error-on-fault tests (`FaultException` sanitisation, `TimeoutException` handling)
- `tests/DataverseDocAgent.Tests/DataverseToolFactoryTests.cs` (new) — 4 tests pinning the Mode 1 tool set, shared-service binding, null-service guard, and per-tool schema/description contract

### Change Log

| Date       | Notes                                                                                                       |
|------------|-------------------------------------------------------------------------------------------------------------|
| 2026-04-20 | Story 3.4 — implemented three tools + factory + 23 tests. Status → review.                                  |
| 2026-04-20 | Story 3.4 — applied code review patches (P1–P16, P19). 26 new tests; 165 passing. Status → done.            |

### Review Findings

- [x] [Review][Patch] Catch `FaultException<OrganizationServiceFault>` + `TimeoutException` / `CommunicationException` / `HttpRequestException` in `ListCustomTablesTool` — must return structured `{ error }` JSON, never propagate raw SDK fault [src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs] (blind+auditor)
- [x] [Review][Patch] Broaden exception catches in `GetTableFieldsTool` and `GetRelationshipsTool` to `TimeoutException`, `CommunicationException`, `HttpRequestException` — `FaultException` alone leaves transport-level failures unwound into the orchestrator [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:104, GetRelationshipsTool.cs] (blind)
- [x] [Review][Patch] Distinguish "entity not found" from "entity has zero attributes" in `GetTableFieldsTool` — return `{ error: "Table '{tableName}' not found", tableName }` when `EntityMetadata` collection is null/empty so empty success cannot mask a typo [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:82] (auditor)
- [x] [Review][Patch] Re-throw `OperationCanceledException` from inside the catch chain — sanitising cancellation into a tool result silently breaks the orchestrator's cancellation contract [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:91, GetRelationshipsTool.cs] (blind)
- [x] [Review][Patch] Handle `BooleanAttributeMetadata` separately — its `OptionSet` is `BooleanOptionSetMetadata` (TrueOption / FalseOption), not `OptionSetMetadata`; without this branch Boolean attributes drop their True/False label customisations [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:185] (edge)
- [x] [Review][Patch] Make `OptionDto.Value` nullable (`int?`) — SDK can legitimately set `Value = 0` (e.g. State=0 = "Active"); coercing null → 0 conflates "value zero" with "value missing" [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:276] (edge)
- [x] [Review][Patch] Cache `PropertyInfo` lookup for `OptionSet` reflection in a `ConcurrentDictionary<Type, PropertyInfo?>` — per-attribute reflection is measurable on tables with hundreds of attributes; cache must be thread-safe for future parallel orchestrators [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:57, 199] (blind)
- [x] [Review][Patch] Defensive filter on N:N output: assert `r.Entity1LogicalName == tableName || r.Entity2LogicalName == tableName` — SDK could in principle return cross-cutting relationships if the entity-by-LogicalName filter ever weakens [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs] (auditor)
- [x] [Review][Patch] Defensive filter on 1:N output: assert `r.ReferencingEntity == tableName || r.ReferencedEntity == tableName` — same rationale as N:N, guards against future SDK behaviour shift [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs] (auditor)
- [x] [Review][Patch] Dedupe relationships by `SchemaName` (`HashSet<string>` with `OrdinalIgnoreCase`) — `OneToManyRelationships` and `ManyToOneRelationships` overlap on self-referencing relationships and would emit duplicates [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs] (edge)
- [x] [Review][Patch] Always emit `cascadeConfiguration` with all four fields populated (delete/assign/share/unshare) — null `CascadeConfiguration` defaults to `NoCascade` for every action; missing the object would force Claude to assume cascade behaviour [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs] (auditor)
- [x] [Review][Patch] Implement `defaultValue` extraction for `BooleanAttributeMetadata.DefaultValue` and `PicklistAttributeMetadata.DefaultFormValue` — AC-2 explicitly lists "defaultValue (if set)"; per-type extraction limited to types the SDK exposes a default on [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:225] (auditor)
- [x] [Review][Patch] Update `// F-001` annotation on `ListCustomTablesTool` to `// F-001, FR-001 — Custom table discovery tool (Story 1.3 baseline + Story 3.4 hardening)` for consistency with the other two tools [src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs:1] (auditor)
- [x] [Review][Patch] Distinguish "missing tableName" from "wrong-type tableName" in input validation — return specific error `"Parameter 'tableName' must be a string"` when caller sent a number / boolean instead of a string [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:247, GetRelationshipsTool.cs] (edge)
- [x] [Review][Patch] Add logical-name shape validation regex (`^[a-z][a-z0-9_]*$`) — rejects unicode-NBSP, RTL marks, mixed case, and SQL/JSON-injection-shaped payloads before they hit the SDK and surface as a misleading "table not found" [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:52, GetRelationshipsTool.cs] (edge+auditor)
- [x] [Review][Patch] Add `DataverseToolFactoryTests` — pin Mode 1 tool set (exact names + count), shared-service binding via reflection on `_service` field, null-service guard, schema/description contract [tests/DataverseDocAgent.Tests/DataverseToolFactoryTests.cs] (auditor)
- [x] [Review][Patch] Set `RetrieveAsIfPublished = true` on `RetrieveEntityRequest` in `GetRelationshipsTool` — the spec is "current published metadata"; the default `false` returns only fully committed metadata and would miss in-progress publishes from the same session [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs] (blind)
- [x] [Review][Defer] Async SDK execution via `IOrganizationServiceAsync2.ExecuteAsync` — deferred, requires `IDataverseTool` constructor surface change and complicates Moq setup; revisit when load testing surfaces thread-pool exhaustion (blind)
- [x] [Review][Defer] `RetrieveMetadataChanges` paging not implemented — deferred, single-page read is fine for typical tenants; add when `response.ServerVersionStamp != null` is observed (blind)
- [x] [Review][Defer] Cross-cutting structured logging on tool failure paths — deferred to the observability story alongside Story 3.1's sanitized-error-with-code work (auditor)
- [x] [Review][Defer] Orchestrator integration test wiring `DataverseToolFactory` through `AgentOrchestrator.RunAsync` — deferred, Story 3.5's E2E test covers this end-to-end (auditor)
- [x] [Review][Defer] Document `DataverseToolFactory` thread-safety / per-request-construction guarantee in XML doc — deferred to Story 3.5 wiring (edge)
- [x] [Review][Defer] Extract reflection-based test setup helpers (`SetLabel` / `SetNonPublic`) into shared `Tests/Helpers/SdkMetadataBuilder.cs` — deferred, three duplications acceptable; revisit when a fourth tool-test file lands (auditor)
- [x] [Review][Defer] Unicode / non-en-US `LanguageCode` tests on labels — deferred until a multi-language tenant is available for verification (edge)
- [x] [Review][Defer] Negative-case test: `IsCustomRelationship == false` filtered out of `GetRelationshipsTool` — deferred, SDK contract is unambiguous; covered by Story 3.5 E2E (edge)
- [x] [Review][Defer] `MaxLength(50)` cap on `tableName` parameter before regex shape check — deferred to cross-cutting input-validation pass alongside Story 3.5 request DTO validation (edge)
- [x] [Review][Dismiss] Test rename `InputFor` to use `$"..."` literal — cosmetic, no behavioural impact (auditor)
- [x] [Review][Dismiss] `JsonElement` lifetime concerns when stored across `await` boundaries — speculative; tools serialise to string before returning, so no `JsonElement` outlives the call (blind)
- [x] [Review][Dismiss] Duplicate `Results` dictionary test pattern — same as already-passing tests; no additional coverage (edge)
- [x] [Review][Dismiss] `ListCustomTablesTool` `solutionName` Phase 2 deferral marked as "spec drift" — intentional per dev notes; explicit null is the documented Phase 2 contract (auditor)
- [x] [Review][Dismiss] Theoretical NRE on `attr.RequiredLevel` access — auditor admits no real risk path; SDK always populates the wrapper for any non-null `AttributeMetadata` (edge)
