// NFR-014 — Structured error response standard
using System.Text.Json.Serialization;

namespace DataverseDocAgent.Api.Common;

/// <summary>
/// Standardized error response for all API errors.
/// Prevents leakage of internal implementation details.
/// </summary>
public sealed class StructuredErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("safeToRetry")]
    public required bool SafeToRetry { get; init; }
}
