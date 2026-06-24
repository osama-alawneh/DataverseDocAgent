# Sprint Change Proposal — 2026-06-23

**Trigger:** R-HF-10 (Mode 1 tool-payload slim) — Large-tier E2E iter=3 context bloat
**Workflow:** `bmad-correct-course`
**Mode:** Batch
**Author:** BMAD correct-course agent (delegated by parent PO session)
**Reference:** [`../implementation-artifacts/HANDOFF-PROMPT-2026-05-15-r-hf-10-decision.md`](../implementation-artifacts/HANDOFF-PROMPT-2026-05-15-r-hf-10-decision.md)
**Scope classification:** **Minor** — Story 3.4 AC tightening + propagated `epics.md` Story 3.4 prose sync at lines 567 / 572. No Epic structural restructure. No PRD FR change.
**Selected path:** **Direct Adjustment** — modify Story 3.4 AC-2 + AC-3, defer downstream PRD/Epic reconciliation.
**Handoff target:** Developer agent (Amelia / `bmad-agent-dev`) — implement R-HF-10 code edits per amended ACs.

---

## Section 1 — Issue Summary

### Triggering issue

The 2026-05-14 evening Mode 1 E2E run against the user's Large-tier Dataverse environment `orgd76c9cf3.crm.dynamics.com` (~200 custom tables) failed at iter=3 of the `AgentOrchestrator` agent loop. Three parallel investigation agents (HttpClient forensics / per-iter payload analysis / spec alignment) converged on the same diagnosis: the failure is **not** a regression of any prior hotfix (R-HF-1..9) — it is **structural context bloat** of the Claude conversation as `tool_result` payloads accumulate.

### Evidence

From `deferred-work.md` "Architectural finding — 2026-05-14 evening E2E run" + `HANDOFF-PROMPT-2026-05-15-r-hf-10-decision.md`:

- iter=1 (`list_custom_tables` over 200+ tables) → ~12,500 tokens of tool result
- iter=2 (15 parallel `get_table_fields` + `get_relationships` for 7 tables + `get_application_users`) → **+65,750 tokens added**
- **iter=3 Claude input: ~80,000 tokens already**, trajectory toward **~1.5M tokens by iter=30** — exceeds Claude Sonnet 4.6's **200k context window** before all 200 tables can be visited.
- Failed run cost: ~$0.40 in Anthropic billing at Sonnet 4.6 pricing. A *successful* Large-tier run under the current architecture would cost ~$5–15 due to cumulative re-upload of conversation history.

### Why this triggers a correct-course

R-HF-10 proposes dropping two fields from `GetTableFieldsTool` and `GetRelationshipsTool` JSON output that **PromptBuilder.cs:52-65 never consumes** (verified by direct file read in the handoff doc):

- `options[]` array on picklist / OptionSet / MultiSelectPicklist columns (~60 chars × ~8 options per picklist column)
- Full `cascadeConfiguration { delete, assign, share, unshare }` block on 1:N relationships (reduce to single `cascadeDelete` string)
- The `referencingEntity`/`referencedEntity` pair on 1:N relationships (replaced by singular `relatedEntity` — the parent table is implicit from the relationships dictionary key)

These three reductions are **code-level changes to tool output JSON shape**, but Story 3.4 acceptance criteria AC-2 and AC-3 explicitly mandate the wider field set. Shipping R-HF-10 against unchanged Story 3.4 ACs would create a silent spec/code divergence — hence this formal sprint change proposal to **narrow the ACs first**, then let the developer agent implement the code edits in compliance with the amended contract.

### Context — Epic 3 exit gate alignment (PRD §7.1)

PRD §7.1 NFR-001 explicitly scopes Epic 3's Mode 1 exit gate to **Typical-tier** environments (≤50 tables). Large-tier (>50 tables) is annotated `[TBD — POC]` and explicitly deferred to Phase 2+. The architectural fixes for Large tier are already scoped under Epic 4:

