# Handoff — 2026-05-15 — R-HF-10 decision + Epic 3 retro framing

**Author:** prior-session Claude • **Branch:** `MultiAgent-Run` • **Status:** uncommitted hotfixes in working tree, E2E blocked, awaiting user decision on R-HF-10.

---

## TL;DR

Mode 1 against the user's `orgd76c9cf3` env (200+ tables) still fails E2E. Three hotfixes shipped tonight but are **uncommitted**. Latest failure (627.7s) is **not a regression** — three parallel investigation agents confirmed R-HF-6's 10-min HttpClient timeout is working correctly; the Claude API call genuinely took >10 min because the conversation context bloats to ~80k tokens by iter=3 and trends toward 1.5M tokens before all tables are visited (exceeding Sonnet 4.6's 200k context window).

**Architecturally** this is **Epic 4 territory** (Stories 4.3 + 4.9), not an Epic 3 bug. Epic 3's exit gate per PRD §7.1 is a Typical-tier env (≤50 tables); the user is testing a Large-tier env (>50 tables) which PRD scopes to Phase 2+.

**Decision pending:** Ship R-HF-10 (slim payload, hours of work) OR freeze and accelerate Epic 4. Recommended sequence: R-HF-10 slim first as a cheap hypothesis test → freeze either way (slim succeeds → Typical-tier MVP demo-ready + freeze for Large; slim fails → freeze with stronger evidence).

⚠️ **DO NOT RUN A FRESH E2E AGAINST `orgd76c9cf3` BEFORE PICKING A DIRECTION.** Each failed run costs ~$0.40 in Anthropic billing.

---

## Branch state

**Recent commits (already on `MultiAgent-Run`):**
```
40278ef Hotfix: Anthropic HttpClient timeout 100s → 10min (R-HF-6)
aeb7874 Hotfix: Mode 1 prose-preamble trim (R-HF-5)
3fcf8c7 Hotfix: Mode 1 E2E runtime hardening (catch order + MaxTokens + JSON diag)
01f80db Fix DI lifetime: IDataverseConnectionFactory Scoped → Singleton
698288b Apply story 3.7 code review patches (P1-P16)
```

**Working tree (UNCOMMITTED — first task tomorrow is to commit or revert):**
```
 D _bmad-output/implementation-artifacts/HANDOFF-PROMPT-stories-3.6-3.7.md  (stale, cosmetic)
 M _bmad-output/implementation-artifacts/deferred-work.md                    (Epic 3 retro material + R-HF-7/8/9 entries)
 M src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs                       (R-HF-7 MaxTokens 64k + R-HF-8 iter logs + R-HF-9 tool-throw log)
 M src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs  (R-HF-9 forensic dump + Anthropic-network split)
 M src/DataverseDocAgent.Api/Jobs/GenerationBackgroundService.cs              (R-HF-8 configurable PerTaskTimeout)
 M src/DataverseDocAgent.Api/appsettings.json                                 (R-HF-8 Generation:PerTaskTimeoutMinutes=30)
 M tests/DataverseDocAgent.Tests/GenerationBackgroundServiceTests.cs          (DI signature for R-HF-8)
?? _bmad-output/implementation-artifacts/story-3.6-diff.patch                 (review-only artifact)
?? _bmad-output/implementation-artifacts/story-3.7-diff.patch                 (review-only artifact)
```

**Tests:** 286/286 pass. Build clean.

---

## Hotfixes shipped tonight (2026-05-14 evening) — UNCOMMITTED

### R-HF-7 — `MaxTokens` 16384 → 64000

**Why:** Even after R-HF-3 raised the budget to 16384, the user's env produced a 67,231-char Mode 1 final JSON that hit the cap mid-stream. The R-HF-4 forensic dump pinpointed truncation. Sonnet 4.6's non-thinking ceiling is 64000; output cost is metered by tokens produced (not budget cap), so smaller envs are not penalised.

**Files:** `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs` (`MaxTokens = 64000`; plus a `Console.Error.WriteLine` if the final iteration's `StopReason != "end_turn"` so a future `max_tokens` truncation names itself before the downstream JSON parse).

### R-HF-8 — `PerTaskTimeout` 10 → 30 min, configurable; per-iteration progress log

**Why:** The user's env hit the hard-coded 10-min job timeout at 599.8s. Architecturally the 10-min number was tied to PRD AC-9; raised default to 30 with an override hook so the cap can be tuned without rebuild.

**Files:**
- `src/DataverseDocAgent.Api/Jobs/GenerationBackgroundService.cs` — `DefaultPerTaskTimeout = 30 min`; reads `Generation:PerTaskTimeoutMinutes` from `IConfiguration`; logs effective value at startup (`GenerationBackgroundService: per-task timeout = 30 min`).
- `src/DataverseDocAgent.Api/appsettings.json` — `Generation:PerTaskTimeoutMinutes: 30`.
- `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs` — per-iter log: `[AgentOrchestrator] iter=N/200 stop=tool_use elapsed=Xs` + a `tools=[...] count=N` line for each round.
- `tests/DataverseDocAgent.Tests/GenerationBackgroundServiceTests.cs` — updated both `new GenerationBackgroundService(...)` call sites to pass `new ConfigurationBuilder().Build()`.

### R-HF-9 — Forensic exception dump + Anthropic-vs-Dataverse network-fault split + per-tool throw log

**Why:** A `DATAVERSE_ERROR (inner=HttpRequestException)` had no message, no stack, no source — undiagnosable. Also the catch chain mis-classified Anthropic-side `HttpRequestException` as Dataverse (R-HF-2 fixed this only for `RateLimitsExceeded`).

**Files:** `src/DataverseDocAgent.Api/Features/DocumentGenerate/DocumentGenerateService.cs`
- New `FormatExceptionForLog(Exception ex)` — bounded 4-level dump of `[depth] FullName (Source=Assembly) / Message / Stack` at WARN.
- New `IsAnthropicNetworkFault(Exception ex)` — inspects `Source` + stack frames for `Anthropic.SDK` markers; routes to `AI_ERROR` instead of `DATAVERSE_ERROR` when matched.
- Forensic dump emitted before every catch wrap (FaultException, RateLimitsExceeded, network filter, unclassified).

Also `src/DataverseDocAgent.Api/Agent/AgentOrchestrator.cs` — per-tool catch now emits `[AgentOrchestrator] tool 'X' threw <type>: <message> @ <first frame>` so tool-level Dataverse faults surface immediately instead of vanishing into the JSON returned to Claude.

**NFR-007 caveat:** Forensic dump may carry tenant/host fragments. Acceptable for dev-time POC; must be scrubbed before production. Credentials are NOT in scope — orchestrator never sends them to Claude; ServiceClient does not echo `ClientSecret` in exception text.

---

## Critical finding — tonight's iter=3 timeout (`AI_ERROR (inner=TaskCanceledException)`)

Logged in full at the top of `_bmad-output/implementation-artifacts/deferred-work.md` under the section **⚠️ Architectural finding — 2026-05-14 evening E2E run (Epic 3 retro material)**.

Three parallel investigation agents (HttpClient forensics / per-iter payload analysis / spec alignment) converged on:

1. **R-HF-6 IS working.** Reflection on `Anthropic.SDK` 5.10.0 confirmed our injected `HttpClient { Timeout = 10min }` IS the one the SDK uses. The 600s in the trace IS our config firing. The Claude API call itself took >10 min.

2. **Real bottleneck is context bloat, NOT network.**
   - iter=1: ~12,500 tokens added (`list_custom_tables` × 200+ tables)
   - iter=2: ~65,750 tokens added (15 parallel tool results)
   - **iter=3 Claude input: ~80,000 tokens already**
   - Trajectory: **~1.5M tokens by iter=30** — exceeds Sonnet 4.6's 200k context window before all 200 tables are visited
   - Run was destined to fail regardless of timeout

3. **Cost: ~$0.40 per failed run today.** Successful Large-tier run would be **~$5–15** if the architecture allowed it — it does not.

4. **NFR-001 is `[TBD — POC]` in PRD §7.1.** Epic 3's exit gate (`epics.md:405`) is *"first customer receives and validates a generated document"* — a Typical-tier (≤50 tables) doc. **Large-tier (>50 tables) is Epic 4's contract.**

5. **Designed fixes for this scaling boundary are already in backlog:**
   - **Story 4.3** — ADR-004 parallel batching with 3-tier dispatch (Small/Medium/Large) for `get_table_record_stats`. Architecture mandates `Task.WhenAll` with default 10 concurrent.
   - **Story 4.9** — ADR-008 two-pass prompt (deterministic enrichment offline → structured JSON → second-pass narrative). Bounds per-call context regardless of env size.
   - **Gap, unowned:** `AgentOrchestrator` streaming + per-iteration HttpClient budget + payload-size budgeting for parallel `tool_result` fan-in. No story scopes this. R-HF-9 exposed it.

---

## Decision matrix for tomorrow

| Option | Effort | Outcome if success | Outcome if fail | Cost |
|---|---|---|---|---|
| **A. R-HF-10 slim payload** (recommended first move) | Hours | Typical-tier MVP demo-ready against `orgd76c9cf3`. Mark Epic 3 done at PRD's actual Typical-tier contract. Freeze for Large + Epic 4 normally. | Confirmed context bloat is structural — freeze with stronger evidence, accelerate 4.3 + 4.9. | 1 paid E2E (~$0.40) |
| **B. Freeze + accelerate** | Weeks | Architecturally clean. Stories 4.3 + 4.9 land. Mode 1 against Large tier works properly. | n/a | $0 in Anthropic, but no demo until Epic 4 ships |
| **C. Streaming spike** | 1–2 days | Buys runway past 600s wall (HttpClient.Timeout applies to headers, not stream body). | Doesn't fix context-window ceiling. Band-aid only. | Token cost + dev time |
| **D. Parallelize orchestrator (preempt ADR-004)** | Hours | Faster tool round-trips. | Doesn't help iter=3 — sequential vs parallel both feed the same Claude input volume. | 1 paid E2E |

**Sequence I'd recommend:**
```
1. Commit R-HF-7/8/9 (one or three commits — three is cleanest)
2. Ship R-HF-10 slim payload (Option A)
3. One paid E2E against orgd76c9cf3
4. Result decides:
   ✅ doc generates → mark Epic 3 done at Typical-tier, demo, freeze for Large, Epic 4 normally
   ❌ still fails    → freeze immediately, open Epic 3 retro, pull 4.3 + 4.9 forward
```

---

## R-HF-10 slim — concrete plan (if you go this route)

**Goal:** Drop unused fields from `get_table_fields` and `get_relationships` so per-iter `tool_result` payloads shrink ~60–80%. No new tools, no orchestrator changes, no architectural shift.

**Prompt contract verification** (`src/DataverseDocAgent.Api/Agent/PromptBuilder.cs:52-65`):
```jsonc
"fields": {
  "<tableLogicalName>": [
    { "logicalName": "<string>", "displayName": "<string|null>",
      "attributeType": "<string|null>", "requiredLevel": "<string|null>",
      "description": "<string|null>" }
  ]
}
```
Prompt uses only these 5 keys per field. Currently `GetTableFieldsTool` emits full `options[]` (picklist values, ~60 ch × 8 per picklist column). Pure waste.

```jsonc
"relationships": {
  "<tableLogicalName>": [
    { "schemaName": "<string|null>", "relationshipType": "<string|null>",
      "relatedEntity": "<string|null>", "cascadeDelete": "<string|null>",
      "businessMeaning": "<short AI-inferred description|null>" }
  ]
}
```
Prompt uses only `cascadeDelete`. `GetRelationshipsTool` currently emits full `cascadeConfiguration` detail (4 strings — Assign/Delete/Merge/Reparent). Drop all but Delete.

**Concrete steps:**

1. **Read** `src/DataverseDocAgent.Api/Agent/Tools/GetTableFieldsTool.cs`. Identify where `options[]` is emitted on picklist columns. Remove from the tool's JSON result. Update the tool's docstring + any unit-test fixtures.
2. **Read** `src/DataverseDocAgent.Api/Agent/Tools/GetRelationshipsTool.cs`. Identify the `cascadeConfiguration` object. Reduce to `cascadeDelete` only (single string, mapped from `CascadeConfiguration.Delete`).
3. **Tests:** Pin the slimmed shape with one new assertion per tool. Existing tests that assert full payload shape will fail — update them to the slim contract.
4. **Build + 286-test suite** — must stay green.
5. **One paid E2E against `orgd76c9cf3`** — read the iter heartbeat (`[AgentOrchestrator] iter=N/200`) to confirm context bloat is reduced.
6. **If success:** mark Epic 3 done at Typical-tier, commit R-HF-7/8/9 + R-HF-10, push, open PR.
7. **If failure:** commit R-HF-7/8/9 + R-HF-10 anyway (forensic value), freeze, open Epic 3 retro.

**Estimated payload reduction at iter=3:** ~80k tokens → ~20–30k tokens. Claude latency at 20–30k input is sub-30s typically — well inside the 10-min HttpClient.Timeout.

---

## Cold-start command sequence

```pwsh
# 1. Confirm working tree (R-HF-7/8/9 still uncommitted, branch unchanged)
git status --short
git log --oneline -5

# 2. Verify build + tests
dotnet build -nologo -v q
dotnet test tests\DataverseDocAgent.Tests --no-build --nologo -v q
# Expect: 286/286 pass

# 3. Confirm secrets present (UserSecretsId 7d4ee0df-7e42-41be-9616-f5c8d3b7935e)
dotnet user-secrets list --project src\DataverseDocAgent.Api
# Expect 5 keys: Anthropic:ApiKey, Dataverse:TestCredentials:Url/ClientId/ClientSecret, tenant id

# 4. Read the architectural finding before any code change
# (see _bmad-output/implementation-artifacts/deferred-work.md top section)

# 5. Pick A/B/C/D from the decision matrix and execute.
#    Recommended: A (R-HF-10 slim payload).
```

---

## Memory pointers

- `MEMORY.md` index: `C:\Users\oaala\.claude\projects\D--Development-DataverseDocAgent\memory\MEMORY.md`
- `project-state.md` — current phase, known gaps. **Update after picking direction.**
- `feedback_autopilot_workflow.md` — Dev-test-commit-push loop, stop at <80% context

---

## Important context the new session will not have

- **User's environment** is `orgd76c9cf3.crm.dynamics.com` with ~200+ tables. Service account does NOT have `prvReadSystemUser` (`get_application_users` returns the structured "Failed to list application users" error — graceful fallback per AC-10 leaves Section 5 empty). Tracked separately as the carried Story 3.7 deferral on SecurityCheckService alignment.
- **Anthropic API key + Dataverse client secret** were exposed in an earlier conversation transcript. **Rotate before sharing logs externally.**
- **NFR-001 status:** still `[TBD — POC]`. The current run *is* the unbudgeted baseline-measurement work, paid for in failed Anthropic dollars. Capture into `docs/poc-baseline.md` once a Mode 1 run completes successfully (Typical-tier minimum).
- **Hotfix tally tonight:** R-HF-7, R-HF-8, R-HF-9. Pattern is unsustainable — see "Architectural finding" recommendation. Tomorrow's session must decide direction, not ship R-HF-10/11/12.

---

## DO NOT

- ❌ Run a paid E2E before R-HF-10 (or chosen direction) ships. Every failure ≈ $0.40.
- ❌ Bump `HttpClient.Timeout` further. R-HF-6 (10 min) is working as designed; the problem is upstream of timeout.
- ❌ Add new tools (`get_all_table_fields`, etc.) — that's Story 4.3's design space. Don't squat.
- ❌ Add streaming until the slim hypothesis is tested. Streaming doesn't fix context-window ceiling.
- ❌ Skip the 286-test run after slim — DI signature and tool payload tests will fail noisily on shape changes.
- ❌ Treat the iter=3 timeout as "another hotfix" — it is the symptom of the architectural boundary that Stories 4.3 + 4.9 are designed to fix.

---

*Generated 2026-05-14 evening, end of session. Next session: cold-start with this file.*
