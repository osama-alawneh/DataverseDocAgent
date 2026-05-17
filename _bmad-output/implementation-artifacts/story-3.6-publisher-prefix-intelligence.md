# Story 3.6: Publisher Prefix Intelligence

Status: done

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

- [x] Add `PublisherPrefixSummary` to the document model (AC: 4)
  - [x] Create `src/DataverseDocAgent.Api/Documents/PublisherPrefixSummary.cs`
    - Properties: `string? PrimaryClientPrefix`, `IReadOnlyList<PrefixCount> MicrosoftPrefixes`, `IReadOnlyList<PrefixCount> ClientPrefixes`, `IReadOnlyList<PrefixCount> UnprefixedTables` (typically count 0 or 1 bucket), `bool NoClientPrefixDetected`.
    - Sub-record: `public sealed record PrefixCount(string Prefix, int ComponentCount)`.
  - [x] Extend `ExecutiveSummary` (`GeneratedDocumentModel.cs`) with a required `PublisherPrefixSummary PrefixSummary { get; init; }` slot. Required so the analyzer cannot be silently skipped.
- [x] Implement `PrefixAnalyzer` (AC: 1, 2, 3)
  - [x] Create `src/DataverseDocAgent.Api/Features/DocumentGenerate/PrefixAnalyzer.cs`
  - [x] Static method: `PublisherPrefixSummary Analyze(IReadOnlyList<TableInfo> tables)`
  - [x] Microsoft-pattern matcher: exact membership of `{ "msdyn", "msft", "adx" }` (prefix segment, no trailing `_`) OR regex `^cr[a-z0-9]*$` against the prefix segment. Covers bare legacy `cr_` and the Power Apps default-environment `crXXXX_` family. Documented inline.
  - [x] Tie-break primary client prefix alphabetically on the **lowercase prefix string**, not on the raw component (Dataverse already lowercases, but the analyzer defensively re-lowers the logical name).
  - [x] Annotate file header: `// F-047 — FR-042 — Publisher Prefix Intelligence (Story 3.6)`
- [x] Wire analyzer into `DocumentGenerateService` (AC: 4)
  - [x] In `RunPipelineAsync`, immediately after `ComplexityRater.Rate` and before `DocxBuilder.Build`, call `PrefixAnalyzer.Analyze(parsed.Tables ?? Array.Empty<TableInfo>())` and inject the result into the `ExecutiveSummary` builder. The analyzer runs **before** the cancellation gate added in Story 3.5 code-review P9 (so a per-task timeout that fires between Claude returning and StoreAsync starting still produces no orphan blob).
- [x] Render the section in `DocxBuilder` (AC: 5, 6, 7)
  - [x] Inside `AppendExecutiveSummary` (after the counts table, before key observations), call a new private helper `AppendPublisherPrefixSection(body, summary.PrefixSummary, summary.TableCount)`.
  - [x] The helper:
    - Returns immediately if `summary.TableCount == 0` (AC-7: no header rendered for an empty environment).
    - Emits an `H2` heading `"Publisher Prefix Summary"`.
    - Emits the one-sentence narrative paragraph computed deterministically from the summary shape (the three branches in AC-5).
    - Emits a two-column table `Prefix | Component count` using the existing `BuildTable` helper, sorted: Microsoft bucket first (descending count), then Client/ISV (descending count), then `Unprefixed` if non-empty.
- [x] Tests (AC: 8)
  - [x] Create `tests/DataverseDocAgent.Tests/PrefixAnalyzerTests.cs` — pure-logic cases (multi-underscore split, MS pattern incl. 3× crXXXX, tie-break, empty Client/ISV bucket, Unprefixed bucket, descending-count ordering).
  - [x] Extend `tests/DataverseDocAgent.Tests/DocxBuilderTests.cs` with one test per AC-5 branch, plus the AC-7 empty-environment header-omission guard.
  - [x] Extend `tests/DataverseDocAgent.Tests/DocumentGenerateServiceTests.cs` with a parse → analyze handshake test.
- [x] No changes to (do **not** touch in this story)
  - [x] `PromptBuilder.cs` — Claude is intentionally NOT asked to produce prefix data. FR-011 deterministic discipline.
  - [x] `DataverseToolFactory.cs` — the four Mode 1 tools from Story 3.5 already supply the data via `list_custom_tables`. No new tool.
  - [x] `IGenerationPipeline` / `GenerationBackgroundService` — pipeline contract is unchanged.

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

- 2026-05-14 dev — initial test run failed `Build_PublisherPrefixSection_SingleClientPrefix_RendersAllClientCustomisationsSentence` because the breakdown-table order assertion measured `IndexOf` from string start and hit the narrative's mention of `'vel_'`/`msdyn_`/`msft_` first. Fix: anchor the search past the trailing narrative sentence `"No third-party ISV components detected."` so the assertion targets only the breakdown rows. All 222 tests pass after the fix.

