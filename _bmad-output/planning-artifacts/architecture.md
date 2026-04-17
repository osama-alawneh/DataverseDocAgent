---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
lastStep: 8
status: complete
completedAt: '2026-04-16'
inputDocuments:
  - docs/prd.md
  - docs/new requirements.md
  - docs/poc-baseline.md
  - docs/validation-report-2026-04-16.md
  - _bmad-output/planning-artifacts/epics.md
workflowType: architecture
project_name: DataverseDocAgent
user_name: Osama
date: 2026-04-16
revisionBasis: PRD v4.0 — 14 new FRs (FR-041–054), 3 new NFRs
preservedDecisions: [1, 2, 3]
---

# DataverseDocAgent — Technical Architecture

**Author:** Winston (BMAD Architect)
**Input:** PRD v4.0 — Validated (2026-04-16)
**Date:** April 2026
**Status:** REVISION IN PROGRESS — PRD v3 decisions confirmed; PRD v4 additions in review

---

## Confirmed Decisions

| # | Decision | Choice |
|---|---------|--------|
| 1 | Generation mode | **Option B** — Async job + polling for Mode 1 and Mode 3; synchronous for Mode 2 and security check |
| 2 | MCP tool architecture | **Option B** — Custom in-process tools, per-request credential scoping |
| 3 | Document temporary storage | **Option A** (in-memory) for Phase 1 POC; **Option B** (Azure Blob Storage) from Phase 2 onwards via `IDocumentStore` abstraction |

---

## 1. Architecture Overview

DataverseDocAgent is a stateless ASP.NET Core Web API that orchestrates Claude as an AI agent over a custom in-process MCP tool layer. Each request brings its own credentials, spawns a scoped agent session, reads Dataverse metadata via custom tools, and either streams output into a `.docx` or returns structured JSON — then cleans up completely.

```
┌─────────────────────────────────────────────────────────────┐
│  API Client (Postman / Integration)                         │
└──────────────────────┬──────────────────────────────────────┘
                       │ HTTPS POST
┌──────────────────────▼──────────────────────────────────────┐
│  ASP.NET Core Web API  (Azure App Service)                  │
│  ┌─────────────────┐  ┌──────────────────────────────────┐  │
│  │ API Controllers │  │ Middleware: HTTPS, Error, Logging │  │
│  └────────┬────────┘  └──────────────────────────────────┘  │
│           │                                                  │
│  ┌────────▼────────────────────────────────────────────┐    │
│  │  AgentOrchestrator                                  │    │
│  │  (scoped per request, holds credentials in memory)  │    │
│  └────────┬──────────────────────────┬─────────────────┘    │
│           │                          │                       │
│  ┌────────▼────────┐      ┌──────────▼───────────────────┐  │
│  │  Claude Client  │      │  DataverseMcpToolProvider    │  │
│  │  (Anthropic SDK)│◄────►│  (per-request, credentialed) │  │
│  └─────────────────┘      └──────────┬───────────────────┘  │
│                                      │                       │
│  ┌───────────────────────────────────▼───────────────────┐  │
│  │  Document / Response Builder                          │  │
│  │  (DocumentGenerator | JsonResponseBuilder)            │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│  Microsoft Dataverse (Customer Environment)                  │
│  Read-only. Accessed via Dataverse SDK client credentials.  │
└─────────────────────────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│  Claude API (Anthropic)                                     │
│  Receives: schema metadata, decompiled C#, JS, flow defs   │
│  Never receives: record-level CRM data                      │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. System Layers

| Layer | Technology | Responsibility |
|-------|-----------|---------------|
| **API** | ASP.NET Core Web API (.NET 8, C#) | Routing, request validation, auth header enforcement, structured error responses |
| **Orchestration** | `AgentOrchestrator` (custom service) | Drives the Claude agent loop; coordinates tool calls; manages request lifecycle |
| **AI** | Anthropic C# SDK (`Anthropic.SDK`) | Claude API client; tool use loop; prompt construction |
| **MCP Tools** | Custom `IDataverseTool` implementations | Expose Dataverse read operations as Claude-callable tools; per-request credentials |
| **Dataverse SDK** | `Microsoft.PowerPlatform.Dataverse.Client` | Authenticates via client credentials; executes read-only SDK/OData queries |
| **Plugin Analysis** | `dnlib` | In-process DLL loading and C# decompilation; no disk writes |
| **Document Output** | `DocumentFormat.OpenXml` | Assembles `.docx` from structured AI output |
| **Temp Storage** | `IDocumentStore` (abstraction) | Phase 1: `InMemoryDocumentStore`; Phase 2+: `BlobDocumentStore` |
| **Hosting** | Azure App Service | Free Tier (MVP); upgrades to Basic without code changes |

---

## 3. Project Structure (Feature Folder)

```
DataverseDocAgent/
├── DataverseDocAgent.Api/              ← Single project (ASP.NET Core Web API)
│   │
│   ├── Program.cs
│   ├── appsettings.json
│   │
│   ├── Features/
│   │   ├── SecurityCheck/              ← F-029, F-030, F-031 (FR-039)
│   │   │   ├── SecurityCheckController.cs
│   │   │   ├── SecurityCheckRequest.cs
│   │   │   ├── SecurityCheckResponse.cs
│   │   │   └── SecurityCheckService.cs
│   │   │
│   │   ├── DocumentGenerate/           ← F-001–013 (FR-036)
│   │   │   ├── DocumentGenerateController.cs
│   │   │   ├── DocumentGenerateRequest.cs
│   │   │   ├── DocumentGenerateResponse.cs
│   │   │   ├── DocumentGenerateService.cs
│   │   │   └── Sections/
│   │   │       ├── ExecutiveSummarySection.cs
│   │   │       ├── TableCatalogueSection.cs
│   │   │       └── ...
│   │   │
│   │   ├── ImpactAnalyse/              ← F-014–019 (FR-037)
│   │   ├── HealthAudit/                ← F-020–028 (FR-038)
│   │   └── Download/                   ← F-040 (FR-040)
│   │
│   ├── Agent/
│   │   ├── AgentOrchestrator.cs        ← Claude tool-use loop
│   │   ├── PromptBuilder.cs
│   │   └── Tools/
│   │       ├── IDataverseTool.cs
│   │       ├── ListCustomTablesTool.cs
│   │       ├── GetTableFieldsTool.cs
│   │       ├── GetRelationshipsTool.cs
│   │       ├── GetPluginAssembliesTool.cs
│   │       ├── GetWebResourcesTool.cs
│   │       ├── GetFlowDefinitionsTool.cs
│   │       ├── GetSecurityRolesTool.cs
│   │       └── GetOrganisationMetadataTool.cs
│   │
│   ├── Dataverse/
│   │   ├── DataverseConnectionFactory.cs
│   │   └── PluginDecompiler.cs         ← dnlib wrapper
│   │
│   ├── Documents/
│   │   └── DocxBuilder.cs
│   │
│   ├── Storage/
│   │   ├── IDocumentStore.cs           ← abstraction
│   │   ├── InMemoryDocumentStore.cs    ← Phase 1 impl
│   │   └── BlobDocumentStore.cs        ← Phase 2+ impl
│   │
│   ├── Jobs/
│   │   ├── IJobStore.cs
│   │   ├── InMemoryJobStore.cs
│   │   └── GenerationBackgroundService.cs
│   │
│   ├── Middleware/
│   │   └── ExceptionHandlingMiddleware.cs
│   │
│   └── Common/
│       ├── EnvironmentCredentials.cs
│       └── StructuredErrorResponse.cs
```

---

## 4. Request Lifecycle — Mode 1 (Async Job Path)

```
POST /api/document/generate
    │
    ▼