- **Story 4.3 / ADR-004** — `GetTableRecordStatsTool` parallel batching with 3-tier dispatch (Small / Medium / Large).
- **Story 4.9 / ADR-008** — two-pass prompt architecture (deterministic enrichment offline → structured JSON → second-pass narrative) — bounds per-call context.

R-HF-10 is the **cheapest viable Typical-tier exit gate enabler**: by slimming Mode 1 payloads now, the orchestrator can plausibly complete a Typical-tier run inside the 200k context budget without waiting for Epic 4's full restructure.

---

## Section 2 — Impact Analysis

### Epic Impact

| Epic | Affected | How |
|------|----------|-----|
| Epic 3 (Mode 1 Core) | **Yes — minor** | Story 3.4 AC-2 + AC-3 narrowed. Story-level only; epic exit gate (PRD §7.1) is unchanged because Typical-tier (≤50 tables) was always the target. |
| Epic 4 (Mode 1 Full) | **No conflict** | Slim payload supports Epic 4: `cascadeDelete` is exactly what FR-049 / Story 4.8 Mermaid edges consume (epics.md:80, 1015). `options[]` is not consumed by any Epic 4 story. |
| Earlier epics (1, 2) | **No** | No cross-cutting impact. |

### Story Impact

| Story | Impact | Action |
|-------|--------|--------|
| **Story 3.4** (Dataverse Tools) | **Direct** — AC-2 + AC-3 spec text mandates fields R-HF-10 drops. | Amend AC-2 and AC-3 inline (see Section 4). Preserve `Status: done` — code is brought back into compliance via R-HF-10, not by rewinding the spec. |
| Story 3.5 (Mode 1 Generation) | **None — confirmation only** | Section 4 of the docx already uses only `Cascade Delete` (singular) per story-3.5-mode1-generation.md:74. Slim cascade matches existing docx-builder contract. |
| Stories 3.6 / 3.7 / 3.8 | None | No cite of dropped field shapes. |
| Stories 4.x (backlog) | None — see Epic Impact above | Slim payload is forward-compatible with Stories 4.3 / 4.8 / 4.9. |

### Artifact Conflict and Impact Analysis

**Impact-scan greps over `_bmad-output/`** for `options[]`, `cascadeConfiguration`, `referencingEntity`, `referencedEntity`, `defaultValue`, plus broader `cascade|picklist|optionset` (case-insensitive):

