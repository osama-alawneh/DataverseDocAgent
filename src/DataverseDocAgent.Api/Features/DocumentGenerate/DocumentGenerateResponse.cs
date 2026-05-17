// F-036 — FR-036 — Mode 1 generation 202 Accepted payload (Story 3.5)
using System.Text.Json.Serialization;

namespace DataverseDocAgent.Api.Features.DocumentGenerate;

/// <summary>
/// 202 Accepted response body for <c>POST /api/document/generate</c>. The
/// caller polls <c>GET /api/jobs/{jobId}</c> using the returned id.
/// </summary>
public sealed class DocumentGenerateResponse
{
    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }
}
