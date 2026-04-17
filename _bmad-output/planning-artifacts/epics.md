---
stepsCompleted: ["step-01-validate-prerequisites", "step-02-design-epics", "step-03-create-stories", "step-04-final-validation"]
inputDocuments:
  - docs/prd.md
  - _bmad-output/planning-artifacts/architecture.md
  - docs/new requirements.md
  - docs/poc-baseline.md
  - docs/validation-report-2026-04-16.md
revisionBasis: PRD v4.0 — 14 new FRs (FR-041–054), 3 new NFRs (NFR-016–018), ADR-004/005/006
---

# DataverseDocAgent - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for DataverseDocAgent, decomposing requirements from the PRD v4.0 and confirmed Architecture (including ADR-004/005/006) into implementable stories for Phase 1 (POC) through Phase 5 (Mode 3). Epics 1–3 cover PRD v3 scope. Epics 4+ cover PRD v4 additions.

---

## Requirements Inventory

### Functional Requirements

FR-001: The system SHALL discover and document all custom tables (IsCustomEntity=true) including display name, logical name, solution membership, AI-inferred purpose, and key field summary.
FR-002: The system SHALL document all custom fields (IsCustomAttribute=true) per table, including data type, required level, default value, option set values/labels, and a plain-English description.
FR-003: The system SHALL map all custom relationships (IsCustomRelationship=true) including type, both tables, schema name, cascade behaviour, and AI-inferred business meaning.
FR-004: The system SHALL retrieve compiled plugin assemblies and decompile them in-process using dnlib without writing to disk. Failed decompilations are flagged explicitly.
FR-005: The system SHALL generate a plain-English explanation of each plugin's logic including what it does, which fields it reads/writes, event/stage, and risk flags.
FR-006: The system SHALL retrieve all JavaScript web resources and generate a plain-English explanation including form registration, event handlers, function purposes, and deprecated API flags.
FR-007: The system SHALL retrieve and document all solution-aware Power Automate flows including trigger type, trigger entity, action summary, tables read/written, and active/disabled status.
FR-008: The system SHALL parse classic Dynamics 365 workflows from XAML and produce step-by-step plain-English documentation.
FR-009: The system SHALL document all active business rules including name, scope, conditions, and actions in plain English.
FR-010: The system SHALL document all non-system security roles and their privilege configurations, flagging any with org-level Create or Delete on any entity.
FR-011: The system SHALL generate an executive summary including environment name, scan date, deterministic complexity rating, and counts for tables/fields/plugins/flows/JS files.
FR-012: The system SHALL generate an AI-produced recommendations section with categorised, environment-specific risks referencing specific artefacts found.
FR-013: The system SHALL produce a downloadable .docx containing the complete 9-section Mode 1 report, returned via a secure time-limited download token.
FR-014: The system SHALL identify all plugins referencing a specified field (reads or writes), with event, stage, and risk flags.
FR-015: The system SHALL identify all solution-aware flows referencing a specified field as trigger condition or action target.
FR-016: The system SHALL identify all business rules referencing a specified field in conditions or actions.
FR-017: The system SHALL identify all JavaScript OnChange handlers registered on a specified field on any form.
FR-018: The system SHALL return all saved views and dashboards displaying a specified field.
FR-019: The system SHALL calculate and return a deterministic risk rating (Low/Medium/High) for changing a specified field, with a plain-English rationale.
FR-020: The system SHALL scan all plugin code for null reference risks and missing error handling, flagging as Critical findings.
FR-021: The system SHALL flag any plugin registered on Update events with no AttributeFilter as a Warning finding.
FR-022: The system SHALL identify cases where multiple plugins, flows, or business rules write to the same field, flagging as a Warning finding.
FR-023: The system SHALL scan all JavaScript web resources for deprecated Dataverse client APIs (Xrm.Page) and flag as Warning findings.
FR-024: The system SHALL identify custom fields that appear on no form and in no view, flagging as Advisory findings.
FR-025: The system SHALL flag custom security roles with org-level Create or Delete access on major entities as Advisory findings.
FR-026: The system SHALL detect hardcoded GUID literals in decompiled plugin code and flag as Warning findings.
FR-027: The system SHALL detect solution-aware flows that exist but are in a disabled state, flagging as Advisory findings.
FR-028: The system SHALL present all Health Audit findings in a prioritised report card (Critical → Warning → Advisory) with summary counts per severity.
FR-029: The system SHALL provide POST /api/security/check returning status/safeToRun/passed/missing/extra/recommendation within 10 seconds.
FR-030: The system SHALL detect missing required permissions and surface a specific, actionable remediation message per missing permission.
FR-031: The system SHALL detect extra permissions not required by any mode and recommend removal; extra permissions do not block execution.
FR-032: The product SHALL ship a pre-built Dataverse solution file (DataverseDocAgent_SecurityRole.zip) containing exactly the required privileges and nothing more.
FR-033: The product SHALL make a five-step service account setup guide available to customers before they provide credentials.
FR-034: The system SHALL handle credentials exclusively in server memory and SHALL NOT write credentials to any persistent storage, log file, database, cache, or external system.
FR-035: The product SHALL publish a privacy policy stating what data is accessed, transmitted, and never stored — naming Anthropic/Claude API as the only third party receiving environment data.
FR-036: POST /api/document/generate SHALL accept credentials, return a job ID (202 Accepted), and complete generation asynchronously, returning a download token on job completion.
FR-037: POST /api/impact/analyze SHALL accept credentials plus tableName/fieldName and return a complete field impact map synchronously within 60 seconds.
FR-038: POST /api/health/audit SHALL accept credentials and return a complete health audit report card (async job pattern).
FR-039: POST /api/security/check is specified in FR-029 with the full contract defined in PRD Section 5.5.
FR-040: GET /api/download/{token} SHALL return the .docx for a valid unexpired token (200) or 404 for expired/invalid. Token lifetime is exactly 24 hours.

**FR-001 (v4 enhancement):** Record count + recency (date of most recently created/modified record) now required per table. Four-signal table assessment (recency, relationship isolation, logic coverage, form presence) required per table. Publisher prefix surfaced per table.
**FR-003 (v4 enhancement):** Cascade-on-delete behaviour explicitly documented per relationship to feed Mermaid diagram generator (FR-049).
**FR-004 (v4 enhancement):** `PluginAssembly.ModifiedOn` retrieved per assembly to support abandoned plugin detection (FR-052).
**FR-005 (v4 enhancement):** Blast radius classification (FR-044) and confidence tags (FR-045) required on every plugin entry.
**FR-012 (v4 rewrite):** Tiered recommendation format — Critical/High: 5-part (What/Why/Consequence/How/Effort); Medium: 3-part (What/Why/Action); Low/Advisory: single sentence. Every recommendation references a specific named entity.
**FR-013 (v4 enhancement):** Two-layer .docx structure — Executive Layer (E1–E5) + Technical Reference Layer (T1–T10). P2 may produce single-layer; two-layer in effect from P3.

FR-041: The system SHALL generate an opening environment narrative paragraph (3–5 sentences) inferring the primary business process and naming top 3–5 tables by record count with actual counts. Inference tagged [INFERRED]; counts tagged [VERIFIED]. Low-confidence inference acknowledged explicitly.
FR-042: The system SHALL identify all solution publisher prefixes present in the environment, identify the primary client customisation prefix, and surface this in the Executive Layer distinguishing client-built/ISV/Microsoft components.
FR-043: The system SHALL evaluate each custom table against four signals and produce a per-table signal summary: Signal 1 — record count + recency (Active/Inactive/Empty); Signal 2 — relationship isolation ("island" flag); Signal 3 — logic coverage count (load-bearing flag at 3+ components); Signal 4 — form presence (background data store / abandoned flag).
FR-044: The system SHALL classify every plugin registration with a deterministic blast radius risk tier: Critical (Sync + Pre-op + Unfiltered + No error handling); High (Sync + Pre-op + one of: Unfiltered OR No error handling); Medium (Sync + Post-op, or Async with risk factors); Low (filtered, error-handled, or async with no risk factors). Risk tier and one-sentence rationale appear per plugin. Tier tagged [VERIFIED].
FR-045: Every AI-generated finding, explanation, and recommendation SHALL carry exactly one confidence tag inline: [VERIFIED] (from code or metadata), [INFERRED] (from patterns or structure), [ESTIMATED] (from extrapolation or failed analysis). No statement carries more than one tag. Untagged AI-generated statement = quality failure (NFR-017).
FR-046: The Mode 1 .docx SHALL contain a distinct Executive Layer and Technical Reference Layer in the same file, separated by page break with separate ToC entries. Executive Layer: E1 (narrative), E2 (publisher prefix), E3 (complexity), E4 (Mermaid diagram), E5 (Top 5 Risks). Technical Reference Layer: T1–T10 as defined in PRD Section 8.1.
FR-047: The Mode 1 Executive Layer SHALL include a Top 5 Risks section (maximum 5, ranked Critical first then High) using the FR-012 5-part format. Sourced from Mode 1 scan findings only — does not require Mode 3. If zero risks: states "No Critical or High risks identified in this scan."
FR-048: The system SHALL document execution identity for every plugin (Calling User / System / Impersonated: [name]), classic workflow (owner name), and Power Automate flow (connection owner; flag if inactive). Execution identity is mandatory on every entry; unknown identity tagged [ESTIMATED].
FR-049: The system SHALL generate a Mermaid entity-relationship diagram deterministically from relationship metadata. Includes all custom tables plus standard tables with a direct relationship to at least one custom table. Each edge shows type (1:N/N:N), schema name, cascade-on-delete label. Capped at custom-tables-only if >40 nodes. Full Mermaid source in T10; embedded in E4.
FR-050: The system SHALL list all application users (SystemUser where IsLicensed=false and ApplicationId populated) in section T8 with display name, application ID, and assigned security roles, labelled as integration identity indicators.
FR-051: The system SHALL flag tables where >50% of records are owned by application users as integration-managed (Mode 3). Requires `get_table_owner_stats` tool. Advisory (🟢) finding.
FR-052: The system SHALL flag plugin assemblies where ModifiedOn > 3 years prior to scan date. No active steps → Advisory; active steps present → Warning. Confidence: [INFERRED] on abandonment classification, [VERIFIED] on date.
FR-053: The system SHALL detect custom tables with zero records and sub-classify: zero records + no relationships + no form → Warning "likely abandoned mid-build"; zero records + has relationships or form → Advisory "possibly pre-production or integration-managed."
FR-054: The system SHALL detect solutions where IsManaged=false and publisher prefix is not Microsoft-owned. Per solution: 5-part FR-012 recommendation. <10 components → Warning; ≥10 components → Critical/High. Requires `get_solutions` tool.

