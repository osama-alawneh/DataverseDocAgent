# Story 2.1: ASP.NET Core Web API Project Setup

Status: done

## Story

As a developer,
I want the `DataverseDocAgent.Api` project fully scaffolded with middleware, error handling, HTTPS enforcement, and structured logging,
so that all subsequent feature stories have a solid, production-ready API host to build into.

## Acceptance Criteria

1. HTTPS is enforced: `app.UseHttpsRedirection()` is registered. HTTP requests are redirected to HTTPS. Running on Azure App Service, HTTPS enforcement is also applied at the App Service level (documented in deployment notes).
2. `ExceptionHandlingMiddleware` is registered in the middleware pipeline and catches all unhandled exceptions, returning the structured error response: `{ "error": "<human-readable>", "code": "INTERNAL_ERROR", "safeToRetry": false }` as JSON with HTTP 500. No raw stack traces, type names, or framework messages are included in the response body.
3. Serilog is configured as the logging provider with structured console output. A Serilog destructuring policy is applied that prevents `EnvironmentCredentials` from being destructured — if an `EnvironmentCredentials` object is passed to a log call, it is replaced with `[REDACTED]`.
4. `GET /api/health` returns HTTP 200 with `{ "status": "healthy" }`. This endpoint is used for uptime measurement per NFR-006 and must not require authentication.
5. `dotnet run` starts the API cleanly, the health endpoint responds, and the application logs start-up confirmation to the console via Serilog.
6. All controller and service stub files created in this story include a `// NFR-xxx` annotation referencing the relevant NFR where applicable (NFR-015 traceability standard).

## Tasks / Subtasks

- [x] Configure `Program.cs` with middleware pipeline (AC: 1, 2)
  - [x] Register `ExceptionHandlingMiddleware` as the first middleware (catches all downstream exceptions)
  - [x] Register `app.UseHttpsRedirection()`
  - [x] Register `app.UseRouting()` and `app.MapControllers()`
  - [x] Remove default Weather Forecast controller from template (none existed)
- [x] Implement `ExceptionHandlingMiddleware` (AC: 2)
  - [x] Create `src/DataverseDocAgent.Api/Middleware/ExceptionHandlingMiddleware.cs`
  - [x] `InvokeAsync(HttpContext context, RequestDelegate next)` pattern
  - [x] Catch `Exception`, log it (without credential data), return `{ error, code: "INTERNAL_ERROR", safeToRetry: false }` as `application/json` with HTTP 500
  - [x] Do not expose `exception.StackTrace`, `exception.GetType().Name`, or inner exception details in the response body
  - [x] Annotate: `// NFR-014`
- [x] Implement `StructuredErrorResponse` (AC: 2)
  - [x] Create `src/DataverseDocAgent.Api/Common/StructuredErrorResponse.cs`
  - [x] Record or class with: `string Error`, `string Code`, `bool SafeToRetry`
  - [x] Used by middleware and all controllers for consistent error serialization
- [x] Configure Serilog with credential destructuring policy (AC: 3)
  - [x] Add Serilog configuration in `Program.cs` before `builder.Build()`
  - [x] Register `Serilog.AspNetCore` via `UseSerilog()`
  - [x] Add a destructuring policy: implement `IDestructuringPolicy` that matches `EnvironmentCredentials` and returns a scalar value `"[REDACTED]"` instead of the object graph
  - [x] Configure structured console output with timestamp, level, message
  - [x] Annotate: `// NFR-007`
- [x] Implement health check endpoint (AC: 4)
  - [x] Use minimal API endpoint in `Program.cs`
  - [x] `GET /api/health` returns `{ "status": "healthy" }` — no auth required
  - [x] Annotate: `// NFR-006`
- [x] Verify clean startup (AC: 5)
  - [x] `dotnet run` — application starts without errors
  - [x] `curl https://localhost:{port}/api/health` returns 200 and expected body

## Dev Notes

