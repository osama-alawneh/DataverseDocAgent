# Story 2.4: Service Account Setup Guide and Privacy Documentation

Status: done

## Story

As a D365 consultant,
I want access to a clear five-step service account setup guide and a privacy policy before I provide any credentials,
so that I can complete setup without contacting support and understand exactly what data leaves my environment.

## Acceptance Criteria

1. `docs/setup-guide.md` exists and covers all five steps from PRD Section 5.3, with exact navigation paths matching those in the PRD: (1) Entra App Registration, (2) Dataverse Application User creation, (3) Security Role Solution import, (4) Role assignment, (5) Permission Checker run.
2. The setup guide is accessible without authentication — it is a public Markdown file committed to the repository (and optionally published to a GitHub Pages or similar static site, but the repository file is the primary source).
3. `docs/privacy-policy.md` exists and explicitly states all four of the following:
   - Only schema metadata and code artefacts are accessed — never record-level CRM data (NFR-012)
   - Credentials are never stored in any form (NFR-007)
   - The Claude API (Anthropic) is the only third party that receives environment data, and the scope is schema metadata and code artefacts only (NFR-010)
   - Generated documents are not retained after the 24-hour download token expires (NFR-013)
4. Both documents use plain English — the setup guide should be followable by a consultant who has never used the product before.

## Tasks / Subtasks

- [x] Write `docs/setup-guide.md` (AC: 1, 2, 4)
  - [x] Section 1 — Prerequisites: lists what the consultant needs before starting (access to Azure portal, Power Platform Admin Centre, make.powerapps.com)
  - [x] Step 1 — Create Entra App Registration:
    - Navigate to: `portal.azure.com` → Microsoft Entra ID → App Registrations → New Registration
    - Name it: `DataverseDocAgent-Reader`
    - Supported account type: "Accounts in this organizational directory only"
    - No redirect URI required
    - Note Application (Client) ID and Directory (Tenant) ID
    - Go to Certificates & Secrets → New Client Secret → set expiry → note the value immediately (only shown once)
  - [x] Step 2 — Create Dataverse Application User:
    - Navigate to: Power Platform Admin Centre → your environment → Settings → Users → Application Users
    - New App User → select the Entra app created in Step 1
    - Do NOT assign the System Administrator role — leave with no role
  - [x] Step 3 — Import the DataverseDocAgent Security Role Solution:
    - Download `DataverseDocAgent_SecurityRole.zip` from `/artefacts/` in this repository
    - Navigate to: `make.powerapps.com` → Solutions → Import Solution → upload the file
    - Confirm "DataverseDocAgent Reader" role appears in the environment
  - [x] Step 4 — Assign the Role:
    - Return to Power Platform Admin Centre → Application Users → find the DataverseDocAgent user
    - Edit → assign "DataverseDocAgent Reader" role → Save
  - [x] Step 5 — Run the Permission Checker:
    - Call `POST /api/security/check` (include the API base URL)
    - Provide `environmentUrl`, `tenantId`, `clientId`, `clientSecret` in the request body
    - Review the response — all required permissions should appear in `passed[]`
    - Only proceed to generation if `safeToRun: true`
  - [x] Section — Troubleshooting: covers the two most common errors (wrong client secret, missing role assignment) with exact resolution steps
- [x] Write `docs/privacy-policy.md` (AC: 3, 4)
  - [x] Section — What We Access: "We access schema metadata and code artefacts only — never record-level CRM data. Specifically: [list entity types from PRD Section 5.4]"
  - [x] Section — What We Never Access: "We never access CRM records (contacts, accounts, leads, opportunities, or any other data table content). We never access user personal data stored in your CRM."
  - [x] Section — Credential Handling: "Your service account credentials are held in server memory only for the duration of the API request. They are never written to disk, database, log files, or any external system. They are discarded immediately when the request completes."
  - [x] Section — Third-Party Data Sharing: "The only third-party system that receives your environment data is the Claude API, operated by Anthropic. Data sent to the Claude API is limited to schema metadata (table and field definitions, relationship definitions, security role privilege lists) and code artefacts (decompiled plugin C#, JavaScript source, flow and workflow definitions). No record-level CRM data is ever sent to Anthropic or any other third party."
  - [x] Section — Document Retention: "Generated documentation reports are stored temporarily on our servers for up to 24 hours to allow download via a secure token. After 24 hours, the document is automatically deleted and the download link becomes invalid. No copy is retained beyond this window."
  - [x] Section — GDPR Note: "Because we access only schema metadata and code artefacts — not personal data records — DataverseDocAgent does not act as a Data Processor under GDPR with respect to your CRM environment's record data. A formal Data Processing Agreement (DPA) is available for enterprise customers on request."