[DocumentGenerateController]
    │  Validate request shape
    │  Create Job record { status: "queued" }
    │  Enqueue generation task
    │  Return 202 Accepted { jobId: "..." }
    │
    ▼  (background via GenerationBackgroundService)
[AgentOrchestrator - scoped to job]
    │  1. Build EnvironmentCredentials (in-memory, never serialized)
    │  2. DataverseConnectionFactory.Connect(credentials)
    │     → Lightweight test call; on failure: mark job failed, return
    │  3. Instantiate IDataverseTool implementations with credentials
    │  4. Build Claude tool definitions from IDataverseTool metadata
    │  5. Claude agent loop:
    │     a. Send mode-specific prompt + tool definitions
    │     b. Claude responds with tool_use blocks
    │     c. Execute tools → return results to Claude
    │     d. Repeat until stop_reason: end_turn
    │  6. Parse structured JSON output from Claude
    │  7. DocxBuilder.Build(output) → byte[]
    │  8. IDocumentStore.Store(bytes) → token (24h TTL)
    │  9. Mark job complete { status: "ready", downloadToken: "..." }
    │  10. EnvironmentCredentials goes out of scope → GC eligible
    │
    ▼  (client polling)
GET /api/jobs/{jobId}  →  { status: "ready", downloadToken: "abc123" }
    │
    ▼
GET /api/download/abc123  →  200 + .docx bytes
```

---

## 5. Custom Tool Inventory (Decision 2: Option B)

| Tool Name | Returns | FR Coverage |
|-----------|---------|-------------|
| `list_custom_tables` | All IsCustomEntity=true tables | FR-001 |
| `get_table_fields` | All IsCustomAttribute=true fields for a table | FR-002 |
| `get_relationships` | All IsCustomRelationship=true | FR-003 |
| `get_plugin_assemblies` | PluginAssembly records + decompiled C# (dnlib) | FR-004 |
| `get_plugin_steps` | SdkMessageProcessingStep per assembly | FR-004 |
| `get_web_resources_js` | WebResource type=3 (JavaScript) with source | FR-006 |
| `get_flow_definitions` | Solution-aware flows (Power Platform API) | FR-007 |
| `get_classic_workflows` | Category=0 workflows with XAML | FR-008 |
| `get_business_rules` | Category=2 workflows | FR-009 |
| `get_security_roles` | Non-system roles with privileges | FR-010 |
| `get_form_definitions` | SystemForm records | FR-006, FR-017 |
| `get_saved_queries` | SavedQuery records (views) | FR-018, FR-024 |
| `get_organisation_metadata` | Environment name, version, language | FR-011 |

---

## 6. Credential Handling Contract

```csharp
// EnvironmentCredentials — never serialized, never logged
public sealed class EnvironmentCredentials
{
    public string EnvironmentUrl { get; }
    public string TenantId { get; }
    public string ClientId { get; }
    [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.Never)]
    public string ClientSecret { get; }
    // Serilog destructuring policy must explicitly exclude this type
}
```

---

## 7. Storage Abstraction

```csharp
public interface IDocumentStore
{
    Task<string> StoreAsync(byte[] documentBytes, TimeSpan ttl);  // returns token
    Task<byte[]?> RetrieveAsync(string token);                    // null = expired/invalid
}

// Phase 1: InMemoryDocumentStore (IMemoryCache)
// Phase 2+: BlobDocumentStore (Azure.Storage.Blobs + SAS URL or stream)
```

---

## 8. Async Job Model

```csharp
public interface IJobStore
{
    string CreateJob();                          // returns jobId
    void UpdateStatus(string jobId, JobStatus status, string? downloadToken = null);
    JobRecord? GetJob(string jobId);
}

