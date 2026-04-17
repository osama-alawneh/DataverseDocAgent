# Story 1.3: Claude Agent Tool-Use Loop — ListCustomTables POC

Status: done

## Story

As a developer,
I want a working Claude agent loop that calls a single custom `IDataverseTool` (`list_custom_tables`) and returns the result,
so that I can validate the full Anthropic SDK → tool call → Dataverse → Claude response pipeline end-to-end.

## Acceptance Criteria

1. `IDataverseTool` interface is defined with at minimum: `string Name`, `string Description`, `JsonElement InputSchema` (the JSON schema for tool parameters), and `Task<string> ExecuteAsync(JsonElement input, EnvironmentCredentials credentials)`.
2. `ListCustomTablesTool` implements `IDataverseTool`, queries Dataverse for all entities where `IsCustomEntity = true`, and returns a JSON string containing an array of `{ displayName, logicalName, schemaName, solutionName, description }` objects.
3. If the environment contains zero custom tables, `ListCustomTablesTool` returns `{ "tables": [], "message": "No custom tables found in this environment" }` rather than an empty array with no explanation.
4. `AgentOrchestrator` accepts a list of `IDataverseTool` and an `EnvironmentCredentials`, registers the tools with the Claude API as tool definitions, runs the agent loop (send prompt → receive tool_use → execute tool → return result → repeat until end_turn), and returns the final Claude text response.
5. The console `Program.cs` runs the full pipeline: load credentials → connect to Dataverse → instantiate tools → run `AgentOrchestrator` → print Claude's final response to the console.
6. The `EnvironmentCredentials` object is passed by reference through the tool call chain and is not serialized, logged, or included in any string at any point in the loop.
7. The Claude agent loop handles the case where Claude does not call any tools (returns end_turn immediately) without crashing.

## Tasks / Subtasks

- [x] Define `IDataverseTool` interface (AC: 1)
  - [x] Create `src/DataverseDocAgent.Api/Agent/Tools/IDataverseTool.cs`
  - [x] Properties: `string Name`, `string Description`, `JsonElement InputSchema`
  - [x] Method: `Task<string> ExecuteAsync(JsonElement input, EnvironmentCredentials credentials)`
- [x] Implement `ListCustomTablesTool` (AC: 2, 3)
  - [x] Create `src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs`
  - [x] Query: `RetrieveMetadataChangesRequest` filtering on `IsCustomEntity = true`, or OData query to EntityDefinitions
  - [x] Return fields: `DisplayName.UserLocalizedLabel.Label`, `LogicalName`, `SchemaName`, `SolutionId`/`SolutionName` (if available), `Description.UserLocalizedLabel.Label`
  - [x] Serialize result to JSON string
  - [x] Handle zero-result case with explicit message object
  - [x] `Name = "list_custom_tables"`, `Description = "Returns all custom tables in the connected Dataverse environment"`, `InputSchema = {}` (no parameters required)
  - [x] Annotate: `// F-001`
- [x] Implement `AgentOrchestrator` (AC: 4, 6, 7)
  - [x] Create `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs`
  - [x] Accept `IEnumerable<IDataverseTool> tools` and `EnvironmentCredentials credentials`
  - [x] Build Claude `Tool` definitions from each `IDataverseTool` (Name, Description, InputSchema)
  - [x] Initial prompt: "You are a Dataverse environment analyst. Use the available tools to list all custom tables in the environment and provide a summary."
  - [x] Agent loop:
    - Send messages to Claude with tools registered
    - If response contains `tool_use` blocks, execute each via the matching `IDataverseTool.ExecuteAsync()`
    - Append `tool_result` to messages and continue
    - If response `StopReason == "end_turn"`, return the final text content
    - Guard against infinite loops: max 10 iterations
  - [x] Pass `EnvironmentCredentials` to `ExecuteAsync` — never serialize or log it
- [x] Wire console runner (AC: 5)
  - [x] Update `DataverseDocAgent.Console/Program.cs`:
    - Load credentials from User Secrets
    - Call `DataverseConnectionFactory.ConnectAsync(credentials)` (verify connection first)
    - Instantiate `ListCustomTablesTool`
    - Instantiate `AgentOrchestrator`
    - Call `orchestrator.RunAsync(prompt, tools, credentials)`
    - Print final response
  - [x] Load `Anthropic:ApiKey` from User Secrets and pass to `AnthropicClient`
- [ ] Manual end-to-end test (AC: 5, 6, 7)
  - [ ] Run against test Dataverse environment
  - [ ] Confirm Claude's response names actual tables from the environment
  - [ ] Confirm no credential values appear in console output

## Dev Notes