- [x] Cross-check both documents against PRD acceptance criteria (AC: 1–4)
  - [x] Setup guide: verify all five steps, verify exact navigation paths match PRD Section 5.3
  - [x] Privacy policy: verify all four required statements from FR-035 are present

### Review Findings

- [x] [Review][Patch] Add caveat to retention section: "planned behaviour — active once Epic 3 (story 3.2 IDocumentStore) ships" [`docs/privacy-policy.md:76-84`] — resolved from D1:b.
- [x] [Review][Patch] Strengthen "role grants no privilege to read record-level data" with qualifier ("if the role is left unaltered — admins who also assign broader roles override this guarantee") [`docs/privacy-policy.md:40`] — resolved from D5:a.
- [x] [Review][Defer] GDPR categorical "not a Data Processor" claim [`docs/privacy-policy.md:90-92`] — deferred pending legal review (D3:c).
- [x] [Review][Dismiss] `{API_BASE_URL}` placeholder kept as-is — acceptable for pre-launch (D2:c).
- [x] [Review][Dismiss] Undefined "we" / no legal entity / no data residency — kept generic for evaluation stage (D4:c).
- [x] [Review][Patch] `Read SavedQuery` in example response must be `Read Query` — Dataverse privilege is `prvReadQuery`, code emits `"Read Query"` (`SecurityCheckService.cs:30`) [`docs/setup-guide.md:123`].
- [x] [Review][Patch] Remove `"degraded"` status reference — only `"blocked"` and `"ready"` exist in code (`SecurityCheckService.cs:185,202`) [`docs/setup-guide.md:131,152`].
- [x] [Review][Patch] Troubleshooting references HTTP 401/400 — endpoint always returns 200 with `status: "blocked"` per story-2.2 AC-5; symptoms section should key off response body, not HTTP status [`docs/setup-guide.md:139`].
- [x] [Review][Patch] Add HTTPS requirement for `environmentUrl` — `SecurityCheckRequest.cs:12` regex rejects `http://` [`docs/setup-guide.md:104`].
- [x] [Review][Patch] Add GUID-format requirement for `tenantId` and `clientId` — `SecurityCheckRequest.cs:16,20` validate GUID regex; non-GUID values yield opaque 400 [`docs/setup-guide.md:105-106`].
- [x] [Review][Patch] Add timeout troubleshooting — `SecurityCheckService.cs` enforces a request-level cap; current guide documents no recovery path [`docs/setup-guide.md:135-162`].
- [x] [Review][Patch] Explain SharePoint DMI known-harmless extras in response example — real environments return extras per `SecurityCheckService.cs` `KnownHarmlessExtraPrivileges` whitelist; current example shows empty `extra[]` [`docs/setup-guide.md:119-128`].
- [x] [Review][Patch] Add troubleshooting for `extra[]` populated but `safeToRun: true` — recommendation wording advises removal but does not block [`docs/setup-guide.md:131`].
- [x] [Review][Patch] Summary-table row "Schema metadata and code artefacts | Read during active request" understates Claude API transmission; body says data is sent to Anthropic [`docs/privacy-policy.md:100` vs `63-68`].
- [x] [Review][Patch] Reconcile "managed" (setup guide) vs "unmanaged" (`artefacts/README.md:5`) solution wording — zip filename is `_managed.zip`; README is stale [`artefacts/README.md:5`].
- [x] [Review][Defer] `credentials.TenantId` validated by GUID regex but silently ignored by `DataverseConnectionFactory.ConnectAsync` [`src/DataverseDocAgent.Api/Dataverse/DataverseConnectionFactory.cs:26-30`] — deferred, pre-existing.
- [x] [Review][Defer] `ExceptionHandlingMiddleware` request-body logging behaviour on unhandled error unverified — deferred, pre-existing middleware concern, not caused by this story.
- [x] [Review][Defer] `POST /api/security/check` error responses may echo parts of the credential via SDK exception messages — deferred, pre-existing credential-leak audit item.