public enum JobStatus { Queued, Running, Ready, Failed }
public record JobRecord(string JobId, JobStatus Status, string? DownloadToken, string? ErrorMessage);
```

---

## 9. Error Response Standard (NFR-014)

```json
{
  "error": "Human-readable description",
  "code": "CREDENTIAL_REJECTED | PERMISSION_MISSING | GENERATION_TIMEOUT | FIELD_NOT_FOUND | TOKEN_EXPIRED | JOB_NOT_FOUND | INTERNAL_ERROR",
  "safeToRetry": true
}
```

---

## 10. NuGet Package Selection

| Package | Purpose |
|---------|---------|
| `Anthropic.SDK` | Claude API, tool use loop |
| `Microsoft.PowerPlatform.Dataverse.Client` | Dataverse connection + SDK |
| `dnlib` | Plugin DLL decompilation (in-process, no disk) |
| `DocumentFormat.OpenXml` | .docx generation |
| `Serilog.AspNetCore` | Structured logging with credential exclusion |
| `Azure.Storage.Blobs` | Blob storage (Phase 2+ — IDocumentStore Option B) |
| `Microsoft.Extensions.Caching.Memory` | InMemoryDocumentStore + InMemoryJobStore |

---

## 11. Phase-by-Phase Scope

| Phase | Architecture Work |
|-------|-----------------|
| **P1 — POC** | Console app. AgentOrchestrator + ListCustomTablesTool only. Validate credential in-memory handling and Claude tool-use loop end-to-end. |
| **P2 — Mode 1 MVP** | Full API project. SecurityCheck (sync). DocumentGenerate (async job). Tables/fields/relationships/.docx. InMemoryDocumentStore. SecurityRole artefact. |
| **P2 — Storage upgrade** | Swap InMemoryDocumentStore → BlobDocumentStore before first paying customer. |
| **P3 — Mode 1 Full** | Plugin tools (dnlib), JS tools, Flow/workflow tools, all 9 sections. |
| **P4 — Mode 2** | ImpactAnalyse (synchronous, 60s target). Field-lookup tools. Risk rating algorithm. |
| **P5 — Mode 3** | HealthAudit. Multi-check prompt. Cross-artefact analysis. |
| **P6 — Web UI** | New frontend project. OAuth2 (MSAL). Subscription middleware. API layer unchanged. |

---

## 12. Starter Template Evaluation

**Project already initialized** — no starter selection required. Existing tech stack (ASP.NET Core Web API, .NET 8, C#) is confirmed adequate for all PRD v4 additions.

**New NuGet packages required by PRD v4:** None. All v4 features are covered by the existing package set. Mermaid diagram generation (FR-049) is pure string construction. Record stats use the existing Dataverse SDK. Two-layer `.docx` structure uses the existing `DocumentFormat.OpenXml` package.

**Rate limiting (NFR-018, P3+):** ASP.NET Core built-in rate limiting middleware (`Microsoft.AspNetCore.RateLimiting`) ships with .NET 7+ — no additional NuGet dependency. Architecture decision on placement deferred to P3 planning.

---

## 13. Project Context Analysis — PRD v4 Delta — PRD v4 Delta

> **Revision context:** Decisions 1–3 above were made against PRD v3. This section records the architectural implications of PRD v4.0 additions (FR-041–054, NFR-016/017/018) before new decisions are made. All v3 decisions are preserved unchanged.

### Requirements Overview

**New Functional Requirements — 14 additions across 3 categories:**

| Category | FRs | Description |
|----------|-----|-------------|
| Environment Intelligence (Mode 1) | FR-041–050 | Opening narrative, publisher prefix, table signal scoring, plugin blast radius classification (deterministic), confidence layer taxonomy, two-layer output structure, Top 5 Risks, execution identity, Mermaid diagram (deterministic), app user inventory |
| Health Audit additions (Mode 3) | FR-051–054 | Table ownership by app user, abandoned plugin detection, zero-record table detection, unmanaged solution detection |
| Output quality standards | NFR-016/017/018 | Recommendation format compliance, confidence tag completeness, rate limiting policy |

**Non-Functional Requirements — critical architectural drivers:**

- **NFR-001 (Performance):** POC baseline (Phase 1) measured 30–44s for a single `list_custom_tables` tool call against 400+ tables. Full Mode 1 calls 8+ tool types. NFR-001 target (5 min typical / 10 min large) is at risk. `get_table_record_stats` additions in v4 increase query load further.
- **NFR-016/017 (Output Quality):** Every AI-generated statement must carry `[VERIFIED]`/`[INFERRED]`/`[ESTIMATED]` — requires prompt engineering architectural decision, not just a runtime instruction.
- **NFR-018 (Rate Limiting):** P1–P2 no enforcement. P3+ per-API-key throttling required. Architecture needs a hook location identified now.

**Deterministic vs. AI-generated — a new boundary:**

PRD v4 explicitly marks two computations as *deterministic, not AI-generated*:
- FR-044: Blast radius risk tier (sync/async × pre/post × filtered/unfiltered × error handling → Critical/High/Medium/Low)
- FR-049: Mermaid ER diagram (built from relationship metadata)

These cannot be delegated to Claude. A server-side computation step is required before data reaches the AI layer.

### Scale & Complexity

- **Domain:** API backend only (no UI until Phase 6)
- **Complexity level:** High — multi-layer AI orchestration + deterministic computation + complex two-layer output structure
- **New architectural components identified:** 3 — `DeterministicAnalyser`, enhanced `DocxBuilder` (layer-aware section model), 5 new/enhanced Dataverse tools
- **New tool inventory additions:** `get_table_record_stats`, `get_application_users`, `get_solutions`, enhanced `get_plugin_assemblies` (add `ModifiedOn`), `get_table_owner_stats` (Mode 3 only)

### Technical Constraints & Dependencies

- `.docx` two-layer structure (FR-046) requires `DocxBuilder` to understand Executive Layer vs Technical Reference Layer — same file, page break separated, separate ToC entries
- Confidence layer enforcement (FR-045/NFR-017) must be structural in the prompt, not advisory — prompt must require tagged output format
- Record recency queries (FR-001/043/053) add significant query load: up to ~400+ queries for large environments → requires batching/tier strategy (see ADR-004)
- Unmanaged solution detection (FR-054) requires a `get_solutions` tool not present in v3 architecture

### Cross-Cutting Concerns Identified

| Concern | Affects | Architecture impact |
|---------|---------|-------------------|
| Deterministic computation | Mode 1, Mode 3 | New `DeterministicAnalyser` component sits between tool layer and Claude prompt |
| Confidence tag discipline | Mode 1, Mode 3 output | Prompt architecture decision required (Step 4) |
| Record stat query performance | Mode 1, Mode 3 | Three-tier batching strategy — see ADR-004 |
| Two-layer docx structure | DocxBuilder | Layer-aware section model required |
| Rate limiting hook | All endpoints | ASP.NET Core rate limiting middleware location decision deferred to P3 |

---

## ADR-004 — Record Stat Query Strategy

**Status:** CONFIRMED  
**Date:** 2026-04-16  
**FR Coverage:** FR-001, FR-043, FR-053  
**Tool:** `get_table_record_stats`

### Decision

`get_table_record_stats` must use parallel batch execution via `Task.WhenAll`. Sequential queries per table are rejected. Batch size is configurable; default is **10 concurrent queries**.

### Three-Tier Table Count Model

| Tier | Custom table count | Strategy |
|------|--------------------|----------|
| **Small** | < 30 | Query all tables — no filtering |
| **Medium** | 30–100 | Parallel batching at default batch size (10 concurrent) |
| **Large** | > 100 | Query only tables that have at least one other qualifying signal: plugin registration, relationship, or form presence. Low-signal tables above threshold are skipped for record count queries. |

**Rationale:** POC measured 400+ tables returned by `list_custom_tables` against a developer environment. At 1 query per table, sequential execution would add minutes to Mode 1 generation time — pushing NFR-001 beyond its boundary before AI analysis begins. The large-tier filter avoids wasting query budget on tables that will score low across all four signals regardless of record count. Record count alone is not the primary signal for abandoned tables.

**Deferred to implementation:** Exact tier thresholds (30 / 100) are implementation-level decisions confirmed in Story 3.4. The three-tier model is the architectural constraint — thresholds may be tuned during Phase 3 based on real environment profiling.

### Design constraint for DeterministicAnalyser

The three-tier dispatch logic must live in `DeterministicAnalyser` (or a dedicated `RecordStatsBatcher` service it owns), not in the tool implementation itself. The tool (`get_table_record_stats`) remains a simple per-table query; batching and tier selection are orchestration concerns.

### Rejected alternatives

| Option | Reason rejected |
|--------|----------------|
| Sequential per-table queries | Unacceptable at 100+ tables — multiple minutes of query time before AI analysis begins |
| Single aggregate FetchXML across all tables | Not supported by Dataverse OData API for cross-entity aggregation |
| Skip record stats entirely for large environments | Violates FR-001 and FR-043 acceptance criteria; record recency is a required signal |

---

## ADR-005 — DeterministicAnalyser: Dedicated Service Component

**Status:** CONFIRMED  
**Date:** 2026-04-16  
**FR Coverage:** FR-044 (blast radius classification), FR-049 (Mermaid diagram), FR-011 (complexity rating)

### Decision

A dedicated `DeterministicAnalyser` service is introduced between the tool data collection layer and Claude prompt construction. All deterministic computations — blast radius risk tier, Mermaid diagram, complexity rating, and record stat batching dispatch — are owned by this component.

```
Tool results (raw)
      │
      ▼