| File | Line(s) | Hit | Disposition |
|------|---------|-----|-------------|
| `planning-artifacts/architecture.md` | 215–216, 136–137, 775–776 | Lists tool names + file paths only. No field-shape detail. | **No conflict.** No edit required. |
| `planning-artifacts/epics.md` | 25 (FR-002 prose) | "default value, option set values/labels" | **Pre-existing divergence — deferred to Epic 3 retro.** `defaultValue` was already deferred at Story 3.4 close (see story-3.4 Dev Notes). FR-002 is a PRD-level surface; reconciliation is out of scope for a story-level correct-course. Owed to the upcoming `bmad-retrospective` for Epic 3. |
| `planning-artifacts/epics.md` | 66 (FR-003 v4) + 80 (FR-049) | "Cascade-on-delete behaviour explicitly documented per relationship to feed Mermaid diagram generator" | **Aligned with slim.** Epic 4 Mermaid uses cascade-on-delete only — slim's `cascadeDelete` is exactly the right shape. |
| `planning-artifacts/epics.md` | 149 (FR-003 epic split) | "Epic 3 (baseline) + Epic 4 (enhanced: cascade-on-delete for Mermaid)" | **Aligned.** |
| `planning-artifacts/epics.md` | 567 (Story 3.4 epic prose) | "default value (if set), and for OptionSet fields, all option values with integer codes and display labels" | **RESOLVED via this pass (parent override).** Edited inline 2026-06-23 to match the narrowed AC-2; an inline amendment note references this proposal. |
| `planning-artifacts/epics.md` | 572 (Story 3.4 epic prose) | "cascade behaviour for delete/assign/share/unshare" | **RESOLVED via this pass (parent override).** Edited inline 2026-06-23 to match the narrowed AC-3; an inline amendment note references this proposal. |
| `planning-artifacts/epics.md` | 584 (Story 3.4 live-validation gate, added by Epic 2 retro) | "`get_table_fields` MUST return results for at least one custom table that include an OptionSet field with option values and display labels populated" | **Interpreted as already-cleared (parent override).** This is a `review → done` transition gate, not a forever-invariant. Story 3.4 transitioned `review → done` on 2026-04-20 when `get_table_fields` did emit `options[]`; the gate fired and passed at that moment. R-HF-10 is a forward change to tool output and does not re-open the prior transition. **No edit applied here.** Re-evaluation of the gate's wording (so future similar gates are not phrased as forever-invariants) is owed to the Epic 3 retrospective. |
| `planning-artifacts/epics.md` | 1015 (Story 4.8 Mermaid) | "cascade-on-delete label" only | **Aligned.** |
| `implementation-artifacts/story-3.4-dataverse-tools.md` | 14, 15 (AC-2, AC-3) | Direct text of ACs being amended | **Direct edit** (this proposal). |
| `implementation-artifacts/story-3.4-dataverse-tools.md` | 33, 40, 95, 128, 129 (subtasks + dev notes + review findings) | Historic record of what shipped under the wider AC | **Preserve as-is** per parent decision. Code change is the corrective action, not a spec rewind. |
| `implementation-artifacts/story-3.5-mode1-generation.md` | 74 | Section 4 docx already uses `Cascade Delete` (singular) only | **Aligned with slim.** |
| `implementation-artifacts/HANDOFF-PROMPT-2026-05-15-r-hf-10-decision.md` | (entire) | Authorising diagnostic + plan | **Reference document; no edit.** |

### Architecture Impact

**None expected.** `architecture.md` cites tool **names** (Section 5 Custom Tool Inventory) and **file paths** (file-tree diagrams at lines 136-137 and 775-776) only. No field-shape detail anywhere. Verified by grep for `cascade|picklist|optionset|cascadeConfiguration|referencingEntity|referencedEntity`. No edit required.

### Technical Impact (downstream code-change footprint)yeah

The developer agent will implement (under R-HF-10, post-correct-course):

1. **`src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs`**
   - Drop `options[]` array emission (picklist / MultiSelect / State / Status branches via the reflection cache).
   - Drop `defaultValue` (already deferred per story-3.4 dev notes; this codifies the deferral into the AC).
   - Tighten `OptionDto` and related types as needed (or delete if unreferenced).
2. **`src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs`**
   - Reduce `cascadeConfiguration { delete, assign, share, unshare }` to single `cascadeDelete` (string, mapped from `CascadeConfiguration.Delete`).
   - Rename `referencingEntity`/`referencedEntity` pair to singular `relatedEntity` (the non-self table on the edge; parent is implicit from dictionary key per PromptBuilder.cs:52-65).
3. **`tests/DataverseDocAgent.Tests/GetTableFieldsToolTests.cs`** — update fixtures to assert slim shape; drop OptionDto/options[] assertions; pin negative case (`options[]` never emitted).
4. **`tests/DataverseDocAgent.Tests/GetRelationshipsToolTests.cs`** — same: pin slim cascade single-string and `relatedEntity` rename.

**Expected payload reduction at iter=3:** ~80k tokens → **~20–30k tokens** per handoff doc. Claude latency at 20–30k input is sub-30s — well inside the R-HF-6 10-min HttpClient.Timeout.

---

## Section 3 — Recommended Approach

### Selected: Direct Adjustment

Modify Story 3.4 acceptance criteria AC-2 and AC-3 inline. No new stories. No epic restructure. No PRD MVP change.

### Rationale

