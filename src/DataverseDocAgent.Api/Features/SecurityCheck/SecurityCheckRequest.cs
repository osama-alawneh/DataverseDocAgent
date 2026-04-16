// F-029, F-030, F-031 — Permission pre-flight checker request model
using System.ComponentModel.DataAnnotations;

namespace DataverseDocAgent.Api.Features.SecurityCheck;

public sealed class SecurityCheckRequest
{
    private const string GuidPattern =
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";

    [Required]
    [RegularExpression(@"^https://\S+", ErrorMessage = "EnvironmentUrl must be a valid HTTPS URL.")]
    public string? EnvironmentUrl { get; init; }

    [Required]
    [RegularExpression(GuidPattern, ErrorMessage = "TenantId must be a valid GUID.")]
    public string? TenantId { get; init; }

    [Required]
    [RegularExpression(GuidPattern, ErrorMessage = "ClientId must be a valid GUID.")]
    public string? ClientId { get; init; }

    [Required]
    [MinLength(1, ErrorMessage = "ClientSecret must not be empty.")]
    public string? ClientSecret { get; init; }

    /// <summary>
    /// "mode1" | "mode2" | "mode3" | "all". Defaults to "all".
    /// All modes share the same required permissions (PRD Section 5.4).
    /// </summary>
    public string TargetMode { get; init; } = "all";
}
