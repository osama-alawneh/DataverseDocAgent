// F-036 — FR-036 — Mode 1 generation request (Story 3.5)
using System.ComponentModel.DataAnnotations;

namespace DataverseDocAgent.Api.Features.DocumentGenerate;

/// <summary>
/// Request body for <c>POST /api/document/generate</c>. Carries the four
/// credential fields required to authenticate against the target Dataverse
/// environment. Credentials must never be logged or persisted (NFR-007).
/// </summary>
public sealed class DocumentGenerateRequest
{
    // Length caps (deferred-work.md, NFR-014): clamp before SDK boundaries
    // so malformed inputs produce a structured INVALID_REQUEST instead of
    // amplifying log volume or stressing downstream parsers.
    private const int UrlMaxLength    = 2048;
    private const int GuidMaxLength   = 64;
    private const int SecretMaxLength = 512;

    [Required(AllowEmptyStrings = false, ErrorMessage = "EnvironmentUrl is required.")]
    [StringLength(UrlMaxLength, MinimumLength = 1)]
    public string? EnvironmentUrl { get; init; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "TenantId is required.")]
    [StringLength(GuidMaxLength, MinimumLength = 1)]
    public string? TenantId { get; init; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientId is required.")]
    [StringLength(GuidMaxLength, MinimumLength = 1)]
    public string? ClientId { get; init; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "ClientSecret is required.")]
    [StringLength(SecretMaxLength, MinimumLength = 1)]
    public string? ClientSecret { get; init; }
}