### NonFunctional Requirements

NFR-001: Mode 1 document generation SHALL complete within 5 minutes (typical: ≤50 tables, ≤20 plugins, ≤30 flows) or 10 minutes (large) from API receipt to download token. [TBD — validate in P1 POC]
NFR-002: POST /api/security/check SHALL return a complete permission report within 10 seconds for any valid Dataverse environment.
NFR-003: POST /api/impact/analyze SHALL return a complete field impact map within 60 seconds.
NFR-004: POST /api/health/audit SHALL complete within 10 minutes for any environment. [TBD — validate in P1 POC]
NFR-005: During MVP phase (P1–P2), the API operates on Azure App Service Free Tier with best-effort availability. Cold-start and spin-down behaviour are accepted.
NFR-006: Once the first paying customer is onboarded, the API SHALL target 99.5% monthly uptime (triggers upgrade to Azure App Service Basic Tier or equivalent).
NFR-007: Customer credentials SHALL be handled exclusively in server memory for the duration of the HTTP request and SHALL NOT persist in any form after request completion.
NFR-008: The system SHALL issue only read-privileged operations against Dataverse across all modes. No write, append, create, delete, or share operations shall be used.
NFR-009: All API client–server communication SHALL use TLS (HTTPS). Plaintext HTTP SHALL be rejected or redirected.
NFR-010: The only third party receiving customer environment data is the Claude API (Anthropic). Data is limited to schema metadata and code artefacts — never record-level CRM data.
NFR-011: The MVP API SHALL support a minimum of 3 concurrent document generation requests without failure or material performance degradation.
NFR-012: DataverseDocAgent SHALL access schema metadata and code artefacts only — never record-level data. This principle SHALL be documented in the privacy policy.
NFR-013: Generated documents SHALL NOT be retained server-side after the 24-hour download token expires. No customer environment data SHALL be retained between requests.
NFR-014: All API endpoints SHALL return structured JSON error responses for all failure conditions. Unhandled exceptions SHALL NOT surface raw stack traces or framework error pages.
NFR-015: All API endpoint implementations and agent tool call definitions SHALL include a code comment referencing the corresponding Feature Registry ID(s) (F-xxx).
NFR-016: All recommendations in Mode 1 and Mode 3 SHALL conform to the severity-tiered format defined in FR-012. Any recommendation lacking a named entity reference or missing required format parts for its severity tier is a quality failure. Validated against ≥3 real Dataverse environments before P3 release.
NFR-017: Every AI-generated finding, explanation, and recommendation in Mode 1 and Mode 3 output SHALL carry exactly one confidence tag ([VERIFIED]/[INFERRED]/[ESTIMATED]). Any untagged AI-generated statement is a quality failure. No statement carries more than one tag.
NFR-018: Rate limiting posture per phase: P1–P2 — no enforcement (known manual cohort, concurrent ceiling NFR-011); P3–P5 — per-API-key throttling defined before P3 release; P6 — per-subscription-tier limits before Web UI launch. Rate limiting policy documented in API reference before first paying customer.

### Additional Requirements

- **Project type:** ASP.NET Core Web API, .NET 8, C#. Single project using feature folder structure.
- **Phase 1 delivery:** Console app (not a Web API) — used to validate the Claude agent loop and Dataverse connectivity. The Web API project is introduced in Phase 2.
- **Async job pattern (Decision 1):** Mode 1 and Mode 3 use an async job + polling model. POST returns 202 with jobId. GET /api/jobs/{jobId} returns status. Mode 2 and security check are synchronous.
- **Custom tool architecture (Decision 2):** All Dataverse operations are implemented as custom `IDataverseTool` implementations scoped per request. No external MCP server process.
- **Storage abstraction (Decision 3):** `IDocumentStore` interface with `InMemoryDocumentStore` (Phase 1 POC) and `BlobDocumentStore` (Phase 2+, Azure Blob Storage).
- **Credential isolation:** `EnvironmentCredentials` is a sealed C# class with `[DebuggerBrowsable(Never)]` on ClientSecret. Serilog destructuring policy must exclude this type from logs.
- **dnlib decompilation:** All plugin DLL decompilation occurs in-process with no disk writes at any stage.
- **Complexity rating (FR-011):** Deterministic — computed in C# from counts, not AI-generated.
- **Risk rating (FR-019):** Deterministic algorithm — derived from dependency counts and types, not AI-generated.
- **Security role artefact:** `DataverseDocAgent_SecurityRole.zip` is a static build artefact (not runtime-generated). Created manually in a developer Dataverse environment and committed to `/artefacts/`.
- **Error response standard:** All errors follow `{ error: string, code: string, safeToRetry: bool }` per NFR-014.
- **FR traceability:** Every implementation file includes a comment referencing its F-xxx Feature Registry ID (NFR-015).
- **NuGet packages confirmed:** Anthropic.SDK, Microsoft.PowerPlatform.Dataverse.Client, dnlib, DocumentFormat.OpenXml, Serilog.AspNetCore, Azure.Storage.Blobs (Phase 2+), Microsoft.Extensions.Caching.Memory, JsonSchema.Net (P3 — output schema validation).
- **No new NuGet packages required for v4 features:** Mermaid diagram is pure string construction. Record stats use existing Dataverse SDK. Two-layer .docx uses existing DocumentFormat.OpenXml. Rate limiting uses ASP.NET Core built-in middleware (ships with .NET 7+).
- **DeterministicAnalyser service (ADR-005, P3):** New dedicated service between tool layer and Claude prompt. Owns: blast radius risk tier (FR-044), Mermaid diagram generation (FR-049), environment complexity rating (FR-011), record stat batch dispatch. Receives `RawEnvironmentData`, returns `EnrichedEnvironmentPayload`. No dependency on AgentOrchestrator or ClaudeClient — pure transformation service.
- **Record stat query strategy (ADR-004, P3):** `get_table_record_stats` uses `Task.WhenAll` parallel batching (default 10 concurrent). Three-tier dispatch: Small (<30 tables) = query all; Medium (30–100) = parallel batching; Large (>100) = query only tables with ≥1 qualifying signal (plugin/relationship/form). Dispatch logic owned by DeterministicAnalyser, not the tool.
- **Confidence layer as structured JSON (ADR-006, P3):** Claude returns all Mode 1 and Mode 3 output as structured JSON with explicit `confidence` field per finding/recommendation. Orchestrator validates against JSON Schema before DocxBuilder. Validation failure returns `{ "code": "OUTPUT_SCHEMA_VIOLATION", "safeToRetry": true }`. `docs/output-schema-mode1.json` is a required artefact and hard prerequisite gate for all P3 stories.
- **New Dataverse tools required (P3):** `get_table_record_stats` (record count + most-recent date per table), `get_application_users` (integration identity), enhanced `get_plugin_assemblies` (add ModifiedOn field).
- **New Dataverse tools required (P5):** `get_solutions` (IsManaged flag — FR-054), `get_table_owner_stats` (record ownership analysis — FR-051).

### UX Design Requirements

N/A — DataverseDocAgent v1 is an API-only product. No UI exists until Phase 6 (explicitly out of scope for v1). All interaction is via Postman or HTTP client.

### FR Coverage Map

| Epic | Stories | FRs Covered | Phase |
|------|---------|-------------|-------|
| Epic 1 — Foundation (POC) | 1.1–1.4 | FR-034, NFR-007, NFR-008, NFR-009 | P1 |
| Epic 2 — Security & Trust | 2.1–2.4 | FR-029, FR-030, FR-031, FR-032, FR-033, FR-035, FR-039 | P2 |
| Epic 3 — Mode 1 Core | 3.1–3.7 | FR-001–003, FR-011, FR-013, FR-036, FR-040, FR-042, FR-050 | P2 |
| Epic 4 — Mode 1 Full: Environment Intelligence | TBD | FR-041, FR-043–049, NFR-016, NFR-017 + enhanced FR-001/003/004/005/012/013 | P3 |
| Epic 5 — Mode 3 Health Audit Additions | TBD | FR-051, FR-052, FR-053, FR-054 | P5 |

### FR Coverage Map — Full

