// F-036, FR-036, NFR-014 — Job status success response (Story 3.1)
using System.Text.Json.Serialization;

namespace DataverseDocAgent.Api.Features.DocumentGenerate;

/// <summary>
/// Success response for <c>GET /api/jobs/{jobId}</c>. Not-found responses reuse
/// <see cref="Common.StructuredErrorResponse"/> (still HTTP 200 per AC-4 and PRD).
/// </summary>
public sealed class JobStatusResponse
{
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("downloadToken")]
    public string? DownloadToken { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
