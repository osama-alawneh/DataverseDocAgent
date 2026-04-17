# DataverseDocAgent — Privacy Policy and Data Handling

**Audience:** D365 consultants, Power Platform administrators, and data protection officers evaluating whether to connect a Dataverse environment to DataverseDocAgent.
**Purpose:** State clearly, in plain English, what data DataverseDocAgent accesses, what it transmits, and what it never stores — before you provide any credentials.

This policy is the canonical statement of our data handling behaviour. The technical enforcement of everything below is described in the architecture and setup documentation.

---

## What We Access

We access **schema metadata** and **code artefacts** only — never record-level CRM data.

Specifically, DataverseDocAgent reads the following from your Dataverse environment:

- **Table (entity) definitions** — names, descriptions, ownership type, and publisher information.
- **Field (attribute) definitions** — names, data types, option sets, and descriptions.
- **Relationship definitions** — 1:N and N:N relationships between tables.
- **Plugin assemblies** — compiled plugin DLLs (for decompilation and analysis).
- **Plugin types and registration steps** — class names and the events each plugin runs on.
- **Web resources** — JavaScript source code registered in the environment.
- **Workflows and flows** — workflow and Power Automate flow definitions.
- **Security roles** — role names and the privilege lists assigned to each role.
- **System forms** — form definitions used to map JavaScript to form events.
- **Saved queries (views)** — view definitions, used for field impact analysis.
- **Organisation metadata** — basic environment information (name, version) for report headers.

The exact list of Dataverse privileges required to read the above is published in the setup guide and enforced by the `DataverseDocAgent Reader` security role — no additional privileges are requested or used.

---

## What We Never Access

We never access **record-level CRM data**. This includes, but is not limited to:

- Contacts, accounts, leads, opportunities, cases, activities, or any other record in a data table.
- Any table content that holds business data or personal data belonging to your users or customers.
- System and user email, chat, or file attachment content stored within Dataverse.

We never access personal data records stored in your CRM. This is a design principle, not a configuration toggle — the `DataverseDocAgent Reader` role we ship, when left unaltered, grants no privilege to read record-level data in any data table. If your administrators also assign broader roles (for example, System Administrator) to the service account, those additional privileges override the least-privilege guarantee from our side. The permission checker described in the setup guide will flag any such additions in its `extra[]` list so you can review and remove them.

---

## Credential Handling

Your service account credentials — environment URL, tenant ID, client ID, and client secret — are held in **server memory only** for the duration of the API request.

They are **never** written to:

- Disk, including temporary files and swap space.
- Database, cache, or any other persistent data store.
- Log files, application traces, diagnostic output, or telemetry.
- Any external system, including the Claude API.

Credentials are discarded as soon as the request completes. They are not reachable after the response is returned. This behaviour is verified by code review before any customer environment is connected and is enforced by the request-scoped credential flow described in the architecture documentation.

---

## Third-Party Data Sharing

The only third-party system that receives your environment data is the **Claude API**, operated by **Anthropic**.

Data sent to the Claude API is limited to:

- **Schema metadata** — table and field definitions, relationship definitions, and security role privilege lists.
- **Code artefacts** — decompiled plugin C# source, JavaScript source from web resources, and flow/workflow definitions.

**No record-level CRM data is ever sent to Anthropic or any other third party.** No credentials are ever sent to Anthropic. We do not share your environment data with advertising networks, analytics providers, or any other external service.

Anthropic's handling of data sent to the Claude API is governed by Anthropic's own terms and privacy policy. We recommend reviewing those if your organisation has specific cross-provider data processing requirements.

---

## Document Retention

> **Note:** The 24-hour retention behaviour described here becomes active once the document generation endpoint ships (Epic 3, story 3.2 — *IDocumentStore*). Until then, no generated documents exist and this section describes the committed future behaviour rather than behaviour the current service already performs.

Generated documentation reports are stored temporarily on our servers for **up to 24 hours** to allow you to download them via a secure, time-limited token.

After 24 hours:

- The generated document is **automatically deleted** from our servers.
- The download token is invalidated — the download link will no longer work.
- No copy of the generated document is retained beyond this window.

We do not retain extracted schema, decompiled code, or any other intermediate data between requests. Each documentation request starts from zero.

---

## GDPR Note

Because DataverseDocAgent accesses only schema metadata and code artefacts — **not personal data records** — we do not act as a **Data Processor** under GDPR with respect to your CRM environment's record-level data. The tool does not read, store, or transmit the personal data held in your Dataverse tables.

For enterprise customers with specific contractual requirements, a formal **Data Processing Agreement (DPA)** is available on request. Contact your DataverseDocAgent point of contact to request one.

---

## Summary

| Category | Our Behaviour |
|----------|--------------|
| Schema metadata and code artefacts | Read from your environment during the active request and sent to the Claude API for analysis |
| Record-level CRM data | Never accessed |
| Credentials | In-memory only, discarded on request completion |
| Third-party transmission | Claude API (Anthropic) only, schema and code scope |
| Generated documents | Retained up to 24 hours, then auto-deleted |
| Record-level data to any third party | Never transmitted |

If any statement above is unclear or you need further detail before connecting an environment, raise the question with your DataverseDocAgent point of contact before providing credentials.