```
FR-001: Epic 3 (baseline) + Epic 4 (enhanced: recency, 4-signal assessment, publisher prefix per table)
FR-002: Epic 3
FR-003: Epic 3 (baseline) + Epic 4 (enhanced: cascade-on-delete for Mermaid)
FR-004: Epic 4 (P3) — enhanced: ModifiedOn field added
FR-005: Epic 4 (P3) — enhanced: blast radius + confidence tags per entry
FR-006: Epic 4 (P3) — JavaScript web resource analysis
FR-007: Epic 4 (P3) — Power Automate flow documentation
FR-008: Epic 4 (P3) — Classic workflow XAML parsing
FR-009: Epic 4 (P3) — Business rules documentation
FR-010: Epic 4 (P3) — Security roles documentation
FR-011: Epic 3 (baseline complexity rating) + Epic 4 (owned by DeterministicAnalyser)
FR-012: Epic 4 (P3) — rewritten tiered 5-part/3-part/1-sentence format
FR-013: Epic 3 (P2 single-layer) + Epic 4 (P3 two-layer restructure)
FR-014–019: Mode 2, P4 — out of scope for current epics
FR-020–028: Mode 3 base (Plugin risk scan, performance flags, duplicate logic detection, deprecated JS, orphaned fields, security role over-privilege, hardcoded GUIDs, disabled flows, report card) — P5, DEFERRED. Must be epiced before Epic 5 work begins. These FRs are a prerequisite for FR-051–054. Add in a dedicated "Mode 3 Base" epic in a future workflow run before P5 sprint planning.
FR-038: POST /api/health/audit endpoint — P5, DEFERRED alongside FR-020–028. Required before Epic 5 stories can run.
FR-029: Epic 2
FR-030: Epic 2
FR-031: Epic 2
FR-032: Epic 2
FR-033: Epic 2
FR-034: Epic 1
FR-035: Epic 2
FR-036: Epic 3
FR-037: Mode 2, P4 — out of scope
FR-038: Mode 3, P5 — out of scope
FR-039: Epic 2
FR-040: Epic 3
FR-041: Epic 4 — opening environment narrative paragraph
FR-042: Epic 3 Story 3.6 — publisher prefix tool + executive output (P2)
FR-043: Epic 4 — table signal scoring (4-signal per-table)
FR-044: Epic 4 — blast radius classification (deterministic risk tier)
FR-045: Epic 4 — confidence layer taxonomy enforcement
FR-046: Epic 4 — two-layer .docx restructure (E1–E5 + T1–T10)
FR-047: Epic 4 — Top 5 Risks section in Executive Layer
FR-048: Epic 4 — execution identity per plugin/workflow/flow
FR-049: Epic 4 — Mermaid relationship diagram (deterministic)
FR-050: Epic 3 Story 3.7 — app user inventory tool + T8 section (P2)
FR-051: Epic 5 — table ownership by app users (Mode 3)
FR-052: Epic 5 — abandoned plugin detection (Mode 3)
FR-053: Epic 5 — zero-record table detection (Mode 3)
FR-054: Epic 5 — unmanaged solution detection (Mode 3)
NFR-016: Epic 4 — recommendation format compliance
NFR-017: Epic 4 — confidence tag completeness (via ADR-006 schema validation)
NFR-018: Rate limiting — P3+ decision, noted in Epic 4 planning
```

---

## Epic List

1. **Epic 1 — Foundation & POC** (Phase 1): Establish the project, validate the Claude agent tool-use loop against a live Dataverse environment, and prove credential in-memory handling before any API surface is built.
2. **Epic 2 — Security & Trust Layer** (Phase 2): Build the permission pre-flight checker, the security role solution artefact, and the credential handling guarantees that gate all subsequent mode delivery.
3. **Epic 3 — Mode 1 Core: Tables, Fields, Relationships, and .docx** (Phase 2): Deliver the first working end-to-end document generation — tables, fields, relationships, and executive summary — as an async job returning a downloadable .docx.
4. **Epic 4 — Mode 1 Full: Environment Intelligence** (Phase 3): Deliver all PRD v4 intelligence features — DeterministicAnalyser service, output schema contract, blast radius classification, table signal scoring with record recency, confidence layer enforced via structured JSON, environment narrative, Mermaid diagram, execution identity, two-layer .docx, Top 5 Risks, enhanced recommendation format. Exit gate: a document a senior D365 consultant trusts enough to hand directly to a client without reviewing every line.
5. **Epic 5 — Mode 3 Health Audit Additions** (Phase 5): Deliver the four PRD v4 Mode 3 detection features — table ownership by app users (FR-051), abandoned plugin detection (FR-052), zero-record table detection (FR-053), unmanaged solution detection (FR-054). Exit gate: consultant confirms at least one finding is a real actionable issue previously unknown.

---

## Epic 1: Foundation & POC

**Goal:** Validate the full technical pipeline — Dataverse connection, Claude tool-use loop, and credential in-memory handling — in a C# console app before any Web API scaffolding is built. This phase produces no shippable product; it produces confidence and baseline performance measurements.

**Exit gate (from PRD):** Pipeline works end-to-end. Credential handling passes code review.

---

### Story 1.1: Project Scaffold and Solution Structure

As a developer,
I want a clean .NET 8 solution with the confirmed feature-folder project structure,
So that all subsequent stories have a consistent, navigable home from day one.

**Acceptance Criteria:**

**Given** a new .NET 8 solution is created
**When** the solution structure is established
**Then** the project follows the feature-folder layout defined in Architecture Section 3
**And** a `DataverseDocAgent.Console` project exists for Phase 1 POC work
**And** a `DataverseDocAgent.Api` project stub exists (no endpoints yet — scaffolded for Phase 2)
**And** all confirmed NuGet packages are added to the appropriate project references
**And** `appsettings.json` and `appsettings.Development.json` are present with placeholder sections for Anthropic API key and Dataverse test credentials (values populated via User Secrets, never committed)
**And** `.gitignore` excludes `appsettings.Development.json`, `secrets.json`, and any `*.user` files
**And** the solution builds cleanly with `dotnet build` and produces zero warnings

---

### Story 1.2: Dataverse Connection and Credential In-Memory Handling

As a developer,
I want a `DataverseConnectionFactory` that authenticates to Dataverse using client credentials held exclusively in memory,
So that I can prove the credential handling contract before any API surface is exposed.

**Acceptance Criteria:**

**Given** `EnvironmentCredentials` is populated from User Secrets (never from committed config)
**When** `DataverseConnectionFactory.ConnectAsync(credentials)` is called
**Then** a `ServiceClient` is returned and a lightweight test query (RetrieveOrganizationRequest or equivalent) succeeds against the test environment
**And** `EnvironmentCredentials` is a sealed C# class with `[DebuggerBrowsable(DebuggerBrowsableState.Never)]` applied to the `ClientSecret` property
**And** no credential value (clientSecret, clientId, tenantId) appears in any console output or log output at any point during the connection lifecycle
**And** if credentials are invalid, the factory throws a typed exception (`DataverseConnectionException`) and does not retry
**And** a code review checklist item is created confirming no credential logging — this is the FR-034 / NFR-007 gate

> **F-034 — FR-034, NFR-007**

---

### Story 1.3: Claude Agent Tool-Use Loop — ListCustomTables POC

As a developer,
I want a working Claude agent loop that calls a single custom `IDataverseTool` (`list_custom_tables`) and returns the result,
So that I can validate the full Anthropic SDK → tool call → Dataverse → Claude response pipeline end-to-end.

**Acceptance Criteria:**

**Given** valid credentials are loaded from User Secrets and the Dataverse connection is live
**When** the console app runs the `AgentOrchestrator` with the `list_custom_tables` tool registered
**Then** Claude receives the tool definition and calls it at least once
**And** `ListCustomTablesTool.ExecuteAsync(credentials)` queries Dataverse for all `IsCustomEntity = true` tables and returns a structured JSON result
**And** Claude produces a final response (stop_reason: end_turn) summarising the tables found
**And** the final Claude response is printed to the console
**And** the `EnvironmentCredentials` object is passed by reference through the tool call and is not serialized or logged at any point in the loop
**And** if Dataverse returns zero custom tables, the tool returns an explicit "no custom tables found" result rather than an empty array with no explanation

> **F-001 — FR-001**

---

### Story 1.4: POC Baseline Measurement and Code Review Gate

As a developer,
I want a documented baseline for connection time and first-response time from the POC run,
So that NFR-001 targets can be confirmed or revised before Phase 2 begins, and credential handling is verified by code review.

**Acceptance Criteria:**

**Given** the POC console app is running against the test Dataverse environment
**When** a full agent loop run is executed (connect → list tables → Claude response)
**Then** elapsed time for Dataverse connection is recorded and logged to console (not credentials — only timing)
**And** elapsed time for the full Claude loop (first tool call to stop_reason: end_turn) is recorded
**And** these measurements are documented in a `docs/poc-baseline.md` file in the repository
**And** a code review of `EnvironmentCredentials`, `DataverseConnectionFactory`, and `AgentOrchestrator` confirms:
  - No credential values appear in any string interpolation, log call, or exception message
  - `ClientSecret` is never passed as a method parameter named `secret`, `password`, or `credential` without the sealed-class wrapper
  - The `EnvironmentCredentials` object is not serialized via `JsonSerializer` or `ToString()` anywhere
