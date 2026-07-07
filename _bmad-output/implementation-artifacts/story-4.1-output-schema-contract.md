# Story 4.1: Mode 1 Output Schema Contract

Status: ready-for-dev

> **Gate story.** First Epic 4 story (execution order 4.1 → 4.2 → 4.12 → 4.3 → …, per planning review 2026-07-07). `docs/output-schema-mode1.json` is the ADR-006 prerequisite deliverable that every downstream Mode 1 output story (4.2–4.12) depends on. Architecture gap G1 closes here.

## Story

As a developer,
I want a formally defined and versioned JSON Schema for all Mode 1 Claude output, with schema validation enforced in the orchestrator,
So that every downstream story can rely on a stable, machine-validated contract and malformed AI output never reaches `DocxBuilder`.

## Acceptance Criteria

1. `docs/output-schema-mode1.json` exists in the repository as a versioned JSON Schema document.
2. The schema defines all Mode 1 output sections: `executive_summary`, `publisher_prefix_summary`, `tables[]`, `fields[]`, `relationships[]`, `plugins[]`, `flows[]`, `workflows[]`, `javascript[]`, `business_rules[]`, `security_roles[]`, `app_users[]`, `recommendations[]`, `top_risks[]`.
3. Every finding, explanation, and recommendation object in the schema has a mandatory `confidence` field with enum values `"VERIFIED"` | `"INFERRED"` | `"ESTIMATED"` (uppercase strings, no brackets — architecture.md JSON-side convention).
4. When the orchestration pipeline receives Claude's Mode 1 response, `JsonSchema.Net` validates the response against `output-schema-mode1.json` **before** the response is passed to `DocxBuilder`.
5. If validation fails, the job is marked `Failed` with `code: "OUTPUT_SCHEMA_VIOLATION"`, `safeToRetry: true`, and a log entry naming the schema path(s) that failed — no credential data and no raw instance values in the log.
6. `DocxBuilder` is never called with an unvalidated response.
7. The schema carries a `$schema` version field and the orchestrator references the schema file by path — no hardcoded schema strings in C# code.
8. **Live probe (retro 2026-07-07, action A2)** — before `review → done`, both parts executed and results recorded in this file's Dev Agent Record:
   - **(a) Reject path, free:** a deliberately malformed variant (missing required key; and a target-section object missing `confidence`) replayed locally through the validator fails with `OUTPUT_SCHEMA_VIOLATION` semantics.
   - **(b) Accept path, live Anthropic (~$0.01–0.05):** one minimal live Claude call through `AgentOrchestrator.RunAsync` (no Dataverse tools; prompt instructs a tiny JSON object conforming to the transitional required keys), response fed through the new validation gate, passes. Same model as production (`AnthropicModels.Claude46Sonnet`). No full Mode 1 pipeline run required — Dataverse is not this story's surface.
9. Existing test suite (296 green at story start) stays green; new tests added per Testing Requirements below.

## Tasks / Subtasks

- [ ] **Author `docs/output-schema-mode1.json`** (AC: 1, 2, 3, 7)
  - [ ] JSON Schema **draft 2020-12** (`"$schema": "https://json-schema.org/draft/2020-12/schema"`), `$id` carrying a semver (e.g. `.../output-schema-mode1/1.0.0`), plus a top-level `"version": "1.0.0"` annotation property
  - [ ] Root object with **two key groups** (see Dev Notes — Transitional Contract Strategy):
    - **Transitional Epic 3 keys (required at v1.0.0):** `organisation`, `tables`, `fields`, `relationships`, `applicationUsers`, `keyObservations` — shapes copied exactly from the shipped contract in `PromptBuilder.cs:40-73` (post-R-HF-10 slim shapes: field = `{logicalName, displayName, attributeType, requiredLevel, description}`; relationship = `{schemaName, relationshipType, relatedEntity, cascadeDelete, businessMeaning}`)
    - **Target Mode 1 Full sections (optional at v1.0.0):** the 14 sections from AC-2, each fully defined in `$defs` with mandatory `confidence` on every finding/explanation/recommendation object; `recommendations[]` items carry the ADR-006 severity-tiered shape (`severity`, `category`, `entityName`, `what`, `whyProblem`, `consequence`, `howToFix`, `estimatedEffort`, `confidence`); `top_risks[]` items the FR-012 5-part shape
  - [ ] Root `"additionalProperties": false` — unknown keys are prompt drift and must fail loudly
  - [ ] One `$defs` entry per section so Story 4.12 can compose per-pass subsets by `$ref`
  - [ ] Section keys snake_case exactly as listed in AC-2; **field names inside objects camelCase** (architecture.md:663 convention — see Dev Notes)
