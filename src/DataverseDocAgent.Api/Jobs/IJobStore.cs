// F-036, FR-036, NFR-014 — Async job store contract (Story 3.1)
namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Thread-safe store of generation-job state. Phase 2: in-memory only, single-instance deployment.
/// Jobs are never expired from the store in Phase 2 (see Dev Notes — acceptable MVP limitation).
/// </summary>
public interface IJobStore
{
    /// <summary>Creates a new <see cref="JobStatus.Queued"/> job and returns its id.</summary>
    string CreateJob();

    /// <summary>Replaces the record for <paramref name="jobId"/>. No-op if the id is unknown.</summary>
    void UpdateStatus(string jobId, JobStatus status, string? downloadToken, string? errorMessage);

    /// <summary>Returns the current record, or <c>null</c> if the id is unknown.</summary>
    JobRecord? GetJob(string jobId);
}