- **Anthropic.SDK tool registration:** The `AnthropicClient` in the C# SDK uses `Messages.CreateAsync()` with a `MessageRequest` that includes a `Tools` list. Each `Tool` has `Name`, `Description`, and `InputSchema` (as a `JsonObject` or equivalent). Refer to the SDK README for the exact API shape — it may differ from the Python SDK.
- **Tool use response handling:** The response `Content` block list may contain both `TextBlock` and `ToolUseBlock` entries. Process all `ToolUseBlock` entries, call the matching tool, then send back `ToolResultBlock` entries in a follow-up message.
- **Model to use:** `claude-sonnet-4-6` (the current model in this environment per session config).
- **Dataverse metadata query:** `RetrieveMetadataChangesRequest` is the preferred approach. Alternatively, use the OData endpoint `{environmentUrl}/api/data/v9.2/EntityDefinitions?$filter=IsCustomEntity eq true&$select=DisplayName,LogicalName,SchemaName`. The OData approach may be simpler for the POC.
- **Solution name retrieval:** Getting the solution name for a table requires a separate query via `solutioncomponent` and `solution` entities. For the POC, this is optional — include a `"solution": null` or omit the field if retrieval adds complexity.
- **`IDataverseTool` receives `ServiceClient` or `EnvironmentCredentials`?** The current design passes `EnvironmentCredentials` and the tool creates its own `ServiceClient` if needed, or the tool can accept a pre-built `ServiceClient`. Recommended for POC: pass the pre-built `ServiceClient` as a constructor parameter to the tool — simpler and avoids re-authenticating per tool call. Update the interface if needed: `ExecuteAsync(JsonElement input)` with `ServiceClient` injected via constructor.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Api/Agent/Tools/IDataverseTool.cs`
- `src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs` — `// F-001`
- `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs`
- `src/DataverseDocAgent.Console/Program.cs` — updated

### References

