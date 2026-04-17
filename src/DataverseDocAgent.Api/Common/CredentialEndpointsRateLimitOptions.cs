// NFR-018, NFR-014, NFR-007 — Rate limiting on credential-accepting endpoints
using System.ComponentModel.DataAnnotations;

namespace DataverseDocAgent.Api.Common;

/// <summary>
/// Bound from configuration section <c>RateLimiting:CredentialEndpoints</c>.
/// Tunable without recompile per AC-7 of story 3.0.
/// </summary>
public sealed class CredentialEndpointsRateLimitOptions
{
    public const string SectionName = "RateLimiting:CredentialEndpoints";
    public const string PolicyName = "credential-endpoints";

    [Range(1, 1000)]
    public int PermitLimit { get; init; } = 10;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;
}
