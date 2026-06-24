# R-HF-10: Mode 1 Tool-Payload Slim — `GetTableFieldsTool` + `GetRelationshipsTool`

Status: review

> **Status flip 2026-06-23 (post-correct-course audit):** Status was originally set `done` when code + tests + review patches were all green, but the Epic 2 retrospective process lesson (`deferred-work.md` line 55) requires a real-env E2E (or at minimum `dotnet run` smoke) before any story touching the Mode 1 pipeline transitions to `done`. R-HF-10 modifies two Mode 1 tools — gate applies. Status flipped back to `review` pending the paid E2E against `orgd76c9cf3`. Flips back to `done` on success, or stays `review` with retro reopened on failure.

> **E2E run 2026-06-23 21:47:08 → 21:57:55 — FAILED.** Job `836c6460-1485-40f7-acf5-6dfd2bb51276` against `orgd76c9cf3.crm.dynamics.com` (~200 custom tables). Wall: **631.5s** (vs pre-slim 627.7s — essentially no improvement). iter=2 finished 26.9s w/ 14 parallel `tool_use` results (7 `get_table_fields` + 7 `get_relationships`); iter=3 Claude API call never returned a `tool_use` response — hit `HttpClient.Timeout = 600s` and threw `TaskCanceledException` ("The request was canceled due to the configured HttpClient.Timeout of 600 seconds elapsing"). Outcome: `AI_ERROR (safeToRetry=true)`. **Cost ≈ $0.40** Anthropic. **Verdict:** R-HF-10 slim removed dead-weight fields but did NOT reduce iter=3 context enough to fit inside Claude's per-request response budget on a Large-tier env. **Structural bottleneck confirmed** — Stories 4.3 (ADR-004 batching) + 4.9 (ADR-008 two-pass) are the actual fix per the architectural finding at `deferred-work.md` line 3. R-HF-10 status stays `review`; transition to `done` only after Epic 4 ships and a second paid E2E succeeds, OR after Epic 3 is formally exit-gated against a Typical-tier (≤50 tables) env per PRD §7.1 and R-HF-10 demonstrates value there.

> **Hotfix-as-mini-story.** Authorising artifact: [`../planning-artifacts/sprint-change-proposal-2026-06-23.md`](../planning-artifacts/sprint-change-proposal-2026-06-23.md). This file exists to satisfy BMAD dev-execution audit (Dev Agent Record + Completion Notes + File List) for a tool-output narrow that re-aligns shipped code with the freshly narrowed Story 3.4 AC-2 / AC-3 contract. Not a forward Epic 3 story — corrective hotfix that already cleared the originating story's ACs (now amended).

## Story

As the Mode 1 agent loop running against a Typical-tier Dataverse environment (≤50 tables per PRD §7.1),
I want `GetTableFieldsTool` and `GetRelationshipsTool` to emit only the JSON keys that `PromptBuilder.cs:52-65` actually consumes,
so that per-iteration `tool_result` payloads stop bloating the Claude conversation context past the 200k window before all tables are visited.

## Acceptance Criteria

