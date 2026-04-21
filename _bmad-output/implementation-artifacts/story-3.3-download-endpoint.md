# Story 3.3: GET /api/download/{token} Endpoint

Status: done

## Story

As a D365 consultant,
I want to call GET /api/download/{token} with my download token,
so that I can retrieve the generated .docx without needing to re-authenticate.

## Acceptance Criteria

1. `GET /api/download/{token}` returns HTTP 200 with the `.docx` file as the response body when the token is valid and unexpired. Content-Type is `application/vnd.openxmlformats-officedocument.wordprocessingml.document`. Content-Disposition is `attachment; filename="DataverseDocAgent-Report.docx"`.
2. `GET /api/download/{token}` returns HTTP 200 with the structured error response `{ error: "Download token not found or expired", code: "TOKEN_EXPIRED", safeToRetry: false }` when the token is expired or invalid — not HTTP 404.
3. No authentication credential is required to call this endpoint — the token itself is the only authorization mechanism.
4. The endpoint delegates entirely to `IDocumentStore.RetrieveAsync(token)` — it does not implement its own storage logic.

## Tasks / Subtasks

- [x] Implement `DownloadController` (AC: 1, 2, 3, 4)
  - [x] Create `src/DataverseDocAgent.Api/Features/Download/DownloadController.cs`
  - [x] `[ApiController]` (route configured per-action via `[HttpGet("/api/download/{token}")]` — matches `JobStatusController` convention in this repo, which uses absolute action routes rather than a class-level `[Route("api")]`)
  - [x] Inject `IDocumentStore`
  - [x] `[HttpGet("/api/download/{token}")]` action:
    - Call `await _documentStore.RetrieveAsync(token)`
    - If `null`: return `Ok(new StructuredErrorResponse { Error = "Download token not found or expired", Code = "TOKEN_EXPIRED", SafeToRetry = false })` — HTTP 200 with error body (consistent with API error contract)
    - If bytes found: return `File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "DataverseDocAgent-Report.docx")`
  - [x] Annotate: `// F-040, FR-040, NFR-013, NFR-014 — GET /api/download/{token} (Story 3.3)`
- [x] Verify correct headers on file response (AC: 1)
  - [x] Content-Type: `application/vnd.openxmlformats-officedocument.wordprocessingml.document` (asserted in `Get_ValidToken_ReturnsFileWithDocxHeadersAndBytes`)
  - [x] Content-Disposition: `attachment; filename="DataverseDocAgent-Report.docx"` (set by `File()` return with `fileDownloadName` parameter; asserted via `FileContentResult.FileDownloadName`)
- [x] Automated test with stored bytes (AC: 1, 2) — covered by xUnit suite, no debug endpoint needed
  - [x] Store bytes in real `InMemoryDocumentStore`, call controller, confirm bytes + headers (`Get_ValidToken_ReturnsFileWithDocxHeadersAndBytes`)
  - [x] Call controller with unknown token → confirm structured error (`Get_UnknownToken_ReturnsOk200WithStructuredErrorTokenExpired`)
  - [x] Expired-token path covered (`Get_ExpiredToken_ReturnsOk200WithStructuredErrorTokenExpired`)
  - [x] Delegation contract pinned via Moq (`Get_DelegatesEntirelyToDocumentStoreRetrieve`)
  - [x] Empty-bytes edge case (`Get_EmptyBytesStored_ReturnsFileWithEmptyBody`)

## Dev Notes

- **HTTP 200 for error:** This is intentional — the API contract uses HTTP 200 for all responses including errors, with the error signalled in the structured body. This matches the security check endpoint pattern and prevents HTTP client libraries from throwing exceptions on "404" responses.
- **`File()` return in controllers:** In ASP.NET Core MVC controllers, `return File(bytes, contentType, fileName)` sets the Content-Disposition to `attachment; filename="{fileName}"` automatically. In minimal API style, use `Results.File(bytes, contentType, fileName)`.
- **No auth on download endpoint:** The 24-hour token is the sole access control mechanism. This is documented in PRD FR-040 and the privacy policy. The token is a 32-character UUID without hyphens — sufficiently unpredictable for 24-hour single-use access.
- **Content-Type MIME type:** The correct MIME type for `.docx` is `application/vnd.openxmlformats-officedocument.wordprocessingml.document`. Do not use `application/octet-stream` or `application/msword`.

### Project Structure Notes

Files created:
- `src/DataverseDocAgent.Api/Features/Download/DownloadController.cs` — `// F-040`

### References