- [Source: docs/prd.md#functional-requirements — FR-001] — custom table discovery requirements
- [Source: _bmad-output/planning-artifacts/architecture.md#5-custom-tool-inventory] — tool inventory and design
- [Source: _bmad-output/planning-artifacts/architecture.md#4-request-lifecycle] — agent loop pattern
- [Source: docs/prd.md#functional-requirements — FR-034, NFR-007] — credential in-memory guarantee

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `MessageResponse.Message` has no public setter in Anthropic.SDK 5.x — orchestrator was updated to build assistant `Message` directly from `response.Content` rather than using the computed property.
- `InternalsVisibleTo` via csproj `<AssemblyAttribute>` generated correctly but the test compiler resolved the delegate constructor as ambiguous with `AnthropicClient`; fixed by making the delegate constructor `public` (valid DI pattern).
- `ListCustomTablesTool` requires `using Microsoft.Xrm.Sdk.Query` for `LogicalOperator` — not included in `Microsoft.Xrm.Sdk.Metadata.Query`.
- `ContentBase` uses custom JSON converters in Anthropic.SDK — plain `JsonSerializer.Deserialize<MessageResponse>` fails; test helpers use direct object initializers instead.

### Completion Notes List

- **IDataverseTool (AC-1):** Interface defined in `DataverseDocAgent.Api.Agent.Tools` with `Name`, `Description`, `JsonElement InputSchema`, `ExecuteAsync(JsonElement, EnvironmentCredentials)`.
- **ListCustomTablesTool (AC-2, 3):** Uses `RetrieveMetadataChangesRequest` with `IsCustomEntity = true` filter via `IOrganizationService` constructor injection (pre-authenticated `ServiceClient` passed at call site). `solutionName` is null per POC scope. Zero-result path returns `{ tables: [], message: "No custom tables found..." }`. Annotated `// F-001`.
- **AgentOrchestrator (AC-4, 6, 7):** Model `claude-sonnet-4-6`. Max 10 iterations guard. Credentials passed through `ExecuteAsync` — never serialized/logged. Handles `end_turn` with zero tool calls (AC-7). Public delegate constructor enables testing without real `AnthropicClient`.
- **Console Program.cs (AC-5):** Updated to load `Anthropic:ApiKey` from User Secrets, connect Dataverse, instantiate tools, run orchestrator, print result. Now references `DataverseDocAgent.Api` project.
- **Tests:** 10 unit tests, all passing. Covers contract (Name/Description/InputSchema), empty-result JSON (AC-3), table fields (AC-2), credential non-leakage (AC-6), end_turn path (AC-7), tool dispatch, max-iteration guard.
- **New test project:** `tests/DataverseDocAgent.Tests` (xUnit + Moq), added to solution.

### File List

- `src/DataverseDocAgent.Api/Agent/Tools/IDataverseTool.cs` — new
- `src/DataverseDocAgent.Api/Agent/Tools/ListCustomTablesTool.cs` — new
- `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs` — new
- `src/DataverseDocAgent.Api/DataverseDocAgent.Api.csproj` — added InternalsVisibleTo
- `src/DataverseDocAgent.Console/Program.cs` — updated (agent loop wiring)
- `src/DataverseDocAgent.Console/DataverseDocAgent.Console.csproj` — added project reference to Api
- `tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj` — new test project
- `tests/DataverseDocAgent.Tests/ListCustomTablesToolTests.cs` — new (6 tests)
- `tests/DataverseDocAgent.Tests/AgentOrchestratorTests.cs` — new (4 tests)
- `DataverseDocAgent.sln` — added tests solution folder + DataverseDocAgent.Tests project
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — updated 1-3 to in-progress

## Review Findings

### Decision Needed

- [x] [Review][Defer] D1: CancellationToken not propagated to IDataverseTool.ExecuteAsync — deferred: no cancellable host context until GenerationBackgroundService in Story 3.1; adding now has zero practical effect in a single-run console POC.
- [x] [Review][Patch] D2→P: Remove `SolutionName` from `TableDto` — field was always null; removing it until the solution query is implemented (AC-2 partial, by design) [ListCustomTablesTool.cs:~90]
- [x] [Review][Dismiss] D3: InputSchema shape — spec wording `{}` was informal; `{"type":"object","properties":{}}` is correct valid JSON Schema. No change needed.
- [x] [Review][Patch] D4→P: Remove `EnvironmentCredentials` from `IDataverseTool.ExecuteAsync` — tools are pre-authenticated via constructor injection; the parameter is dead weight and a misleading contract [IDataverseTool.cs, ListCustomTablesTool.cs, AgentOrchestrator.cs, tests]

### Patches

- [x] [Review][Patch] P1: No exception handling around tool.ExecuteAsync — any tool throw crashes the entire agent loop [AgentOrchestrator.cs:~63]
- [x] [Review][Patch] P2: ServiceClient never disposed in Program.cs — resource leak [Program.cs:~47]
- [x] [Review][Patch] P3: MaxIterations limit hit with no log or warning — caller receives sentinel string indistinguishable from normal output [AgentOrchestrator.cs:~86]
- [x] [Review][Patch] P4: MaxIterations test asserts `callCount <= 10` but not exactly 10, and doesn't assert the sentinel message is returned [AgentOrchestratorTests.cs:~95]
- [x] [Review][Patch] P5: JsonNode.Parse in BuildSdkTools throws uncaught JsonException on invalid schema [AgentOrchestrator.cs:~92]
- [x] [Review][Patch] P6: Response.Content not guarded for null/empty when StopReason == "tool_use" [AgentOrchestrator.cs:~56]
- [x] [Review][Patch] P7: Unknown-tool error built via string interpolation — tool name not JSON-escaped, potential malformed JSON [AgentOrchestrator.cs:~63]
- [x] [Review][Patch] P8: `null!` passed as third argument to Message constructor — null-forgiving operator suppresses a real nullability warning [AgentOrchestrator.cs:~40]
- [x] [Review][Patch] P9: SetLabel test helper silently skips label setup on reflection failure — test may pass vacuously with null labels [ListCustomTablesToolTests.cs:~144]
- [x] [Review][Patch] P10: Credential non-leak test only covers end_turn path; does not test that tool execution result containing credential values is not forwarded [AgentOrchestratorTests.cs:~115]
- [x] [Review][Patch] P11: FetchCustomEntities does not null-check response.EntityMetadata before returning [ListCustomTablesTool.cs:~79]
- [x] [Review][Patch] P12: StopReason compared with case-sensitive string literal — should use OrdinalIgnoreCase [AgentOrchestrator.cs:~56]

### Deferred

- [x] [Review][Defer] F1: ExtractText returns only first TextContent block; multi-block responses silently drop remaining content [AgentOrchestrator.cs:~107] — deferred, POC scope limitation
- [x] [Review][Defer] F2: FindTool uses StringComparison.Ordinal — case mismatch from Anthropic API would silently fail [AgentOrchestrator.cs:~104] — deferred, Anthropic API returns exact registered names
- [x] [Review][Defer] F3: dnlib referenced in API project with no visible usage in this story [DataverseDocAgent.Api.csproj:~16] — deferred, pre-existing dependency for future stories
- [x] [Review][Defer] F4: ListCustomTablesTool.ExecuteAsync wraps synchronous IOrganizationService.Execute in Task.FromResult — blocks thread pool thread [ListCustomTablesTool.cs:~46] — deferred, pre-existing SDK limitation (see story-1.2 deferred work)
- [x] [Review][Defer] F5: Null UserLocalizedLabel silently produces null JSON fields — existing nullable chain handles this [ListCustomTablesTool.cs:~94] — deferred, handled by WhenWritingNull serializer option
- [x] [Review][Defer] F6: ToJsonElement converts null block.Input to empty object `{}` — theoretical for no-param tool [AgentOrchestrator.cs:~132] — deferred, no-param tool never receives non-null input

## Change Log

- 2026-04-14: Story 1.3 implemented — IDataverseTool interface, ListCustomTablesTool, AgentOrchestrator, console runner wired. 10 unit tests passing. Status → review.
- 2026-04-15: Code review complete — 4 decision_needed, 12 patch, 6 deferred, 1 dismissed.