DeterministicAnalyser.Analyse(rawData) → EnrichedEnvironmentPayload
      │
      ▼
PromptBuilder.Build(enrichedPayload) → Claude prompt
      │
      ▼
Claude (receives pre-classified data; never re-derives deterministic values)
```

### Responsibilities

| Computation | FR | DeterministicAnalyser responsibility |
|-------------|-----|--------------------------------------|
| Blast radius risk tier | FR-044 | Apply deterministic tier logic (sync/async × pre/post × filtered × error handling) to every `SdkMessageProcessingStep` before data reaches prompt |
| Mermaid ER diagram source | FR-049 | Build full Mermaid diagram string from relationship metadata; output included verbatim in docx T10 section and embedded (E4) |
| Environment complexity rating | FR-011 | Score deterministically from table/plugin/flow/JS counts per the defined scoring model |
| Record stat batch dispatch | ADR-004 | Own three-tier table count logic; dispatch `get_table_record_stats` in parallel batches |

### Design constraint

`DeterministicAnalyser` receives `RawEnvironmentData` (aggregated tool results) and returns `EnrichedEnvironmentPayload`. It has no dependency on `AgentOrchestrator` or `ClaudeClient` — it is a pure transformation service. This boundary ensures deterministic outputs are never re-computed or overridden by the AI layer.

### Rejected alternative

**Static helpers on Tool classes** — rejected because blast radius classification requires cross-referencing `SdkMessageProcessingStep` data with `PluginAssembly` data (two separate tools), and Mermaid generation spans all relationship data. Neither can be isolated inside a single tool implementation.

---

## ADR-006 — Confidence Layer: Structured JSON Output Schema

**Status:** CONFIRMED  
**Date:** 2026-04-16  
**FR Coverage:** FR-045, NFR-017  
**Note:** NFR-017 classifies any untagged AI-generated statement as a quality failure.

### Decision

Claude must return all Mode 1 and Mode 3 output as structured JSON conforming to a formally defined output schema. Every finding, explanation, and recommendation is an object with an explicit `confidence` field. The orchestrator validates schema conformance before handing output to `DocxBuilder`. Malformed or incomplete output is rejected at this validation gate.

```json
{
  "findings": [
    {
      "type": "plugin_analysis",
      "entityName": "EquipmentStatusPlugin",
      "text": "Synchronous pre-operation plugin on vel_equipment Update with no try/catch block.",
      "confidence": "VERIFIED"
    }
  ],
  "recommendations": [
    {
      "severity": "Critical",
      "category": "Code Safety",
      "entityName": "EquipmentStatusPlugin",
      "what": "...",
      "whyProblem": "...",
      "consequence": "...",
      "howToFix": "...",
      "estimatedEffort": "...",
      "confidence": "VERIFIED"
    }
  ]
}
```

### Schema as a standalone deliverable

**The output schema is a contract, not an implementation detail.** It must be formally defined and documented as a standalone artefact before any Mode 1 output stories are written. Stories for Executive Layer sections, Technical Reference sections, and recommendations all depend on this schema — they cannot be correctly implemented without it.

**Required deliverable:** `docs/output-schema-mode1.json` (or equivalent) — a complete, versioned JSON Schema definition covering all Mode 1 output sections. This document is a prerequisite gate for P3 story writing.

### Enforcement mechanism

The orchestrator validates Claude's response against the schema using a JSON Schema validator (e.g., `JsonSchema.Net`). If validation fails:
1. Log the failure (without logging credential data)
2. Return a structured error to the client: `{ "error": "Output generation failed schema validation", "code": "OUTPUT_SCHEMA_VIOLATION", "safeToRetry": true }`
3. Do not attempt to partially assemble a document from invalid output

### Rejected alternatives

| Option | Reason rejected |
|--------|----------------|
| System prompt instruction only (no schema enforcement) | NFR-017 requires runtime-verifiable compliance — an instruction without enforcement cannot satisfy "quality failure" language |
| Post-processing second Claude pass to add tags | Two API calls doubles cost and latency; still produces no structural guarantee of completeness |

---

## ADR-007 — DocxBuilder: Layer-Aware Section Model

**Status:** CONFIRMED  
**Date:** 2026-04-16  
**FR Coverage:** FR-046 (two-layer output structure), FR-013 (Word document generation)

### Decision

`IDocumentSection` carries a `DocumentLayer` property. `DocxBuilder` groups sections by layer, renders Executive Layer first, inserts a page break, renders Technical Reference Layer, then constructs a unified ToC with entries for both layers.

```csharp
public enum DocumentLayer { Executive, TechnicalReference }

