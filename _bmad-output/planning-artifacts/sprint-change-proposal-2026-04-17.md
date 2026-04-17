# Sprint Change Proposal — Epic 2 Retrospective Findings Applied to Epic 3 Plan

**Date:** 2026-04-17
**Triggering retrospective:** `_bmad-output/implementation-artifacts/epic-2-retro-2026-04-17.md`
**Author:** Amelia (Developer) via `bmad-correct-course`
**Mode:** Batch (directives already explicit in retro document and user invocation)
**Approval posture:** Changes applied inline per autopilot workflow (see `feedback_autopilot_workflow`).

---

## Section 1 — Issue Summary

The Epic 2 retrospective surfaced two findings that alter the Epic 3 plan **before** story creation begins:

1. **Rate limiting is a cross-cutting credential-protection concern.** `POST /api/security/check` (story 2.2, shipped) and `POST /api/document/generate` (story 3.5, planned) both accept raw Dataverse credentials in the request body. Without per-client throttling, either endpoint can be used as a credential-probing oracle. NFR-018 originally deferred rate-limit enforcement to Phase 3; the retrospective (Significant Discovery #3) upgrades this to a P2 exit-gate item.
2. **"Validated by design" is unsafe for external-system acceptance criteria.** Story 2.3 transitioned to `done` on XML + unit-test evidence, then AC6 live execution against environment `orgd76c9cf3` surfaced four real bugs (missing `prvReadUser`, phantom `prvReadRolePrivilege`, `prvReadSavedQuery` → `prvReadQuery` rename, SharePoint DMI baseline privileges). Story 3.4 ships three Dataverse tools in a single story and carries the same failure mode at triple scale. Live-environment validation must be an explicit AC, not an inferred one.

**Evidence:**
- Retro Significant Discoveries #2, #3 (pre-existing in committed retro doc).
- Story 2.3 AC6 near-miss documented in `story-2.3-security-role-artefact.md` review findings (pre-existing).
- PRD NFR-018 phase-gating text (`docs/prd.md:1445–1452`) and architecture ADR-009 (`docs/architecture.md:588–606`).

---

## Section 2 — Impact Analysis

### Epic Impact

| Epic | Status | Change |
|---|---|---|
| Epic 1 | COMPLETE | No change. |
| Epic 2 | DONE + retro conducted | No change. |
| **Epic 3** | IN PROGRESS (stories not yet started) | **Insert story 3.0**; **strengthen story 3.4 AC text**. |
| Epic 4 | NOT STARTED | NFR-018 references now explicitly phased: P2 gets enforced policy on credential endpoints; P3+ keeps API-key partitioning upgrade as originally planned. No structural change. |
| Epic 5 | NOT STARTED | No change. |

### Story Impact

- **NEW:** Story 3.0 — Rate Limiting on Credential-Accepting Endpoints. Ships `AddRateLimiter` named policy `credential-endpoints`, attached to `POST /api/security/check` immediately and to `POST /api/document/generate` when story 3.5 lands. Dependency gate: story 3.5 cannot transition to `done` until 3.0 is `done`.
- **MODIFIED:** Story 3.4 — Live-Dataverse validation gate added as an explicit AC set. Evidence (env name, date, tool call summary) required in the story's Debug Log section before `review` → `done`.
- No other stories altered.

### Artifact Conflicts

| Artifact | Conflict? | Resolution |
|---|---|---|
| PRD (`docs/prd.md`) | NFR-018 language unchanged but tightened at Epic 3 layer. | No PRD edit required — NFR-018 is a policy envelope; Epic 3 policy now lives inside that envelope. Architecture ADR-009 already envisaged `AddRateLimiter` registration in P2 (line 642: "Register `AddRateLimiter` (permissive)"); Story 3.0 changes permissive → enforced on credential endpoints. |
| Architecture (`docs/architecture.md`) | ADR-009 "permissive in P2" phrase is now stricter on credential endpoints. | Optional follow-up: refine ADR-009 Phase Plan row for P2. Not a blocker — architecture decision unchanged, only policy tuning. Deferred to opportunistic edit. |
| UX / Spec | None. | N/A. |
| `sprint-status.yaml` | 3.0 not present; 3.6 / 3.7 also missing (pre-existing gap). | Updated: 3.0 added as `backlog`; 3.6 / 3.7 registered as `backlog` to reconcile with `epics.md`. |
| Privacy policy (`docs/privacy-policy.md`) | Rate limiting does not change credential-handling claim. | No edit required. |

### Technical Impact

- No new NuGet packages (`Microsoft.AspNetCore.RateLimiting` ships with .NET 7+; confirmed in `epics.md` line 123 and `architecture.md` ADR-009).
- Story 3.0 configuration surface: `appsettings.json` — externalised window + permit count.
- Story 3.4 adds a process requirement (live env access at review-gate) but no code cost.

---

## Section 3 — Recommended Approach

**Selected: Option 1 — Direct Adjustment.**

Both changes fit inside the existing Epic 3 structure. No rollback, no MVP scope reduction. Story 3.0 is additive; Story 3.4 AC is a tightening, not a scope change.

- **Effort:** Low (3.0 is ~½ story day for registration + unit tests; 3.4 adds a review-gate procedure with zero code delta).
- **Risk:** Low — rate-limit policy is externally configurable, so if the chosen limit proves wrong under traffic it tunes without redeploy.
- **Timeline impact:** Story 3.0 sits ahead of story 3.1; total Epic 3 duration rises by roughly one story, but 3.0 can run in parallel with 3.1/3.2 since it has no dependency on the job infrastructure.
- **Alternatives considered:** Option 2 (rollback) — not applicable, no Epic 3 code exists yet. Option 3 (MVP review) — not applicable, MVP scope unchanged.

---

## Section 4 — Detailed Change Proposals

### 4.1 — Insert Story 3.0 into `_bmad-output/planning-artifacts/epics.md`

**Location:** Immediately before "Story 3.1: Async Job Infrastructure" under the Epic 3 header.

**Epic 3 header addendum (inserted):** Note block explaining the 3.0 dependency gate and the 3.4 live-validation gate, tagged "Epic 2 retrospective addendum (2026-04-17)".

**Story 3.0 full spec:** User story, acceptance criteria covering (a) `AddRateLimiter` registration per ADR-009, (b) named policy `credential-endpoints` attached to `POST /api/security/check`, (c) same policy applied to `POST /api/document/generate` at story 3.5 landing, (d) externalised config in `appsettings.json`, (e) per-IP partitioning in P2, (f) 429 + `Retry-After` + structured error body per NFR-014, (g) unit test coverage of the 429 path. Explicit dependency gate: 3.5 cannot transition to `done` until 3.0 is `done`.

**FR/NFR coverage tag:** `F-030 — NFR-014, NFR-018, NFR-007, ADR-009`.

### 4.2 — Strengthen Story 3.4 ACs in `_bmad-output/planning-artifacts/epics.md`

**Location:** Appended to Story 3.4 before the FR coverage tag.

**New AC block — "Live-Dataverse validation gate":** Each of the three tools must be executed at least once against a named real Dataverse environment using the `DataverseDocAgent Reader` role. Specific content checks: non-empty custom-table result; at least one OptionSet field surfaced by `get_table_fields`; both a 1:N and (where available) an N:N relationship by `get_relationships`. Execution evidence recorded in Debug Log References before `review` → `done`. Any surfaced bug must be fixed in-story or deferred with a named follow-up story ID.

**Driver paragraph:** References the 2.3 AC6 near-miss explicitly so the rationale is discoverable at review time.

### 4.3 — Sprint status reconciliation in `_bmad-output/implementation-artifacts/sprint-status.yaml`

- Added `3-0-rate-limiting-on-credential-accepting-endpoints: backlog` at top of Epic 3 block.
- Added `3-6-publisher-prefix-intelligence: backlog` and `3-7-application-user-inventory: backlog` (pre-existing in `epics.md`, missing from sprint-status — reconciled in the same edit).
- Updated `last_updated` header to reflect course-correction origin.

---

## Section 5 — Implementation Handoff

**Scope classification: Moderate.** Backlog reorganisation (new story slot + AC change) without strategic replan.

| Handoff | Owner | Action |
|---|---|---|
| Create story 3.0 file | Developer agent via `bmad-create-story` (next step of current session) | Scaffold `_bmad-output/implementation-artifacts/story-3.0-rate-limiting.md`, mark `ready-for-dev` in sprint-status. |
| Story 3.0 implementation | Dev agent (future session) | `AddRateLimiter` registration, policy definition, endpoint attribute attachment, tests. |
| Story 3.4 review gate | Developer + reviewer (future) | Enforce the live-Dataverse validation block at `review` → `done`. |
| ADR-009 P2 phase-plan copy edit | Opportunistic, low priority | Refine "permissive" → "enforced on credential endpoints" when next touching `architecture.md`. Non-blocking. |

**Success criteria:**
- `epics.md` contains Story 3.0 and strengthened 3.4 ACs (✅ applied).
- `sprint-status.yaml` lists 3-0, 3-6, 3-7 under Epic 3 (✅ applied).
- Story 3.0 file scaffolded in a subsequent step (next task in the session plan).
- Story 3.4 cannot transition to `done` without live-validation evidence (enforced at review time).

---

## Approval

Applied inline under the autopilot workflow preference — user invocation carried explicit directives matching the retro's PREP-1 and PREP-2 action items. Proposal document retained at `_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-17.md` for audit trail.
