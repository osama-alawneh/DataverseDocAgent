# DataverseDocAgent — Technical Architecture

**Author:** Winston (BMAD Architect)
**Input:** PRD v3 — Validated
**Date:** April 2026
**Status:** CONFIRMED — All decisions accepted by Osama

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
