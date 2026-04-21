// F-040, FR-040, NFR-013, NFR-014 — GET /api/download/{token} (Story 3.3)
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Storage;
using Microsoft.AspNetCore.Mvc;

namespace DataverseDocAgent.Api.Features.Download;

[ApiController]
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
    public async Task<IActionResult> Get(string token)
    {
        var bytes = await _documentStore.RetrieveAsync(token);
        if (bytes is null)
        {
            // HTTP 200 with structured error (NOT 404) — matches the JobStatusController
            // pattern so HTTP client libraries do not throw on a missing/expired token.
            return Ok(new StructuredErrorResponse
            {
                Error = "Download token not found or expired",
                Code = "TOKEN_EXPIRED",
                SafeToRetry = false,
            });
        }

        // File() sets Content-Type and Content-Disposition: attachment; filename="..."
        // automatically — no manual header writes needed.
        return File(bytes, DocxContentType, DownloadFileName);
    }
}
