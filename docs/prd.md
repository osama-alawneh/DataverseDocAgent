# DataverseDocAgent — Product Requirements Document

**Version:** 3.0 — BMAD Compliant  
**BMAD Status:** VALIDATED — 6/6 core sections present  
**Previous Versions:** v1 (Engineering Spec) | v2 (Security + Feature Registry) | v3 (Full BMAD Merge)  
**Owner:** Osama — Founder & Lead Developer  
**Last Updated:** April 2026  

---

> *"X-ray vision into your own CRM — delivered in minutes, not days."*

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Product Scope](#2-product-scope)
3. [User Journeys](#3-user-journeys)
4. [Feature Registry](#4-feature-registry)
5. [Security Architecture](#5-security-architecture)
6. [Functional Requirements](#6-functional-requirements)
7. [Non-Functional Requirements](#7-non-functional-requirements)
8. [Technical Architecture](#8-technical-architecture)
9. [Build Roadmap](#9-build-roadmap)
10. [Business Model](#10-business-model)
11. [Success Metrics](#11-success-metrics)

---

## 1. Executive Summary

DataverseDocAgent is an AI-powered SaaS platform that automatically generates deep technical documentation and intelligence reports for Microsoft Dynamics 365 and Dataverse environments. It connects directly to a client environment through read-only, least-privilege access, reads every layer of customisation, and synthesises everything into clear, structured documentation with plain-English explanations and risk recommendations.

The platform is built with security as a first-class concern — not an afterthought. Every design decision, from the permission model to the credential handling architecture, is oriented around giving customers confidence before they connect their environment.

**Core value proposition:** What takes a D365 consultant 2–3 days to document manually, DataverseDocAgent delivers in minutes — securely, reliably, and with zero write access to the environment.

### Target Users

The primary buyer and user is the **independent Microsoft Dynamics 365 consultant** — a working freelancer or boutique-agency consultant who personally pays for and runs the tool. The defining engagement context is arriving at a new client with a D365 environment that has accumulated years of customisation and no documentation. Their time on that engagement starts billing from day one; the 2–3 days spent manually documenting is dead time.

Secondary users, targeted from Phase 2 onward: in-house D365 developers who need field impact analysis before making changes; ISVs managing multiple client environments; and IT administrators responsible for environment governance and audit evidence.

### Market Context

There is no specialist tooling for Dynamics 365 environment documentation. Consultants rely on manual methods: screenshots, XrmToolBox metadata exports, personal templates, and notes taken directly in client environments. The process is inconsistent, consultant-dependent, and non-scalable. The field impact problem (Mode 2) and health audit problem (Mode 3) are currently solved with nothing — consultants trace dependencies by reading plugin source code and asking colleagues.

General-purpose Dataverse tools (XrmToolBox, solution export) surface raw metadata but produce nothing human-readable. They require significant manual effort to turn raw data into usable documentation, and they have no AI analysis layer.

### Key Differentiator

DataverseDocAgent is the first tool to combine deep, multi-layer environment reading (metadata + decompiled plugin code + JavaScript + flow definitions + security roles) with AI-generated plain-English explanation at full report scope. The output is immediately shareable with a client or used directly for project planning — without post-processing. Security is treated as a first-class product concern: the permission model, credential handling architecture, and pre-flight checker exist to eliminate the buying objection before it arises.

---

## 2. Product Scope

### 2.1 In Scope — v1 (Phases P1–P5)

- Microsoft Dataverse environments connected to Dynamics 365 (online, cloud-hosted)
- All three product modes: Documentation Generator (Mode 1), Field Impact Analyser (Mode 2), Health Audit (Mode 3)
- Service account authentication via Microsoft Entra App Registration (client credentials / daemon flow)
- Read-only access to: custom tables, custom fields, relationships, plugin assemblies (decompiled), JavaScript web resources, Power Automate flow definitions (solution-aware), classic workflow XAML, security roles and privileges, system forms, saved queries, organisation metadata
- API-only delivery: ASP.NET Core Web API accessed via HTTP client (Postman, custom integration)
- Word (.docx) output for Mode 1; structured JSON responses for Modes 2 and 3
- Pre-built importable Dataverse security role solution (`DataverseDocAgent_SecurityRole.zip`)
- Permission pre-flight checker (`POST /api/security/check`) as a mandatory gate before any mode runs
- Azure App Service hosting (Free Tier for MVP; Basic Tier upon first paying customer)
- European customer environments (schema and code access only — no record-level data processed)

### 2.2 Out of Scope — v1

The following are explicitly excluded from v1. Some are planned for future phases; others are permanently excluded.

| Item | Notes |
|------|-------|
| Phase 6 Web UI features | Environment submission form, report history, OAuth login, subscription billing, multi-environment dashboard — explicitly deferred to Phase 6 |
| Non-Microsoft CRM platforms | Salesforce, HubSpot, ServiceNow, and any non-Dataverse system |
| On-premise Dynamics deployments | Dynamics CRM 2016 and earlier on-premise; any environment not accessible via the Dataverse cloud API |
| Pure Power Apps environments | Dataverse environments not backed by a Dynamics 365 application |
| Write, append, create, delete, or share access | No write operations against any customer environment — in any version, under any circumstance |
| Real-time monitoring | Continuous polling, alerting, or event-driven notifications against live environments |
| Automated remediation | The product identifies issues; it never acts on them |
| GDPR Data Processing Agreement (DPA) | Flagged as a pre-enterprise-sales requirement; not in scope for MVP |
| Multi-tenant team management | Single-user API access only for v1 |

### 2.3 Permanently Out of Scope (All Versions)

These are hard product boundaries that will not change regardless of version or customer request:

1. **Write access to any customer environment** — the product is read-only by design, not by configuration
2. **Non-Microsoft CRM platforms** — the product is purpose-built for Dataverse/D365
3. **On-premise Dynamics** — cloud-only; the Dataverse SDK and MCP tooling require cloud connectivity
4. **Real-time monitoring** — the product produces point-in-time reports, not continuous observation
5. **Automated remediation** — the product advises; remediation is always a human decision

---

## 3. User Journeys

### 3.1 Personas

#### Primary Persona: The Independent D365 Consultant

| Attribute | Detail |
|-----------|--------|
| Role | Freelance or boutique-agency Microsoft Dynamics 365 consultant |
| Engagement trigger | Arrives at a new client engagement; environment has 2–10 years of accumulated customisation with little or no existing documentation |
| Core pain | First 2–3 days of every new engagement consumed by manual environment documentation before meaningful work can begin |
| Goal | Deliver client value immediately; bill hours on consulting work, not documentation |
| Technical profile | High technical comfort — uses Postman, Azure portal, Power Platform Admin Centre, and Dataverse developer tooling daily |
| Buying behaviour | Self-authorised spending under ~$500/month; no procurement process; pays from personal or agency card |
| Phase 1 profile | Network contacts in D365 consulting; starting new client engagements; no prior exposure to the product |

#### Secondary Persona: The In-House D365 Developer

| Attribute | Detail |
|-----------|--------|
| Role | Internal developer at a company running Dynamics 365 |
| Core pain | Needs to understand the full downstream impact of changing a specific field before touching it; currently does this by reading plugin code and asking colleagues |
| Goal | Make changes confidently and quickly without introducing regressions |
| Phase relevance | Becomes a primary target in Phase 2 (API product); secondary buyer in v1 |

---

### 3.2 Journey 1 — First-Time Setup (All Modes)

**Actor:** Independent D365 Consultant  
**Trigger:** First use of the product on any client environment  
**Pre-condition:** Consultant has access to the client's Azure portal and Power Platform Admin Centre  

| Step | Consultant Action | System Response |
|------|-------------------|-----------------|
| 1 | Reads the Service Account Setup Guide | — |
| 2 | Creates Microsoft Entra App Registration in the client's Azure portal | Azure returns: Application (Client) ID, Directory (Tenant) ID, Client Secret |
| 3 | Creates a Dataverse Application User in Power Platform Admin Centre; does not assign System Administrator role | Application user created with no role |
| 4 | Downloads `DataverseDocAgent_SecurityRole.zip` and imports it via make.powerapps.com | "DataverseDocAgent Reader" security role created in the environment |
| 5 | Assigns DataverseDocAgent Reader role to the Application User | Service account now has least-privilege, read-only access |
| 6 | Calls `POST /api/security/check` with `environmentUrl`, `tenantId`, `clientId`, `clientSecret` | Returns `status: ready`, `safeToRun: true`, full `passed[]` list |
| 7 | Proceeds to the required mode | Journey 2, 3, or 4 begins |

**Success:** Permission checker returns `status: ready`. Consultant proceeds with confidence the service account is correctly scoped.  
**Failure — missing permissions:** Checker returns `status: blocked`. The `missing[]` array names the specific permissions absent. The `recommendation` field gives exact remediation steps. Consultant resolves and re-runs the check.  
**Failure — extra permissions:** Checker returns `status: ready` but `extra[]` is non-empty. Tool is safe to run. `recommendation` advises removing the named surplus privileges.

---

### 3.3 Journey 2 — Generate Environment Documentation (Mode 1)

**Actor:** Independent D365 Consultant  
**Trigger:** Consultant needs a complete technical documentation package for a client environment at the start of an engagement  
**Pre-condition:** Journey 1 complete; `status: ready` confirmed  

| Step | Consultant Action | System Response |
|------|-------------------|-----------------|
| 1 | Calls `POST /api/document/generate` with credentials and environment URL | API acknowledges request; agent begins environment scan |
| 2 | Waits (target: under 5 minutes for typical environments; under 10 minutes for large) | Agent reads: tables, fields, relationships, plugins (decompiled), Power Automate flows, classic workflows, JavaScript web resources, security roles |
| 3 | Receives JSON response containing a secure download URL and token | Token is valid for 24 hours |
| 4 | Calls `GET /api/download/{token}` | Receives the `.docx` file |
| 5 | Opens document and reviews the 9-section report | Complete technical documentation ready for client sharing or internal project planning |
| 6 | Reviews the Recommendations section | Identifies immediate risks, performance concerns, and deprecated API usage as talking points for the client |

**Success:** `.docx` received within the time target. The manual 2–3 day documentation task is replaced in a single API call.  
**Failure — generation timeout:** Request times out or returns a partial result. Consultant receives a structured error response. Credentials are not retained; no partial output is stored.  
**Failure — credential rejection:** Dataverse rejects the credentials during connection test. API returns a 401-equivalent with a clear message before any AI tokens are consumed.

---

### 3.4 Journey 3 — Analyse Field Impact Before a Change (Mode 2)

**Actor:** Independent D365 Consultant or In-House D365 Developer  
**Trigger:** A client wants to rename, remove, or retype a Dataverse field. The consultant needs to know everything that will break.  
**Pre-condition:** Journey 1 complete; `status: ready` confirmed  

| Step | Actor Action | System Response |
|------|--------------|-----------------|
| 1 | Identifies the target table's logical name and the field's logical name in Dataverse | — |
| 2 | Calls `POST /api/impact/analyze` with credentials, `tableName`, and `fieldName` | Agent traces all logic referencing the field across plugins, flows, business rules, JavaScript, views, and dashboards |
| 3 | Receives a structured JSON impact map | Sections: plugins, Power Automate flows, business rules, JavaScript OnChange functions, views/dashboards, overall risk rating |
| 4 | Reviews the risk rating (Low / Medium / High) and the plain-English rationale summary | Understands the full scope of the change before touching anything |
| 5 | Uses the output to advise the client or plan a safe change sequence | — |

**Success:** Complete dependency map returned. Consultant can make the change — or advise against it — with a documented, evidence-based rationale.  
**Failure — field not found:** Specified table/field combination does not exist. Returns a clear 404-equivalent with a human-readable explanation.  
**Failure — no dependencies found:** Field exists but has no logic referencing it. Returns a valid response with empty dependency arrays and a risk rating of Low with an explicit "no dependencies found" explanation.

---

### 3.5 Journey 4 — Run Environment Health Audit (Mode 3)

**Actor:** Independent D365 Consultant  
**Trigger:** A client requests a technical health review, or the consultant suspects accumulated technical debt  
**Pre-condition:** Journey 1 complete; `status: ready` confirmed  

| Step | Consultant Action | System Response |
|------|-------------------|-----------------|
| 1 | Calls `POST /api/health/audit` with credentials and environment URL | Agent begins full environment scan: plugins, flows, JavaScript, security roles, field usage |
| 2 | Waits for scan to complete | — |
| 3 | Receives a structured JSON response containing the prioritised report card | Issues organised by severity (🔴 Critical, 🟡 Warning, 🟢 Advisory) and category |
| 4 | Reviews Critical items (🔴) first | Plugins with no error handling, null reference risks, hardcoded GUIDs |
| 5 | Reviews Warning items (🟡) | Performance risks (unfiltered plugins), duplicate logic, deprecated JavaScript APIs |
| 6 | Reviews Advisory items (🟢) | Orphaned fields, over-privileged security roles, undocumented tables |
| 7 | Uses the report as the basis for a remediation scoping conversation with the client | Structured, prioritised finding list supports time and cost estimation |

**Success:** Prioritised issue list returned. Consultant has a defensible, structured basis for recommending and scoping remediation work.  
**Failure — no issues found:** Environment is clean. Returns a valid response with empty finding arrays per category and an explicit confirmation that no issues were detected.

---

## 4. Feature Registry

Every feature planned for DataverseDocAgent is tracked below. This is the single source of truth for what gets built, when, and why. Features F-041 through F-045 (Phase 6 Web UI) are tracked here but excluded from Functional Requirements as they are explicitly out of scope for v1.

**Status key:** `In Design` | `Planned` | `Done`  
**Priority key:** `Critical` > `High` > `Medium`

### Core Documentation

| ID | Feature | Mode | Phase | Status | Priority |
|----|---------|------|-------|--------|----------|
| F-001 | Custom table discovery & metadata | Mode 1 | P2 | Planned | Critical |
| F-002 | Field catalogue — all custom fields per table | Mode 1 | P2 | Planned | Critical |
| F-003 | Relationship mapping — 1:N and N:N | Mode 1 | P2 | Planned | Critical |
| F-004 | Plugin DLL retrieval & decompilation (dnlib) | Mode 1 | P3 | Planned | Critical |
| F-005 | Plain-English plugin logic explanation | Mode 1 | P3 | Planned | Critical |
| F-006 | JavaScript web resource reading & analysis | Mode 1 | P3 | Planned | High |
| F-007 | Power Automate flow parsing (solution-aware) | Mode 1 | P3 | Planned | High |
| F-008 | Classic workflow XAML parsing | Mode 1 | P3 | Planned | High |
| F-009 | Business rules documentation | Mode 1 | P3 | Planned | Medium |
| F-010 | Security roles & privileges documentation | Mode 1 | P3 | Planned | High |
| F-011 | Executive summary with complexity rating | Mode 1 | P2 | Planned | High |
| F-012 | AI-generated recommendations section | Mode 1 | P3 | Planned | High |
| F-013 | Word (.docx) document generation | Mode 1 | P2 | Planned | Critical |

### Field Impact Analyser

| ID | Feature | Mode | Phase | Status | Priority |
|----|---------|------|-------|--------|----------|
| F-014 | Field-level trigger map — plugins | Mode 2 | P4 | Planned | Critical |
| F-015 | Field-level trigger map — Power Automate | Mode 2 | P4 | Planned | Critical |
| F-016 | Field-level trigger map — business rules | Mode 2 | P4 | Planned | High |
| F-017 | Field-level trigger map — JavaScript OnChange | Mode 2 | P4 | Planned | High |
| F-018 | Field-level trigger map — views & dashboards | Mode 2 | P4 | Planned | Medium |
| F-019 | Overall risk rating per field (Low/Med/High) | Mode 2 | P4 | Planned | High |

### Health Audit

| ID | Feature | Mode | Phase | Status | Priority |
|----|---------|------|-------|--------|----------|
| F-020 | Plugin risk scan — null checks, error handling | Mode 3 | P5 | Planned | Critical |
| F-021 | Performance flag — plugins with no filter condition | Mode 3 | P5 | Planned | High |
| F-022 | Duplicate logic detection across plugins & flows | Mode 3 | P5 | Planned | High |
| F-023 | JavaScript deprecated API detection (Xrm.Page) | Mode 3 | P5 | Planned | High |
| F-024 | Orphaned field detection (not on any form/view) | Mode 3 | P5 | Planned | Medium |
| F-025 | Security role over-privilege detection | Mode 3 | P5 | Planned | High |
| F-026 | Hardcoded GUID detection in plugin code | Mode 3 | P5 | Planned | Medium |
| F-027 | Disabled flow detection (exists but inactive) | Mode 3 | P5 | Planned | Medium |
| F-028 | Prioritised report card (🔴🟡🟢) | Mode 3 | P5 | Planned | High |

### Security & Trust

| ID | Feature | Mode | Phase | Status | Priority |
|----|---------|------|-------|--------|----------|
| F-029 | Permission checker — pre-flight validation | All Modes | P2 | In Design | Critical |
| F-030 | Missing permission detection with clear messaging | All Modes | P2 | In Design | Critical |
| F-031 | Extra permission detection & removal recommendation | All Modes | P2 | In Design | High |
| F-032 | Importable least-privilege security role solution | All Modes | P2 | In Design | Critical |
| F-033 | Service account setup guide (in-product) | All Modes | P2 | In Design | High |
| F-034 | Credential in-memory-only guarantee (no logging) | All Modes | P1 | In Design | Critical |
| F-035 | Privacy policy & data handling documentation | All Modes | P2 | In Design | High |

### API Layer

| ID | Feature | Mode | Phase | Status | Priority |
|----|---------|------|-------|--------|----------|
| F-036 | POST /api/document/generate | API | P2 | Planned | Critical |
| F-037 | POST /api/impact/analyze | API | P4 | Planned | Critical |
| F-038 | POST /api/health/audit | API | P5 | Planned | High |
| F-039 | POST /api/security/check | API | P2 | In Design | Critical |
| F-040 | GET /api/download/{token} | API | P2 | Planned | Critical |

### Future / Web UI (Out of Scope v1 — Tracked for Reference)

| ID | Feature | Mode | Phase | Status | Priority |
|----|---------|------|-------|--------|----------|
| F-041 | Web UI — environment submission form | Web | P6 | Planned | High |
| F-042 | Web UI — report history & download centre | Web | P6 | Planned | Medium |
| F-043 | OAuth delegated auth (Microsoft login) | Web | P6 | Planned | High |
| F-044 | Subscription billing integration | Web | P6 | Planned | High |
| F-045 | Multi-environment dashboard | Web | P6 | Planned | Medium |

---

## 5. Security Architecture

Security is the primary buying objection for a tool that connects to a live CRM environment. This chapter defines the complete security model and serves as the source of truth for both engineering implementation and customer-facing trust documentation.

### 5.1 Core Security Principles

| Principle | What It Means in Practice |
|-----------|--------------------------|
| Read-only. Always. | The tool never requests, uses, or needs write access to any table. If a credential has write access, the tool still only reads. The permission checker flags the extra privilege and recommends removal. |
| Zero credential storage | Credentials are accepted per request, held in server memory only for the lifetime of that request, and never written to disk, database, logs, or any external system. |
| Least privilege by design | A pre-built Dataverse security role ships with exactly the permissions needed — nothing more. Customers import it in one click rather than configuring manually. |
| Fail fast and loud | If credentials are wrong, the connection is refused before any AI tokens are spent. The customer gets a clear, specific error — not a generic timeout after minutes of waiting. |
| Transparent operation | Every API call the tool makes appears in the Dataverse audit log under the service account. Customers can verify exactly what was accessed at any time. |
| Proof over promises | The permission checker shows customers exactly what access the service account has before the tool runs — including flagging anything unexpected. Trust is earned by showing, not telling. |

### 5.2 Credential Handling Architecture

| # | Stage | What Happens |
|---|-------|-------------|
| 1 | Request received | Credentials arrive in the JSON body of the HTTPS POST request. TLS in transit — never plaintext. |
| 2 | Validation | API validates format only. No external call yet. Credentials never logged at this stage. |
| 3 | Connection test | A single lightweight Dataverse call tests the credentials. If it fails, credentials are immediately discarded and a 401 is returned. |
| 4 | Passed to agent | Credentials are passed as a parameter to the agent service — in memory only, as a C# object on the call stack. |
| 5 | MCP session | The MCP client uses credentials to authenticate each tool call. Credentials remain in memory as part of the active service scope. |
| 6 | Request completes | The request ends. The C# object is garbage collected. Credentials no longer exist in any form on the server. |
| 7 | What is NEVER done | Credentials are NEVER written to logs, NEVER stored in a database, NEVER sent to any third party, NEVER cached between requests. |

### 5.3 Service Account Setup Guide

Published to customers before they connect their first environment. Goal: a locked-down, auditable, revocable service account with exactly the right permissions and nothing else.

**Step 1 — Create a Microsoft Entra App Registration**
- Go to `portal.azure.com` → Microsoft Entra ID → App Registrations → New Registration
- Name it: `DataverseDocAgent-Reader`
- Supported account type: Accounts in this organizational directory only
- No redirect URI needed — this is a service-to-service (daemon) application
- Note the Application (Client) ID and Directory (Tenant) ID
- Go to Certificates & Secrets → New Client Secret → set expiry → note the secret value immediately

**Step 2 — Create a Dataverse Application User**
- Go to Power Platform Admin Centre → your environment → Settings → Users → Application Users
- Click New App User → select the Entra app you just created
- Do NOT assign the System Administrator role
- Leave it with no role for now — you will assign the custom role in Step 4

**Step 3 — Import the DataverseDocAgent Security Role Solution**
- Download `DataverseDocAgent_SecurityRole.zip` from the product documentation
- Go to `make.powerapps.com` → Solutions → Import Solution → upload the file
- This creates a security role called `DataverseDocAgent Reader` with exactly the right privileges

**Step 4 — Assign the Role to the Application User**
- Return to Power Platform Admin Centre → Application Users → find your DataverseDocAgent user
- Edit → assign the `DataverseDocAgent Reader` role → Save

**Step 5 — Run the Permission Checker**
- Call `POST /api/security/check` with your environment URL and credentials
- Review the response — all required permissions should show as PASS
- If any extra permissions appear, follow the recommendations to remove them
- Only proceed to document generation once the checker returns `status: ready`

### 5.4 Exact Permissions Required

> **All three modes require identical permissions. The tool is purely read-only across all modes — no write, append, or delete access is ever needed or used.**

| Table / Entity | Privilege Required | Why It Is Needed | Write Needed? |
|---------------|-------------------|------------------|---------------|
| Entity (EntityMetadata) | Read | Discover all custom tables and their properties | Never |
| Attribute (AttributeMetadata) | Read | Read all field definitions per table | Never |
| Relationship | Read | Read all 1:N and N:N relationships | Never |
| PluginAssembly | Read | Retrieve compiled plugin DLL for analysis | Never |
| PluginType | Read | Identify plugin classes within assemblies | Never |
| SdkMessageProcessingStep | Read | Read which events each plugin is registered on | Never |
| WebResource | Read | Retrieve JavaScript source code | Never |
| Workflow | Read | Read flow and workflow definitions | Never |
| Role | Read | List all custom security roles | Never |
| RolePrivilege | Read | Read privileges assigned to each security role | Never |
| SystemForm | Read | Read form definitions for JavaScript registration map | Never |
| SavedQuery | Read | Read view definitions for field impact analysis | Never |
| Organization | Read | Read basic environment metadata for report header | Never |

### 5.5 Permission Checker — Endpoint Specification

**Endpoint:** `POST /api/security/check`

**Request Body:**

| Field | Type | Description |
|-------|------|-------------|
| environmentUrl | string | Full Dataverse org URL e.g. `https://contoso.crm.dynamics.com` |
| tenantId | string (GUID) | Microsoft Entra tenant ID |
| clientId | string (GUID) | App registration client ID |
| clientSecret | string | Client secret value |
| targetMode | string (optional) | `"mode1"` \| `"mode2"` \| `"mode3"` \| `"all"` — defaults to `"all"` |

**Response Fields:**

| Field | Type | Description |
|-------|------|-------------|
| status | string | `"ready"` \| `"degraded"` \| `"blocked"` — overall assessment |
| safeToRun | boolean | True only when status is `"ready"` — no missing permissions |
| passed | string[] | List of permission checks that passed |
| missing | string[] | Required permissions not found on the service account |
| extra | string[] | Permissions found that are not needed — flagged for removal |
| recommendation | string | Plain-English summary of what to do before proceeding |

**Example Response — Ready State:**
```json
{
  "status": "ready",
  "safeToRun": true,
  "passed": [
    "Read PluginAssembly", "Read WebResource", "Read Workflow",
    "Read Attribute", "Read Entity", "Read Role"
  ],
  "missing": [],
  "extra": [],
  "recommendation": "All permissions verified. Safe to run all modes."
}
```

**Example Response — Extra Permissions Detected:**
```json
{
  "status": "ready",
  "safeToRun": true,
  "passed": ["Read PluginAssembly", "Read WebResource", "..."],
  "missing": [],
  "extra": ["Write Contact", "Delete Account"],
  "recommendation": "Tool can run safely. However we detected 2 unnecessary privileges. We recommend removing Write Contact and Delete Account from the service account to minimise risk surface."
}
```

**Example Response — Missing Permissions:**
```json
{
  "status": "blocked",
  "safeToRun": false,
  "passed": ["Read Entity", "Read Attribute", "..."],
  "missing": ["Read PluginAssembly", "Read WebResource"],
  "extra": [],
  "recommendation": "Cannot run Mode 1 — plugin and JavaScript analysis will fail. Please add Read PluginAssembly and Read WebResource to the DataverseDocAgent Reader role and re-run this check."
}
```

---

## 6. Functional Requirements

Each FR is derived from the Feature Registry (Section 4) and elevated to a measurable capability contract. All criteria use SHALL (mandatory) language and are verifiable by functional test, code review, or output inspection.

**Conventions:**
- Traceability: every FR maps to one or more Feature IDs (F-xxx) from the Feature Registry
- Priority values are inherited from the Feature Registry: Critical > High > Medium
- Phase values map to the Build Roadmap (Section 9)

---

### 6.1 Core Documentation (Mode 1)

---

**FR-001 — Custom Table Discovery**  
`F-001 | Phase P2 | Priority: Critical`

The system SHALL discover and document all custom tables in the connected Dataverse environment.

*Acceptance Criteria:*
- All tables where `IsCustomEntity = true` are included in the output
- Each table entry includes: display name, logical name, solution membership, AI-inferred business purpose, and key field summary
- System tables are excluded unless they carry solution-owned customisations
- If zero custom tables exist, the document section states this explicitly rather than being omitted

---

**FR-002 — Field Catalogue**  
`F-002 | Phase P2 | Priority: Critical`

The system SHALL document all custom fields for every discovered custom table.

*Acceptance Criteria:*
- All fields where `IsCustomAttribute = true` are included per table
- Each field entry includes: display name, logical name, data type, required level, default value (if set), option set values with labels (if applicable), and a plain-English description
- Fields are grouped by table in the output document
- Option set fields list all active option values with their integer codes and display labels

---

**FR-003 — Relationship Mapping**  
`F-003 | Phase P2 | Priority: Critical`

The system SHALL map and document all custom relationships between tables.

*Acceptance Criteria:*
- All relationships where `IsCustomRelationship = true` are included (both 1:N and N:N)
- Each entry includes: relationship type, both participating tables, schema name, cascade behaviour (delete, assign, share, unshare), and AI-inferred business meaning
- Self-referential relationships are supported and labelled as such

---

**FR-004 — Plugin DLL Retrieval and Decompilation**  
`F-004 | Phase P3 | Priority: Critical`

The system SHALL retrieve compiled plugin assemblies from Dataverse and decompile them to readable C# source.

*Acceptance Criteria:*
- All `PluginAssembly` records registered in the environment are retrieved
- DLLs are decompiled in-process using dnlib without writing to disk at any stage
- Decompilation produces syntactically valid C# that is passed directly to the AI analysis step
- If a DLL cannot be decompiled (e.g., obfuscated or corrupted), the plugin entry is included in the output with an explicit flag: "Decompilation failed — manual review required"

---

**FR-005 — Plain-English Plugin Logic Explanation**  
`F-005 | Phase P3 | Priority: Critical`

The system SHALL generate a plain-English explanation of each plugin's logic via AI analysis of the decompiled source.

*Acceptance Criteria:*
- Each plugin explanation includes: what the plugin does in plain English, which fields it reads, which fields it writes, which event and stage it fires on, and any risk flags identified
- Explanations are written to be understandable to a non-developer stakeholder
- Raw decompiled C# source is not reproduced verbatim in the output document
- If a plugin's logic could not be analysed (decompilation failed per FR-004), the explanation section notes this and does not fabricate content

---

**FR-006 — JavaScript Web Resource Analysis**  
`F-006 | Phase P3 | Priority: High`

The system SHALL retrieve all JavaScript web resources and generate a plain-English explanation of each.

*Acceptance Criteria:*
- All `WebResource` records of type JavaScript (type = 3) are retrieved as plain text source
- For each file: which forms it is registered on, which functions are registered on which events (OnLoad, OnSave, OnChange), and a plain-English explanation of each registered function's purpose
- Deprecated API usage (e.g., `Xrm.Page`) is explicitly flagged per file in the analysis section
- If a JavaScript file contains no form event registrations, this is noted explicitly

---

**FR-007 — Power Automate Flow Documentation**  
`F-007 | Phase P3 | Priority: High`

The system SHALL retrieve and document all solution-aware Power Automate flow definitions.

*Acceptance Criteria:*
- All flows retrieved via the Power Platform API that belong to a Dataverse solution are included
- Each entry includes: flow name, trigger type, trigger entity and event, action summary in plain English, tables read/written, and active/disabled status
- Flows not in a solution are noted as present but explicitly excluded from deep analysis with a count

---

**FR-008 — Classic Workflow XAML Parsing**  
`F-008 | Phase P3 | Priority: High`

The system SHALL parse classic Dynamics 365 workflows from XAML and produce step-by-step plain-English documentation.

*Acceptance Criteria:*
- All `Workflow` records with `Category = 0` (classic workflow) are retrieved and parsed
- XAML is deserialised and each step is expressed in plain English: scope, trigger conditions, on-demand vs. automatic execution, and each action in sequence
- If XAML cannot be parsed, the entry is included with a flag noting the parse failure

---

**FR-009 — Business Rules Documentation**  
`F-009 | Phase P3 | Priority: Medium`

The system SHALL document all active business rules defined in the environment.

*Acceptance Criteria:*
- All `Workflow` records with `Category = 2` (business rule) are retrieved
- Each entry includes: name, scope (entity / all forms / specific form), conditions in plain English, and actions in plain English

---

**FR-010 — Security Roles Documentation**  
`F-010 | Phase P3 | Priority: High`

The system SHALL document all non-system security roles and their privilege configurations.

*Acceptance Criteria:*
- All security roles not marked as system roles are retrieved and documented
- Each entry includes: role name, key privilege levels (Create/Read/Write/Delete) for major entities, and notable differences from a standard user's access
- Roles with organisation-level Create or Delete on any entity are flagged as potentially over-privileged

---

**FR-011 — Executive Summary Section**  
`F-011 | Phase P2 | Priority: High`

The system SHALL generate an executive summary as the first section of the Mode 1 output document.

*Acceptance Criteria:*
- Includes: environment name, scan date, complexity rating (Low / Medium / High), and counts for custom tables, custom fields, plugins, flows, and JavaScript files
- The complexity rating is deterministic: derived from a defined scoring model specified in technical design (not AI-generated)
- Key observations are AI-generated and specific to the scanned environment — they reference actual content found, not generic placeholder text

---

**FR-012 — AI Recommendations Section**  
`F-012 | Phase P3 | Priority: High`

The system SHALL generate a recommendations section containing AI-identified risks, bad practices, and improvement suggestions specific to the environment.

*Acceptance Criteria:*
- Every recommendation references the specific plugin, field, flow, or role it applies to — no generic advice
- Recommendations are categorised (Performance, Security, Code Quality, Maintenance, Deprecated APIs)
- At minimum one recommendation is generated if any plugin, flow, or JavaScript file is present in the environment

---

**FR-013 — Word Document Generation**  
`F-013 | Phase P2 | Priority: Critical`

The system SHALL produce a downloadable Word (.docx) document containing the complete Mode 1 report.

*Acceptance Criteria:*
- Output is a valid `.docx` file openable in Microsoft Word 2016 and later
- Document contains the 9 sections defined in Section 8.1 (Product Modes)
- Document is returned via a secure time-limited download token (`GET /api/download/{token}`)
- Download token expires after 24 hours; requests after expiry return a 404-equivalent
- The document is not stored server-side after the token expires

---

### 6.2 Field Impact Analyser (Mode 2)

---

**FR-014 — Plugin Trigger Map Per Field**  
`F-014 | Phase P4 | Priority: Critical`

The system SHALL identify all plugins that fire on the specified field's parent table and determine whether each reads or writes the specified field.

*Acceptance Criteria:*
- Returns all matching plugins with: plugin name, class, event, stage, whether it reads the field, whether it writes the field, and any risk flags from its logic
- If no plugins reference the field, returns an empty array with an explicit "no plugin dependencies found" note

---

**FR-015 — Power Automate Trigger Map Per Field**  
`F-015 | Phase P4 | Priority: Critical`

The system SHALL identify all solution-aware flows that include the specified field as a trigger condition or action target.

*Acceptance Criteria:*
- Each matching flow entry includes: flow name, trigger details, and whether the field is used as a trigger condition or an action target (or both)
- If no flows reference the field, returns an empty array with an explicit note

---

**FR-016 — Business Rule Trigger Map Per Field**  
`F-016 | Phase P4 | Priority: High`

The system SHALL identify all business rules that reference the specified field in their conditions or actions.

*Acceptance Criteria:*
- Returns rule name, scope, and whether the field is used in a condition, an action, or both, with a plain-English description

---

**FR-017 — JavaScript OnChange Trigger Map Per Field**  
`F-017 | Phase P4 | Priority: High`

The system SHALL identify all JavaScript functions registered on the OnChange event of the specified field on any form.

*Acceptance Criteria:*
- Returns: function name, form name, JavaScript file name, and a plain-English description of what the function does
- If no OnChange handlers are registered for the field on any form, returns an empty array with an explicit note

---

**FR-018 — Views and Dashboards Containing the Field**  
`F-018 | Phase P4 | Priority: Medium`

The system SHALL return all saved views and dashboards that display the specified field.

*Acceptance Criteria:*
- Returns: view/dashboard name, type (public view, system view, personal view, dashboard), and entity context
- Personal views from individual users are included if accessible via the service account

---

**FR-019 — Overall Risk Rating Per Field**  
`F-019 | Phase P4 | Priority: High`

The system SHALL calculate and return an overall risk rating (Low / Medium / High) for changing the specified field.

*Acceptance Criteria:*
- Rating is derived deterministically from the number and type of dependencies found (logic to be specified in technical design)
- Plain-English rationale explains what drove the rating
- A field with zero dependencies returns Low with an explicit "no dependencies found" rationale
- Rating is consistent: the same field in the same environment always returns the same rating

---

### 6.3 Health Audit (Mode 3)

---

**FR-020 — Plugin Risk Scan**  
`F-020 | Phase P5 | Priority: Critical`

The system SHALL scan all plugin code for null reference risks and missing error handling.

*Acceptance Criteria:*
- Flags any plugin method lacking a try/catch block as a Critical finding
- Flags attribute access patterns on potentially null `Entity` objects as a Critical finding
- Each finding includes: plugin name, class name, method name, and a plain-English description of the risk

---

**FR-021 — Performance Flag: Unfiltered Plugins**  
`F-021 | Phase P5 | Priority: High`

The system SHALL flag any plugin registered on Update events with no `AttributeFilter` set.

*Acceptance Criteria:*
- Returns: plugin name, registered event, and a plain-English explanation that the plugin fires on every entity save regardless of which field changed
- Classified as a Warning (🟡) finding

---

**FR-022 — Duplicate Logic Detection**  
`F-022 | Phase P5 | Priority: High`

The system SHALL identify cases where multiple plugins, flows, or business rules appear to write to the same field.

*Acceptance Criteria:*
- Returns conflicting logic items grouped by the shared target field
- Each finding includes: the target field, the list of logic items that write to it, and a plain-English explanation of the race condition or overwrite risk
- Classified as a Warning (🟡) finding

---

**FR-023 — Deprecated JavaScript API Detection**  
`F-023 | Phase P5 | Priority: High`

The system SHALL scan all JavaScript web resources for usage of deprecated Dataverse client APIs.

*Acceptance Criteria:*
- Detects at minimum: `Xrm.Page` usage (deprecated; replaced by `formContext`)
- Returns: file name, the deprecated API reference found, and the recommended modern replacement
- Classified as a Warning (🟡) finding

---

**FR-024 — Orphaned Field Detection**  
`F-024 | Phase P5 | Priority: Medium`

The system SHALL identify custom fields that appear on no form and in no view.

*Acceptance Criteria:*
- A field is classified as orphaned if it appears in no `SystemForm` definition and in no `SavedQuery` column list
- Returns: table name, field logical name, field display name
- Classified as an Advisory (🟢) finding

---

**FR-025 — Security Role Over-Privilege Detection**  
`F-025 | Phase P5 | Priority: High`

The system SHALL flag custom security roles with organisation-level Create or Delete access on major entities.

*Acceptance Criteria:*
- Returns: role name, entity name, and the specific privilege that appears over-scoped
- Includes a recommendation to scope the privilege down to Business Unit or User level
- Classified as an Advisory (🟢) finding

---

**FR-026 — Hardcoded GUID Detection in Plugins**  
`F-026 | Phase P5 | Priority: Medium`

The system SHALL detect hardcoded GUID literals in decompiled plugin code.

*Acceptance Criteria:*
- Detects GUID-formatted string literals (pattern: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`) in plugin method bodies
- Returns: plugin name, method name, and the detected GUID value
- Explains that hardcoded GUIDs break environment migration (sandbox to production, or between tenants)
- Classified as a Warning (🟡) finding

---

**FR-027 — Disabled Flow Detection**  
`F-027 | Phase P5 | Priority: Medium`

The system SHALL detect solution-aware Power Automate flows that exist but are in a disabled state.

*Acceptance Criteria:*
- Returns: flow name, solution membership, and last modified date
- Notes that disabled flows may represent abandoned logic or flows pending activation
- Classified as an Advisory (🟢) finding

---

**FR-028 — Prioritised Health Audit Report Card**  
`F-028 | Phase P5 | Priority: High`

The system SHALL present all Health Audit findings in a prioritised, categorised report card.

*Acceptance Criteria:*
- Findings are ordered: 🔴 Critical first, 🟡 Warning second, 🟢 Advisory third
- All findings are categorised by type: Code Safety, Performance, Duplication, Deprecated APIs, Hygiene, Security, Documentation
- A summary count per severity (Critical: N, Warning: N, Advisory: N) appears at the top of the report
- An environment with no findings returns a clean report card with zero counts and an explicit confirmation statement

---

### 6.4 Security & Trust

---

**FR-029 — Permission Pre-Flight Checker**  
`F-029 | Phase P2 | Priority: Critical`

The system SHALL provide a standalone endpoint (`POST /api/security/check`) that validates service account permissions before any mode is run.

*Acceptance Criteria:*
- Returns: `status` ("ready" / "degraded" / "blocked"), `safeToRun` (boolean), `passed[]`, `missing[]`, `extra[]`, `recommendation` (string)
- Response is returned within 10 seconds for any valid environment
- No mode endpoint shall proceed to environment scanning if `safeToRun` is false

---

**FR-030 — Missing Permission Detection**  
`F-030 | Phase P2 | Priority: Critical`

The system SHALL detect any required permissions absent from the service account and surface a specific, actionable remediation message.

*Acceptance Criteria:*
- Each entry in `missing[]` names the specific Dataverse entity and privilege required (e.g., "Read PluginAssembly")
- The `recommendation` string provides exact steps to resolve the missing permission
- `status` is set to "blocked" and `safeToRun` is false when any required permission is missing

---

**FR-031 — Extra Permission Detection**  
`F-031 | Phase P2 | Priority: High`

The system SHALL detect permissions present on the service account that are not required by any mode and recommend their removal.

*Acceptance Criteria:*
- Extra permissions are listed in `extra[]` with the specific entity and privilege named
- Extra permissions do not prevent execution; `safeToRun` may remain true
- The `recommendation` string explains the least-privilege rationale for removing each surplus permission

---

**FR-032 — Importable Security Role Solution**  
`F-032 | Phase P2 | Priority: Critical`

The product SHALL ship a pre-built Dataverse solution file containing the DataverseDocAgent Reader security role with precisely the required permissions and nothing more.

*Acceptance Criteria:*
- Solution imports successfully into any Dynamics 365 / Dataverse cloud environment via standard solution import
- Imported role contains exactly the permissions listed in Section 5.4 — no additions, no omissions
- Import is idempotent: re-importing does not create duplicate roles or corrupt the existing role

---

**FR-033 — In-Product Service Account Setup Guide**  
`F-033 | Phase P2 | Priority: High`

The product SHALL make the five-step service account setup guide available to customers before they are asked to provide credentials.

*Acceptance Criteria:*
- Guide covers all steps: Entra App Registration, Application User creation, security role import, role assignment, and permission checker run
- Guide is published in a form customers can access without contacting support

---

**FR-034 — Credential In-Memory-Only Guarantee**  
`F-034 | Phase P1 | Priority: Critical`

The system SHALL handle credentials exclusively in server memory and SHALL NOT write credentials to any persistent storage, log file, database, cache, or external system.

*Acceptance Criteria:*
- No credential values (client ID, client secret, tenant ID) appear in any application log — verified by log review
- No credential values are written to disk, database, or cache at any stage of the request lifecycle (see Section 5.2 for the credential flow)
- Credentials are passed as C# object parameters on the call stack and are eligible for garbage collection immediately upon request completion
- This requirement must pass code review before any paying customer connects a live environment

---

**FR-035 — Privacy Policy and Data Handling Documentation**  
`F-035 | Phase P2 | Priority: High`

The product SHALL publish a privacy policy that clearly states what data is accessed, what is transmitted, and what is never stored.

*Acceptance Criteria:*
- Policy explicitly states: only schema metadata and compiled/source code artefacts are read — never record-level CRM data
- Policy explicitly states: credentials are never stored
- Policy names the Claude API (Anthropic) as the only third party that receives environment data, and defines the scope of that transmission (schema and code only)
- Policy explicitly states: generated documents are not retained after the 24-hour download token expires

---

### 6.5 API Layer

---

**FR-036 — POST /api/document/generate**  
`F-036 | Phase P2 | Priority: Critical`

The system SHALL expose an endpoint that accepts environment credentials and returns a secure, time-limited download link for a generated `.docx` document.

*Acceptance Criteria:*
- Accepts: `environmentUrl`, `tenantId`, `clientId`, `clientSecret`
- Returns a download token on success
- Returns a structured error response (not HTTP 500) on: credential failure, permission failure, generation timeout, or internal error
- Structured error includes: `error` (human-readable string), `code` (machine-readable string), `safeToRetry` (boolean)

---

**FR-037 — POST /api/impact/analyze**  
`F-037 | Phase P4 | Priority: Critical`

The system SHALL expose an endpoint that accepts environment credentials plus a table and field name, and returns a complete field impact map.

*Acceptance Criteria:*
- Accepts: `environmentUrl`, `tenantId`, `clientId`, `clientSecret`, `tableName` (logical name), `fieldName` (logical name)
- Returns structured JSON with sections for: plugins, flows, business rules, JavaScript, views/dashboards, and risk rating
- Returns a structured 404-equivalent if the specified table or field does not exist in the environment

---

**FR-038 — POST /api/health/audit**  
`F-038 | Phase P5 | Priority: High`

The system SHALL expose an endpoint that accepts environment credentials and returns a complete health audit report card.

*Acceptance Criteria:*
- Accepts: `environmentUrl`, `tenantId`, `clientId`, `clientSecret`
- Returns structured JSON with findings grouped by severity and category
- Returns a valid, well-formed response with zero-count findings if no issues are detected

---

**FR-039 — POST /api/security/check**  
`F-039 | Phase P2 | Priority: Critical`

Specified in FR-029. The endpoint contract is defined in Section 5.5.

*Acceptance Criteria:*
- Accepts: `environmentUrl`, `tenantId`, `clientId`, `clientSecret`, `targetMode` (optional)
- Response states detailed in Section 5.5 with three example states: ready, extra permissions detected, blocked

---

**FR-040 — GET /api/download/{token}**  
`F-040 | Phase P2 | Priority: Critical`

The system SHALL expose a secure document download endpoint that returns the generated `.docx` for a valid, unexpired token.

*Acceptance Criteria:*
- Returns HTTP 200 and the `.docx` file for a valid, unexpired token
- Returns HTTP 404 for an expired or invalid token
- Token lifetime is exactly 24 hours from generation
- No authentication credential beyond the token itself is required to complete the download

---

## 7. Non-Functional Requirements

NFRs use SHALL (mandatory) language for hard requirements and SHOULD for strong recommendations. Performance targets marked **[TBD — POC]** are to be confirmed or revised following Phase 1 POC testing.

### 7.1 Performance

---

**NFR-001 — Mode 1 Document Generation Time**  
`Category: Performance`

The system SHALL complete Mode 1 document generation within the following time bounds, measured from API request receipt to download token returned:

| Environment Size | Definition | Target |
|-----------------|------------|--------|
| Typical | ≤50 custom tables, ≤20 plugins, ≤30 flows | Under 5 minutes |
| Large | >50 custom tables, or >20 plugins, or >30 flows | Under 10 minutes |

**Status: [TBD — POC]** These targets shall be baselined against real Dataverse environments during Phase 1 POC testing and confirmed or revised before Phase 2 release.

*Rationale:* The core value proposition is replacing a 2–3 day manual task. Even a 10-minute generation represents a >99% time reduction. Generation time is a key trust signal for first-time users.

---

**NFR-002 — Permission Checker Response Time**  
`Category: Performance`

`POST /api/security/check` SHALL return a complete permission report within **10 seconds** for any valid Dataverse environment.

*Rationale:* The permission checker is a synchronous pre-flight gate. Slow response creates friction at the moment users are deciding whether to trust the product.

---

**NFR-003 — Field Impact Analyser Response Time**  
`Category: Performance`

`POST /api/impact/analyze` SHALL return a complete field impact map within **60 seconds**.

*Rationale:* Mode 2 is an interactive, on-demand query. Consultants will run it during client conversations and expect a near-interactive response. 60 seconds is the outer bound for an acceptable synchronous response.

---

**NFR-004 — Health Audit Response Time**
`Category: Performance`

`POST /api/health/audit` SHALL complete a full environment health scan within **10 minutes** for any environment.

**Status: [TBD — POC]** Mode 3 scans the same environment surface as Mode 1 plus performs cross-artefact analysis (duplicate logic detection, orphan detection). This target shall be baselined during Phase 1 POC testing alongside NFR-001 and confirmed or revised before Phase 5 release.

*Rationale:* Mode 3 is the most computationally intensive operation in the product — it runs all plugin, flow, JavaScript, security role, and field-usage analysis simultaneously. An explicit ceiling is required for the Architect to make informed decisions about async job handling and timeout strategy.

---

### 7.2 Availability

---

**NFR-005 — Availability: MVP Phase (Pre-Paying Customer)**  
`Category: Availability`

During the MVP phase (P1–P2, before the first paying customer is onboarded), the API operates on Azure App Service Free Tier with **best-effort availability**. Planned off-hours downtime, cold-start latency, and Free Tier spin-down behaviour are explicitly accepted.

*Rationale:* Phase 1 targets a small cohort of known network contacts under manual delivery. The risk of downtime is low and the cost of over-engineering availability at this stage is high.

---

**NFR-006 — Availability: Post First Paying Customer**  
`Category: Availability`

Once the first paying customer is onboarded, the API SHALL target **99.5% monthly uptime**.

*Implementation trigger:* Upgrade to Azure App Service Basic Tier (or equivalent SLA-backed tier) **before** the first paid API key is issued. This upgrade is a hard prerequisite for commercial launch.

*Measurement:* Uptime is measured as the percentage of minutes in a calendar month during which the health check endpoint (`GET /api/health`) returns a 200 response.

---

### 7.3 Security

---

**NFR-007 — Credential Handling**  
`Category: Security`

Customer credentials SHALL be handled exclusively in server memory for the duration of the HTTP request and SHALL NOT persist in any form after request completion. (See also FR-034 and Section 5.2.)

*Verification:* Code review before any live customer environment is connected. Log review to confirm no credential values appear in application logs.

---

**NFR-008 — Read-Only Enforcement**  
`Category: Security`

The system SHALL issue only read-privileged operations against Dataverse across all modes and all components. Write, Append, AppendTo, Create, Delete, and Share operations SHALL NOT be used.

*Verification:* All MCP tool definitions and Dataverse SDK calls are code-reviewed for write operations before Phase 2 release.

---

**NFR-009 — Transport Security**  
`Category: Security`

All communication between API clients and the API SHALL use TLS (HTTPS). Plaintext HTTP requests SHALL be rejected or automatically redirected to HTTPS.

*Enforcement:* Configured at Azure App Service level; no application-layer handling required.

---

**NFR-010 — Third-Party Data Transmission Scope**  
`Category: Security`

The only third-party system that receives customer environment data is the Claude API (Anthropic). Data transmitted to the Claude API SHALL be limited to: schema metadata (table/field definitions, relationship definitions, security role privilege lists) and decompiled or source code artefacts (plugin C#, JavaScript, flow/workflow definitions). Record-level CRM data SHALL NOT be transmitted to any external system.

*Rationale:* Customers operating under client confidentiality obligations require a clear, documented statement of what leaves their environment and where it goes.

---

### 7.4 Scalability

---

**NFR-011 — Concurrent Request Baseline**  
`Category: Scalability`

The MVP API SHALL support a minimum of **3 concurrent document generation requests** without request failure or material performance degradation.

*Rationale:* Phase 1 manual delivery targets a small cohort of network contacts. 3 concurrent requests covers this cohort comfortably.

*Future:* Scalability targets shall be revisited during Phase 3 (Mode 2) planning and again at Phase 6 (Web UI) planning, when concurrent user projections are better defined.

---

### 7.5 Compliance

---

**NFR-012 — GDPR: Schema-Only Access Principle**  
`Category: Compliance`

DataverseDocAgent SHALL access schema metadata and code artefacts only — never record-level data. This design principle SHALL be documented in the privacy policy (FR-035) and referenced in all customer-facing security documentation.

*Implication:* Because no personal data is processed or stored, DataverseDocAgent does not act as a Data Processor under GDPR with respect to the connected environment's record data. This materially simplifies the compliance posture for European customers.

*Pre-enterprise requirement:* A full GDPR Data Processing Agreement (DPA) is out of scope for MVP. It SHALL be prepared before the first enterprise sales conversation.

---

**NFR-013 — Data Retention**  
`Category: Compliance`

Generated documents SHALL NOT be retained server-side after the 24-hour download token expires. No customer environment data (schema extracts, decompiled code, credentials) SHALL be retained between requests.

*Verification:* Code review confirms no persistent storage write path for generated documents or extracted environment data.

---

### 7.6 Reliability

---

**NFR-014 — Structured Error Responses**  
`Category: Reliability`

All API endpoints SHALL return structured JSON error responses for all failure conditions. Unhandled exceptions SHALL NOT surface raw stack traces, internal system details, or framework error pages to API consumers.

*Minimum error response structure:*
```json
{
  "error": "Human-readable description of what went wrong",
  "code": "MACHINE_READABLE_ERROR_CODE",
  "safeToRetry": true
}
```

---

### 7.7 Maintainability

---

**NFR-015 — Feature Registry Traceability**  
`Category: Maintainability`

All API endpoint implementations and agent tool call definitions SHALL include a code comment or inline annotation referencing the corresponding Feature Registry ID(s) (F-xxx).

*Rationale:* The Feature Registry is the single source of truth for build scope. Traceability from code to registry is necessary for Phase 2 feature audits, future SOC 2 preparation, and onboarding future contributors.

---

## 8. Technical Architecture

| Layer | Technology & Notes |
|-------|-------------------|
| API Backend | ASP.NET Core Web API — .NET 8, C#. API-only MVP, tested via Postman. |
| AI Layer | Claude API via Anthropic.SDK (official C# SDK). Claude acts as the autonomous agent. |
| Dataverse Access | Microsoft.PowerPlatform.Dataverse.Client for connection. Dataverse MCP for agent tool calls. |
| Plugin Analysis | dnlib — .NET library for reading and decompiling plugin DLL assemblies in-process. |
| Flow Analysis | Power Platform API for solution-aware flows. XAML deserialisation for classic workflows. |
| JavaScript Analysis | Dataverse webresource entity API. Source returned as plain text — Claude reads directly. |
| Document Output | Word .docx generated server-side. Returned as a secure time-limited download link. |
| Hosting | Azure App Service Free Tier for MVP. Upgradeable without code changes. |
| Security Role | Pre-built Dataverse solution file shipped with the product. Import in one click. |

### 8.1 Product Modes — Output Structure

**Mode 1 — Documentation Generator — 9-Section Output Document:**

| # | Section | Contents |
|---|---------|----------|
| 1 | Executive Summary | Environment name, scan date, complexity rating, total counts, key observations |
| 2 | Custom Tables | Display name, logical name, solution, AI-inferred purpose, key fields summary, record volume |
| 3 | Field Catalogue | Every custom field: data type, required level, option set values, plain-English description |
| 4 | Relationship Map | All custom relationships: type, business meaning, cascade behaviours |
| 5 | Plugin Analysis | Decompiled logic explained, fields read/written, event/stage, risk flags |
| 6 | Flow & Workflow Docs | Power Automate flows parsed; classic workflows step-by-step |
| 7 | JavaScript Analysis | Functions per form, events registered, logic explained, deprecated APIs flagged |
| 8 | Security Overview | Custom roles, key privilege differences, team configurations |
| 9 | Recommendations | AI-generated risks, performance concerns, duplicate logic, deprecated API usage |

### 8.2 API Endpoints — Complete Reference

| Method | Endpoint | Mode | FR Reference |
|--------|----------|------|-------------|
| POST | /api/security/check | Pre-flight | FR-029, FR-039 |
| POST | /api/document/generate | Mode 1 | FR-036 |
| POST | /api/impact/analyze | Mode 2 | FR-037 |
| POST | /api/health/audit | Mode 3 | FR-038 |
| GET | /api/download/{token} | All | FR-040 |

---

## 9. Build Roadmap

| Phase | Name | Deliverable | FR Coverage | Exit Gate |
|-------|------|-------------|-------------|-----------|
| P1 | POC | C# console app connects to Dataverse MCP, asks Claude to list custom tables, prints response. Credential in-memory handling verified. | FR-034 | Pipeline works end-to-end. Credential handling passes code review. |
| P2 | Mode 1 MVP | Permission checker live. Tables, fields, relationships documented. First real .docx returned. Security role solution shipped. | FR-001–003, FR-011, FR-013, FR-029–040 | First customer receives and validates a generated document. |
| P3 | Mode 1 Full | Plugin decompilation, JavaScript reading, flow parsing, recommendations section added. | FR-004–012 | Customer confirms output accurately represents 90%+ of environment components. |
| P4 | Mode 2 | Field Impact Analyser live. Complete trigger map across all logic types. | FR-014–019, FR-037 | First successful field impact query on a real environment. |
| P5 | Mode 3 | Health Audit live. Full risk scan. Prioritised report card. | FR-020–028, FR-038 | Customer confirms at least 1 Critical or Warning finding is actionable. |
| P6 | Web UI | Simple frontend. Microsoft OAuth login. Subscription billing. Report history. | FR-041–045 | 5 paying subscribers before Web UI investment begins. |

---

## 10. Business Model

### 10.1 Go-to-Market Strategy

- **Phase 1 — Manual delivery:** Run the tool personally for the first 3–5 customers. Charge per report ($500–1,500 per environment). Validate output quality before automating delivery.
- **Phase 2 — API product:** Self-serve access with subscription billing. Target D365 consultants and ISVs directly.
- **Phase 3 — Web UI:** Full self-serve product with dashboard, report history, and team management.

### 10.2 Subscription Tiers

| Plan | Price | Environments | Modes Included |
|------|-------|--------------|----------------|
| Starter | $49 / mo | 3 environments | Mode 1 only — Documentation Generator |
| Professional | $149 / mo | 10 environments | Modes 1 + 2 — Documentation + Impact Analyser |
| Agency | $399 / mo | Unlimited | All 3 modes + priority support + security role solution |

---

## 11. Success Metrics

| Phase | Metric | Target |
|-------|--------|--------|
| P1–P2 | First customer using the output | 1 consultant pays for a report within 30 days of Mode 1 MVP |
| P2 | Output quality validation | Customer confirms output would have taken 2+ days manually |
| P2 | Permission checker adoption | 100% of onboarded customers run security/check before first generation |
| P2 | Mode 1 generation time — typical | Under 5 minutes **[TBD — validate in P1 POC]** |
| P2 | Mode 1 generation time — large | Under 10 minutes **[TBD — validate in P1 POC]** |
| P2 | Permission checker response time | Under 10 seconds for any valid environment |
| P2 | Service account setup — no support needed | 100% of onboarded customers complete setup without manual assistance |
| P2 | Permission checker first-attempt success | ≥75% of setups return `status: ready` on first attempt |
| P2 | Documentation accuracy | First 3 customers confirm output accurately represents ≥90% of environment components reviewed |
| P2 | Output replaces manual work | Customer confirms document is shareable without requiring manual correction |
| P3 | Paying subscribers | 5 paying subscribers before building the web UI |
| P3 | API uptime — post first paying customer | ≥99.5% monthly — requires move to Azure App Service Basic Tier |
| P4 | Monthly Recurring Revenue | $1,000 MRR before investing in marketing |
| P5 | Health audit actionability | Customer confirms ≥1 Critical or Warning finding is actioned following a Mode 3 report |
| P5 | Net Revenue Retention | >100% — customers upgrade, not churn |
| P6 | Security objection rate | <10% of sales conversations blocked by security concerns |

---

*DataverseDocAgent v3 — BMAD Compliant — Built by a D365 developer who felt this pain firsthand.*