- **Scope is genuinely Story-level.** The change is "narrow two ACs to match what PromptBuilder actually consumes." It does not redefine any FR, does not affect Epic 3's exit gate (PRD §7.1 Typical-tier), and does not change Epic 4's shape.
- **Code is already the source of friction**, not the spec. The wider AC was honoured at Story 3.4 close (2026-04-20) because PromptBuilder did not yet exist (Story 3.5, 2026-05-14). Once PromptBuilder shipped, the wider tool output was retroactively confirmed as dead weight by direct file inspection. R-HF-10 simply removes the dead weight.
- **Forward-compatibility verified.** Epic 4 Mermaid (FR-049, Story 4.8) consumes only cascade-on-delete; Story 4.3 record-stats is orthogonal; Story 4.9 two-pass prompt is orthogonal. No future Epic 4 story re-introduces the dropped fields.
- **Escape hatch documented.** Each AC amendment includes a forward-pointer: "If a Mode 1 consumer ever requires picklist option labels [or full cascade quad], scope a dedicated `get_picklist_options(tableName, fieldName)` [or `get_relationship_cascade(schemaName)`] tool under Epic 4." This preserves optionality at zero current cost.

### Alternatives considered

| Option | Effort | Risk | Verdict |
|--------|--------|------|---------|
| Rollback Story 3.4 (revert tool output to slim) | Medium | High — discards verified test coverage on a `done` story | **Rejected.** The story shipped fine against its original AC; only the AC was over-specified relative to actual downstream consumption. |
| MVP review / PRD §7.1 amendment | High | Major — invokes PM/Architect | **Rejected — over-scoped.** PRD §7.1 already scopes Epic 3 to Typical-tier; no PRD change needed. |
| New story under Epic 4 for slim payload | Medium | Delays Typical-tier demo by weeks | **Rejected — R-HF-10 is hours of dev work; one new story for that is process overhead.** |

### Effort / risk / timeline

- **Effort:** Hours (R-HF-10 implementation — two tool files + two test files).
- **Risk:** Low. PromptBuilder consumes only the slim shape (verified). Existing test suite catches AC drift on the dropped fields (will fail until tests are updated — explicit and noisy, not silent).
- **Timeline impact:** None negative; **positive** for Epic 3 exit gate (unblocks Typical-tier E2E demo). Large-tier Hard-fail remains Epic 4 territory regardless.

---

## Section 4 — Detailed Change Proposals

### 4.1 Story 3.4 — AC-2 amendment

**File:** `_bmad-output/implementation-artifacts/story-3.4-dataverse-tools.md`
**Section:** Acceptance Criteria, item 2

**OLD:**
> `GetTableFieldsTool` accepts a `tableName` (logical name) parameter, queries all attributes where `IsCustomAttribute = true` for that table, and returns a JSON array including: `displayName`, `logicalName`, `attributeType`, `requiredLevel`, `defaultValue` (if set), and for OptionSet/MultiSelectPicklist types, an `options` array of `{ label, value }` pairs.

**NEW:**
> `GetTableFieldsTool` accepts a `tableName` (logical name) parameter, queries all attributes where `IsCustomAttribute = true` for that table, and returns a JSON array including: `displayName`, `logicalName`, `attributeType`, `requiredLevel`, `description`. **Phase 2.5 scope reduction (R-HF-10):** picklist `options[]` and `defaultValue` are NOT emitted — `PromptBuilder.cs:52-65` never consumes them and each picklist column inflates `tool_result` payloads ~60 chars × ~8 options. If a Mode 1 consumer ever requires picklist option labels, scope a dedicated `get_picklist_options(tableName, fieldName)` tool under Epic 4.

**Rationale:** PromptBuilder is the ONLY consumer of `get_table_fields` output and reads only five keys (`logicalName`, `displayName`, `attributeType`, `requiredLevel`, `description`). Confirmed via direct file read at `src/DataverseDocAgent.Api/Agent/PromptBuilder.cs:52-65` (cited in the R-HF-10 handoff doc).

### 4.2 Story 3.4 — AC-3 amendment

**File:** `_bmad-output/implementation-artifacts/story-3.4-dataverse-tools.md`
**Section:** Acceptance Criteria, item 3