- [Source: docs/prd.md#functional-requirements — FR-040] — token lifetime, HTTP 200/404 behaviour, no additional auth
- [Source: docs/prd.md#api-endpoints-complete-reference] — GET /api/download/{token}
- [Source: _bmad-output/planning-artifacts/architecture.md#9-error-response-standard] — structured error format
- [Source: _bmad-output/planning-artifacts/architecture.md#7-storage-abstraction] — IDocumentStore retrieval behaviour

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `dotnet test tests/DataverseDocAgent.Tests/DataverseDocAgent.Tests.csproj` → 109 passed / 0 failed (5 new `DownloadControllerTests`).

### Completion Notes List

- Implemented `DownloadController` with absolute action route `[HttpGet("/api/download/{token}")]`. Chose absolute-route style over class-level `[Route("api")]` to match the existing `JobStatusController` convention in this codebase — keeps controller routing uniform and avoids two competing styles.
- Reused existing `StructuredErrorResponse` (NFR-014) for the miss/expired path — no new error DTO added. HTTP 200 + structured body, not 404, per API error contract.
- `File(bytes, contentType, fileDownloadName)` handles both `Content-Type` and `Content-Disposition: attachment; filename="..."` automatically; tests assert through `FileContentResult` rather than poking response headers directly.
- AC-3 (no auth on endpoint) is satisfied implicitly: the controller has no `[Authorize]` attribute and no auth middleware is wired into the API today, so the endpoint is anonymous by default. No change needed.
- AC-4 (delegation only) pinned with a strict Moq + `VerifyNoOtherCalls()` test so any future side-channel storage in this controller will fail loudly.
- DI: no new registration needed — `IDocumentStore` → `InMemoryDocumentStore` already wired as a singleton in `Program.cs:80` from Story 3.2.

### File List

- `src/DataverseDocAgent.Api/Features/Download/DownloadController.cs` (new) — `// F-040`
- `tests/DataverseDocAgent.Tests/DownloadControllerTests.cs` (new) — 5 unit tests covering AC-1, AC-2, AC-3 (anonymous-by-default), AC-4

## Change Log

| Date       | Change                                                                              |
|------------|-------------------------------------------------------------------------------------|
| 2026-04-20 | Story 3.3 — implemented `DownloadController` + 5 unit tests. Status → review.       |
| 2026-04-20 | Story 3.3 — applied code review patches (P1–P7). Status → done.                     |

### Review Findings

- [x] [Review][Patch] Token shape validation + null/empty/whitespace guard — controller-level early-return TOKEN_EXPIRED for malformed input + 32-hex route constraint [src/DataverseDocAgent.Api/Features/Download/DownloadController.cs:30] (blind+edge)
- [x] [Review][Patch] Add `[AllowAnonymous]` to controller — pin AC-3 against future global auth filters [src/DataverseDocAgent.Api/Features/Download/DownloadController.cs:8] (edge+auditor)
- [x] [Review][Patch] Propagate `CancellationToken` from `HttpContext.RequestAborted` to `RetrieveAsync` [src/DataverseDocAgent.Api/Features/Download/DownloadController.cs:30] (blind)
- [x] [Review][Patch] Set `Cache-Control: no-store, private` on download response — token-bearing URL must not be cached by intermediaries [src/DataverseDocAgent.Api/Features/Download/DownloadController.cs:46] (blind)
- [x] [Review][Patch] Add reflection-based test pinning AC-3 — assert no `[Authorize]` and presence of `[AllowAnonymous]` on `Get` [tests/DataverseDocAgent.Tests/DownloadControllerTests.cs] (auditor)
- [x] [Review][Patch] Add `NFR-013` to test file header comment for symmetry with controller [tests/DataverseDocAgent.Tests/DownloadControllerTests.cs:1] (auditor)
- [x] [Review][Patch] Add tests for malformed-token paths (null/empty/whitespace short-circuit) — pin TOKEN_EXPIRED contract [tests/DataverseDocAgent.Tests/DownloadControllerTests.cs] (blind+edge)
- [x] [Review][Defer] Per-route rate limiting on `/api/download/{token}` — deferred, security follow-up beyond story 3.3 scope (blind+edge)
- [x] [Review][Defer] Token in URL leaks via referrer / proxy logs — deferred, NFR-level concern; revisit when Phase 3 deployment story lands (blind+edge)
- [x] [Review][Defer] Stream large `.docx` via `FileStreamResult` — deferred, current docs are small; would require `IDocumentStore` surface change (blind+edge)
- [x] [Review][Defer] Audit log on every download attempt (success + miss) — deferred, cross-cutting observability concern (blind)
- [x] [Review][Defer] Reconcile PRD FR-040 (says HTTP 404) with story AC-2 (HTTP 200 + structured body) — deferred, PRD edit, not code (auditor)
- [x] [Review][Defer] Constant-time token compare / timing side-channel — deferred, store-level, low ROI on a 128-bit GUID (blind)
- [x] [Review][Defer] Verify token never appears in routing/hosting structured logs — deferred, logging-config audit (edge)