1. `GetTableFieldsTool` emits each field as exactly `{ logicalName, displayName, attributeType, requiredLevel, description }`. No `options[]`, no `defaultValue`. Confirmed against `PromptBuilder.cs:52-65`. (per amended Story 3.4 AC-2)
2. `GetRelationshipsTool` emits each relationship as exactly `{ schemaName, relationshipType, relatedEntity, cascadeDelete }`. `relatedEntity` is the non-self table on the edge (parent table implicit from the relationships dictionary key per the AgentOrchestrator's emission contract). `cascadeDelete` is the SDK `CascadeConfiguration.Delete?.ToString()` value, with deterministic fallback `"NoCascade"` when the SDK leaves `CascadeConfiguration` null. No `cascadeConfiguration` quad, no `referencingEntity`/`referencedEntity` pair. (per amended Story 3.4 AC-3)
3. All Story 3.4 preserved behaviours hold: regex shape validation, "table not found" structured error JSON, exception chain (`FaultException`, `TimeoutException`, `CommunicationException`, `HttpRequestException`), `OperationCanceledException` re-throw, `RetrieveAsIfPublished = true`, `IsCustomRelationship` filter, defensive endpoint filter, schemaName dedupe.
4. Negative test coverage: both tool test files contain assertions that the dropped keys (`options`, `defaultValue`, `cascadeConfiguration`, `referencingEntity`, `referencedEntity`) NEVER appear in the JSON output. Dual-orientation coverage for `relatedEntity` (same edge queried from both sides flips `relatedEntity` correctly).
5. Full test suite (`tests/DataverseDocAgent.Tests`) green; no unrelated regressions.

## Tasks / Subtasks

- [x] **Slim `GetTableFieldsTool`** (AC: 1, 3)
  - [x] Remove `options[]` emission from picklist / MultiSelectPicklist / State / Status branches (reflection-on-`OptionSet` path)
  - [x] Remove `BooleanOptionSetMetadata` TrueOption / FalseOption branch (no `options[]` emitted for Boolean either)
  - [x] Remove `defaultValue` extractor (`ExtractDefaultValue`) — was already deferred per Story 3.4 dev notes; now codified out of contract
  - [x] Delete `OptionDto` and `BuildOption` helper (unreferenced after slim)
  - [x] Delete `ConcurrentDictionary<Type, PropertyInfo?> s_optionSetPropertyCache` (Story 3.4 Review Patch P7) — unreferenced after slim
  - [x] Preserve regex shape validation, structured-error path, full exception chain, `OperationCanceledException` re-throw, `// F-002, FR-002` annotation
- [x] **Slim `GetRelationshipsTool`** (AC: 2, 3)
  - [x] Collapse `CascadeDto { delete, assign, share, unshare }` → single `string cascadeDelete` on the 1:N DTO
  - [x] Map `CascadeConfiguration.Delete?.ToString()`; null `CascadeConfiguration` → `"NoCascade"` (preserves the spirit of Story 3.4 Review Patch P11)
  - [x] Replace 1:N `referencingEntity`/`referencedEntity` pair with single `relatedEntity` (= non-self table; computed via the `isReferenced` flag)
  - [x] Replace N:N `entity1LogicalName`/`entity2LogicalName` pair with single `relatedEntity` (computed via the `isEntity1` flag)
  - [x] Delete `CascadeDto` and `MapCascade` (unreferenced after slim)
  - [x] Preserve `RetrieveAsIfPublished = true`, `IsCustomRelationship` filter, defensive endpoint filter, schemaName dedupe, exception chain, cancellation re-throw, `// F-003, FR-003` annotation
- [x] **Update `GetTableFieldsToolTests`** (AC: 4)
  - [x] Drop fixtures + assertions that hydrated `OptionDto` / `options[]` / `defaultValue`
  - [x] Add negative assertions: picklist column never emits `options`; Boolean attribute never emits `options`; attribute with default value source never emits `defaultValue`
  - [x] Add `description` to positive field-shape test
- [x] **Update `GetRelationshipsToolTests`** (AC: 4)
  - [x] Replace assertions on `cascadeConfiguration {…}` with `cascadeDelete` (string) assertions
  - [x] Replace assertions on `referencingEntity` + `referencedEntity` with `relatedEntity` assertions
  - [x] Add dual-orientation tests: 1:N edge from BOTH the referenced (parent) and referencing (child) side; N:N edge from BOTH endpoints — `relatedEntity` flips correctly each time
  - [x] Add negative assertions for dropped keys (`cascadeConfiguration`, `referencingEntity`, `referencedEntity`)
  - [x] Retain null-cascade → `"NoCascade"` test (mirrors Story 3.4 Review Patch P11 intent)
- [x] **Build + test gate** (AC: 5)
  - [x] `dotnet build -nologo -v q` → 0 warnings, 0 errors
  - [x] `dotnet test tests/DataverseDocAgent.Tests --no-build --nologo -v q` → 287 passed / 0 failed (baseline 286 → +1 net from added negative + dual-orientation coverage minus removed picklist-option assertions)

## Dev Notes

- **Contract source of truth.** `src/DataverseDocAgent.Api/Agent/PromptBuilder.cs:52-65` declares the exact JSON shape Claude must return at end of Mode 1. The five field keys + four relationship keys consumed there are the ONLY keys the slim tools need to emit. Anything else is dead weight on the conversation context.
- **Owning-table semantic for `relatedEntity`.** The AgentOrchestrator emits `relationships` as `{ "<owningTableLogicalName>": [...] }`. The dictionary key IS the parent (the table the call was scoped to). `relatedEntity` is therefore the OTHER end of the edge — `referencingEntity` when the table is the referenced side, `referencedEntity` when the table is the referencing side. For N:N, the same logic applies: `relatedEntity = entity1` if the call's tableName matches entity2, else `entity2`.
- **Null `CascadeConfiguration` fallback.** Story 3.4 Review Patch P11 (line 128) mandated that the cascade block always emit all four fields populated rather than letting null `CascadeConfiguration` silently drop the object — the intent was "Claude sees a deterministic value, never an absent field." We preserve that intent under the slim by defaulting null → `"NoCascade"` (the SDK's documented default behaviour for an unset `CascadeType`). The contract is "always a non-null string on `cascadeDelete`."
- **No new SDK behaviour, no architectural change.** R-HF-10 is strictly a JSON-output narrow on two tools. No new Anthropic SDK calls. No orchestrator changes. No new error paths. No configuration surface. The reflection cache deletion is a side-effect of `options[]` removal, not a separate optimisation.
- **Expected runtime effect.** Per the R-HF-10 handoff doc (`HANDOFF-PROMPT-2026-05-15-r-hf-10-decision.md`): iter=3 Claude input on `orgd76c9cf3` (200+ tables) should drop from ~80k tokens → ~20–30k tokens. Single round-trip latency at 20–30k input should be sub-30s (well inside the R-HF-6 10-min HttpClient.Timeout).
- **PRD scope alignment.** PRD §7.1 NFR-001 scopes Epic 3 to Typical-tier (≤50 tables). Large-tier (>50 tables) remains Epic 4 territory (Stories 4.3 ADR-004 batching + 4.9 ADR-008 two-pass). R-HF-10 is the cheapest enabler for a Typical-tier exit-gate E2E; it does NOT structurally solve Large tier.
- **Escape hatch for dropped fields.** If a future Mode 1 consumer requires picklist option labels, scope a dedicated `get_picklist_options(tableName, fieldName)` tool under Epic 4. Likewise `get_relationship_cascade(schemaName)` for the full cascade quad. Both surfaces documented in the amended Story 3.4 ACs.

## Project Structure Notes

No new files. No file renames. No DI registration changes. No `appsettings.json` keys. No new test project. Reflection cache `s_optionSetPropertyCache` removed (was the only thread-safety surface in `GetTableFieldsTool`; no replacement needed because no per-attribute reflection remains).

## References

- [Source: `_bmad-output/planning-artifacts/sprint-change-proposal-2026-06-23.md`] — authorising artifact for this hotfix; documents impact analysis, alternatives considered, scope classification (Minor), and handoff to developer agent.
- [Source: `_bmad-output/implementation-artifacts/story-3.4-dataverse-tools.md`] — amended Story 3.4 AC-2 + AC-3 (lines ~14-15). The contract this hotfix delivers code-compliance to.
- [Source: `_bmad-output/implementation-artifacts/HANDOFF-PROMPT-2026-05-15-r-hf-10-decision.md`] — diagnostic report; iter=3 context bloat math; A/B/C/D decision matrix.
- [Source: `_bmad-output/implementation-artifacts/deferred-work.md`] — "R-HF-10 — Tool payload slim (2026-06-23)" entry + the "Architectural finding — 2026-05-14 evening E2E run" context above it.
- [Source: `_bmad-output/planning-artifacts/epics.md` lines 567 + 572] — Story 3.4 epic prose synced inline as part of the originating correct-course pass.
- [Source: `_bmad-output/planning-artifacts/architecture.md` §5 Custom Tool Inventory] — tool names + file paths only; no field-shape impact (verified by grep).
- [Source: `docs/prd.md` §7.1 NFR-001] — Typical-tier (≤50 tables) is Epic 3's exit-gate scope; Large-tier is `[TBD — POC]` and deferred to Epic 4.

## Dev Agent Record

### Agent Model Used

- Parent (PO / boss): claude-opus-4-7 (interactive session)
- Worker (dev execution): general-purpose subagent invoked under hotfix-equivalent brief (no `bmad-agent-dev` persona invoked — see Completion Notes for the conscious procedural deviation)

### Debug Log References

- `dotnet build -nologo -v q` → 0 warnings, 0 errors
- `dotnet test tests/DataverseDocAgent.Tests --no-build --nologo -v q` → **287 passed / 0 failed** / 0 skipped (baseline 286 → +1 net)
- Boss-side independent re-run of the test suite: same result (287/0/0)

### Completion Notes List

- **Procedural deviation captured.** This hotfix was executed via a `general-purpose` subagent under a tight brief, NOT via the `bmad-agent-dev` (Amelia) skill against a pre-written story spec. The deviation matches the precedent set by R-HF-1..9 (hotfix-style execution, no story file) but was retroactively converted to this mini-story file when the user flagged the missing BMAD audit surface mid-session. Lesson logged for the Epic 3 retro: define a written threshold for "hotfix vs story-dev" rather than letting size creep into the hotfix bucket case-by-case.
- **`relatedEntity` orientation.** The non-trivial part of File B was deciding which side of each edge to emit. The owning table is implicit in the dictionary key per `PromptBuilder.cs:59-65`; `relatedEntity` is computed via an `isReferenced` (1:N) or `isEntity1` (N:N) boolean. Dual-orientation tests in File D pin both directions so a future refactor cannot accidentally flip the polarity.
- **`cascadeDelete` null fallback.** SDK can return `null` for `CascadeConfiguration` on relationships with no cascade rules set. Story 3.4 Review Patch P11 deliberately emitted all four cascade fields rather than null, so Claude saw a deterministic value. Under the slim, we preserve that intent: null → `"NoCascade"`. The contract is "always a non-null string."
- **Reflection cache deletion.** `GetTableFieldsTool` carried a `ConcurrentDictionary<Type, PropertyInfo?> s_optionSetPropertyCache` (Review Patch P7) to keep per-attribute reflection thread-safe for parallel orchestrators. After `options[]` removal, no reflection remains in the tool — cache was deleted to avoid dead-code drift. If a future picklist tool (the escape-hatch `get_picklist_options`) re-introduces reflection, the cache pattern is documented in the original Story 3.4 review record for re-introduction.
- **Type deletions.** `OptionDto` (File A), `CascadeDto` (File B), `BuildOption` and `MapCascade` helpers removed. Verified unreferenced via build (no compiler errors) and search (no other call sites).
- **No E2E in this hotfix.** Per the proposal's "no paid E2E before R-HF-10 ships" guardrail, the live E2E against `orgd76c9cf3` is deferred to the next session step. The result of that E2E decides Epic 3 closeout (Typical-tier exit gate met) vs freeze (open Epic 3 retro, pull Stories 4.3 + 4.9 forward).
- **NFR-007 still holds.** No new credential surfaces, no new logging fields, no new exception propagation paths. The slim only removes output keys.

### File List

- `src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs` (modified) — slimmed 278 → 208 lines. Removed: `options[]` emission across all OptionSet variants + Boolean branch, `defaultValue` extraction, `OptionDto`, `BuildOption`, reflection cache. Added: `Description` on `FieldDto`. Preserved: `// F-002, FR-002` annotation.
- `src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs` (modified) — slimmed 259 → 247 lines. Replaced: `CascadeDto` quad → single `cascadeDelete` string; `referencingEntity`/`referencedEntity` + `entity1LogicalName`/`entity2LogicalName` → single `relatedEntity`. Removed: `CascadeDto`, `MapCascade`. Preserved: `RetrieveAsIfPublished = true`, defensive filters, dedupe, exception chain, `// F-003, FR-003` annotation.
- `tests/DataverseDocAgent.Tests/GetTableFieldsToolTests.cs` (modified) — dropped picklist/MultiSelect/Boolean options + value-zero + defaultValue assertions; added 3 negative tests (`PicklistAttribute_NeverEmitsOptionsArray`, `BooleanAttribute_NeverEmitsOptionsArray`, `BooleanAttributeWithDefault_NeverEmitsDefaultValue`); added `description` to positive field-shape test.
- `tests/DataverseDocAgent.Tests/GetRelationshipsToolTests.cs` (modified) — replaced cascade-quad / referencing-referenced assertions with `cascadeDelete` + `relatedEntity`; added 1:N + N:N dual-orientation pairs; added 3 negative tests for dropped keys; retained null-cascade → `"NoCascade"` test.

### Change Log

| Date       | Notes                                                                                                                                                                                                                                            |
|------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 2026-06-23 | R-HF-10 hotfix-as-mini-story created retroactively for BMAD dev-execution audit. Code already implemented per amended Story 3.4 AC-2 + AC-3 via a `general-purpose` subagent. Build + tests green (287/0). Status → done. E2E gate deferred to next step. |
| 2026-06-23 | Applied 7 `bmad-code-review` patches (LocalizedLabels fallbacks on DisplayName + Description, non-nullable `CascadeDelete`, positive-anchor assertions on negative tests, CascadeType enum-roundtrip Theory + self-N:N Fact, NBSP source-format restoration). Build clean; test suite 287 → 294. 7 findings deferred to `deferred-work.md`; 4 dismissed. |
| 2026-06-23 | Paid E2E against `orgd76c9cf3` (~200 tables) — **FAILED at 631.5s** with `AI_ERROR (TaskCanceledException, HttpClient.Timeout=600s)`. Same failure mode as pre-slim (627.7s). R-HF-10 slim insufficient on Large-tier; structural bottleneck confirmed. Status stays `review`. Cost ~$0.40. Epic 3 retrospective owed before any further Mode 1 work. Forensic log at `_bmad-output/e2e-runs/r-hf-10-2026-06-23.log`. |

### Review Findings

**`bmad-code-review` 3-layer pass run 2026-06-23.** Blind Hunter + Edge Case Hunter + Acceptance Auditor against commit `4c1b345` diff (src/ + tests/ only). 18 raw findings → 0 decision-needed / 7 patch / 7 deferred / 4 dismissed. **All 7 patches applied 2026-06-23**; build clean; test suite 287 → 294 (+6 CascadeType-roundtrip Theory rows + 1 self-N:N Fact). Notes per patch:

- **Patch 1 (NBSP restoration):** investigation found it was a source-format change only, not a behaviour regression — the slim commit replaced the C# ` ` escape sequence with the raw NBSP UTF-8 bytes; both produce the SAME compiled string and the `InvalidLogicalName` regex test still exercises NBSP rejection. Source restored to the explicit ` ` escape form for code-reader clarity (intent visible to a future maintainer).

- [x] [Review][Patch] Restore NBSP ` ` InlineData in `GetTableFieldsToolTests` — diff replaced it with a literal ASCII space and lost the NBSP rejection coverage [tests/DataverseDocAgent.Tests/GetTableFieldsToolTests.cs] (blind+auditor)
- [x] [Review][Patch] Add `Description.LocalizedLabels` fallback in `BuildField` — silently drops description on non-en-US orgs or `Label(string,int)`-constructed metadata [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs ~180] (edge)
- [x] [Review][Patch] Add `DisplayName.LocalizedLabels` fallback in `BuildField` — same root cause as the description fallback, both regressed when the old `BuildOption` coalesce path was deleted [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs ~174] (edge)
- [x] [Review][Patch] Strengthen negative-coverage tests — `PicklistAttribute_NeverEmitsOptionsArray` / `BooleanAttribute_NeverEmitsOptionsArray` / `BooleanAttributeWithDefault_NeverEmitsDefaultValue` would all pass if `BuildField` itself regressed; add a positive `logicalName` assertion in each so a complete-field-drop is caught noisily [tests/DataverseDocAgent.Tests/GetTableFieldsToolTests.cs] (blind)
- [x] [Review][Patch] Make `OneToManyDto.CascadeDelete` non-nullable `string` with an initializer — XML doc + diff claim "deterministic non-null value" but the property type is `string?`, leaving the contract fragile against future `JsonIgnoreCondition.WhenWritingNull` config [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs ~245] (blind)
- [x] [Review][Patch] Add CascadeType enum-roundtrip tests for `RemoveLink`, `Restrict`, `Active`, `UserOwned`, `Cascade` — current coverage exercises only `NoCascade` (via null cascade) and would miss a future `ToString()` shape change for the other enum members [tests/DataverseDocAgent.Tests/GetRelationshipsToolTests.cs] (edge)
- [x] [Review][Patch] Add self-referencing N:N test (parity with the 1:N self-ref coverage) — comments at `GetRelationshipsTool.cs:196-203` claim symmetric collapse but no test pins it; mirrors Story 3.4 Review Patch P10 1:N variant [tests/DataverseDocAgent.Tests/GetRelationshipsToolTests.cs] (edge)
- [x] [Review][Defer] `relatedEntity` case-leak when SDK returns mixed-case logical names — slim elevates this string from a redundant field to the sole link to the related table [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs:170, 203] (blind+edge) — deferred, theoretical (Dataverse SDK convention returns lowercase logical names in practice; PromptBuilder downstream is tolerant)
- [x] [Review][Defer] Polymorphic lookup edges silently dropped by `schemaName` dedup (e.g. `customerid` → 2 edges sharing schema, second collapsed) [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs:163] (edge) — deferred, pre-existing Story 3.4 P10 dedup pattern; custom polymorphic lookups are uncommon and full handling belongs in Epic 4 if surfaced
- [x] [Review][Defer] System entities (`team` / `systemuser` / `businessunit`) appear as `relatedEntity` but not in `tables[]` — Claude can't cross-reference the related side [`PromptBuilder.cs` prompt contract] (edge) — deferred, pre-existing prompt-engineering gap unrelated to R-HF-10; document in PromptBuilder rules if it surfaces in E2E
- [x] [Review][Defer] Cancellation not re-checked between SDK return and JSON serialise [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:84, GetRelationshipsTool.cs:86] (edge) — deferred, pre-existing; `AgentOrchestrator` re-raises `OperationCanceledException` on next iteration so cancellation eventually wins
- [x] [Review][Defer] NRE on null element in `OneToManyRelationships` / `ManyToManyRelationships` array [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs:154, 193] (edge) — deferred, defensive only; SDK production behaviour does not produce this
- [x] [Review][Defer] Non-ASCII lowercase `tableName` (e.g. `"café"`) triggers misleading "must be lowercase letters" error [src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs:198, GetRelationshipsTool.cs:231] (edge) — deferred, pre-existing regex error-message surface; cosmetic
- [x] [Review][Defer] Cross-type schemaName dedup investigation (1:N + N:N legitimately sharing schemaName collapse?) [src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs:163] (blind) — deferred, needs Dataverse SDK contract investigation to confirm whether schemaNames are unique per relationship-type bucket or globally; pre-existing
- [x] [Review][Dismiss] Tautological negative cascade-quad probes at relationship root — `assign`/`share`/`unshare` were never on `r` directly; the real defence is the `cascadeConfiguration` absence assertion that already exists (blind) — dismissed, harmless redundancy
- [x] [Review][Dismiss] `relationshipType` field redundant with dictionary structure — reviewer self-dismissed as out of scope; matches PromptBuilder contract (blind) — dismissed
- [x] [Review][Dismiss] `businessMeaning` in `PromptBuilder` shape but not tool-emitted — by design; Claude synthesises it at final-JSON time, parallels `purpose`/`keyObservations` (auditor) — dismissed
- [x] [Review][Dismiss] Slim loses parent/child directionality from `referencingEntity`/`referencedEntity` pair — intentional per amended Story 3.4 AC-3; owning-table is wrapper-key (auditor) — dismissed