**OLD:**
> `GetRelationshipsTool` accepts a `tableName` (logical name) parameter, returns all 1:N relationships where `IsCustomRelationship = true` involving that table as either the referencing or referenced entity, and all N:N custom relationships. Each entry includes: `relationshipType` ("OneToMany" or "ManyToMany"), `schemaName`, `referencingEntity`, `referencedEntity`, and `cascadeConfiguration` (delete, assign, share, unshare behaviours as strings).

**NEW:**
> `GetRelationshipsTool` accepts a `tableName` (logical name) parameter, returns all 1:N relationships where `IsCustomRelationship = true` involving that table as either the referencing or referenced entity, and all N:N custom relationships. Each entry includes: `relationshipType` ("OneToMany" or "ManyToMany"), `schemaName`, `relatedEntity` (the non-self table on the edge), and `cascadeDelete` (string, mapped from `CascadeConfiguration.Delete`). **Phase 2.5 scope reduction (R-HF-10):** `assign` / `share` / `unshare` cascade behaviours and the separate `referencingEntity`/`referencedEntity` pair are dropped — PromptBuilder consumes only `cascadeDelete` and emits the entry under the owning table's key (so the related side is implicit). If documentation ever needs the full cascade quad, scope a dedicated `get_relationship_cascade(schemaName)` tool under Epic 4.

