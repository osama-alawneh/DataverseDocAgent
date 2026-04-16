# POC Baseline — Story 1.4

## Test Environment

| Field | Value |
|---|---|
| Environment | Dataverse (developer environment) |
| Environment URL | *(redacted — not logged per FR-034)* |
| Custom business tables (`doc_` prefix) | 7 |
| Total custom tables returned by `list_custom_tables` | ~400+ (includes platform, AI, portal, ALM, and junction tables) |

---

## Performance Measurements (AC: 2)

Two full runs executed against the test environment on 2026-04-15.

| Metric | Run 1 | Run 2 | Average |
|---|---|---|---|
| Dataverse connection time (ms) | 1320 | 1317 | 1319 |
| Claude agent loop time (ms) | 44265 | 29446 | 36856 |

**Notes:**
- Connection time is consistent (~1.3 s), using client credentials (service principal) auth — no interactive login.
- **Measurement boundary (connection):** figure includes both the OAuth token exchange and a `WhoAmIRequest` verification round-trip fired inside `ConnectAsync`. It is not purely auth handshake time.
- Agent loop time variance (29–44 s) is normal; driven by Claude's response generation time for a large table list (~400+ tables).
- **Measurement boundary (agent loop):** figure is wall-clock time from `RunAsync` entry (including SDK tool list construction) to return — not purely `Messages.CreateAsync`-to-`end_turn`. Pre-loop setup overhead is negligible for a one-tool POC but should be noted for Phase 2 comparisons.
- Both runs completed successfully end-to-end (no exceptions thrown; no iteration-limit sentinel returned).

---

## NFR-001 Assessment (AC: 2c)

NFR-001 target: full Mode 1 generation under 5 minutes for typical environments.

| Assessment | Result |
|---|---|
| Connection time vs. target | **Confirmed** — 1.3 s well within budget |
| Single-tool agent loop vs. target | **Confirmed for POC scope** — 30–44 s for one tool call |
| Full Mode 1 extrapolation | **Insufficient data — re-test in Phase 2** |

**Extrapolation note:** POC measures a single-tool loop (`list_custom_tables`). Full Mode 1 will call 8+ tool types across multiple documentation sections. Assuming linear scaling (unlikely to hold exactly), a 10-tool pipeline could take 5–7 minutes — near the NFR-001 boundary. Phase 2 should calibrate with real multi-tool runs before committing to the target. The NFR-001 target may need revision upward for large environments with 400+ tables.

---

## Credential Handling Code Review (AC: 3)

Reviewed files: `DataverseDocAgent.Api.Common.EnvironmentCredentials`, `DataverseDocAgent.Api.Dataverse.DataverseConnectionFactory`, `DataverseDocAgent.Api.Agent.AgentOrchestrator`, `DataverseDocAgent.Console.Program`.  
Review date: 2026-04-15. Code state: commit `c77914f0558ea24592bdd601813b8a14e84f44a7`.

| # | Item | Status | Notes |
|---|---|---|---|
| a | No credential values (`ClientSecret`, `ClientId`, `TenantId`) appear in any `Console.Write`, `Console.WriteLine`, log call, or exception `Message` string | **PASS** | Reviewed files confirmed. `Program.cs` catch blocks print `ex.Message` (not credential properties); SDK error message content was deferred to Phase 2 hardening. |
| b | `ClientSecret` is never passed as a raw `string` parameter outside the `EnvironmentCredentials` wrapper | **PASS** | `ClientSecret` is accessed only once — inside `DataverseConnectionFactory.ConnectAsync` as a constructor argument to `ServiceClient` (SDK auth boundary). Never passed to any user-defined method. |
| c | `EnvironmentCredentials` is not passed to `JsonSerializer.Serialize()` or any equivalent serialization call | **PASS** | No serialization call referencing `credentials` found across the codebase. `AgentOrchestrator` only serializes tool results and error objects. |

---

## Phase 1 Exit Gate (AC: 4)

**Overall result: PASSED**

| Gate item | Result |
|---|---|
| Pipeline works end-to-end (connect → tool call → Claude response) | PASS |
| Credential handling passes code review (all 3 items) | PASS |

Phase 1 is complete. The POC validates the full Anthropic SDK → tool call → Dataverse → Claude response pipeline. Credential values are not observable in any console output, exception message, or serialized payload.

**Risk item (NFR-001):** The performance gate is conditional. The POC measures a single-tool loop (30–44 s). Full Mode 1 will call 8+ tool types, and linear scaling is unlikely to hold: token context grows with each tool result (non-linear latency increase), each orchestrator iteration may dispatch multiple tools in parallel, and the orchestrator is currently capped at 10 total iterations. A 10-tool pipeline may approach or exceed the 5-minute NFR-001 target for large environments (~400+ tables). This risk must be re-evaluated with real multi-tool runs in Phase 2 before committing to the NFR-001 target.
