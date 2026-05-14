# Story 3.6: Publisher Prefix Intelligence

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a D365 consultant,
I want the Mode 1 document to identify all solution publisher prefixes and tell me which is the client's customisation prefix,
so that I can immediately distinguish client-built tables from Microsoft tables on day one without reading through every table name.

## Acceptance Criteria

1. After the Mode 1 agent loop produces its structured JSON, a deterministic `PrefixAnalyzer` (C# host-side, not Claude) extracts the publisher prefix from each `tables[].logicalName` (segment before the first underscore — e.g. `vel_account` → `vel`) and computes per-prefix component counts across the full custom-table set returned by `list_custom_tables`.
2. The analyzer classifies each prefix into exactly one of three buckets: **Microsoft** (prefix matches a known Microsoft pattern — exact-string membership of `{ "msdyn_", "msft_", "adx_", "cr" }`, with `cr` also matching `crXXXX_` Power-Apps default-environment prefixes via the regex shape `cr[a-z0-9]+_`), **Client/ISV** (everything else), or **Unprefixed** (logical names that contain no underscore — guarded against, treated as a special bucket so the analyzer never emits an empty-string prefix). Prefix matching is case-sensitive against the lowercase logical name (Dataverse normalises logical names to lowercase).
3. The analyzer identifies the **primary client prefix** as the Client/ISV-bucket prefix with the highest component count. Ties are broken alphabetically (lowercase) so the output is deterministic across runs. If the Client/ISV bucket is empty, the primary client prefix is `null` and a flag `noClientPrefixDetected` is set on the summary.
4. The new `PublisherPrefixSummary` object carries: `primaryClientPrefix` (string or null), `microsoftPrefixes` (array of `{ prefix, componentCount }`), `clientPrefixes` (array of `{ prefix, componentCount }` sorted descending by count), `noClientPrefixDetected` (bool). The summary is computed in `DocumentGenerateService` immediately after `ComplexityRater.Rate` and is injected into the `GeneratedDocumentModel` consumed by `DocxBuilder`. Claude is never asked to produce this data — FR-011 deterministic-rating discipline applies (the spec mirror of complexity rating, FR-042).
5. `DocxBuilder` renders a new sub-section **"Publisher Prefix Summary"** under Section 1 (Executive Summary), placed immediately after the counts table and before the key observations bullets. The sub-section opens with one of these three deterministic sentences (chosen by the analyzer output, not by Claude):
   - **Single client prefix:** `"All client customisations use the prefix '{primary}_'. Microsoft components use {msdyn_, msft_, …}. {No third-party ISV components detected. | n additional non-Microsoft prefixes detected — see breakdown below.}"`
   - **No client prefix:** `"No client-defined publisher prefix detected — all custom components use default or Microsoft prefixes."`
   - **Multiple client prefixes (≥2 in Client/ISV bucket):** `"Multiple custom prefixes detected — environment may have multiple development teams or migration history. Primary client prefix: '{primary}_' ({primaryCount} components). See full breakdown below."`
6. Below the summary sentence, the section renders a two-column table `Prefix | Component count` listing every prefix found (Microsoft + Client/ISV), sorted by bucket then descending by count. Each row's `Prefix` cell carries the trailing underscore for legibility (`vel_`, `msdyn_`); the special `Unprefixed` bucket row, if any, is labelled exactly `"(no prefix)"` without an underscore.
7. The summary survives the existing failure modes from Story 3.5: if `tables` is empty the section is omitted (no header rendered); if the agent JSON parse threw `AI_ERROR` the prefix work is unreachable (already covered by the existing error path); the `GetOrganisationMetadataTool` failure or any per-table tool failure does not block prefix analysis because the analyzer operates on whatever subset of tables Claude returned.
8. Unit tests cover: the underscore-split rule including a logical name with multiple underscores (`new_my_widget` → `new`); the Microsoft-pattern recogniser including `cr` and three randomly-shaped `crXXXX_` prefixes; the tie-break-on-alphabetical rule; the empty-Client/ISV-bucket path; the `Unprefixed` bucket path; an end-to-end `DocxBuilder` test asserts the sub-section renders the correct sentence variant for each of the three branches in AC-5 and that the breakdown table is sorted as specified.

## Tasks / Subtasks

- [ ] Add `PublisherPrefixSummary` to the document model (AC: 4)
  - [ ] Create `src/DataverseDocAgent.Api/Documents/PublisherPrefixSummary.cs`
    - Properties: `string? PrimaryClientPrefix`, `IReadOnlyList<PrefixCount> MicrosoftPrefixes`, `IReadOnlyList<PrefixCount> ClientPrefixes`, `IReadOnlyList<PrefixCount> UnprefixedTables` (typically count 0 or 1 bucket), `bool NoClientPrefixDetected`.
    - Sub-record: `public sealed record PrefixCount(string Prefix, int ComponentCount)`.
  - [ ] Extend `ExecutiveSummary` (`GeneratedDocumentModel.cs`) with a required `PublisherPrefixSummary PrefixSummary { get; init; }` slot. Required so the analyzer cannot be silently skipped.
- [ ] Implement `PrefixAnalyzer` (AC: 1, 2, 3)
  - [ ] Create `src/DataverseDocAgent.Api/Features/DocumentGenerate/PrefixAnalyzer.cs`
  - [ ] Static method: `PublisherPrefixSummary Analyze(IReadOnlyList<TableInfo> tables)`
  - [ ] Microsoft-pattern matcher: exact membership of `{ "msdyn_", "msft_", "adx_" }` OR regex `^cr([a-z0-9]+_)?$` against the **prefix segment + underscore** (i.e. compare `"cr_"` and `"crXXXX_"` shapes). Document the regex inline.
  - [ ] Tie-break primary client prefix alphabetically on the **lowercase prefix string**, not on the raw component (Dataverse already lowercases, but the comment should pin this).
  - [ ] Annotate file header: `// F-047 — FR-042 — Publisher Prefix Intelligence (Story 3.6)`
- [ ] Wire analyzer into `DocumentGenerateService` (AC: 4)
  - [ ] In `RunPipelineAsync`, immediately after `ComplexityRater.Rate` and before `DocxBuilder.Build`, call `PrefixAnalyzer.Analyze(parsed.Tables ?? Array.Empty<TableInfo>())` and inject the result into the `ExecutiveSummary` builder. The analyzer must run **before** the cancellation gate added in Story 3.5 code-review P9 (so a per-task timeout that fires between Claude returning and StoreAsync starting still produces no orphan blob).
- [ ] Render the section in `DocxBuilder` (AC: 5, 6, 7)
  - [ ] Inside `AppendExecutiveSummary` (after the counts table, before key observations), call a new private helper `AppendPublisherPrefixSection(body, summary.PrefixSummary, summary.TableCount)`.
  - [ ] The helper:
    - Returns immediately if `summary.TableCount == 0` (AC-7: no header rendered for an empty environment).
    - Emits an `H2` heading `"Publisher Prefix Summary"`.
    - Emits the one-sentence narrative paragraph computed deterministically from the summary shape (the three branches in AC-5).
    - Emits a two-column table `Prefix | Component count` using the existing `BuildTable` helper, sorted: Microsoft bucket first (descending count), then Client/ISV (descending count), then `Unprefixed` if non-empty.
- [ ] Tests (AC: 8)
  - [ ] Create `tests/DataverseDocAgent.Tests/PrefixAnalyzerTests.cs` — pure-logic cases.
  - [ ] Extend `tests/DataverseDocAgent.Tests/DocxBuilderTests.cs` with one test per AC-5 branch, asserting the rendered prose AND the breakdown table presence/order.
  - [ ] Extend `tests/DataverseDocAgent.Tests/DocumentGenerateServiceTests.cs` so the parsed-and-enriched model carries the `PrefixSummary` (a single small integration test is enough — the controller and orchestrator paths are unchanged).
- [ ] No changes to (do **not** touch in this story)
  - [ ] `PromptBuilder.cs` — Claude is intentionally NOT asked to produce prefix data. FR-011 deterministic discipline.
  - [ ] `DataverseToolFactory.cs` — the four Mode 1 tools from Story 3.5 already supply the data via `list_custom_tables`. No new tool.
  - [ ] `IGenerationPipeline` / `GenerationBackgroundService` — pipeline contract is unchanged.

## Dev Notes

- **Why no `GetPublisherPrefixesTool`?** The epic-level AC offered "a `GetPublisherPrefixesTool` (or equivalent logic)". Doing it in-process is strictly better for this story: (a) it sidesteps another Dataverse round-trip per scan (NFR-001 / 5-minute target is already tight — see PRD §7 and the 30–44s per-call baseline in `docs/poc-baseline.md`); (b) it follows the FR-011 / `ComplexityRater` precedent — anything deterministic stays out of Claude's hands; (c) Claude already calls `list_custom_tables` once, so the underlying data is already in scope when the JSON returns. A tool variant would only be justified if a future story needs prefixes BEFORE the agent loop (e.g. to prune which tables get `get_table_fields` calls), which is not in this scope.
- **`cr` Microsoft-prefix handling.** Power Apps default-environment solutions emit auto-generated prefixes like `cr3a7_`, `crf2b_`, etc. — these are Microsoft-owned in the sense that they were never customised by the consultant's client. AC-2 captures both the bare `cr` (rare, legacy) and the `crXXXX_` shape via a single regex. The regex anchors `^cr...$` against the prefix-with-underscore string; this is documented inline because the matcher's success on `cr3a7_` is non-obvious.
- **Prefix segmentation rule.** `logicalName.Split('_')[0]` is brittle if Dataverse ever surfaces a logical name with no underscore (extremely rare — system-only tables, never custom). Defensive code: if no underscore is present, the table goes into the `Unprefixed` bucket and is rendered as `(no prefix)` in AC-6. The analyzer does not exclude system tables — `list_custom_tables` already filters on `IsCustomEntity=true` (see `ListCustomTablesTool` line 87-97), so an unprefixed custom table is an artefact, not a no-op.
- **Required-init contract.** Making `ExecutiveSummary.PrefixSummary` `required` ensures the analyzer cannot be silently bypassed in a future refactor. The dev test in `DocxBuilderTests.Build_HandlesEmptyEnvironment` (Story 3.5) will need its `BuildSampleModel` updated to include an empty `PrefixSummary` — flag this in the PR.
- **Determinism + tie-break.** Alphabetical-on-lowercase keeps the rendered document byte-stable across runs against the same environment — important for diffing two consecutive Mode 1 outputs to spot real drift. Spec is silent on tie-break order; this story pins it explicitly so future PRs don't quietly reorder the breakdown table.
- **Existing failure-mode coverage.** `AI_ERROR`, `DATAVERSE_ERROR`, `GENERATION_TIMEOUT`, `CREDENTIAL_REJECTED`, `HOST_SHUTDOWN` are all unaffected — the analyzer runs on the deserialised model, after Claude returned successfully. If `parsed.Tables` is `null` or empty, `PrefixAnalyzer.Analyze` returns a summary with `NoClientPrefixDetected=true` and empty bucket lists, and `DocxBuilder` skips the section entirely per AC-7.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Api/Documents/PublisherPrefixSummary.cs`
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/PrefixAnalyzer.cs` — `// F-047 — FR-042`
- `tests/DataverseDocAgent.Tests/PrefixAnalyzerTests.cs`

Files modified:
- `src/DataverseDocAgent.Api/Documents/GeneratedDocumentModel.cs` — adds `PrefixSummary` to `ExecutiveSummary`
- `src/DataverseDocAgent.Api/Documents/DocxBuilder.cs` — new `AppendPublisherPrefixSection` private + call site in `AppendExecutiveSummary`
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs` — call `PrefixAnalyzer.Analyze` in `RunPipelineAsync`
- `tests/DataverseDocAgent.Tests/DocxBuilderTests.cs` — three new AC-5-branch tests + update to `BuildSampleModel`
- `tests/DataverseDocAgent.Tests/DocumentGenerateServiceTests.cs` — model-enrichment test
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — story 3-6 → review on commit

No new csproj packages required. `DocumentFormat.OpenXml`, used by `DocxBuilder`, is already referenced via Story 3.5.

### References

- [Source: docs/prd.md#functional-requirements — FR-042 (lines 1065-1075)] — full FR text and AC for publisher prefix intelligence
- [Source: docs/prd.md#user-flows — flow step 5 (line 179)] — "Publisher prefix summary tells them immediately which tables are client-built vs Microsoft"
- [Source: docs/prd.md#per-table-data (line 506-507)] — per-table prefix surfacing, examples (`doc_`, `vel_`, `msdyn_`)
- [Source: docs/prd.md#executive-layer (line 1134)] — Executive Layer composition: opening narrative, prefix summary, complexity rating, Mermaid diagram, Top 5 Risks
- [Source: _bmad-output/planning-artifacts/architecture.md#environment-intelligence (line 338)] — Mode 1 environment intelligence inventory
- [Source: _bmad-output/planning-artifacts/architecture.md#ADR-008 (lines 555-583)] — Mode 1 two-pass prompt rationale; the prefix summary is deterministic in pass 1's executive layer.
- [Source: _bmad-output/planning-artifacts/epics.md#story-36 (lines 639-664)] — story epic source with full BDD ACs
- [Source: src/DataverseDocAgent.Api/Features/DocumentGenerate/ComplexityRater.cs] — the precedent pattern for deterministic post-Claude analysis; `PrefixAnalyzer` follows the same shape.
- [Source: src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs#RunPipelineAsync] — call-site for the analyzer; immediately after `ComplexityRater.Rate`, before the cancellation gate that precedes `IDocumentStore.StoreAsync`.
- [Source: src/DataverseDocAgent.Api/Documents/DocxBuilder.cs#AppendExecutiveSummary] — host method for the new sub-section.

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

### File List