**And** the code review outcome (pass/fail and any remediation) is recorded in `docs/poc-baseline.md`
**And** the Phase 1 exit gate is formally marked complete in this document

> **F-034 — FR-034, NFR-007, NFR-001**

---

## Epic 2: Security & Trust Layer

**Goal:** Deliver the complete permission pre-flight checker, the importable security role solution, and all credential/privacy guarantees — the trust layer that gates every subsequent mode delivery and resolves the primary customer buying objection.

**Exit gate (from PRD):** Permission checker live. Security role solution ships. All credential handling confirmed by code review.

---

### Story 2.1: ASP.NET Core Web API Project Setup

As a developer,
I want the `DataverseDocAgent.Api` project fully scaffolded with middleware, error handling, and HTTPS enforcement,
So that all subsequent feature stories have a solid, production-ready API host to build into.

**Acceptance Criteria:**

**Given** the `DataverseDocAgent.Api` project is created in the solution
**When** the application starts
**Then** HTTPS is enforced — HTTP requests are redirected to HTTPS (NFR-009)
**And** `ExceptionHandlingMiddleware` is registered and catches all unhandled exceptions, returning the structured error response format `{ error, code, safeToRetry }` (NFR-014)
**And** no raw stack traces, internal type names, or framework error messages are returned to API consumers on any unhandled exception
**And** Serilog is configured with a structured logging output and a destructuring policy that explicitly excludes `EnvironmentCredentials` properties from log output
**And** `GET /api/health` returns HTTP 200 with `{ status: "healthy" }` — this is the endpoint used for uptime measurement per NFR-006
**And** `dotnet run` starts the application cleanly with no errors or warnings
**And** all placeholder Feature Registry comment annotations (`// F-xxx`) are present on all controller and service files

> **NFR-009, NFR-014, NFR-006, NFR-015**

---

### Story 2.2: Permission Pre-Flight Checker — Core Implementation

As a D365 consultant,
I want to call POST /api/security/check with my service account credentials,
So that I can verify the account has exactly the right permissions before connecting it to any mode.

**Acceptance Criteria:**

**Given** a valid POST /api/security/check request with environmentUrl, tenantId, clientId, clientSecret, and optional targetMode
**When** the endpoint is called with a correctly configured DataverseDocAgent Reader service account
**Then** the response returns HTTP 200 with `status: "ready"`, `safeToRun: true`, a populated `passed[]` array, empty `missing[]` and `extra[]`, and a recommendation string confirming safe to run
**And** the response is returned within 10 seconds (NFR-002)
**And** all 12 required privileges from PRD Section 5.4 are checked and reported individually in `passed[]`

**Given** the service account is missing one or more required permissions
**When** POST /api/security/check is called
**Then** `status` is `"blocked"`, `safeToRun` is `false`, `missing[]` lists each absent privilege by exact entity and operation (e.g., "Read PluginAssembly"), and `recommendation` gives exact remediation steps

**Given** the service account has extra permissions beyond those required
**When** POST /api/security/check is called
**Then** `status` is `"ready"`, `safeToRun` is `true`, `extra[]` lists each surplus privilege by entity and operation, and `recommendation` advises removal with least-privilege rationale

**Given** credentials are invalid (wrong secret, non-existent app registration)
**When** POST /api/security/check is called
**Then** HTTP 200 is returned with `status: "blocked"`, `safeToRun: false`, and an error message in `recommendation` explaining the credential failure — not HTTP 401 from the API itself
**And** no credential values appear in any log entry generated during the request

> **F-029, F-030, F-031 — FR-029, FR-030, FR-031, FR-039, NFR-002, NFR-007**

---

### Story 2.3: Importable Security Role Solution Artefact

As a D365 consultant,
I want to download and import a pre-built Dataverse solution containing the DataverseDocAgent Reader security role,
So that I can establish least-privilege access without manually configuring 12 individual privileges.

**Acceptance Criteria:**

**Given** the `DataverseDocAgent_SecurityRole.zip` artefact is committed to `/artefacts/` in the repository
**When** the solution is imported into any Dynamics 365 / Dataverse cloud environment via make.powerapps.com → Solutions → Import Solution
**Then** the import completes without error
**And** a security role named "DataverseDocAgent Reader" is created in the environment
**And** the role contains exactly the 12 privileges listed in PRD Section 5.4 — no additions, no omissions
**And** re-importing the solution (idempotency test) does not create a duplicate role or corrupt the existing role
**And** running POST /api/security/check against an environment where only this role is assigned returns `status: "ready"` with an empty `extra[]` array

> **F-032 — FR-032**

---

### Story 2.4: Service Account Setup Guide and Privacy Documentation

As a D365 consultant,
I want access to a clear five-step service account setup guide and a privacy policy before I provide any credentials,
So that I can complete setup without contacting support and understand exactly what data leaves my environment.

**Acceptance Criteria:**

**Given** a new customer is onboarding
**When** they visit the product documentation (a Markdown file committed to `/docs/setup-guide.md`)
**Then** the guide covers all five steps from PRD Section 5.3: Entra App Registration, Application User creation, security role solution import, role assignment, and permission checker run
**And** each step includes exact navigation paths (portal names, menu locations) as specified in PRD Section 5.3
**And** the guide is accessible without logging in or contacting support

**Given** a customer reads the privacy policy (`/docs/privacy-policy.md`)
**When** they review the data handling section
**Then** the policy explicitly states that only schema metadata and code artefacts are accessed — never record-level CRM data (NFR-012)
**And** the policy explicitly states credentials are never stored (NFR-007)
**And** the policy names the Claude API (Anthropic) as the only third party receiving environment data and defines the scope of that transmission (NFR-010)
**And** the policy explicitly states generated documents are not retained after the 24-hour download token expires (NFR-013)

> **F-033, F-035 — FR-033, FR-035, NFR-010, NFR-012, NFR-013**

---

## Epic 3: Mode 1 Core — Tables, Fields, Relationships, and .docx

**Goal:** Deliver the first working end-to-end document generation — tables, fields, relationships, and executive summary — as an async job that accepts credentials, scans the environment, and returns a downloadable .docx. This is the Mode 1 MVP: enough to put in front of the first customer.

**Exit gate (from PRD):** First customer receives and validates a generated document.

---

### Story 3.1: Async Job Infrastructure (Job Store + Background Service)

As a developer,
I want a job store and background service that accepts generation tasks and tracks their status,
So that Mode 1 (and later Mode 3) can run for 5–10 minutes without holding an HTTP connection open.

**Acceptance Criteria:**

**Given** a generation request is received by a controller
**When** the controller calls `IJobStore.CreateJob()`
**Then** a unique jobId (UUID) is returned and the job is stored with status `Queued`
**And** the generation task is enqueued to `GenerationBackgroundService`
**And** the controller returns HTTP 202 Accepted with `{ jobId: "..." }`

**Given** a job is in the `Running` or `Ready` state
**When** GET /api/jobs/{jobId} is called
**Then** HTTP 200 is returned with `{ jobId, status, downloadToken? }`
**And** `downloadToken` is only present when `status` is `"ready"`

**Given** a non-existent jobId is supplied
**When** GET /api/jobs/{jobId} is called
**Then** HTTP 200 is returned with the structured error format: `{ error: "Job not found", code: "JOB_NOT_FOUND", safeToRetry: false }`

**Given** a generation job fails (credential rejection after connection test, Dataverse error, Claude API error)
**When** the failure occurs in the background service
**Then** the job status is set to `Failed` with a human-readable errorMessage
**And** GET /api/jobs/{jobId} returns `{ status: "failed", error: "..." }`
**And** credentials are discarded regardless of failure mode — no partial credential state is retained

> **Decision 1 (Async Job), NFR-014**

---

### Story 3.2: IDocumentStore — In-Memory Implementation (Phase 1)

As a developer,
I want an `InMemoryDocumentStore` that stores generated .docx bytes with a 24-hour TTL,
So that the download endpoint can retrieve documents by token without any external storage dependency in Phase 1.

**Acceptance Criteria:**

**Given** a `.docx` byte array is stored via `IDocumentStore.StoreAsync(bytes, ttl: 24h)`
**When** the method is called
**Then** a unique token (UUID) is returned
**And** the bytes are held in `IMemoryCache` with a 24-hour absolute expiry

**Given** a valid, unexpired token is supplied to `IDocumentStore.RetrieveAsync(token)`
**When** the method is called within 24 hours of storage
**Then** the original byte array is returned

**Given** an expired or invalid token is supplied
**When** `IDocumentStore.RetrieveAsync(token)` is called
**Then** `null` is returned (not an exception)

**Given** the `IDocumentStore` interface is defined
**When** DI is configured
**Then** `InMemoryDocumentStore` is registered as the `IDocumentStore` implementation for Phase 1
**And** switching to `BlobDocumentStore` (Phase 2) requires only a single DI registration change in `Program.cs`

> **Decision 3 (Option A for Phase 1), FR-040, NFR-013**

---

### Story 3.3: GET /api/download/{token} Endpoint

As a D365 consultant,
I want to call GET /api/download/{token} with my download token,
So that I can retrieve the generated .docx without needing to re-authenticate.

**Acceptance Criteria:**

**Given** a valid, unexpired download token
**When** GET /api/download/{token} is called
**Then** HTTP 200 is returned with the `.docx` file as the response body
**And** Content-Type is `application/vnd.openxmlformats-officedocument.wordprocessingml.document`
**And** Content-Disposition is `attachment; filename="DataverseDocAgent-Report.docx"`