- [ ] **Add `JsonSchema.Net` 9.2.2 to `DataverseDocAgent.Api.csproj`** (AC: 4)
- [ ] **Implement `OutputSchemaValidator`** (AC: 4, 5, 7)
  - [ ] New file `src/DataverseDocAgent.Api/Agent/OutputSchemaValidator.cs`: interface `IOutputSchemaValidator` + sealed implementation, registered as DI **singleton** in `Program.cs`
  - [ ] Loads the schema **once** (lazy) from a file path resolved against `AppContext.BaseDirectory`; schema file delivered to output via a `<Content Include="..\..\docs\output-schema-mode1.json" Link="docs\output-schema-mode1.json" CopyToOutputDirectory="PreserveNewest" />` item in `DataverseDocAgent.Api.csproj` — no schema string in C#
  - [ ] `Validate(JsonNode instance)` returns pass/fail + bounded failure detail: at most 10 `(instanceLocation, evaluationPath/keyword)` pairs from `EvaluationResults` (use `OutputFormat.List`); **never** include instance values in the detail (NFR-007 discipline)
- [ ] **Wire the gate into `DocumentGenerateService.RunPipelineAsync`** (AC: 4, 5, 6)
  - [ ] Order: `rawResponse` → existing `StripCodeFences` + `TrimToJsonObject` → parse to `JsonNode` → **`OutputSchemaValidator.Validate`** → only then deserialize `AgentJsonModel` and proceed to `DocxBuilder.Build`
  - [ ] On failure: throw `GenerationFailureException(JobFailureCodes.OutputSchemaViolation, safeToRetry: true, "Claude output failed Mode 1 schema validation.")` and log the bounded failure paths via `_logger.LogWarning` (pattern-match the existing R-HF-4 bounded-forensics style; reuse `TruncateForLog` only if a raw snippet is genuinely needed — prefer paths-only)
  - [ ] Add `public const string OutputSchemaViolation = "OUTPUT_SCHEMA_VIOLATION";` to `JobFailureCodes` (public API contract — document in the XML doc like the existing codes)
- [ ] **Tests** (AC: 5, 6, 9 — see Testing Requirements)
- [ ] **Live probe + record results** (AC: 8)
- [ ] **Build + test gate:** `dotnet build -nologo -v q` zero warnings/errors; `dotnet test tests/DataverseDocAgent.Tests --no-build --nologo -v q` all green

## Dev Notes

### Transitional Contract Strategy — the load-bearing design decision

The AC-2 section list is the **target** Mode 1 Full shape. But this story lands **before** the tools/prompt that produce those sections (4.4–4.9), and the prompt is untouched here (Story 4.9 owns prompt redesign). Today's pipeline emits the Epic 3 shape (`organisation`/`tables`/`fields`/`relationships`/`applicationUsers`/`keyObservations` per `PromptBuilder.cs:40-73`). If v1.0.0 required the target sections, every Mode 1 run would fail from day one; if the gate were stubbed off until 4.9, we'd repeat the Epic 3 retro failure ("gate on paper, not in execution" — retro §3, Epic 2 P1).

**Decision: schema v1.0.0 formalizes BOTH.** Transitional keys required (validates the real, shipped contract immediately — the gate is live and meaningful from this story onward); target sections defined-but-optional (the contract downstream stories code against). Stories 4.9/4.11 flip target sections to required per ADR-008 pass and retire the transitional keys via a schema version bump — AC-7's versioning exists precisely for this. Do not soften this into `additionalProperties: true` at the root: strict root is what makes the gate catch prompt drift (e.g. a stale model emitting dropped R-HF-10 keys).

### Naming convention split (do not "fix" this)

