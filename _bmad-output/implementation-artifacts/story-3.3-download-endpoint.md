# Story 3.3: GET /api/download/{token} Endpoint

Status: ready-for-dev

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

- [ ] Implement `DownloadController` (AC: 1, 2, 3, 4)
  - [ ] Create `src/DataverseDocAgent.Api/Features/Download/DownloadController.cs`
  - [ ] `[ApiController]`, `[Route("api")]`
  - [ ] Inject `IDocumentStore`
  - [ ] `[HttpGet("download/{token}")]` action:
    - Call `await _documentStore.RetrieveAsync(token)`
    - If `null`: return `Results.Ok(new StructuredErrorResponse("Download token not found or expired", "TOKEN_EXPIRED", false))` — HTTP 200 with error body (consistent with API error contract)
    - If bytes found: return `File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "DataverseDocAgent-Report.docx")`
  - [ ] Annotate: `// F-040 — FR-040`
- [ ] Verify correct headers on file response (AC: 1)
  - [ ] Content-Type: `application/vnd.openxmlformats-officedocument.wordprocessingml.document`
  - [ ] Content-Disposition: `attachment; filename="DataverseDocAgent-Report.docx"` (set by `File()` return with `fileDownloadName` parameter)
- [ ] Manual test with stored bytes (AC: 1, 2)
  - [ ] Manually store test bytes in `IDocumentStore` (via a debug endpoint or unit test)
  - [ ] Call `GET /api/download/{token}` with the returned token → confirm bytes and headers
  - [ ] Call `GET /api/download/invalid-token` → confirm structured error response

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

### Completion Notes List

### File List
