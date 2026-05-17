// F-036, NFR-014 — Stable failure-code vocabulary (Story 3.5)
namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Centralised set of <c>code</c> values surfaced on a Failed job. Used by
/// <see cref="GenerationFailureException"/> and the background service. Strings
/// are part of the public API contract — do not rename without coordinating
/// with API consumers.
/// </summary>
public static class JobFailureCodes
{
    /// <summary>Credentials supplied by the caller were rejected by Dataverse (AC-9).</summary>
    public const string CredentialRejected = "CREDENTIAL_REJECTED";

    /// <summary>Dataverse responded with a fault other than credential rejection (AC-9).</summary>
    public const string DataverseError = "DATAVERSE_ERROR";

    /// <summary>Claude API or agent loop failed (AC-9).</summary>
    public const string AiError = "AI_ERROR";

    /// <summary>Per-task 10-minute timeout exceeded (AC-9).</summary>
    public const string GenerationTimeout = "GENERATION_TIMEOUT";

    /// <summary>Host shutdown interrupted the job mid-flight (deferred-work watchout).</summary>
    public const string HostShutdown = "HOST_SHUTDOWN";

    /// <summary>Fallback for unrecognised failures (NFR-014 default).</summary>
    public const string GenerationFailed = "GENERATION_FAILED";
}
