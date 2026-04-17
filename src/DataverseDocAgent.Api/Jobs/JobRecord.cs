// F-036, FR-036 — Async job model (Story 3.1)
namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Immutable snapshot of a generation job's state. Stored in <see cref="IJobStore"/>.
/// Replaced (not mutated) on every status transition.
/// </summary>
public sealed record JobRecord(
    string JobId,
    JobStatus Status,
    string? DownloadToken,
    string? ErrorMessage);
