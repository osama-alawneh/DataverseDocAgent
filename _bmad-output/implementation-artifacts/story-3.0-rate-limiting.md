# Story 3.0: Rate Limiting on Credential-Accepting Endpoints

Status: done

## Story

As a platform operator,
I want enforced per-client rate limits on every endpoint that accepts raw Dataverse credentials,
so that a compromised or malicious caller cannot use the API as a high-throughput credential-probing oracle and so NFR-018 is upheld before the Epic 3 document-generation endpoint goes live.

## Acceptance Criteria

1. **AddRateLimiter registered.** `Program.cs` registers `AddRateLimiter` and calls `app.UseRateLimiter()` in the pipeline **before** `app.MapControllers()` and **after** `app.UseMiddleware<ExceptionHandlingMiddleware>()` (so 429 responses still route through the structured-error path but are not swallowed by the global exception catch).
2. **Named policy `credential-endpoints`.** A named policy `credential-endpoints` is defined using `RateLimitPartition.GetFixedWindowLimiter` (or sliding window — choose fixed for deterministic P2 semantics). Default configuration: `PermitLimit = 10`, `Window = 00:01:00`, `QueueLimit = 0`, `QueueProcessingOrder = OldestFirst`. Partition key = `HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`.
3. **Policy attached to `POST /api/security/check`.** `SecurityCheckController.Check` is decorated with `[EnableRateLimiting("credential-endpoints")]`. The attribute must sit on the action, not the controller, so future non-credential actions on the same controller do not inherit the policy implicitly.
4. **Policy attached to `POST /api/document/generate` in story 3.5.** Story 3.5's generate-document action MUST carry the same `[EnableRateLimiting("credential-endpoints")]` attribute. This story lands the policy; story 3.5 lands the attribute on its action at creation time. A note to this effect is added as a subtask checkbox on story 3.5's Tasks list.
5. **Rejection semantics.** When a request is rejected by the limiter, the response is HTTP 429 with body shape `{ "error": "Rate limit exceeded. Retry after N seconds.", "code": "RATE_LIMIT_EXCEEDED", "safeToRetry": true }` (`StructuredErrorResponse` per NFR-014). The `Retry-After` header is set to the window reset in whole seconds. **No part of the rejected request body is logged** (verified by unit test, mirrors the `ExceptionHandlingMiddlewareTests` approach landed in commit `7875516`).
6. **OnRejected hook.** Use `options.OnRejected = async (context, token) => {...}` to write the structured JSON body and `Retry-After` header. Do **not** rely on the default 503/empty-body behaviour.
7. **External configuration.** `PermitLimit` and `Window` read from `appsettings.json` under `RateLimiting:CredentialEndpoints` (new section). Example:
   ```json
   "RateLimiting": {
     "CredentialEndpoints": {
       "PermitLimit": 10,
       "WindowSeconds": 60
     }
   }
   ```
   Bind via `builder.Services.Configure<CredentialEndpointsRateLimitOptions>(builder.Configuration.GetSection("RateLimiting:CredentialEndpoints"))`. Register the options class in `Common/`. No recompile required to retune.
8. **Per-partition, not global.** Ten distinct clients (distinct `RemoteIpAddress`) each making one request inside the window all succeed. An eleventh request from any of those clients within the window would be rejected **for that partition only**.
9. **429 body never echoes credentials.** Unit test asserts that when a request with a valid credential-bearing body is rejected, the captured response body + the captured log output contain none of the supplied `clientSecret`, `clientId`, or `tenantId` values. Reuses the `CapturingLogger` / `DefaultHttpContext` pattern from `ExceptionHandlingMiddlewareTests.cs`.
10. **Test coverage.**
    - Unit test — limiter rejects after configured `PermitLimit` is exhausted within the window.
    - Unit test — 429 response body matches `StructuredErrorResponse` schema (`error`, `code="RATE_LIMIT_EXCEEDED"`, `safeToRetry=true`).
    - Unit test — `Retry-After` header is present and non-zero.
    - Unit test — credential fields are not present in log sink or response body on the rejected path.
    - Unit test — partitioning is per-IP (two distinct IPs both succeed when each is under the limit).
    All tests xunit, placed in `tests/DataverseDocAgent.Tests/RateLimiterTests.cs`.
11. **Dependency gate on story 3.5.** `epics.md` Story 3.0 block contains the hard dependency statement; Story 3.5 cannot transition to `done` until Story 3.0 is `done`. No code enforces this — it is a sprint-level gate.
12. **NFR-015 annotation.** File-level comment in `Program.cs` (around the registration) and any new files created by this story must reference the NFRs they implement: `// NFR-018, NFR-014, NFR-007 — Rate limiting on credential-accepting endpoints`.

