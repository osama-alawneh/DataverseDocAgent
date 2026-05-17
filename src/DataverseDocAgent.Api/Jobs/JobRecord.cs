// F-036, FR-036, NFR-014 — Async job model (Story 3.1, extended in Story 3.5)
namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Immutable snapshot of a generation job's state. Stored in <see cref="IJobStore"/>.
/// Replaced (not mutated) on every status transition.
/// </summary>
/// <param name="ErrorCode">
/// Stable machine-readable failure code (Story 3.5, NFR-014). One of:
/// CREDENTIAL_REJECTED, DATAVERSE_ERROR, AI_ERROR, GENERATION_TIMEOUT, GENERATION_FAILED,
/// HOST_SHUTDOWN. <c>null</c> for non-Failed states.
/// </param>
/// <param name="SafeToRetry">
/// Per NFR-014, every Failed response advertises whether retrying with the same
/// inputs is safe. <c>null</c> for non-Failed states.
/// </param>
public sealed record JobRecord(
    string    JobId,
    JobStatus Status,
    string?   DownloadToken,
    string?   ErrorMessage,
    string?   ErrorCode = null,
    bool?     SafeToRetry = null);