**Rationale:** PromptBuilder.cs:59-65 confirms `cascadeDelete` is the only cascade field consumed; `relatedEntity` (singular) replaces the referencing/referenced pair because the parent table is implicit from the dictionary key (relationships are emitted under the owning table's logical name). Epic 4 Story 4.8 Mermaid (FR-049) likewise consumes only cascade-on-delete, so the slim shape is forward-compatible.

### 4.3 Story 3.4 — preserved sections

- **AC-1, AC-4, AC-5, AC-6** — unchanged. Numbering preserved.
- **Status** — remains `done`. Code will be brought into compliance by R-HF-10 implementation (forward), not by rewinding the spec (backward).
- **Dev Agent Record, Completion Notes, File List, Review Findings** — preserved as historic record of what shipped under the original AC. The R-HF-10 amendment is forward-looking only.
- **Change Log** — append one row dated 2026-06-23 documenting the AC narrowing and cross-referencing this proposal.

### 4.4 deferred-work.md — new R-HF-10 entry

**File:** `_bmad-output/implementation-artifacts/deferred-work.md`
**Section:** New section inserted near the top, under the existing "Architectural finding — 2026-05-14 evening E2E run (Epic 3 retro material)" section.

**New heading:** `## R-HF-10 — Tool payload slim (2026-06-23)`

**Body (single paragraph):** Captures hotfix scope (drop `options[]` + cascade detail + referencingEntity/referencedEntity pair), evidence basis (5/14 E2E iter=3 context bloat), Typical-tier exit gate alignment (PRD §7.1), authorising artifact (this proposal), and expected payload reduction (~80k → ~20–30k tokens at iter=3 per handoff doc).

### 4.5 NOT changed by this proposal

- `epics.md` lines 567 + 572 — **amended in this pass via parent (PO) override** after the impact scan surfaced the staleness; subagent flagged, parent decided amend-now to avoid epic-prose drift. Inline notes cross-reference this proposal.
- `epics.md` line 584 (live-validation gate) — **interpreted as a `review → done` transition gate that already fired and cleared on 2026-04-20**; no edit applied. Phrasing re-evaluation deferred to Epic 3 retrospective.
- `epics.md` line 25 (FR-002 prose: "default value, option set values/labels") — **deferred to Epic 3 retrospective.** PRD-level FR-002 reconciliation is out of scope for a story-level correct-course.
- `architecture.md` — no edit required; verified no field-shape conflict.
- `docs/prd.md` — no edit required; FR-002 prose divergence at epics.md:25 is the same surface, deferred to parent.
- Source code under `src/` and tests under `tests/` — parent instruction is no source edits in this correct-course pass; developer agent implements per amended ACs after this proposal is committed.

---

## Section 5 — Implementation Handoff

### Scope classification

**Minor.** Story-level AC tightening. Direct implementation by the Developer agent.

### Handoff recipient

**Primary:** Developer agent (Amelia / `bmad-agent-dev`) — implement R-HF-10 code edits per amended Story 3.4 AC-2 and AC-3.

**Secondary (Epic 3 retrospective, deferred):** two follow-ups owed to the upcoming `bmad-retrospective` for Epic 3 — (a) `epics.md:584` live-validation gate phrasing (currently reads as forever-invariant; should be reworded to bind only at the `review → done` transition); (b) `epics.md:25` FR-002 PRD-level prose (mentions "default value, option set values/labels") — needs PRD reconciliation pass. Both are explicitly out of scope for this story-level correct-course.

### Developer deliverables (R-HF-10 implementation pass)

1. **`GetTableFieldsTool.cs`** — drop `options[]` emission; drop `defaultValue` slot; tighten DTO surface.
2. **`GetRelationshipsTool.cs`** — reduce cascade block to single `cascadeDelete`; rename to `relatedEntity`.
3. **Test suite** — update `GetTableFieldsToolTests` + `GetRelationshipsToolTests` to pin slim shape; assert dropped fields never emitted (negative assertions); remove fixtures that hydrated `options[]`.
4. **Build + 286-test suite green** before commit.
5. **One paid Mode 1 E2E against `orgd76c9cf3`** — read the `[AgentOrchestrator] iter=N/200` heartbeat to confirm context bloat is reduced (target: iter=3 input ≤ 30k tokens).

### Success criteria

- Story 3.4 file shows amended AC-2 + AC-3 text (decisions 4 + 5) and the new Change Log row dated 2026-06-23.
- `deferred-work.md` shows the new "R-HF-10 — Tool payload slim (2026-06-23)" section near the top.
- Code under `src/DataverseDocAgent.Api/Agent/Tools/` no longer emits `options[]` or the wider cascade quad.
- Test suite remains 286/286 (or whatever the new count is after slim-shape pinning).
- One Mode 1 E2E against `orgd76c9cf3` either (a) completes successfully → Typical-tier exit gate met; commit + push + PR; mark Epic 3 done; OR (b) still fails → commit + push for forensic value, freeze, open Epic 3 retro per the handoff doc.

### Things explicitly NOT in this proposal

- No PRD §7.1 amendment (Typical-tier scope is already correct).
- No new story creation (R-HF-10 is hotfix territory, not new-story territory).
- No Epic 4 acceleration decision (that is the parent's call, post-R-HF-10 result).
- No architecture.md edit (verified no field-shape impact).
- No source code edit in this correct-course pass (developer agent's job after acceptance).
- No `MEMORY.md` update (parent's responsibility).
- No git operations (parent's responsibility).

---

## Appendix — Provenance

- **Diagnostic source:** [`../implementation-artifacts/HANDOFF-PROMPT-2026-05-15-r-hf-10-decision.md`](../implementation-artifacts/HANDOFF-PROMPT-2026-05-15-r-hf-10-decision.md)
- **PromptBuilder field consumption:** `src/DataverseDocAgent.Api/Agent/PromptBuilder.cs:52-65` (cited via handoff doc; not re-read in this correct-course pass per parent instruction "do not modify source code")
- **PRD scope reference:** `docs/prd.md` §7.1 NFR-001 — Typical-tier ≤50 tables; Large-tier `[TBD — POC]`
- **Epic 3 exit gate reference:** `_bmad-output/planning-artifacts/epics.md:405` (per handoff doc)
- **Architectural follow-on stories (Large tier):** Story 4.3 (ADR-004 batching) + Story 4.9 (ADR-008 two-pass)

---

*Generated 2026-06-23 by BMAD `bmad-correct-course` workflow. Mode: Batch. Scope: Minor. Selected path: Direct Adjustment.*