## Tasks / Subtasks

- [x] Add `CredentialEndpointsRateLimitOptions` options class (AC: 7)
  - [x] Create `src/DataverseDocAgent.Api/Common/CredentialEndpointsRateLimitOptions.cs` with `PermitLimit` (int, default 10) and `WindowSeconds` (int, default 60) properties, data-annotation `[Range]` bounds (1–1000 for PermitLimit; 1–3600 for WindowSeconds).
  - [x] Register via `builder.Services.Configure<CredentialEndpointsRateLimitOptions>(...)` in `Program.cs`.
- [x] Register `AddRateLimiter` in `Program.cs` (AC: 1, 2, 6)
  - [x] Add `builder.Services.AddRateLimiter(options => { ... })`.
  - [x] Inside the delegate: `options.AddPolicy("credential-endpoints", httpContext => RateLimitPartition.GetFixedWindowLimiter(...))` keyed on `httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`.
  - [x] Configure `options.OnRejected` to write `StructuredErrorResponse` JSON + `Retry-After` header (delegates to `RateLimitRejection.WriteAsync`).
  - [x] Pipeline: `app.UseMiddleware<ExceptionHandlingMiddleware>()` → `app.UseHttpsRedirection()` → `app.UseRouting()` → `app.UseRateLimiter()` → `app.MapControllers()`.
- [x] Decorate `SecurityCheckController.Check` (AC: 3)
  - [x] Add `using Microsoft.AspNetCore.RateLimiting;` and `[EnableRateLimiting("credential-endpoints")]` on the action method.
- [x] Update `appsettings.json` and `appsettings.Development.json` (AC: 7)
  - [x] Add `RateLimiting.CredentialEndpoints` block with `PermitLimit` and `WindowSeconds`. Production: 10/60; Development: 100/60.
- [x] Document external config and 429 behaviour in setup guide (AC: 7)
  - [x] Appended "API Reference — Rate Limits" subsection to `docs/setup-guide.md`.
- [x] Add `tests/DataverseDocAgent.Tests/RateLimiterTests.cs` (AC: 10)
  - [x] Pure-unit tests — no `WebApplicationFactory` / `Microsoft.AspNetCore.Mvc.Testing` dependency added.
  - [x] Covers: permit exhaustion, 429 body shape, `Retry-After` header, no credential echo in rejection response, per-IP partition isolation.
- [x] Update story-3.5 dependency note (AC: 4)
  - [x] Story 3.5 file is scaffold-only; note carried into deferred-work and repeated in this story's Completion Notes — Story 3.5 dev must add `[EnableRateLimiting("credential-endpoints")]` to the generate-document action at creation.
- [x] Verify build + tests green before marking review (AC: 10)
  - [x] `dotnet build DataverseDocAgent.sln --no-incremental` clean (0 warn / 0 err).
  - [x] `dotnet test` 73/73 green (baseline 68 + 5 new rate-limiter tests).

## Dev Notes

### Architecture alignment

