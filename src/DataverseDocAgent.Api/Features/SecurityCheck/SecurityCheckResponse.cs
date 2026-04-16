// F-029, F-030, F-031 — Permission pre-flight checker response model
using System.Text.Json.Serialization;

namespace DataverseDocAgent.Api.Features.SecurityCheck;

public sealed class SecurityCheckResponse
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("safeToRun")]
    public required bool SafeToRun { get; init; }

    [JsonPropertyName("passed")]
    public required string[] Passed { get; init; }

    [JsonPropertyName("missing")]
    public required string[] Missing { get; init; }

    [JsonPropertyName("extra")]
    public required string[] Extra { get; init; }

    [JsonPropertyName("recommendation")]
    public required string Recommendation { get; init; }
}
