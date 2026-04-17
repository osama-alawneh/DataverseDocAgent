# Story 2.4: Service Account Setup Guide and Privacy Documentation

Status: ready-for-dev

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

- [ ] Write `docs/setup-guide.md` (AC: 1, 2, 4)
  - [ ] Section 1 — Prerequisites: lists what the consultant needs before starting (access to Azure portal, Power Platform Admin Centre, make.powerapps.com)
  - [ ] Step 1 — Create Entra App Registration:
    - Navigate to: `portal.azure.com` → Microsoft Entra ID → App Registrations → New Registration
    - Name it: `DataverseDocAgent-Reader`
    - Supported account type: "Accounts in this organizational directory only"
    - No redirect URI required
    - Note Application (Client) ID and Directory (Tenant) ID
    - Go to Certificates & Secrets → New Client Secret → set expiry → note the value immediately (only shown once)
  - [ ] Step 2 — Create Dataverse Application User:
    - Navigate to: Power Platform Admin Centre → your environment → Settings → Users → Application Users
    - New App User → select the Entra app created in Step 1
    - Do NOT assign the System Administrator role — leave with no role
  - [ ] Step 3 — Import the DataverseDocAgent Security Role Solution:
    - Download `DataverseDocAgent_SecurityRole.zip` from `/artefacts/` in this repository
    - Navigate to: `make.powerapps.com` → Solutions → Import Solution → upload the file
    - Confirm "DataverseDocAgent Reader" role appears in the environment
  - [ ] Step 4 — Assign the Role:
    - Return to Power Platform Admin Centre → Application Users → find the DataverseDocAgent user
    - Edit → assign "DataverseDocAgent Reader" role → Save
  - [ ] Step 5 — Run the Permission Checker:
    - Call `POST /api/security/check` (include the API base URL)
    - Provide `environmentUrl`, `tenantId`, `clientId`, `clientSecret` in the request body
    - Review the response — all required permissions should appear in `passed[]`
    - Only proceed to generation if `safeToRun: true`
  - [ ] Section — Troubleshooting: covers the two most common errors (wrong client secret, missing role assignment) with exact resolution steps
- [ ] Write `docs/privacy-policy.md` (AC: 3, 4)
  - [ ] Section — What We Access: "We access schema metadata and code artefacts only — never record-level CRM data. Specifically: [list entity types from PRD Section 5.4]"
  - [ ] Section — What We Never Access: "We never access CRM records (contacts, accounts, leads, opportunities, or any other data table content). We never access user personal data stored in your CRM."
  - [ ] Section — Credential Handling: "Your service account credentials are held in server memory only for the duration of the API request. They are never written to disk, database, log files, or any external system. They are discarded immediately when the request completes."
  - [ ] Section — Third-Party Data Sharing: "The only third-party system that receives your environment data is the Claude API, operated by Anthropic. Data sent to the Claude API is limited to schema metadata (table and field definitions, relationship definitions, security role privilege lists) and code artefacts (decompiled plugin C#, JavaScript source, flow and workflow definitions). No record-level CRM data is ever sent to Anthropic or any other third party."
  - [ ] Section — Document Retention: "Generated documentation reports are stored temporarily on our servers for up to 24 hours to allow download via a secure token. After 24 hours, the document is automatically deleted and the download link becomes invalid. No copy is retained beyond this window."
  - [ ] Section — GDPR Note: "Because we access only schema metadata and code artefacts — not personal data records — DataverseDocAgent does not act as a Data Processor under GDPR with respect to your CRM environment's record data. A formal Data Processing Agreement (DPA) is available for enterprise customers on request."
- [ ] Cross-check both documents against PRD acceptance criteria (AC: 1–4)
  - [ ] Setup guide: verify all five steps, verify exact navigation paths match PRD Section 5.3
  - [ ] Privacy policy: verify all four required statements from FR-035 are present

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

### Completion Notes List

### File List