public interface IDocumentSection
{
    DocumentLayer Layer { get; }
    string SectionTitle { get; }
    void Render(WordprocessingDocument doc, GenerationOutput output);
}
```

**Executive Layer sections (E1–E5):** `EnvironmentNarrativeSection`, `PublisherPrefixSection`, `ComplexitySummarySection`, `MermaidDiagramSection`, `TopRisksSection`

**Technical Reference Layer sections (T1–T10):** `CustomTablesSection`, `FieldCatalogueSection`, `RelationshipMapSection`, `PluginAnalysisSection`, `FlowWorkflowSection`, `JavaScriptSection`, `SecurityOverviewSection`, `IntegrationSignalsSection`, `RecommendationsSection`, `MermaidSourceSection`

### ToC construction

`DocxBuilder` iterates all sections in layer order to build ToC entries. Both layers appear in the same ToC — Executive Layer entries first, Technical Reference entries second — each with correct page number references.

### Rejected alternative

**Separate `ExecutiveLayerBuilder` and `TechnicalReferenceLayerBuilder` classes** — rejected because unified ToC construction requires both layers visible simultaneously. Splitting the builder introduces coordination coupling with no structural benefit.

---

## ADR-008 — Mode 1 Prompt Architecture: Section-Group Multi-Pass

**Status:** CONFIRMED  
**Date:** 2026-04-16  
**FR Coverage:** FR-013, FR-041–050 (all Mode 1 sections)

### Decision

Mode 1 generation uses two targeted Claude calls, not one monolithic prompt. The orchestrator splits execution into two passes with bounded, focused contexts.

| Pass | Claude call | Sections produced | Input data |
|------|-------------|-------------------|------------|
| **Pass 1 — Executive Layer** | 1 | E1–E5 (environment narrative, publisher prefix, complexity summary, Top 5 Risks, Mermaid diagram source) | Aggregated signals: table stats, plugin blast radius tiers (pre-classified by `DeterministicAnalyser`), relationship summary, top-N findings |
| **Pass 2 — Technical Reference** | 2 | T1–T10 (full field catalogue, plugin analysis, flows, JS, security, integration signals, recommendations) | Full detail: all fields, full plugin analysis, all flows/workflows, all JS, all security roles, all app users |

**Rationale:** POC baseline (Phase 1) measured 30–44s for a single-tool call against a developer environment with 400+ tables. Full Mode 1 data — tables, fields, plugins (decompiled C#), flows, JS, security roles — will be significantly larger. A single prompt at that context size risks hitting token limits and degrades Claude's reasoning quality on later sections. Pass 1 works from a lean, pre-aggregated payload; Pass 2 works from full detail but has a focused, well-scoped task.

### Implementation constraints

- Both passes use the structured JSON output schema defined in ADR-006 — same schema, different section subsets
- Both pass outputs are validated by the orchestrator before docx assembly
- If Pass 1 fails, the job is marked failed; Pass 2 is not attempted
- Exact section grouping is confirmed in Story 3.x implementation; the two-pass model is the architectural constraint

### Rejected alternatives

| Option | Reason rejected |
|--------|----------------|
| Single prompt, all 15 sections | Context window risk at full Mode 1 data size; reasoning quality degrades on later sections in large contexts |
| Per-section prompts (15 calls) | 15× API cost; sequential latency compounds to exceed NFR-001 |

---

## ADR-009 — Rate Limiting Hook: ASP.NET Core Built-In Middleware

**Status:** CONFIRMED  
**Date:** 2026-04-16  
**FR Coverage:** NFR-018

### Decision

Rate limiting uses ASP.NET Core's built-in `AddRateLimiter` (ships with .NET 7+, zero additional package dependency). Registered in `Program.cs` as pipeline middleware before controllers.

**P1–P2 (current):** Permissive policy registered — no limits enforced. The hook exists in the pipeline; no structural change required at P3.

**P3+ (future):** Swap permissive policy for per-API-key sliding window or token bucket policy, partitioned on the `X-Api-Key` header. No middleware refactoring required — policy configuration only.

```csharp
// Program.cs — P1/P2: registered but permissive
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("default", _ => RateLimitPartition.GetNoLimiter(HttpContext.Request));
});
// P3: replace GetNoLimiter with per-key sliding window policy
```

### Rejected alternative

**Custom middleware** — rejected in favour of the idiomatic .NET 7+ built-in. No control gap exists that would justify the implementation cost of a hand-rolled solution.

---

## 14. Updated Tool Inventory — PRD v4

The following tools are added to the v3 inventory (Section 5) to cover new PRD v4 FRs. All existing tools are unchanged.

| Tool Name | Returns | FR Coverage | Phase |
|-----------|---------|-------------|-------|
| `get_table_record_stats` | Record count + most recent `createdon`/`modifiedon` per table | FR-001, FR-043, FR-053 | P3 |
| `get_application_users` | All `SystemUser` records where `IsLicensed=false` and `ApplicationId` populated | FR-050 | P2 |
| `get_solutions` | All solutions where `IsManaged=false` and non-Microsoft publisher prefix | FR-054 | P5 |
| `get_table_owner_stats` | Per-table record owner split: app user vs human user percentage | FR-051 | P5 |

**Enhanced existing tools:**

| Tool | Enhancement | FR |
|------|-------------|-----|
| `get_plugin_assemblies` | Add `ModifiedOn` to returned assembly record | FR-052 |

---

## 15. Updated Phase-by-Phase Scope — PRD v4

Extends Section 11. PRD v3 phase work is unchanged; v4 additions are listed per phase.

| Phase | PRD v4 Architecture Work |
|-------|--------------------------|
| **P2 — Mode 1 MVP** | Add `get_application_users` tool. Register `AddRateLimiter` (permissive). Introduce `IDocumentSection` + `DocumentLayer` model in `DocxBuilder`. |
| **P3 — Mode 1 Full** | Introduce `DeterministicAnalyser`. Add `get_table_record_stats` with three-tier batching (ADR-004). Deliver `docs/output-schema-mode1.json` as prerequisite to story writing. Implement two-pass prompt architecture (ADR-008). Implement all 15 document sections across both layers. Enforce structured JSON output with schema validation. |
| **P5 — Mode 3** | Add `get_solutions`, `get_table_owner_stats`. Enhance `get_plugin_assemblies` with `ModifiedOn`. Extend `DeterministicAnalyser` for Mode 3 checks. |
| **P3+ (any)** | Swap `AddRateLimiter` permissive policy for per-API-key throttling policy (no structural change). |

---

## 16. Implementation Patterns & Consistency Rules

Rules for AI agents implementing PRD v4 additions. Existing codebase conventions (PascalCase classes, camelCase JSON properties, feature-folder structure) remain in force. Rules below govern new components and boundaries introduced by v4.

### Confidence Tag Convention

| Context | Form | Rule |
|---------|------|------|
| JSON output schema (Claude response, ADR-006) | `"VERIFIED"` / `"INFERRED"` / `"ESTIMATED"` | Uppercase string, no brackets — enum-safe, clean JSON |
| Rendered in `.docx` output | `[VERIFIED]` / `[INFERRED]` / `[ESTIMATED]` | Bracket form added by `DocxBuilder` at render time only |
| Never | `"[VERIFIED]"` in JSON | Bracket form must not appear in any data model or schema field |

### JSON Output Schema Field Naming

All fields in the structured output schema (ADR-006) and all `GenerationOutput` / `ExecutiveLayerOutput` / `TechnicalReferenceOutput` model properties use **camelCase** — consistent with `System.Text.Json` defaults and existing `StructuredErrorResponse`.

### Tool Return Types

All Dataverse tool implementations return **strongly-typed C# records**, not `JsonElement`, anonymous types, or raw strings. `DeterministicAnalyser` consumes tool results directly — type safety at that boundary is mandatory. New tool record types live in `Agent/Tools/Results/`.

### `EnrichedEnvironmentPayload` Structure

Flat model. Does not wrap `RawEnvironmentData`. All data — raw tool results and computed fields — in one type. Agents access `payload.Tables`, `payload.BlastRadiusTiers`, `payload.MermaidSource` at the same level. No nested wrapper navigation.

### `IDocumentSection.Render` Signature

```csharp
void Render(WordprocessingDocument doc, EnrichedEnvironmentPayload data, IGenerationOutput output);
```

Both parameters are always passed. Sections that render deterministic data (e.g., `MermaidDiagramSection`, `ComplexitySummarySection`) use `data`; sections that render AI output (e.g., `EnvironmentNarrativeSection`, `PluginAnalysisSection`) use `output`. Sections may use both. Neither parameter is ever null.

### Multi-Pass Output Model Types

| Pass | Type | Interface |
|------|------|-----------|
| Pass 1 (Executive Layer) | `ExecutiveLayerOutput` | `IGenerationOutput` |
| Pass 2 (Technical Reference) | `TechnicalReferenceOutput` | `IGenerationOutput` |

Separate types. Each validated independently against the corresponding section of the ADR-006 schema. Never merged into one type with nullable fields.

### `DeterministicAnalyser` Call Sequence

`DeterministicAnalyser.Analyse()` is called **once**, after all tool calls complete, before `PromptBuilder` is called. It is never called:
- Mid-tool-loop
- After a Claude pass
- Incrementally per tool result

`AgentOrchestrator` owns and enforces this sequence: collect all tool results → `DeterministicAnalyser.Analyse()` → `PromptBuilder.BuildPass1()` → Claude Pass 1 → validate → `PromptBuilder.BuildPass2()` → Claude Pass 2 → validate → `DocxBuilder.Build()`.

### Anti-Patterns

| Anti-pattern | Why rejected |
|-------------|-------------|
| Delegating blast radius tier logic to Claude prompt | Violates FR-044 determinism requirement |
| Storing `[VERIFIED]` bracket form in JSON fields | Bracket form is display-only — breaks enum parsing |
| Calling `DeterministicAnalyser` per-tool as results arrive | Blast radius and Mermaid require complete cross-tool data; partial calls produce incorrect output |
| Returning `JsonElement` from tool implementations | Breaks type safety at `DeterministicAnalyser` boundary |
| Merging Pass 1 + Pass 2 into one `GenerationOutput` type | Conflates validation boundaries; makes schema conformance checks ambiguous |

---

## 17. Project Structure & Boundaries — PRD v4 Delta

Extends Section 3. Existing structure unchanged. New files and folders marked below.

```
DataverseDocAgent/
├── DataverseDocAgent.Api/
│   ├── Program.cs                          ← AddRateLimiter registered (ADR-009)
│   │
│   ├── Features/
│   │   ├── SecurityCheck/                  (unchanged)
│   │   ├── DocumentGenerate/
│   │   │   ├── DocumentGenerateController.cs
│   │   │   ├── DocumentGenerateRequest.cs
│   │   │   ├── DocumentGenerateResponse.cs
│   │   │   ├── DocumentGenerateService.cs
│   │   │   └── Sections/                   ← NEW — layer-aware section classes (ADR-007)
│   │   │       ├── IDocumentSection.cs     ← NEW — interface + DocumentLayer enum
│   │   │       ├── Executive/              ← NEW — DocumentLayer.Executive
│   │   │       │   ├── EnvironmentNarrativeSection.cs   ← E1 (FR-041)
│   │   │       │   ├── PublisherPrefixSection.cs        ← E2 (FR-042)
│   │   │       │   ├── ComplexitySummarySection.cs      ← E3 (FR-011)
│   │   │       │   ├── MermaidDiagramSection.cs         ← E4 (FR-049)
│   │   │       │   └── TopRisksSection.cs               ← E5 (FR-047)
│   │   │       └── TechnicalReference/     ← NEW — DocumentLayer.TechnicalReference
│   │   │           ├── CustomTablesSection.cs           ← T1 (FR-001, FR-043)
│   │   │           ├── FieldCatalogueSection.cs         ← T2 (FR-002)
│   │   │           ├── RelationshipMapSection.cs        ← T3 (FR-003)
│   │   │           ├── PluginAnalysisSection.cs         ← T4 (FR-005, FR-044, FR-048)
│   │   │           ├── FlowWorkflowSection.cs           ← T5 (FR-007, FR-008, FR-048)
│   │   │           ├── JavaScriptSection.cs             ← T6 (FR-006)
│   │   │           ├── SecurityOverviewSection.cs       ← T7 (FR-010)
│   │   │           ├── IntegrationSignalsSection.cs     ← T8 (FR-050)
│   │   │           ├── RecommendationsSection.cs        ← T9 (FR-012)
│   │   │           └── MermaidSourceSection.cs          ← T10 (FR-049)
│   │   │
│   │   ├── ImpactAnalyse/                  (unchanged)
│   │   ├── HealthAudit/                    (unchanged)
│   │   └── Download/                       (unchanged)
│   │
│   ├── Agent/
│   │   ├── AgentOrchestrator.cs            (updated: two-pass sequence)
│   │   ├── PromptBuilder.cs                (updated: BuildPass1/BuildPass2)
│   │   ├── Analysis/                       ← NEW — deterministic computation layer (ADR-005)
│   │   │   ├── DeterministicAnalyser.cs    ← NEW — orchestrates all deterministic analysis
│   │   │   ├── BlastRadiusClassifier.cs    ← NEW — FR-044 tier logic
│   │   │   ├── MermaidBuilder.cs           ← NEW — FR-049 diagram construction
│   │   │   └── RecordStatsBatcher.cs       ← NEW — ADR-004 three-tier batch dispatch
│   │   ├── Models/                         ← NEW — typed pipeline models
│   │   │   ├── RawEnvironmentData.cs       ← NEW — aggregated tool results
│   │   │   ├── EnrichedEnvironmentPayload.cs ← NEW — flat enriched model (ADR-005)
│   │   │   ├── IGenerationOutput.cs        ← NEW — shared output interface (ADR-008)
│   │   │   ├── ExecutiveLayerOutput.cs     ← NEW — Pass 1 validated output
│   │   │   └── TechnicalReferenceOutput.cs ← NEW — Pass 2 validated output
│   │   └── Tools/
│   │       ├── IDataverseTool.cs           (unchanged)
│   │       ├── Results/                    ← NEW — strongly-typed tool result records
│   │       │   ├── TableRecord.cs
│   │       │   ├── TableRecordStats.cs     ← NEW — FR-001/043/053
│   │       │   ├── PluginAssemblyRecord.cs ← enhanced: ModifiedOn added (FR-052)
│   │       │   ├── ApplicationUserRecord.cs ← NEW — FR-050
│   │       │   ├── SolutionRecord.cs       ← NEW — FR-054
│   │       │   └── TableOwnerStats.cs      ← NEW — FR-051
│   │       ├── ListCustomTablesTool.cs     (unchanged)
│   │       ├── GetTableFieldsTool.cs       (unchanged)
│   │       ├── GetRelationshipsTool.cs     (unchanged)
│   │       ├── GetPluginAssembliesTool.cs  ← enhanced: ModifiedOn in result (FR-052)
│   │       ├── GetPluginStepsTool.cs       (unchanged)
│   │       ├── GetWebResourcesTool.cs      (unchanged)
│   │       ├── GetFlowDefinitionsTool.cs   (unchanged)
│   │       ├── GetClassicWorkflowsTool.cs  (unchanged)
│   │       ├── GetBusinessRulesTool.cs     (unchanged)
│   │       ├── GetSecurityRolesTool.cs     (unchanged)
│   │       ├── GetFormDefinitionsTool.cs   (unchanged)
│   │       ├── GetSavedQueriesTool.cs      (unchanged)
│   │       ├── GetOrganisationMetadataTool.cs (unchanged)
│   │       ├── GetTableRecordStatsTool.cs  ← NEW (FR-001/043/053)
│   │       ├── GetApplicationUsersTool.cs  ← NEW (FR-050)
│   │       ├── GetSolutionsTool.cs         ← NEW (FR-054)
│   │       └── GetTableOwnerStatsTool.cs   ← NEW (FR-051, Mode 3 only)
│   │
│   ├── Dataverse/                          (unchanged)
│   ├── Documents/
│   │   └── DocxBuilder.cs                  ← updated: layer-aware section model (ADR-007)
│   ├── Storage/                            (unchanged)
│   ├── Jobs/                               (unchanged)
│   ├── Middleware/                         (unchanged)
│   └── Common/                             (unchanged)
│
└── DataverseDocAgent.Console/              (unchanged — POC only)

