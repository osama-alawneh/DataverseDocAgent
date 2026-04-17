# Story 3.0: Rate Limiting on Credential-Accepting Endpoints

Status: ready-for-dev

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

- [ ] Add `CredentialEndpointsRateLimitOptions` options class (AC: 7)
  - [ ] Create `src/DataverseDocAgent.Api/Common/CredentialEndpointsRateLimitOptions.cs` with `PermitLimit` (int, default 10) and `WindowSeconds` (int, default 60) properties, data-annotation `[Range]` bounds (1–1000 for PermitLimit; 1–3600 for WindowSeconds).
  - [ ] Register via `builder.Services.Configure<CredentialEndpointsRateLimitOptions>(...)` in `Program.cs`.
- [ ] Register `AddRateLimiter` in `Program.cs` (AC: 1, 2, 6)
  - [ ] Add `builder.Services.AddRateLimiter(options => { ... })`.
  - [ ] Inside the delegate: `options.AddPolicy("credential-endpoints", httpContext => RateLimitPartition.GetFixedWindowLimiter(...))` keyed on `httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"`.
  - [ ] Configure `options.OnRejected` to write `StructuredErrorResponse` JSON + `Retry-After` header.
  - [ ] Pipeline: `app.UseMiddleware<ExceptionHandlingMiddleware>()` → `app.UseHttpsRedirection()` → `app.UseRouting()` → `app.UseRateLimiter()` → `app.MapControllers()`.
- [ ] Decorate `SecurityCheckController.Check` (AC: 3)
  - [ ] Add `using Microsoft.AspNetCore.RateLimiting;` and `[EnableRateLimiting("credential-endpoints")]` on the action method.
- [ ] Update `appsettings.json` and `appsettings.Development.json` (AC: 7)
  - [ ] Add `RateLimiting.CredentialEndpoints` block with `PermitLimit` and `WindowSeconds`. Keep Development values permissive if useful (e.g. 100/60) but default-shipped `appsettings.json` carries the enforced production defaults.
- [ ] Document external config and 429 behaviour in setup guide (AC: 7)
  - [ ] Add a short "API reference — rate limits" subsection at the end of `docs/setup-guide.md` stating default limits, the 429 response shape, and how to request an uplift.
- [ ] Add `tests/DataverseDocAgent.Tests/RateLimiterTests.cs` (AC: 10)
  - [ ] Drive the limiter through `WebApplicationFactory<Program>` **or** direct `RateLimiterPolicy` unit tests (prefer unit tests over TestHost to keep the test project free of `Microsoft.AspNetCore.Mvc.Testing` dependency unless absolutely required).
  - [ ] Cover: permit exhaustion, 429 body shape, `Retry-After` header, no credential in log/response, per-IP partition isolation.
- [ ] Update `_bmad-output/implementation-artifacts/story-3.5-*.md` Tasks list (AC: 4)
  - [ ] When story 3.5 file is created, include a subtask: "Decorate generate-document action with `[EnableRateLimiting(\"credential-endpoints\")]`". If story 3.5 file does not yet exist at this story's implementation time, record this as a note in the Epic 3 section of the dev journal / commit message so it is not forgotten.
- [ ] Verify build + tests green before marking review (AC: 10)
  - [ ] `dotnet build` clean.
  - [ ] `dotnet test` 73/73 green (current baseline 68 + 5 new rate-limiter tests).

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

### Completion Notes List

### File List
