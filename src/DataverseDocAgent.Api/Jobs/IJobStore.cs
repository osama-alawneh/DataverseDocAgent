// F-036, FR-036, NFR-014 — Async job store contract (Story 3.1, extended in Story 3.5)
namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Thread-safe store of generation-job state. Phase 2: in-memory only, single-instance deployment.
/// Jobs are never expired from the store in Phase 2 (see Dev Notes — acceptable MVP limitation).
/// </summary>
public interface IJobStore
{
    /// <summary>Creates a new <see cref="JobStatus.Queued"/> job and returns its id.</summary>
    string CreateJob();

    /// <summary>
    /// Replaces the record for <paramref name="jobId"/>. No-op if the id is unknown.
    /// <paramref name="errorCode"/> and <paramref name="safeToRetry"/> are required
    /// machine-readable diagnostics for Failed transitions (NFR-014, Story 3.5).
    /// Callers updating to non-Failed states pass <c>null</c>/<c>null</c>.
    /// </summary>
    void UpdateStatus(
        string    jobId,
        JobStatus status,
        string?   downloadToken,
        string?   errorMessage,
        string?   errorCode   = null,
        bool?     safeToRetry = null);

    /// <summary>Returns the current record, or <c>null</c> if the id is unknown.</summary>
    JobRecord? GetJob(string jobId);
}