docs/
├── prd.md
├── architecture.md
├── output-schema-mode1.json               ← NEW — ADR-006 prerequisite deliverable
├── poc-baseline.md
└── validation-report-2026-04-16.md
```

### Architectural Boundaries

| Boundary | Input | Output | Owner |
|----------|-------|--------|-------|
| `DeterministicAnalyser` | `RawEnvironmentData` | `EnrichedEnvironmentPayload` | `Agent/Analysis/` |
| `PromptBuilder` | `EnrichedEnvironmentPayload` | Structured prompt string | `Agent/` |
| Claude Pass 1 | Pass 1 prompt | `ExecutiveLayerOutput` (schema-validated) | `AgentOrchestrator` |
| Claude Pass 2 | Pass 2 prompt | `TechnicalReferenceOutput` (schema-validated) | `AgentOrchestrator` |
| `DocxBuilder` | Both output models + `EnrichedEnvironmentPayload` | `.docx` `byte[]` | `Documents/` |
| `IDocumentSection.Render` | `EnrichedEnvironmentPayload` + `IGenerationOutput` | Writes to `WordprocessingDocument` | `Features/DocumentGenerate/Sections/` |

### Requirements-to-Structure Mapping

| FR group | Location |
|----------|----------|
| FR-044 blast radius classification | `Agent/Analysis/BlastRadiusClassifier.cs` |
| FR-049 Mermaid diagram | `Agent/Analysis/MermaidBuilder.cs` |
| FR-045/NFR-017 confidence layer enforcement | `AgentOrchestrator` schema validation + all section `Render()` calls |
| FR-041–050 Executive Layer sections | `Features/DocumentGenerate/Sections/Executive/` |
| FR-001–013 Technical Reference sections | `Features/DocumentGenerate/Sections/TechnicalReference/` |
| FR-050 app user inventory | `GetApplicationUsersTool.cs` → T8 `IntegrationSignalsSection` |
| FR-052 abandoned plugin | `GetPluginAssembliesTool.cs` (enhanced) → `DeterministicAnalyser` → Mode 3 |
| FR-054 unmanaged solutions | `GetSolutionsTool.cs` → Mode 3 HealthAudit |
| ADR-006 output schema contract | `docs/output-schema-mode1.json` |
| NFR-018 rate limiting hook | `Program.cs` `AddRateLimiter` |

---

## 18. Architecture Validation Results

**Validation date:** 2026-04-16  
**Against:** PRD v4.0, all ADRs (001–009), Implementation Patterns (§16), Project Structure (§17)

### Coherence Validation ✅

| Check | Result |
|-------|--------|
| ADR-001 async job → ADR-005 DeterministicAnalyser → ADR-008 two-pass | ✅ Clean pipeline — orchestrator drives all three in sequence |
| ADR-006 schema validation → ADR-007 DocxBuilder sections | ✅ Schema validated before DocxBuilder receives data |
| ADR-004 record stats batching owned by DeterministicAnalyser | ✅ Tool remains simple; orchestration concern isolated |
| ADR-009 rate limiting at API layer before orchestration | ✅ Independent — no coupling to downstream decisions |
| Confidence tag: `"VERIFIED"` in JSON → `[VERIFIED]` in docx | ✅ Consistent across ADR-006 schema and ADR-007 rendering |
| v3 confirmed decisions (1–3) + v4 new ADRs (4–9) | ✅ No contradictions |

### Requirements Coverage Validation ✅

| FR | Architectural coverage |
|----|----------------------|
| FR-041 | E1 `EnvironmentNarrativeSection` ← Pass 1 output |
| FR-042 | E2 `PublisherPrefixSection` ← `EnrichedEnvironmentPayload` |
| FR-043 | T1 `CustomTablesSection` + `DeterministicAnalyser` |
| FR-044 | `BlastRadiusClassifier` (deterministic, owned by `DeterministicAnalyser`) |
| FR-045 | ADR-006 mandatory `confidence` field on every output object |
| FR-046 | ADR-007 `DocumentLayer` enum + `DocxBuilder` layer-aware rendering |
| FR-047 | E5 `TopRisksSection` ← Pass 1 output |
| FR-048 | T4 `PluginAnalysisSection` + T5 `FlowWorkflowSection` |
| FR-049 | `MermaidBuilder.cs` → E4 (rendered diagram) + T10 (Mermaid source) |
| FR-050 | `GetApplicationUsersTool` → T8 `IntegrationSignalsSection` |
| FR-051 | `GetTableOwnerStatsTool` (Phase 5) |
| FR-052 | `GetPluginAssembliesTool` enhanced (`ModifiedOn`) → `DeterministicAnalyser` |
| FR-053 | `get_table_record_stats` + `DeterministicAnalyser` (zero-record classification) |
| FR-054 | `GetSolutionsTool` → Mode 3 `HealthAudit` (Phase 5) |
| NFR-016 | ADR-006 schema: severity-tiered recommendation fields enforced structurally |
| NFR-017 | ADR-006 schema validation gate in `AgentOrchestrator` |
| NFR-018 | ADR-009 `AddRateLimiter` in `Program.cs` |

### Gap Analysis

**G1 — Important: Output schema has no assigned story (Phase 3 gate)**

`docs/output-schema-mode1.json` is a prerequisite for all Mode 1 output section stories (ADR-006). No story tracks its creation. **Resolution:** First story in Phase 3 epics must be "Define Mode 1 output schema" — gating all `Executive/` and `TechnicalReference/` section implementation stories.

**G2 — Important: Mode 3 output contract undefined (Phase 5 gate)**

ADR-006 covers Mode 1 only. Mode 3 returns structured JSON. Before Phase 5 story writing, a parallel deliverable is required: `docs/output-schema-mode3.json`. Same gate pattern as G1. Not blocking until Phase 5 planning begins.

**G3 — Minor: AgentOrchestrator requires structural refactor for two-pass**

Current `AgentOrchestrator.cs` is single-pass (POC). Phase 3 requires `BuildPass1`/`BuildPass2` sequence. Architecture decision is clear; implementation agent must know the class requires structural change, not just extension.

### Architecture Completeness Checklist

- [x] PRD v4 context and delta documented
- [x] All v3 decisions preserved and confirmed
- [x] 6 new ADRs (004–009) with rationale and rejected alternatives
- [x] Deterministic computation layer defined and bounded
- [x] Structured output schema designated as standalone deliverable
- [x] Two-layer docx section model specified
- [x] Two-pass prompt architecture specified
- [x] Rate limiting hook placed and deferred correctly
- [x] 5 new tools + 1 enhanced tool identified with FR coverage
- [x] Updated tool inventory and phase scope documented
- [x] Implementation patterns covering 7 conflict points
- [x] Complete project structure delta with new/enhanced files marked
- [x] Boundary table mapping inputs/outputs across all components
- [x] All 14 new FRs and 3 new NFRs architecturally covered
- [x] 2 phase gate prerequisites identified (G1, G2)

### Architecture Readiness Assessment

**Overall status: READY FOR IMPLEMENTATION**  
**Confidence: High**

**Key strengths:**
- Deterministic/AI boundary is explicit and enforced by component design — no drift possible
- Output schema as a formal contract prevents story-level ambiguity on confidence tags and section structure
- Three-tier record stat strategy protects NFR-001 performance target
- All v3 decisions unchanged — no refactoring risk from v4 additions outside Phase 3 new components

**Phase gate reminders:**
- Phase 3 story writing blocked until `docs/output-schema-mode1.json` exists
- Phase 5 story writing blocked until `docs/output-schema-mode3.json` exists
- `AgentOrchestrator` requires structural two-pass refactor as Phase 3 first implementation story
