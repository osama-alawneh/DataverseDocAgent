// F-040, FR-040, NFR-013, NFR-014 — GET /api/download/{token} (Story 3.3)
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DataverseDocAgent.Api.Features.Download;

[ApiController]
// AC-3 — token is the sole authorization mechanism. The explicit [AllowAnonymous]
// pins this against a future global authorization filter being wired into Program.cs;
// a regression test in DownloadControllerTests asserts the attribute is present.
[AllowAnonymous]
public sealed class DownloadController : ControllerBase
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const string DownloadFileName = "DataverseDocAgent-Report.docx";

    private readonly IDocumentStore _documentStore;

    public DownloadController(IDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    /// <summary>
    /// Returns the generated .docx for a valid token, or HTTP 200 with a structured
    /// TOKEN_EXPIRED error body for unknown/expired tokens. The 24-hour token is the
    /// sole authorization mechanism — no additional credentials are required.
    /// </summary>
    [HttpGet("/api/download/{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken cancellationToken)
    {
        // Validate inside the controller (not via a route constraint) so that malformed
        // tokens take the AC-2 path — HTTP 200 + structured TOKEN_EXPIRED — instead of
        // 404 from the routing layer. The 32-char limit caps log/lookup amplification
        // before the call ever reaches IMemoryCache.TryGetValue (which would NRE on null).
        if (string.IsNullOrWhiteSpace(token) || token.Length > 32)
        {
            return TokenExpiredResponse();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var bytes = await _documentStore.RetrieveAsync(token);
        if (bytes is null)
        {
            return TokenExpiredResponse();
        }

        // Cache-Control: no-store, private — token is a 24-hour bearer-equivalent
        // capability in the URL path. Intermediate caches must not retain the body
        // even if a misconfigured proxy ignores Authorization headers.
        Response.Headers[HeaderNames.CacheControl] = "no-store, private";

        // File() sets Content-Type and Content-Disposition: attachment; filename="..."
        // automatically — no manual header writes needed.
        return File(bytes, DocxContentType, DownloadFileName);
    }

    private OkObjectResult TokenExpiredResponse() =>
        // HTTP 200 with structured error (NOT 404) — matches the JobStatusController
        // pattern so HTTP client libraries do not throw on a missing/expired token.
        Ok(new StructuredErrorResponse
        {
            Error = "Download token not found or expired",
            Code = "TOKEN_EXPIRED",
            SafeToRetry = false,
        });
}