- **Section keys:** snake_case (`executive_summary`, `top_risks`, …) — exactly as the epic ACs and Story 4.11 ACs spell them.
- **Object field names:** camelCase (`entityName`, `whyProblem`, `howToFix`, …) — architecture.md:663 mandates camelCase for all output-schema fields, consistent with `System.Text.Json` defaults and `StructuredErrorResponse`.
- **Confidence values:** `"VERIFIED"` / `"INFERRED"` / `"ESTIMATED"` — uppercase, no brackets (architecture.md:657; the bracketed `[VERIFIED]` form is docx-rendering, not JSON).
- Transitional keys stay exactly as shipped (`organisation` — British spelling, `applicationUsers`, `keyObservations`).

### Where the gate lives

`AgentOrchestrator.RunAsync` returns a raw string and knows nothing about Mode 1 shapes — keep it that way (Story 4.12 restructures it; don't pre-empt). The gate belongs in `DocumentGenerateService.RunPipelineAsync` (src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs:172-281), after the R-HF-5 trim helpers and **before** `JsonSerializer.Deserialize<AgentJsonModel>`. Rationale: validating the `JsonNode` before typed deserialization means a schema failure produces `OUTPUT_SCHEMA_VIOLATION` with named paths instead of an opaque `AI_ERROR (JsonException)` — strictly better forensics than the current path. The existing `ParseAgentJson` JsonException fallback stays as defence-in-depth for non-JSON input the trim helpers can't rescue.

### Existing failure-code machinery (reuse, don't reinvent)

- `GenerationFailureException(code, safeToRetry, message, inner?)` → `GenerationBackgroundService` translates to structured job records (NFR-014). New code slots in with zero background-service changes.
- `JobFailureCodes` (src/DataverseDocAgent.Api/Jobs/JobFailureCodes.cs) — string constants are public API contract; add, never rename.
- Catch-ordering in `RunAsync` (DocumentGenerateService.cs:82-154): `GenerationFailureException` is already caught and re-thrown with its own code *before* the generic `AI_ERROR` catch — the new exception flows through untouched. Do not add a new catch block.

### Library

`JsonSchema.Net` **9.2.2** (json-everything; net8.0 + netstandard2.0; released 2026-06-14). Pin exactly, like every other package in the csproj. Use **draft 2020-12** — do not target the in-development "v1/2026" schema dialect. Core API: `JsonSchema.FromFile(path)` (or `FromText` on file contents read once), `schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List })`, then walk `result.Details` where `IsValid == false` for `InstanceLocation` / `EvaluationPath`. ADR-006 names this library explicitly.

### NFR-007 (credential hygiene) in this story

The validated instance is Claude output (public Dataverse metadata + AI prose) — no credentials by construction (see the R-HF-4 comment block at DocumentGenerateService.cs:202-209). Discipline anyway: log **schema paths and keywords only**, never instance values; fixed-string exception message; bounded list (≤10 entries). This mirrors the sanitised-failure-message pattern from Stories 3.1/3.4/3.7.

### Forward-compat for Story 4.12 (do not implement, do not block)

4.12 will validate **per-pass section subsets** (ADR-008: Pass 1 = executive sections, Pass 2 = technical sections). Structure the schema so each section is a standalone `$defs` entry referenced from the root — 4.12 then composes subset schemas by `$ref` without touching v1.0.0 definitions. That is this story's only obligation to 4.12.

### Testing Requirements

Test project: `tests/DataverseDocAgent.Tests` (xUnit; 296 green at story start). Follow the negative-assertion culture (R-HF-10 pattern: assert dropped/forbidden things NEVER appear, with a positive anchor assertion so a total regression fails noisily). New tests:

1. **Schema document tests:** file exists at repo `docs/`; parses as valid draft 2020-12; `$id`/version present; every AC-2 section key defined; every finding/recommendation `$defs` object lists `confidence` in `required` with the exact 3-value enum (walk the schema JSON — this pins AC-3 structurally).
2. **Validator accept:** a golden Epic-3-shape sample (build from the `PromptBuilder` contract; include slim field/relationship shapes) passes. A sample with target sections populated (well-formed `recommendations[]` + `top_risks[]` items) passes.
3. **Validator reject:** missing required transitional key fails; unknown root key fails (`additionalProperties: false` pinned); target-section object missing `confidence` fails; wrong enum casing (`"verified"`) fails. Assert failure detail contains instance paths and NO instance values.
4. **Pipeline gate:** `DocumentGenerateService` with a stubbed orchestrator (`Func<AgentOrchestrator>` delegate seam already exists) returning invalid JSON → `GenerationFailureException` with `Code == "OUTPUT_SCHEMA_VIOLATION"`, `SafeToRetry == true`, and the document store never receives a blob (AC-6 proxy: `DocxBuilder.Build` is static — assert via `IDocumentStore` mock receiving zero calls).
5. **Bounded logging:** >10 schema violations → detail capped at 10.

### Previous Story Intelligence (R-HF-10 mini-story + Epic 3 retro)

- **Contract source of truth is `PromptBuilder.cs`** — the R-HF-10 slim narrowed Story 3.4 AC-2/AC-3; the transitional schema must encode the *slim* shapes, not the pre-slim ones. Team agreement: no re-introduction of dropped keys (`options[]`, cascade quad, `referencingEntity`/`referencedEntity`).
- **`businessMeaning` and `purpose`** are AI-synthesised at final-JSON time (not tool-emitted) — they ARE in the output contract; keep them in the transitional relationship/table shapes (nullable).
- **Null-tolerance precedent:** parse boundary filters null array entries (Story 3.5 P3 / 3.6 P2 / 3.7 patterns in `RunPipelineAsync`). Schema should allow `null` items? No — schema rejects `[null, …]` (type violation) which *supersedes* the null-filters for schema-validated runs; keep the C# filters as defence-in-depth, do not remove them.
- **294→296 suite runs on every Mode 1 pipeline change** (team agreement, commit 193f259 negative-assertion net).
- **Review discipline:** expect a 3-layer `bmad-code-review` pass before done; patch counts on pipeline-central stories ran 12–16 — write defensively up front.
- **Static-helper-for-testability** pattern (3.0/3.1) and `required` init slots (3.6/3.7) are house style.

### Project Structure Notes

- New: `docs/output-schema-mode1.json`, `src/DataverseDocAgent.Api/Agent/OutputSchemaValidator.cs`, tests file(s).
- Modified: `DataverseDocAgent.Api.csproj` (package + Content item), `Program.cs` (one DI line), `DocumentGenerateService.cs` (gate wiring), `JobFailureCodes.cs` (one constant).
- NOT touched: `AgentOrchestrator.cs`, `PromptBuilder.cs`, `DocxBuilder.cs`, any tool, `HttpClient` config, iteration caps. If you find yourself editing those, stop — scope creep into 4.9/4.12 territory.
- Annotation convention: lead new files with `// F-xxx — <purpose> (Story 4.1)` matching house style; this story's traceability: **ADR-006 — NFR-017, FR-045 (schema portion)**.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` — Story 4.1 + Epic 4 planning-review header]
- [Source: `_bmad-output/planning-artifacts/architecture.md:461-517` — ADR-006 (decision, example shape, enforcement mechanism, rejected alternatives); `:555-585` ADR-008 (pass subsets); `:645-665` naming conventions; `:876-888` gaps G1/G3]
- [Source: `src/DataverseDocAgent.Api/Agent/PromptBuilder.cs:40-97` — shipped Mode 1 JSON contract (transitional-key shapes)]
- [Source: `src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs:172-342` — gate wiring point, `ParseAgentJson`, trim helpers, catch ordering]
- [Source: `src/DataverseDocAgent.Api/Jobs/JobFailureCodes.cs` — failure-code contract]
- [Source: `_bmad-output/implementation-artifacts/r-hf-10-tool-payload-slim.md` — slim contract + review-pattern precedent]
- [Source: `_bmad-output/implementation-artifacts/epic-3-retro-2026-07-07.md` — A2 live-probe gate, "gate on paper" lesson, team agreements]
- [Source: nuget.org/packages/JsonSchema.Net — 9.2.2, net8.0, 2026-06-14]

## Dev Agent Record

### Agent Model Used

(story creation: claude-fable-5 via bmad-create-story, 2026-07-07)

### Debug Log References

### Live Probe Results (AC-8 — fill before review → done)

- Reject path (local replay):
- Accept path (live Anthropic call):

### Completion Notes List

### File List