**Given** an expired or invalid token
**When** GET /api/download/{token} is called
**Then** HTTP 404 is returned with the structured error: `{ error: "Download token not found or expired", code: "TOKEN_EXPIRED", safeToRetry: false }`

**Given** a download is completed
**When** the 24-hour TTL passes
**Then** the document is no longer retrievable (IMemoryCache expiry handles this automatically)
**And** no credential values or environment data are retained after the token expires (NFR-013)

> **F-040 — FR-040, NFR-013**

---

### Story 3.4: Dataverse Tools — Tables, Fields, Relationships

As a developer,
I want `ListCustomTablesTool`, `GetTableFieldsTool`, and `GetRelationshipsTool` implemented as `IDataverseTool` instances,
So that Claude can call them during Mode 1 generation to gather all required environment metadata.

**Acceptance Criteria:**

**Given** `ListCustomTablesTool` is registered with the agent
**When** Claude calls `list_custom_tables`
**Then** the tool queries Dataverse for all entities where `IsCustomEntity = true`
**And** returns: display name, logical name, schema name, solution membership, and a description field for each table
**And** if zero custom tables exist, returns `{ tables: [], message: "No custom tables found in this environment" }`

**Given** `GetTableFieldsTool` is registered with the agent
**When** Claude calls `get_table_fields` with a `tableName` parameter
**Then** the tool queries all attributes where `IsCustomAttribute = true` for that table
**And** returns: display name, logical name, data type, required level, default value (if set), and for OptionSet fields, all option values with integer codes and display labels

**Given** `GetRelationshipsTool` is registered with the agent
**When** Claude calls `get_relationships` with a `tableName` parameter
**Then** the tool returns all 1:N and N:N relationships where `IsCustomRelationship = true` for that table
**And** returns: relationship type, schema name, both participating table logical names, and cascade behaviour for delete/assign/share/unshare

**For all three tools:**
**Given** credentials are invalid at tool execution time
**When** the tool is called
**Then** a typed exception is thrown that the `AgentOrchestrator` catches and converts to a job failure — credentials are not logged

> **F-001, F-002, F-003 — FR-001, FR-002, FR-003, NFR-007, NFR-008**

---

### Story 3.5: Mode 1 — POST /api/document/generate Endpoint and Full Request-to-Download Flow

As a D365 consultant,
I want to call POST /api/document/generate with my credentials and receive a .docx via the async job flow,
So that the complete request-to-download journey works end-to-end: submit → poll → download.

**Acceptance Criteria:**

**Given** a valid POST /api/document/generate request body containing environmentUrl, tenantId, clientId, clientSecret
**When** the endpoint is called
**Then** HTTP 202 Accepted is returned with `{ jobId: "..." }` within 2 seconds of receipt
**And** the request body is validated for required field presence and format before any external call is made
**And** credentials are never logged at any point in the request lifecycle

