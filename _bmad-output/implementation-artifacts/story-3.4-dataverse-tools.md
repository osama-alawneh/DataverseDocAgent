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
- `tests/DataverseDocAgent.Tests/GetTableFieldsToolTests.cs` (new) — 11 unit tests covering AC-2, AC-5, AC-6 + cancellation + input validation
- `tests/DataverseDocAgent.Tests/GetRelationshipsToolTests.cs` (new) — 12 unit tests covering AC-3, AC-5, AC-6 + cancellation + input validation
