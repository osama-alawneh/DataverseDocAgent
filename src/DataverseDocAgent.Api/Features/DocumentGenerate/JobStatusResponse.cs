// F-036, FR-036, NFR-014 — Job status success response (Story 3.1, extended in Story 3.5)
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

    /// <summary>Machine-readable failure code (Story 3.5). Null for non-Failed jobs.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>NFR-014 retry hint. Null for non-Failed jobs.</summary>
    [JsonPropertyName("safeToRetry")]
    public bool? SafeToRetry { get; init; }
}