### Completion Notes List

- Implemented deterministic `PrefixAnalyzer` (in-process, mirrors `ComplexityRater` shape) — no new Mode 1 agent tool added; FR-011 / FR-042 deterministic discipline preserved.
- Microsoft matcher: exact set `{ msdyn, msft, adx }` plus regex `^cr[a-z0-9]*$` covering bare legacy `cr_` and the Power Apps default-environment `crXXXX_` family. Regex documented inline.
- `ExecutiveSummary.PrefixSummary` is `required` — required-init contract prevents a future refactor from silently bypassing the analyzer.
- `DocxBuilder.AppendPublisherPrefixSection` renders between the counts table and the key-observation bullets. Section omitted entirely when `TableCount == 0` (AC-7) — header is also omitted, matching the empty-environment Story 3.5 baseline.
- Narrative variants: AC-5's three deterministic sentences are produced from the analyzer summary shape (no Claude involvement).
- Tests: 222 pass (was 215 pre-story). New: 7 `PrefixAnalyzerTests` cases + 3 `DocxBuilderTests` AC-5 branches + AC-7 empty-environment header-omission assertion + 1 integration handshake in `DocumentGenerateServiceTests`.

### File List

- `src/DataverseDocAgent.Api/Documents/PublisherPrefixSummary.cs` (new)
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/PrefixAnalyzer.cs` (new)
- `src/DataverseDocAgent.Api/Documents/GeneratedDocumentModel.cs` (modified — required `PrefixSummary` on `ExecutiveSummary`)
- `src/DataverseDocAgent.Api/Documents/DocxBuilder.cs` (modified — new private `AppendPublisherPrefixSection` + narrative helper + call-site)
- `src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs` (modified — analyzer call in `RunPipelineAsync`, model build picks up `PrefixSummary`)
- `tests/DataverseDocAgent.Tests/PrefixAnalyzerTests.cs` (new)
- `tests/DataverseDocAgent.Tests/DocxBuilderTests.cs` (modified — AC-5 branch tests, AC-7 header-omission, `BuildSampleModel` updated for required init)
- `tests/DataverseDocAgent.Tests/DocumentGenerateServiceTests.cs` (modified — parse → analyze integration test)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (modified — story 3-6 → review)
- `_bmad-output/implementation-artifacts/story-3.6-publisher-prefix-intelligence.md` (modified — task checkboxes, Dev Agent Record, status)

### Change Log

| Date       | Change                                                                       |
|------------|------------------------------------------------------------------------------|
| 2026-05-14 | Story 3.6 implemented — PrefixAnalyzer + Publisher Prefix Summary sub-section; 222 tests pass. |
| 2026-05-14 | Code review patches P1–P14 applied; 230 tests pass. Story → done.            |

### Review Findings

Adversarial review run on 2026-05-14 with three parallel subagents (Blind Hunter — diff-only, Edge Case Hunter — diff + read access, Acceptance Auditor — diff + spec + PRD). Severity rubric: HIGH = wrong output / crash / broken invariant; MED = edge case / coverage gap; LOW = nit. Patches applied below; everything else captured in `_bmad-output/implementation-artifacts/deferred-work.md` under the new heading "Deferred from: code review of story-3.6 (2026-05-14)".

#### Patches applied

- [x] **P1** — `PrefixAnalyzer.Analyze` skips a null `TableInfo` element instead of NRE-ing on `table.LogicalName`. Mirrors the Story 3.5 P3 KeyObservations null-filter pattern. (`src/DataverseDocAgent.Api/Features/DocumentGenerate/PrefixAnalyzer.cs` lines ~51–55)
- [x] **P2** — `DocumentGenerateService.RunPipelineAsync` strips null entries from `parsed.Tables` before building the model, so both `PrefixAnalyzer` and `DocxBuilder.AppendCustomTablesSection` are protected at the parse boundary. (`src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs`)
- [x] **P3** — `DocxBuilder.AppendPublisherPrefixSection` returns early when `TableCount > 0` but every prefix bucket is empty (e.g. Claude reported `tableCount` elsewhere yet the array contained only whitespace logical names). Avoids the header + lonely "No client-defined publisher prefix" sentence with no breakdown table. (`src/DataverseDocAgent.Api/Documents/DocxBuilder.cs`)
- [x] **P4** — Variant 3 prose uses singular `component` when the primary's count is 1 ("1 component" not "1 components"). (`src/DataverseDocAgent.Api/Documents/DocxBuilder.cs#BuildPrefixNarrative`)
- [x] **P5** — Extract the `(no prefix)` magic literal to `PrefixAnalyzer.UnprefixedLabel`; both the analyzer and the renderer reference the constant so a future rename cannot drift the analyzer's stored label out of sync with the renderer's underscore-suppression branch. (`PrefixAnalyzer.cs`, `DocxBuilder.cs`, tests)
- [x] **P6** — Inline comment in `BuildPrefixNarrative` documents that AC-5 variant 1's pipe-alternation second form (`"n additional non-Microsoft prefixes detected — see breakdown below."`) is unreachable by design — its trigger (≥2 Client/ISV prefixes) routes into variant 3 instead. Locks the design choice for future maintainers.
- [x] **P7** — Test pin: `Analyze_LeadingUnderscoreLogicalName_IsBucketedAsUnprefixed` — `_widget` → `UnprefixedLabel`. (`tests/DataverseDocAgent.Tests/PrefixAnalyzerTests.cs`)
- [x] **P8** — Test pin: `Analyze_BucketsMicrosoftPrefixes` extended with `crm_widget` and `crystal_thing` as expected-Microsoft cases. Locks the spec-accepted over-match contract so a regex tightening cannot silently flip classification. (`PrefixAnalyzerTests.cs`)
- [x] **P9** — Test: `Build_PublisherPrefixSection_SingleClient_NoMicrosoft_DropsMicrosoftSentence` — variant 1 with empty Microsoft bucket must omit the "Microsoft components use …" sentence. (`DocxBuilderTests.cs`)
- [x] **P10** — Test: variant-3 (multi-client) test now asserts `Microsoft components use` is absent — locks the prose surface. (`DocxBuilderTests.cs`)
- [x] **P11** — Integration test asserts `summary.ClientPrefixes[0].Prefix == "vel"`, not only the count, so a regression bucketing by full logical name (instead of segment) would fail. (`DocumentGenerateServiceTests.cs`)
- [x] **P12** — Test: `Analyze_NullTableEntry_IsSilentlySkipped` — direct callers of the analyzer cannot NRE on a null element. (`PrefixAnalyzerTests.cs`)
- [x] **P13** — Test: `ParseAgentJson_NullTableEntry_SurvivesAndIsFilteredAtServiceBoundary` — JSON `[null, {...}]` survives parsing and is filtered at the service layer before reaching analyzer/builder. (`DocumentGenerateServiceTests.cs`)
- [x] **P14** — Variant-3 ordering assertion anchors on the stable phrase `"See full breakdown below."` instead of the primary-sentence apostrophe shape. Robust to future prose tweaks. (`DocxBuilderTests.cs`)
- [x] **P3b** — `Build_PublisherPrefixSection_AllBucketsEmptyWithNonZeroTableCount_OmitsHeader` test pins the P3 guard.