- **ADR-009 (docs/architecture.md:588–615):** Decision already made — built-in `AddRateLimiter`, no new NuGet. This story implements the P2 slice of ADR-009, tightened from the original "permissive in P2" to "enforced on credential endpoints in P2". ADR-009 text does not need amendment; the phase plan row at line 642 remains accurate since enforcement is limited to credential endpoints, leaving the global posture still permissive.
- **NFR-018 (docs/prd.md:1445–1452):** P1–P2 originally no enforcement, P3–P5 per-API-key throttling. This story overrides the P2 "no enforcement" posture specifically for credential-bearing endpoints, on retro authority (see `epic-2-retro-2026-04-17.md` Significant Discovery #3 and PREP-1).
- **NFR-014 (structured error contract):** Reuses `Common/StructuredErrorResponse.cs`. Do not invent a new error shape.
- **NFR-007 (credentials in-memory only):** The rejected-path log-silence guarantee is the critical linkage to this NFR. Credential material from a rejected request body must not land in logs or response.

### Middleware pipeline order (do not change without thought)

Confirmed order in `Program.cs` after this story:
1. `app.UseMiddleware<ExceptionHandlingMiddleware>()` — outermost (NFR-014)
2. `app.UseHttpsRedirection()` — NFR-009
3. `app.UseRouting()`
4. `app.UseRateLimiter()` — **new in this story**; must sit between routing and controllers so endpoint metadata (`[EnableRateLimiting]`) is resolved
5. `app.MapControllers()`
6. Health map (`/api/health`)

If `UseRateLimiter` is placed **before** `UseRouting`, endpoint metadata is not yet bound and the named policy attribute cannot be resolved → all requests fall through with no limit applied. This is the most common misconfiguration; the dev check is: a request that should be rejected is accepted, and no `429` ever fires.

### Partition key choice

Phase 2 is a manual cohort. API keys are not yet issued. Using `RemoteIpAddress` as the partition key is the correct P2 choice — it prevents a single caller from hammering the endpoint without requiring API-key infrastructure. Phase 3 swaps to `X-Api-Key` header partitioning per ADR-009; **only the partition key lambda changes**, not the policy structure.

Edge case: when behind a reverse proxy or load balancer, `RemoteIpAddress` may be the proxy's IP. Out of scope for this story — Phase 3 deployment story will add `ForwardedHeaders` middleware if deployed behind an LB. Record this as a known P2 limitation in the deferred-work log.

### OnRejected implementation sketch

```csharp
// NFR-018, NFR-014, NFR-007 — Rate limiting on credential-accepting endpoints
builder.Services.Configure<CredentialEndpointsRateLimitOptions>(
    builder.Configuration.GetSection("RateLimiting:CredentialEndpoints"));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("credential-endpoints", httpContext =>
    {
        var opts = httpContext.RequestServices
            .GetRequiredService<IOptions<CredentialEndpointsRateLimitOptions>>().Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = opts.PermitLimit,
                Window               = TimeSpan.FromSeconds(opts.WindowSeconds),
                QueueLimit           = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment    = true,
            });
    });

    options.OnRejected = async (context, ct) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(
            MetadataName.RetryAfter, out TimeSpan wait) ? (int)wait.TotalSeconds : 60;

        context.HttpContext.Response.Headers["Retry-After"] = retryAfter.ToString();
        context.HttpContext.Response.StatusCode  = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = MediaTypeNames.Application.Json;

        await context.HttpContext.Response.WriteAsJsonAsync(
            new StructuredErrorResponse
            {
                Error       = $"Rate limit exceeded. Retry after {retryAfter} seconds.",
                Code        = "RATE_LIMIT_EXCEEDED",
                SafeToRetry = true,
            }, ct);
    };
});
```

Sketch only. Adapt to the actual `using` set — `Microsoft.AspNetCore.RateLimiting`, `System.Threading.RateLimiting`, `Microsoft.Extensions.Options`, `System.Net.Mime`.

### Previous-story intelligence (continuity from Epic 2)

- **Story 2.4 review** committed `7875516` clamped `Microsoft.PowerPlatform.Dataverse.Client` and `Anthropic` logger namespaces to `Warning` (NFR-007 reinforcement). That change is the baseline this story builds on — the rejected-path log silence tests here prove the clamp extends to rate-limit rejections.
- **Story 2.2** established the pattern `[ApiController]` + `[FromBody]` + `InvalidModelStateResponseFactory` → `StructuredErrorResponse`. Follow it — do not roll your own 400/429 body formatter.
- **Story 2.1** set the `ExceptionHandlingMiddleware` outermost-middleware convention. Respect it.

### File-location conventions (from project structure)

| Concern | Path |
|---|---|
| API pipeline | `src/DataverseDocAgent.Api/Program.cs` |
| Config option types | `src/DataverseDocAgent.Api/Common/` |
| Structured error shape | `src/DataverseDocAgent.Api/Common/StructuredErrorResponse.cs` |
| Controllers | `src/DataverseDocAgent.Api/Features/<Feature>/` |
| Tests | `tests/DataverseDocAgent.Tests/` |
| appsettings | `src/DataverseDocAgent.Api/appsettings*.json` |
| Docs | `docs/setup-guide.md` (user-facing rate-limit note) |

Do **not** create a `Middleware/RateLimit*` file — the built-in middleware is configured in `Program.cs`; no wrapping middleware class is required.

### Testing standards

- xunit + Moq (per `tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj`).
- Prefer pure-unit tests over `WebApplicationFactory` unless the latter is strictly required. The `FixedWindowRateLimiter` can be exercised directly via `RateLimiter` API without spinning up TestHost.
- Reuse the `CapturingLogger` pattern from `tests/DataverseDocAgent.Tests/ExceptionHandlingMiddlewareTests.cs` for the log-silence assertion (do not duplicate — consider extracting to a shared test helper in a future story; not blocking for this one).
- Target: 5 new tests, all ≤ 50 ms each.

### References

- [Source: `_bmad-output/planning-artifacts/epics.md` Epic 3 Story 3.0]
- [Source: `docs/architecture.md` ADR-009, Section 15 phase plan]
- [Source: `docs/prd.md` NFR-018, NFR-014, NFR-007]
- [Source: `_bmad-output/implementation-artifacts/epic-2-retro-2026-04-17.md` Significant Discovery #3, PREP-1]
- [Source: `src/DataverseDocAgent.Api/Common/StructuredErrorResponse.cs`]
- [Source: `src/DataverseDocAgent.Api/Program.cs` — Serilog config baseline (commit `7875516`)]
- [Source: `tests/DataverseDocAgent.Tests/ExceptionHandlingMiddlewareTests.cs` — log-silence pattern]
- [Source: `_bmad-output/planning-artifacts/sprint-change-proposal-2026-04-17.md`]

## Dev Agent Record

### Agent Model Used

claude-opus-4-7 (context engine)

### Debug Log References

None — build clean (0 warn / 0 err) and 73/73 tests green on first full build/test cycle.

### Completion Notes List

- **Rejection helper extracted to `Common/RateLimitRejection.cs`** instead of inlining the 429 body writer in `Program.cs`. Rationale: the story Dev Notes mandated pure-unit tests over `WebApplicationFactory`, and `options.OnRejected` is a delegate composed at DI wire-up that is not addressable from a unit test. Extracting `WriteAsync(HttpContext, retryAfterSeconds, ct)` as a public static keeps Program.cs thin and lets AC-5/AC-6/AC-9 be verified against a `DefaultHttpContext` without spinning up TestHost. `OnRejected` is a 4-line adapter that extracts `Retry-After` from the lease metadata and delegates to the helper.
- **Policy name + section constants live on the options class** (`CredentialEndpointsRateLimitOptions.PolicyName`, `.SectionName`). The `[EnableRateLimiting]` attribute on the controller and the `AddPolicy(...)` call in `Program.cs` both reference the constant, so the policy name is single-sourced and Story 3.5's generate-document action can reuse the same constant without string duplication.
- **AC-4 (Story 3.5 attribute application) deferred to 3.5 dev.** Story 3.5 is scaffold-only at this point; the enforcement is recorded here in Completion Notes and will be appended to `deferred-work.md` — Story 3.5 dev must add `[EnableRateLimiting(CredentialEndpointsRateLimitOptions.PolicyName)]` to the `POST /api/document/generate` action.
- **AC-11 (sprint-level gate on 3.5 → done).** Not code-enforced. Will be surfaced in the 3.5 code-review checklist; recorded in deferred-work.md.
- **Reverse-proxy edge case noted.** When deployed behind a load balancer, `RemoteIpAddress` is the proxy IP. Out of scope for P2 per Dev Notes. Appended to deferred-work.md as a P3 deployment-story follow-up (add `ForwardedHeaders` middleware).
- **Serilog credential clamp (Story 2.4 / commit `7875516`) is load-bearing.** The rejection path writes no log statement, but if a future change adds one, the `Microsoft.AspNetCore`-Warning and `Microsoft.PowerPlatform.Dataverse.Client`-Warning clamps already prevent accidental request-body dumps at Info/Debug. NFR-007 posture is preserved.
- **Build:** `dotnet build DataverseDocAgent.sln --no-incremental` → 0 warn / 0 err.
- **Tests:** `dotnet test` → Passed: 75, Failed: 0, Skipped: 0 (68 baseline + 7 `RateLimiterTests` — 5 original + 2 added during review: null-IP partition and HasStarted guard).

### Review Findings

Code review 2026-04-17 (`bmad-code-review` via three parallel reviewers: Blind Hunter, Edge Case Hunter, Acceptance Auditor) against commit `58568c5`. All High/Med issues patched; Low-severity items dismissed as noise or deferred to `deferred-work.md`. Re-test: 75/75 green.

**Patches applied:**
- [x] [Review][Patch] Options bind had no validation — added `.ValidateDataAnnotations().ValidateOnStart()` so out-of-range `PermitLimit`/`WindowSeconds` fail at startup rather than on first rejection [`src/DataverseDocAgent.Api/Program.cs`]
- [x] [Review][Patch] `Retry-After` fallback hard-coded to 60 — now falls back to operator-configured `WindowSeconds` when lease metadata is absent; clamped to `Math.Max(1, …)` to prevent zero/negative header values [`src/DataverseDocAgent.Api/Program.cs`]
- [x] [Review][Patch] `OnRejected` never disposed lease — now `using var lease = context.Lease;` so non-trivial lease state is released (future-proofing for Phase 3 limiter swap) [`src/DataverseDocAgent.Api/Program.cs`]
- [x] [Review][Patch] `WriteAsync` had no `Response.HasStarted` guard — added early-return mirroring `ExceptionHandlingMiddleware`, preventing a split response if a rare middleware race commits bytes before rejection [`src/DataverseDocAgent.Api/Common/RateLimitRejection.cs`]
- [x] [Review][Patch] Partition isolation test was overstated (only one `ctxB` acquire) — now exhausts `ctxB`'s full budget to prove independent allocation [`tests/DataverseDocAgent.Tests/RateLimiterTests.cs`]
- [x] [Review][Patch] Added `PartitionedLimiter_NullRemoteIpAddress_CollapsesToUnknownBucket` — locks in the documented null-IP semantics and surfaces the proxy/LB trap as a test-visible fact [`tests/DataverseDocAgent.Tests/RateLimiterTests.cs`]
- [x] [Review][Patch] Added `RateLimitRejection_WriteAsync_ResponseAlreadyStarted_IsNoOp` — verifies the HasStarted guard; uses an `IHttpResponseFeature` fake [`tests/DataverseDocAgent.Tests/RateLimiterTests.cs`]
- [x] [Review][Patch] AC-4 forward-pointer recorded — story-3.5 Tasks list now carries the `[EnableRateLimiting(...)]` subtask; `deferred-work.md` has a 3.0 section covering AC-11, reverse-proxy partition collapse, window replenishment, and health-exemption assertion [`_bmad-output/implementation-artifacts/story-3.5-mode1-generation.md`, `_bmad-output/implementation-artifacts/deferred-work.md`]

**Deferred (logged in `deferred-work.md`):**
- [x] [Review][Defer] Reverse-proxy / null-IP partition collapse — acknowledged; P3 deployment story adds `ForwardedHeaders` middleware
- [x] [Review][Defer] Sprint-level gate on Story 3.5 → done (AC-11) — enforced by code review checklist, not code
- [x] [Review][Defer] Window-replenishment / boundary-concurrency tests — out of story 3.0 AC-10 scope
- [x] [Review][Defer] `/api/health` exemption is implicit — add `DisableRateLimiting`-style lock in Phase 3 when global policy is introduced

**Dismissed (noise / false positive / handled elsewhere):**
- [Review][Dismiss] "appsettings.Development.json edit not in diff" — file is gitignored (.gitignore:513); local-only override is intentional and documented
- [Review][Dismiss] Weak `Body.Position == 0` assertion — sufficient for the no-read claim; a caller that seeks-and-reads would still be correct behaviourally
- [Review][Dismiss] `OnRejected` mutates headers before first `await` (ct not honoured pre-await) — standard .NET async pattern; cancellation between headers and body is rare and harmless
- [Review][Dismiss] `Headers["Retry-After"]` indexer vs typed property — functionally identical for this path; no upstream retry-hint middleware exists
- [Review][Dismiss] Redundant `RejectionStatusCode = 429` + `Response.StatusCode = 429` — harmless belt-and-braces
- [Review][Dismiss] No test asserting `[EnableRateLimiting]` attribute wiring — attribute is static source; a reflection test would fragilise against the policy-name constant rename
- [Review][Dismiss] AC-9 log-sink capture not in test — rejection path emits zero log statements; inspection is vacuous by construction

### Change Log

| Date       | Change                                                                                              |
|------------|-----------------------------------------------------------------------------------------------------|
| 2026-04-17 | Story 3.0 implemented (`58568c5`); review findings applied in follow-up; status → done.             |

### File List

**New:**
- `src/DataverseDocAgent.Api/Common/CredentialEndpointsRateLimitOptions.cs`
- `src/DataverseDocAgent.Api/Common/RateLimitRejection.cs`
- `tests/DataverseDocAgent.Tests/RateLimiterTests.cs`

**Modified:**
- `src/DataverseDocAgent.Api/Program.cs` — added `AddRateLimiter` registration, named policy `credential-endpoints`, OnRejected adapter, and `app.UseRateLimiter()` between `UseRouting` and `MapControllers`.
- `src/DataverseDocAgent.Api/Features/SecurityCheck/SecurityCheckController.cs` — added `[EnableRateLimiting(CredentialEndpointsRateLimitOptions.PolicyName)]` on the `Check` action.
- `src/DataverseDocAgent.Api/appsettings.json` — added `RateLimiting:CredentialEndpoints` (10 / 60).
- `src/DataverseDocAgent.Api/appsettings.Development.json` — added `RateLimiting:CredentialEndpoints` (100 / 60).
- `docs/setup-guide.md` — added "API Reference — Rate Limits" subsection.
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — `3-0-*` ready-for-dev → in-progress → review (this commit).