## Dev Notes

- Both files are static Markdown documentation — no code implementation required for this story.
- The setup guide is the primary onboarding artefact. Its quality directly affects the "100% of onboarded customers complete setup without manual assistance" success metric in PRD Section 11.
- The permission checker endpoint URL in the setup guide should reference the production Azure App Service URL once deployed, or use a placeholder `{API_BASE_URL}` with an explanation.
- Keep the privacy policy plain and specific. Avoid legal boilerplate that obscures the key facts consultants need to trust the tool.

### Project Structure Notes

Files created:
- `docs/setup-guide.md`
- `docs/privacy-policy.md`

### References

- [Source: docs/prd.md#53-service-account-setup-guide] — exact five-step content
- [Source: docs/prd.md#functional-requirements — FR-033] — setup guide acceptance criteria
- [Source: docs/prd.md#functional-requirements — FR-035] — privacy policy required statements
- [Source: docs/prd.md#7-non-functional-requirements — NFR-007, NFR-010, NFR-012, NFR-013] — data handling NFRs
- [Source: docs/prd.md#11-success-metrics] — "100% of onboarded customers complete setup without manual assistance"

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

No runtime debugging required — story is documentation-only.

### Completion Notes List

- Authored `docs/setup-guide.md` with Prerequisites + 5-step flow + Troubleshooting. Navigation paths mirror PRD §5.3 exactly; newer Admin Centre "Users + permissions" variant is called out in a parenthetical so either UI layout is usable.
- Step 5 uses `{API_BASE_URL}` placeholder per Dev Notes guidance; a representative `passed[]` example sourced from PRD §5.4 (12 privileges) is included so the consultant can visually match the success response.
- Solution filename in the guide matches the committed artefact: `DataverseDocAgentSecurityRole_1_0_0_6_managed.zip` (not the older `DataverseDocAgent_SecurityRole.zip` stub in the story draft).
- Authored `docs/privacy-policy.md` covering all four FR-035 required statements (NFR-012 schema-only, NFR-007 credentials not stored, NFR-010 Claude API only with defined scope, NFR-013 24-hour document retention) plus GDPR positioning and a summary table.
- Both docs are plain English, no legal boilerplate, followable by a first-time consultant — satisfies AC-4 and PRD §11 success metric intent ("100% of onboarded customers complete setup without manual assistance").
- Revocation guidance added to setup guide (disable/delete App Registration) — zero-cost addition that reinforces customer trust posture.

### File List

- `docs/setup-guide.md` (new)
- `docs/privacy-policy.md` (new)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (modified — status transition to `review`)
- `_bmad-output/implementation-artifacts/story-2.4-setup-guide-privacy.md` (modified — story progress record)

## Change Log

| Date | Change | Author |
|------|--------|--------|
| 2026-04-17 | Initial implementation: created `docs/setup-guide.md` and `docs/privacy-policy.md`; marked story review. | Dev Agent (claude-opus-4-7) |
| 2026-04-17 | Code review: applied 12 patches (SavedQuery→Query, degraded removal, HTTP 200/body-status fix, HTTPS/GUID field notes, timeout + extras troubleshooting, retention caveat, role-grants qualifier, summary-table row, artefacts/README reconciliation). 3 items deferred (tenantId ignored, middleware log audit, auth-error echo) + 1 from D3 (GDPR categorical claim — legal review). 5 items dismissed. Status → done. | Reviewer (claude-opus-4-7) |