#### Deferred (see `deferred-work.md`)

- [x] **D-CR1** — Microsoft `cr` regex over-match is spec-accepted; behaviour pinned by P8.
- [x] **D-CR2** — No length cap on prefix copied into the Word narrative; bounded in practice by Dataverse 50-char logical-name limit. Cross-cutting concern.
- [x] **D-CR3** — Microsoft narrative list has no upper bound — defer until a real environment surfaces a readability problem.
- [x] **D-CR4** — Tie-break disclosure on `Primary client prefix` silent — alphabetical determinism is the design.
- [x] **D-CR5** — `NoClientPrefixDetected` flag redundant with `PrimaryClientPrefix is null`.
- [x] **D-CR6** — Three-`cr*`-tie ordering test gap.
- [x] **D-CR7** — `BuildTable` row/header width contract has no defensive assert.
- [x] **D-CR8** — One-character prefix `v_thing` accepted as Client/ISV.
- [x] **D-CR9** — Duplicate `LogicalName` from Claude inflates counts.
- [x] **D-CR10** — Null `PrefixSummary` at render-time not defended (required-init contract makes this theoretical).
- [x] **D-CR11** — AC-2 spec prose vs implementation cosmetic divergence — behaviourally equivalent and covered by AC-8 tests.
- [x] **D-CR12** — FR-042 tri-partition collapsed to two buckets — documented spec choice.
- [x] **D-CR13** — `DateTime.UtcNow` in `ApplyCorePackageMetadata` non-determinism — pre-existing Story 3.5 surface, out of scope.

#### Dismissed

- Local-variable coupling (`var tables` flowing to both analyzer and model in `RunPipelineAsync`) — not a defect, just a fragile-coupling caution. No change.
- `StringBuilder` allocation for the variant-1 narrative — readability nit, no perf concern. No change.
- `RegexOptions.Compiled` startup cost — correct choice for the hot path. No change.
- `using System.Text;` import — single legitimate consumer.
- "Place analyzer before cancellation gate" — explicitly required by the story spec; not a finding.
- Determinism of LINQ `OrderBy*` over `Dictionary` enumeration — total-ordered by (count desc, prefix ordinal asc); byte-for-byte stable. No change.