**Given** the 202 is returned and the background job is running
**When** the `AgentOrchestrator` executes the generation pipeline
**Then** a lightweight Dataverse connection test is performed first; if it fails, the job is marked `Failed` with `code: "CREDENTIAL_REJECTED"` and credentials are discarded
**And** on success, Claude is invoked with the `list_custom_tables`, `get_table_fields`, and `get_relationships` tools registered
**And** Claude calls each tool as needed and produces a structured JSON output with sections for: executive_summary, tables, fields, relationships
**And** the executive summary includes: environment name, scan date, deterministic complexity rating (computed in C# from counts — not AI-generated), and counts for custom tables, custom fields, and relationships
**And** `DocxBuilder.Build(output)` produces a valid `.docx` file containing sections 1–4 as defined in PRD Section 8.1
**And** the document is stored via `IDocumentStore.StoreAsync()` with a 24-hour TTL, returning a download token
**And** the job is marked `Ready` with the download token

**Given** the job is complete
**When** GET /api/jobs/{jobId} is called by the client
**Then** `{ status: "ready", downloadToken: "abc123" }` is returned
**And** GET /api/download/{token} returns HTTP 200 with the `.docx` file and correct Content-Type and Content-Disposition headers

**Given** any structured failure occurs (credential rejection, Dataverse connectivity error, Claude API error, generation timeout)
**When** the failure is caught in the background service
**Then** the job is marked `Failed` with a human-readable errorMessage and a machine-readable code
**And** GET /api/jobs/{jobId} returns `{ status: "failed", error: "...", code: "..." }`
**And** credentials are discarded regardless of failure mode — no partial credential state is retained

**Given** the full flow is exercised against the test Dataverse environment
**When** the generated document is downloaded and opened
**Then** the content accurately represents the custom tables, fields, and relationships in the environment (baseline accuracy check)
**And** elapsed time from POST /api/document/generate receipt to job status "ready" is recorded and compared against NFR-001 targets (under 5 minutes for typical environments)

> **F-001, F-002, F-003, F-011, F-013, F-036, F-040 — FR-001, FR-002, FR-003, FR-011, FR-013, FR-036, FR-040, NFR-001, NFR-014**

---

### Story 3.6: Publisher Prefix Intelligence

As a D365 consultant,
I want the Mode 1 document to identify all solution publisher prefixes and tell me which is the client's customisation prefix,
So that I can immediately distinguish client-built tables from Microsoft tables on day one without reading through every table name.

**Acceptance Criteria:**

**Given** the `AgentOrchestrator` runs the Mode 1 pipeline
**When** custom table metadata is retrieved via `list_custom_tables`
**Then** a `GetPublisherPrefixesTool` (or equivalent logic) extracts all unique publisher prefixes from the custom component set
**And** the primary client customisation prefix is identified as the prefix with the highest count of custom components
**And** prefixes matching known Microsoft patterns (`msdyn_`, `msft_`, `adx_`, `cr`) are labelled as Microsoft
**And** all other prefixes are labelled as client-built or potential ISV origin

**Given** the publisher prefix summary is computed
**When** `DocxBuilder` assembles the Mode 1 document
**Then** a publisher prefix summary section is included in the document stating: the primary client prefix, all Microsoft prefixes found, and any additional prefixes with component counts
**And** the summary reads e.g.: "All client customisations use the prefix `vel_`. Microsoft components use `msdyn_`. No third-party ISV components detected."
**And** if multiple custom prefixes exist, each is listed with its component count — multiple prefixes flag multiple dev teams or migration history

**Given** zero non-Microsoft custom prefixes exist
**When** the prefix summary is generated
**Then** the section states this explicitly: "No client-defined publisher prefix detected — all custom components use default or Microsoft prefixes"

> **F-047 — FR-042**

---

### Story 3.7: Application User Inventory

As a D365 consultant,
I want the Mode 1 document to list all application users registered in the environment,
So that I can identify which external integrations are writing to this environment without needing audit log access.

**Acceptance Criteria:**

**Given** the Mode 1 pipeline runs
**When** a `GetApplicationUsersTool` is called
**Then** it queries all `SystemUser` records where `IsLicensed = false` and `ApplicationId` is populated
**And** returns per app user: display name, application (client) ID, and a list of security roles assigned to that user

**Given** the security role assignment query fails for an individual application user
**When** `GetApplicationUsersTool` processes that user
**Then** the user is still included in the result with display name and application ID populated
**And** the roles field for that user shows `"role lookup unavailable"` — the failure does not propagate as a job failure or exception

**Given** application user data is retrieved
**When** `DocxBuilder` assembles the Mode 1 document
**Then** a section "Application Users (Integration Signals)" is included in the Technical Reference section of the document
**And** the section carries the label: "Application users are typically used by external integrations. The following application users are registered and may be writing to tables in this environment"
**And** each app user is listed with display name, application ID, and assigned roles (or "role lookup unavailable" where applicable)

**Given** zero application users exist in the environment
**When** the section is generated
**Then** the section states: "No application users registered in this environment" — it is never omitted from the document

**Given** credentials are invalid at tool execution time
**When** `GetApplicationUsersTool` is called
**Then** a typed exception is thrown and the job fails with `code: "CREDENTIAL_REJECTED"` — credential values are not logged

> **F-055 — FR-050, NFR-007, NFR-008**

---

## Epic 4: Mode 1 Full — Environment Intelligence

**Goal:** Deliver all PRD v4 intelligence features. A Mode 1 document a senior D365 consultant trusts enough to hand directly to a client without reviewing every line.

**Exit gate:** Customer confirms output accurately represents ≥90% of environment components reviewed. Document reads like something a senior consultant wrote — specific, referenced, honest about gaps.

---

### Story 4.1: Mode 1 Output Schema Contract

As a developer,
I want a formally defined and versioned JSON Schema for all Mode 1 Claude output, with schema validation enforced in the orchestrator,
So that every downstream story can rely on a stable, machine-validated contract and malformed AI output never reaches `DocxBuilder`.

**Acceptance Criteria:**

**Given** Epic 4 work begins
**When** this story is complete
**Then** `docs/output-schema-mode1.json` exists in the repository as a versioned JSON Schema document
**And** the schema defines all Mode 1 output sections: `executive_summary`, `publisher_prefix_summary`, `tables[]`, `fields[]`, `relationships[]`, `plugins[]`, `flows[]`, `workflows[]`, `javascript[]`, `business_rules[]`, `security_roles[]`, `app_users[]`, `recommendations[]`, `top_risks[]`
**And** every finding, explanation, and recommendation object in the schema has a mandatory `confidence` field with enum values `"VERIFIED"` | `"INFERRED"` | `"ESTIMATED"`

**Given** the `AgentOrchestrator` receives Claude's structured JSON response
**When** the response is parsed
**Then** `JsonSchema.Net` validates the response against `output-schema-mode1.json` before the response is passed to `DocxBuilder`
**And** if validation fails, the job is marked `Failed` with `code: "OUTPUT_SCHEMA_VIOLATION"`, `safeToRetry: true`, and a log entry naming the schema path that failed — no credential data in the log
**And** `DocxBuilder` is never called with an unvalidated response

**Given** the schema is updated in a future story
**When** the schema file is modified
**Then** the schema carries a `$schema` version field and the orchestrator references the schema file by path — no hardcoded schema strings in C# code

> **ADR-006 — NFR-017**

---

### Story 4.2: DeterministicAnalyser Service + Blast Radius Tier Logic

As a developer,
I want a `DeterministicAnalyser` service that pre-classifies plugin blast radius and computes complexity rating before data reaches Claude,
So that these deterministic values are never AI-generated and cannot be overridden or varied by the AI layer.

**Acceptance Criteria:**

**Given** `AgentOrchestrator` has collected raw tool results
**When** `DeterministicAnalyser.Analyse(RawEnvironmentData)` is called
**Then** it returns an `EnrichedEnvironmentPayload` — a new type containing all raw data plus deterministic computation results
**And** `DeterministicAnalyser` has no dependency on `AgentOrchestrator`, `ClaudeClient`, or any AI-layer type — it is a pure transformation service registered in DI

**Given** `SdkMessageProcessingStep` data is present in raw results
**When** blast radius classification runs
**Then** every step is classified with a risk tier using the deterministic matrix:
  - `Critical` — Synchronous + Pre-operation + Unfiltered + No error handling (detected from decompiled source)
  - `High` — Synchronous + Pre-operation + any one of: Unfiltered OR No error handling
  - `Medium` — Synchronous + Post-operation, or Asynchronous with Unfiltered OR No error handling
  - `Low` — filtered, error-handled, or asynchronous with no risk factors
**And** each classification includes a one-sentence rationale string (template-generated from the tier inputs — not AI-generated)
**And** the risk tier and rationale are fields in `EnrichedEnvironmentPayload.Plugins[]`, not computed at prompt time

**Given** table/plugin/flow/JS counts are available
**When** complexity rating is computed
**Then** the deterministic scoring model from PRD is applied and the result (`Low` / `Medium` / `High`) is a field in `EnrichedEnvironmentPayload` — identical to the P2 complexity rating but now owned and computed by this service

**Given** a plugin's decompiled source is unavailable (decompilation failed per FR-004)
**When** blast radius classification runs for that plugin
**Then** error handling presence defaults to `Unknown` and the tier is computed conservatively (Sync + Pre-op + Unfiltered + Unknown error handling → `High`, not `Critical`)
**And** the rationale string notes: "Error handling status could not be determined — decompilation failed"

> **ADR-005, F-049, F-011 — FR-044, FR-011**

---

### Story 4.3: Record Stats Tool + Three-Tier Batch Strategy

As a developer,
I want a `GetTableRecordStatsTool` that retrieves record count and most-recent-record date per table, with a three-tier batching strategy owned by `DeterministicAnalyser`,
So that record recency data is available for table signal scoring without exceeding NFR-001 time bounds on large environments.

**Acceptance Criteria:**

**Given** `GetTableRecordStatsTool` is registered
**When** called with a `tableName` parameter
**Then** it queries Dataverse for the total record count and the `createdon` or `modifiedon` date of the most recently created/modified record for that table
**And** returns: `{ tableName, recordCount, mostRecentDate }` — `mostRecentDate` is `null` if the table is empty
**And** credentials are not logged at any point in the query

**Given** `DeterministicAnalyser` has the full custom table list
**When** `BatchRecordStats(tables)` is called
**Then** the three-tier dispatch logic applies:
  - **Small** (<30 tables): all tables queried, no filtering
  - **Medium** (30–100): parallel batching via `Task.WhenAll`, default batch size 10 concurrent queries
  - **Large** (>100): only tables with ≥1 qualifying signal (plugin registration, custom relationship, or form presence) are queried — low-signal tables above threshold skip the record count query
**And** batch size is configurable via `appsettings.json` under `AnalysisSettings:RecordStatBatchSize` (default: 10) — this section also houses `AbandonedPluginAgeYears` (default: 3) and `IntegrationOwnershipThresholdPercent` (default: 50) defined in later stories
**And** `GetTableRecordStatsTool` itself remains a simple per-table query — all batching and tier selection logic is in `DeterministicAnalyser`, not the tool

**Given** a record stat query fails for a specific table
**When** the batch processes that table
**Then** the table is included in the result with `recordCount: null` and `mostRecentDate: null` — it does not fail the overall batch or the job
**And** a non-credential log entry records the table name and error type

> **ADR-004, F-001 — FR-001 (enhanced), NFR-007**

---

### Story 4.4: Plugin DLL Retrieval, Decompilation, and Blast Radius Pipeline

As a developer,
I want enhanced plugin assembly retrieval that captures `ModifiedOn` and full `SdkMessageProcessingStep` registration details, with decompiled source passed to Claude for plain-English explanation after DeterministicAnalyser pre-classifies blast radius,
So that every plugin entry in the Mode 1 document contains all required fields from FR-004 and FR-005.

**Acceptance Criteria:**

**Given** `GetPluginAssembliesTool` is called
**When** it retrieves plugin assemblies
**Then** for each `PluginAssembly` it returns: assembly name, version, all associated `SdkMessageProcessingStep` records (message name, entity name, execution mode, pipeline stage, attribute filter, rank), and `PluginAssembly.ModifiedOn`
**And** the DLL is decompiled in-memory using dnlib — no file written to disk at any stage
**And** decompiled C# source is included in the tool result as a string field

**Given** decompilation succeeds
**When** the enriched payload is assembled
**Then** `DeterministicAnalyser` classifies blast radius tier and rationale per step (from Story 4.2) using the step metadata
**And** Claude receives the `EnrichedEnvironmentPayload.Plugins[]` entries (pre-classified) and the decompiled source
**And** Claude generates: plain-English description of what the plugin does, fields read, fields written — all carrying `[VERIFIED]` confidence tags
**And** Claude does NOT re-derive or override the blast radius tier — it receives it as a pre-populated field

**Given** a DLL cannot be decompiled (obfuscated, corrupted)
**When** the tool result is assembled
**Then** the plugin entry is included with: assembly name, step registrations, `ModifiedOn`, and an explicit flag: `"decompilationFailed": true`
**And** the confidence tag on all AI-generated analysis for that plugin is `[ESTIMATED]`
**And** the document entry notes: "Decompilation failed — manual review required"

**Given** all plugins have been processed
**When** any plugin step has execution mode Synchronous and the decompiled source has no `try` / `catch` block
**Then** `error_handling_present` is set to `false` in `EnrichedEnvironmentPayload` — this field feeds the blast radius tier in Story 4.2

> **F-004, F-005 — FR-004, FR-005, NFR-007, NFR-008**

---

### Story 4.5: JavaScript, Flow, and Workflow Analysis Tools

As a developer,
I want `GetJavaScriptWebResourcesTool`, `GetPowerAutomateFlowsTool`, and `GetClassicWorkflowsTool` implemented and integrated into the Mode 1 pipeline,
So that JavaScript, flow, and workflow sections of the Mode 1 document are populated with Claude-generated plain-English analysis.

**Acceptance Criteria:**

**Given** `GetJavaScriptWebResourcesTool` is called
**When** it queries Dataverse
**Then** it retrieves all `WebResource` records of type JavaScript (type = 3) as plain-text source
**And** for each file it returns: resource name, the plain-text JavaScript source, and which `SystemForm` records reference this web resource
**And** if a file contains no form event registrations, this is noted in the result explicitly

**Given** `GetPowerAutomateFlowsTool` is called
**When** it retrieves flows
**Then** it returns all solution-aware Power Automate flows with: flow name, trigger type, trigger entity and event, active/disabled status, and the raw flow definition JSON for Claude to analyse
**And** flows not in a solution are noted in the result as present but excluded from deep analysis, with a count

**Given** `GetClassicWorkflowsTool` is called
**When** it retrieves workflows
**Then** it returns all `Workflow` records with `Category = 0` with the XAML definition
**And** if XAML cannot be parsed, the entry is included with a `xamlParseFailed: true` flag

**Given** all three tools return results
**When** Claude receives the enriched payload
**Then** Claude generates for each JavaScript file: form registrations, function-level event handlers (OnLoad/OnSave/OnChange), and plain-English explanation of each registered function's purpose — deprecated `Xrm.Page` usage flagged per file
**And** Claude generates for each flow: trigger summary, action summary in plain English, tables read/written
**And** Claude generates for each classic workflow: scope, trigger conditions, step-by-step plain-English action list
**And** all AI-generated statements carry a confidence tag per FR-045

**Given** a flow connection owner is a disabled or inactive system user
**When** Claude analyses execution identity for that flow
**Then** the flow is flagged: "At risk of silent failure — connection owner [name] is no longer active" — confidence tag `[VERIFIED]` for the owner lookup, `[INFERRED]` for the risk inference

> **F-006, F-007, F-008 — FR-006, FR-007, FR-008, FR-048 (partial), NFR-008**

---

### Story 4.6: Business Rules and Security Roles Documentation Tools

As a developer,
I want `GetBusinessRulesTool` and `GetSecurityRolesTool` implemented and integrated into the Mode 1 pipeline,
So that business rules and security role sections of the Mode 1 document are populated with Claude-generated plain-English documentation.

**Acceptance Criteria:**

**Given** `GetBusinessRulesTool` is called
**When** it queries Dataverse
**Then** it retrieves all `Workflow` records with `Category = 2` (business rule)
**And** returns per rule: name, scope (entity / all forms / specific form), and the rule definition for Claude to analyse

**Given** `GetSecurityRolesTool` is called
**When** it queries Dataverse
**Then** it retrieves all security roles not marked as system roles
**And** returns per role: role name and the full privilege configuration (entity/privilege matrix)

**Given** both tools return results
**When** Claude generates documentation
**Then** for each business rule: scope, conditions in plain English, and actions in plain English — confidence tag `[VERIFIED]` for metadata fields, `[INFERRED]` for any logic interpretation
**And** for each security role: key privilege levels (Create/Read/Write/Delete) for major entities and notable differences from a standard user's access
**And** roles with organisation-level Create or Delete on any entity are flagged as potentially over-privileged — confidence tag `[VERIFIED]`

**Given** either tool fails to retrieve data (Dataverse error or missing permission)
**When** the pipeline runs
**Then** a typed exception is thrown with the specific entity that failed, and the orchestrator marks the job `Failed` with a human-readable error — credential values not logged

> **F-009, F-010 — FR-009, FR-010, NFR-008**

---

### Story 4.7: Table Signal Scoring + Execution Identity

As a developer,
I want `DeterministicAnalyser` to compute the four-signal table assessment for every custom table, and execution identity to be extracted per plugin/workflow/flow,
So that each table entry in the Mode 1 document has a clear signal summary and every logic item shows what identity it runs under.

**Acceptance Criteria:**

**Given** `DeterministicAnalyser.Analyse()` runs after all tools complete
**When** table signal scoring is computed
**Then** every custom table receives a `TableSignalSummary` with four fields:
  1. `recencySignal`: `Active` (records within 90 days), `Inactive` (no records within 1 year), `Empty` (zero records) — confidence `[VERIFIED]`
  2. `isolationSignal`: `Island` (zero inbound and outbound custom relationships) or `Connected` — confidence `[VERIFIED]`
  3. `logicCoverageSignal`: count of plugins + flows watching this table; `LoadBearing` flag set if count ≥ 3 — confidence `[VERIFIED]`
  4. `formPresenceSignal`: `HasForm`, `NoForm` — confidence `[VERIFIED]`
**And** the `TableSignalSummary` is a field on each table entry in `EnrichedEnvironmentPayload.Tables[]`
**And** any AI-generated interpretation (e.g., "abandoned mid-build") carries `[INFERRED]`

**Given** plugin step data is in the enriched payload
**When** execution identity is extracted
**Then** every `SdkMessageProcessingStep` entry has an `executionIdentity` field set to one of: `"Calling User"`, `"System"`, or `"Impersonated: [user name]"` — derived from the step's `ImpersonatingUserId` field
**And** every classic workflow entry has an `ownerName` field from the workflow `OwnerId` lookup
**And** every flow entry has a `connectionOwnerName` field; if that user is inactive, `connectionOwnerInactive: true` is set
**And** if any identity lookup fails, the field is set to `"[ESTIMATED] — identity lookup unavailable"`

> **F-048, F-049 (partial) — FR-043, FR-048**

---

### Story 4.8: Mermaid Diagram Generator

As a developer,
I want `DeterministicAnalyser` to generate a Mermaid ER diagram string deterministically from relationship metadata,
So that the Mode 1 document includes an accurate, always-current relationship diagram that requires no AI involvement.

**Acceptance Criteria:**

**Given** relationship metadata is available in `RawEnvironmentData.Relationships`
**When** `DeterministicAnalyser.GenerateMermaidDiagram(relationships, tables)` runs
**Then** it produces a valid Mermaid `erDiagram` syntax string
**And** the diagram includes: all custom tables, plus standard Dataverse/D365 tables that have a direct relationship to at least one custom table
**And** each relationship edge shows: relationship type (1:N shown as `||--o{`, N:N shown as `}|--|{`), schema name as edge label, and cascade-on-delete label (e.g., `"Cascade"` or `"Restrict"`)
**And** self-referential relationships are included with a loop edge

**Given** the total node count would exceed 40
**When** the diagram is generated
**Then** only custom tables are included (standard tables excluded), with a note string: `"Diagram limited to custom tables only — environment exceeds 40-node threshold for full relationship display"`

**Given** the Mermaid diagram string is generated
**When** it is stored in `EnrichedEnvironmentPayload`
**Then** two fields are set: `mermaidDiagramEmbedded` (the diagram string for DocxBuilder to embed in the Executive Layer) and `mermaidDiagramSource` (same string, for verbatim inclusion in the T10 Technical Reference section)
**And** the string is never passed through Claude — it is inserted verbatim by DocxBuilder

**Given** zero custom relationships exist
**When** the diagram is generated
**Then** the result is an empty diagram with a note: "No custom relationships found — Mermaid diagram not generated"

> **F-054 — FR-049**

---

### Story 4.9: Confidence Layer — Structured JSON Output Enforcement

As a developer,
I want the Mode 1 Claude prompt redesigned to require structured JSON output with a mandatory `confidence` field on every AI-generated object, validated against the output schema from Story 4.1,
So that NFR-017 (every AI-generated statement carries exactly one confidence tag) is structurally enforced — not advisory.

**Acceptance Criteria:**

**Given** `AgentOrchestrator` invokes Claude for Mode 1 generation
**When** the prompt is constructed by `PromptBuilder`
**Then** the system prompt instructs Claude to return a single JSON object conforming to `output-schema-mode1.json`
**And** the prompt explicitly defines the three confidence values and when each applies:
  - `"VERIFIED"` — derived directly from Dataverse metadata or decompiled code analysis
  - `"INFERRED"` — reasoned from naming patterns, structure, record volumes, relationships
  - `"ESTIMATED"` — extrapolation with limited data, or source could not be analysed
**And** the prompt instructs Claude that every `text`, `description`, `explanation`, and `recommendation` field in the JSON output must have a sibling `confidence` field — no exceptions

**Given** Claude returns a response
**When** the orchestrator validates it against `output-schema-mode1.json` (from Story 4.1)
**Then** any response missing a `confidence` field on any required object fails schema validation
**And** the failure is handled per the validation gate defined in Story 4.1 (`code: "OUTPUT_SCHEMA_VIOLATION"`, `safeToRetry: true`)

**Given** a finding's source data is a mix of confidence levels
**When** Claude assigns the confidence tag
**Then** the recommendation or finding carries the tag of its lowest-confidence supporting statement (e.g., a recommendation based partly on `[INFERRED]` data is tagged `[INFERRED]`)

**Given** an entire section could not be analysed (all plugins failed decompilation)
**When** Claude generates that section
**Then** the section's findings carry `[ESTIMATED]` and Claude's output includes an explicit rationale string for why the section is estimated

> **F-050 — FR-045, NFR-017**

---

### Story 4.10: Two-Layer DocxBuilder Restructure

As a developer,
I want `DocxBuilder` refactored to produce a two-layer `.docx` — Executive Layer (E1–E5) followed by Technical Reference Layer (T1–T10) — in a single file,
So that the Mode 1 document serves both senior stakeholders (Executive Layer, readable in <10 minutes) and developers (Technical Reference as working reference).

**Acceptance Criteria:**

**Given** `DocxBuilder.Build(enrichedPayload, claudeOutput)` is called
**When** the document is assembled
**Then** the output `.docx` contains two clearly labelled layers in sequence:
  - **Executive Layer** with sections: E1 (environment narrative), E2 (publisher prefix summary), E3 (complexity summary + headline counts), E4 (Mermaid diagram embedded), E5 (Top 5 Risks)
  - **Technical Reference Layer** with sections: T1 (custom tables with signal summaries), T2 (field catalogue per table), T3 (relationship map), T4 (plugin analysis with blast radius), T5 (flow + workflow docs with execution identity), T6 (JavaScript analysis), T7 (security overview), T8 (integration signals — app users), T9 (recommendations in tiered format), T10 (Mermaid source code block)
**And** a page break separates the Executive Layer from the Technical Reference Layer
**And** the document's table of contents has separate entries pointing to both layers

**Given** the Executive Layer is assembled
**When** E4 (Mermaid diagram) is inserted
**Then** the Mermaid diagram string from `EnrichedEnvironmentPayload.mermaidDiagramEmbedded` is inserted as a formatted code block with a preceding label: "Custom Data Model — Relationship Diagram"
**And** the T10 section inserts the same string as a plain code block with the label: "Mermaid Source — Copy/Paste into any Mermaid renderer"

**Given** a field-level schema dump (T2) is requested
**When** DocxBuilder assembles the document
**Then** field-level schema content appears ONLY in T2 (Technical Reference Layer) — never in the Executive Layer sections

**Given** the P2 Mode 1 document produced a single-layer output
**When** this story is complete
**Then** the existing test Dataverse environment generates a two-layer document via the unchanged `POST /api/document/generate` endpoint, without requiring a new request body field — the two-layer structure is the default for all Mode 1 generation from this story onward

> **F-051 — FR-046, FR-013 (enhanced)**

---

### Story 4.11: Mode 1 Full Pipeline — Narrative, Recommendations, and End-to-End Validation

As a D365 consultant,
I want to call `POST /api/document/generate` and receive a Mode 1 document that contains all Phase 3 intelligence features — environment narrative, tiered recommendations, Top 5 Risks, confidence tags, and the complete two-layer structure — validated against a real Dataverse environment,
So that the Phase 3 exit gate is formally met: a document I trust enough to hand directly to a client.

**Acceptance Criteria:**

**Given** all Epic 4 stories 4.1–4.10 are complete
**When** Claude receives the `EnrichedEnvironmentPayload` and generates Mode 1 output
**Then** the structured JSON output includes an `opening_narrative` object: a single coherent paragraph (3–5 sentences) identifying the inferred business process, naming top 3–5 tables with actual record counts, and stating where business logic is concentrated
**And** the narrative's business process inference sentence carries `[INFERRED]`; record counts and table names carry `[VERIFIED]`
**And** if inference confidence is low (fewer than 3 custom tables, no clear naming pattern), the narrative states: "The environment's purpose could not be inferred with confidence from available signals"

**Given** Mode 1 analysis is complete
**When** Claude generates the `top_risks[]` array
**Then** it contains at most 5 items, ranked Critical first then High
**And** each item follows the FR-012 5-part format: `what`, `why_problem`, `consequence`, `how_to_fix`, `estimated_effort`
**And** items are sourced from: Critical/High blast radius plugins, unfiltered synchronous plugins, plugins with no error handling on pre-operation steps — all from Mode 1 scan data, not requiring Mode 3
**And** if zero risks are found, `top_risks` contains a single entry: `{ "severity": "none", "text": "No Critical or High risks identified in this scan" }`

**Given** the recommendations section is generated
**When** Claude produces `recommendations[]`
**Then** every Critical/High recommendation has all 5 parts populated; any missing part is a schema validation failure caught by Story 4.1's gate
**And** every Medium recommendation has the 3-part format (`what`, `why_matters`, `recommended_action`)
**And** every Low/Advisory recommendation is a single sentence naming the specific entity
**And** every recommendation references a named entity — no generic recommendations pass schema validation

**Given** the full pipeline runs against the test Dataverse environment
**When** the generated document is downloaded and opened
**Then** the Executive Layer is readable in under 10 minutes without reference to the Technical Reference Layer
**And** elapsed time from `POST /api/document/generate` receipt to job status `ready` is recorded and documented in `docs/poc-baseline.md` alongside the P1 baseline measurements
**And** a code review confirms: no credential values in logs, no AI-generated blast radius tiers (all pre-classified by `DeterministicAnalyser`), no AI-generated Mermaid diagram (inserted verbatim), confidence tags present on all AI-generated fields in the JSON output

> **F-046, F-052, F-012 — FR-041, FR-047, FR-012, NFR-001, NFR-016**

---

## Epic 5: Mode 3 Health Audit Additions

**Goal:** Detect abandoned artifacts, integration-managed tables, and unmanaged solutions — the technical debt that accumulates invisibly over years of D365 customisation work.

**Exit gate:** Consultant confirms at least one finding is a real actionable issue previously unknown.

---

### Story 5.1: Unmanaged Solution Detection

As a D365 consultant,
I want the health audit to detect unmanaged solutions in the environment and produce a full 5-part recommendation for each,
So that I can identify ALM risk before a solution import silently overwrites client customisations.

**Acceptance Criteria:**

**Given** `GetSolutionsTool` is registered with the Mode 3 agent
**When** called
**Then** it retrieves all `Solution` records where `IsManaged = false` and the publisher customisation prefix is not a Microsoft-owned prefix (`msdyn_`, `msft_`, `adx_`, `cr`)
**And** returns per solution: solution name, publisher name, publisher prefix, creation date, and component count

**Given** unmanaged solutions are found
**When** the health audit report is assembled
**Then** each unmanaged solution produces a finding with all five parts of the FR-012 Critical/High format: What / Why this is a problem / What will happen if not fixed / How to fix / Estimated effort
**And** each part references environment-specific values (solution name, publisher name, component count, year) — the AC passes if the 5-part structure is present with correct environment-specific content, not if it matches any specific wording
**And** severity classification: `Warning` (🟡) if the solution has <10 components; `High` (🔴) if ≥10 components
**And** confidence tag: `[VERIFIED]` — `IsManaged` flag is a direct metadata value

**Given** zero non-Microsoft unmanaged solutions exist
**When** the health audit report is assembled
**Then** the section states: "No unmanaged solutions detected" — not omitted

> **F-059 — FR-054, NFR-016**

---

### Story 5.2: Abandoned Plugin Detection

As a D365 consultant,
I want the health audit to flag plugin assemblies that show signs of abandonment based on age and registration state,
So that I can identify plugins that might still fire but haven't been touched in years — a compliance and stability risk.

**Acceptance Criteria:**

**Given** plugin assembly data (including `ModifiedOn` from Story 4.4) is available to the Mode 3 agent
**When** abandoned plugin detection runs
**Then** all `PluginAssembly` records where `ModifiedOn` is more than `AnalysisSettings:AbandonedPluginAgeYears` years prior to the scan date are flagged as candidates
**And** `AbandonedPluginAgeYears` is read from `appsettings.json` under `AnalysisSettings` (default: 3) — hardcoding this value is forbidden
**And** each candidate is sub-classified:
  - Has zero active `SdkMessageProcessingStep` records → **Advisory** (🟢): no active steps — likely never completed or intentionally disabled
  - Has one or more active steps → **Warning** (🟡): actively registered but not modified within the threshold

**Given** a flagged plugin is found
**When** the finding is written
**Then** it includes: assembly name, last modified date, active step count, and registered events (if any)
**And** the finding carries a mandatory note: "An old assembly date does not confirm abandonment — confirm with the client whether this plugin is still intentionally active"
**And** confidence tag: `[INFERRED]` — age is `[VERIFIED]`, abandonment classification is `[INFERRED]`
**And** the recommendation follows FR-012 format for the applicable severity tier

**Given** no assemblies exceed the configured age threshold
**When** the section is generated
**Then** the section states: "No abandoned plugin assemblies detected" — not omitted

> **F-057 — FR-052, NFR-016, NFR-017**

---

### Story 5.3: Zero-Record Table Detection

As a D365 consultant,
I want the health audit to detect custom tables with zero records and classify them by their risk signal,
So that I can distinguish tables abandoned mid-build from tables that are legitimately pre-production or integration-managed.

**Acceptance Criteria:**

**Given** record stat data (from `GetTableRecordStatsTool` introduced in Story 4.3) is available to the Mode 3 agent
**When** zero-record table detection runs
**Then** all custom tables with `recordCount = 0` are identified
**And** each is sub-classified using a two-factor check:
  - Zero records + no custom relationships + no `SystemForm` → **Warning** (🟡): likely abandoned mid-build
  - Zero records + has at least one relationship OR has a `SystemForm` → **Advisory** (🟢): possibly pre-production or integration-managed

**Given** a zero-record table is found
**When** the finding is written
**Then** it includes: table name, publisher prefix, created date, relationship count, and form presence
**And** confidence tags: `[VERIFIED]` for record count, relationship count, form presence; `[INFERRED]` for the abandonment or pre-production classification
**And** the recommendation follows FR-012 format for the applicable severity tier

**Given** no custom tables have zero records
**When** the section is generated
**Then** the section states: "No zero-record custom tables detected" — not omitted

> **F-058 — FR-053, NFR-016, NFR-017**

---

### Story 5.4: Table Ownership by App Users

As a D365 consultant,
I want the health audit to identify tables where a significant proportion of records are owned by application users,
So that I know which tables are primarily managed by external integrations — and can flag undocumented write paths.

**Acceptance Criteria:**

**Given** `GetTableOwnerStatsTool` is registered with the Mode 3 agent
**When** called with a `tableName` parameter
**Then** it queries Dataverse for the count of records owned by application users (where `OwnerId` maps to a `SystemUser` where `IsLicensed = false` and `ApplicationId` is populated) vs. human users
**And** returns: `{ tableName, totalRecords, appUserOwnedRecords, appUserOwnedPercent, appUserNames[] }`

**Given** a table has app-user-owned record percentage exceeding `AnalysisSettings:IntegrationOwnershipThresholdPercent`
**When** the health audit report is assembled
**Then** the table is flagged as an **Advisory** (🟢) finding
**And** `IntegrationOwnershipThresholdPercent` is read from `appsettings.json` under `AnalysisSettings` (default: 50) — hardcoding this value is forbidden
**And** the finding includes: table name, percentage of app-user-owned records, and the application user name(s)
**And** the label reads: "This table appears to be primarily managed by an external integration via [application user name]"
**And** confidence tag: `[VERIFIED]` — derived from record ownership metadata directly

**Given** a `GetTableOwnerStatsTool` query fails for a specific table
**When** the batch processes that table
**Then** the table is skipped for ownership analysis — no finding is generated and a non-credential log entry records the failure
**And** the overall health audit job does not fail due to a per-table ownership query failure

**Given** no tables exceed the configured threshold
**When** the section is generated
**Then** the section states: "No tables with dominant application-user record ownership detected" — not omitted

> **F-056 — FR-051, NFR-016, NFR-017**