- **Serilog destructuring policy:** Implement `Serilog.Core.IDestructuringPolicy` with `TryDestructure(object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue result)`. Return `true` and set `result = new ScalarValue("[REDACTED]")` when `value is EnvironmentCredentials`.
- **Middleware registration order matters:** `ExceptionHandlingMiddleware` must be registered first (outermost) so it catches exceptions from all downstream middleware and controllers.
- **`StructuredErrorResponse`:** Should be serializable to `application/json`. Use `Results.Json(response, statusCode: 500)` in minimal API style, or `return new ObjectResult(response) { StatusCode = 500 }` in controller style. Be consistent — pick one pattern and use it everywhere.
- **Health endpoint:** A minimal API endpoint in `Program.cs` is the simplest approach: `app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));`. No controller needed.
- **HTTPS in development:** The default `appsettings.Development.json` Kestrel config binds to both HTTP and HTTPS. `UseHttpsRedirection()` handles the redirect. The development HTTPS certificate is managed by `dotnet dev-certs`.

### Project Structure Notes

Files created or modified:
- `src/DataverseDocAgent.Api/Program.cs` — full pipeline configured
- `src/DataverseDocAgent.Api/Middleware/ExceptionHandlingMiddleware.cs` — `// NFR-014`
- `src/DataverseDocAgent.Api/Common/StructuredErrorResponse.cs`

### References

- [Source: docs/prd.md#7-non-functional-requirements — NFR-009] — HTTPS enforcement
- [Source: docs/prd.md#7-non-functional-requirements — NFR-014] — structured error responses
- [Source: docs/prd.md#7-non-functional-requirements — NFR-007] — credential logging prohibition
- [Source: docs/prd.md#7-non-functional-requirements — NFR-006] — health endpoint for uptime measurement
- [Source: _bmad-output/planning-artifacts/architecture.md#9-error-response-standard] — error code list

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- Configured Serilog with CredentialDestructuringPolicy to redact EnvironmentCredentials
- Implemented ExceptionHandlingMiddleware as first middleware in pipeline
- Created StructuredErrorResponse for consistent error serialization
- Added minimal API health endpoint at /api/health returning { status: "healthy" }
- Configured HTTPS redirection middleware
- Verified clean startup and health endpoint responds correctly

### File List

- `src/DataverseDocAgent.Api/Program.cs` — full middleware pipeline with Serilog, HTTPS, exception handling, health endpoint
- `src/DataverseDocAgent.Api/Middleware/ExceptionHandlingMiddleware.cs` — global exception handler (NFR-014)
- `src/DataverseDocAgent.Api/Common/StructuredErrorResponse.cs` — standardized error response
- `src/DataverseDocAgent.Api/Common/CredentialDestructuringPolicy.cs` — Serilog policy to redact credentials (NFR-007)

### Review Findings

- [x] [Review][Patch] JSON casing mismatch — added `[JsonPropertyName]` attributes to `StructuredErrorResponse`. [StructuredErrorResponse.cs]
- [x] [Review][Patch] `Log.CloseAndFlush()` dead code — wrapped `app.Run()` in try/finally; moved to `UseSerilog((ctx, cfg) => ...)` callback pattern. [Program.cs]
- [x] [Review][Patch] Missing `Response.HasStarted` guard — added guard in `ExceptionHandlingMiddleware` catch block; rethrows if headers committed. [ExceptionHandlingMiddleware.cs]
- [x] [Review][Patch] `MinimumLevel.Debug()` hardcoded — switched to `.ReadFrom.Configuration()`; `Serilog:MinimumLevel` set in `appsettings.json` (Information) and `appsettings.Development.json` (Debug). [Program.cs, appsettings*.json]
- [x] [Review][Defer] Health endpoint `AllowAnonymous` not annotated — auth middleware not registered yet; when added, health endpoint must be explicitly excluded. [Program.cs] — deferred, pre-existing
- [x] [Review][Defer] Silent failure in AgentOrchestrator — tool exceptions return error string without `LogError`. [AgentOrchestrator.cs] — deferred, pre-existing
- [x] [Review][Defer] `Console.Error.WriteLine` in AgentOrchestrator/ListCustomTablesTool — bypasses Serilog. [AgentOrchestrator.cs, ListCustomTablesTool.cs] — deferred, pre-existing
- [x] [Review][Defer] NFR-015 annotations missing on pre-existing tool/service files. — deferred, pre-existing
- [x] [Review][Defer] DataverseConnectionFactory uses non-conforming `F-xxx` annotation instead of `// NFR-xxx`. — deferred, pre-existing
