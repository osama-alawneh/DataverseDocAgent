# Story 3.4: Dataverse Tools — Tables, Fields, and Relationships

Status: ready-for-dev

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

- [ ] Update `ListCustomTablesTool` to production quality (AC: 1)
  - [ ] Move from Console project to `DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs`
  - [ ] Ensure all five return fields are populated: `displayName`, `logicalName`, `schemaName`, `solutionName` (query via solution component if feasible; `null` if not), `description`
  - [ ] Handle zero-result case explicitly
  - [ ] Annotate: `// F-001 — FR-001`
- [ ] Implement `GetTableFieldsTool` (AC: 2, 5)
  - [ ] Create `src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs`
  - [ ] `Name = "get_table_fields"`, `Description = "Returns all custom fields for a specified table"`, `InputSchema = { "type": "object", "properties": { "tableName": { "type": "string" } }, "required": ["tableName"] }`
  - [ ] Query: `RetrieveMetadataChangesRequest` for attributes of the given entity where `IsCustomAttribute = true`
  - [ ] Per field: extract `DisplayName.UserLocalizedLabel.Label`, `LogicalName`, `AttributeType.ToString()`, `RequiredLevel.Value.ToString()`
  - [ ] For `PicklistAttributeMetadata` and `MultiSelectPicklistAttributeMetadata`: extract all `OptionSet.Options` as `{ label, value }` pairs
  - [ ] For `DefaultValue`: include if non-null
  - [ ] Handle "table not found" gracefully — return `{ "error": "Table '{tableName}' not found", "tableName": tableName }`
  - [ ] Annotate: `// F-002 — FR-002`
- [ ] Implement `GetRelationshipsTool` (AC: 3, 5)
  - [ ] Create `src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs`
  - [ ] `Name = "get_relationships"`, `Description = "Returns all custom relationships for a specified table"`, InputSchema with `tableName` parameter
  - [ ] Query 1: `RetrieveEntityRequest` (with relationship details) for the given table → extract `OneToManyRelationships` where `IsCustomRelationship = true`
  - [ ] Query 2: `RetrieveManyToManyRelationshipsRequest` or filter from full relationship list for N:N where `IsCustomRelationship = true` and the given table is `Entity1LogicalName` or `Entity2LogicalName`
  - [ ] Per 1:N: `schemaName`, `referencingEntity = ReferencingEntity`, `referencedEntity = ReferencedEntity`, `cascadeConfiguration` (delete, assign, share, unshare as strings from the CascadeType enum)
  - [ ] Per N:N: `schemaName`, `entity1LogicalName`, `entity2LogicalName`
  - [ ] Handle "table not found" gracefully
  - [ ] Annotate: `// F-003 — FR-003`
- [ ] Register all three tools in `AgentOrchestrator` for Mode 1 (AC: 4)
  - [ ] Update `AgentOrchestrator` to accept a factory or DI-resolved list of `IDataverseTool`
  - [ ] All three tools are included in the `Tools` list sent to Claude for Mode 1 generation
- [ ] Integration test against real Dataverse environment (AC: 1–5)
  - [ ] Run each tool individually against a known table (e.g., `account` or a custom test table)
  - [ ] Verify field types including at least one OptionSet field
  - [ ] Verify relationship output includes cascade behaviours

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

### Completion Notes List

### File List
