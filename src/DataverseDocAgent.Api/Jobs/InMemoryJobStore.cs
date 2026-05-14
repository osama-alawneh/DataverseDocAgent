// F-036, FR-036 — In-memory job store (Story 3.1, extended in Story 3.5)
using System.Collections.Concurrent;

namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Single-instance in-memory <see cref="IJobStore"/>. Backed by
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> so concurrent
/// writes from controller (Create) and background service (UpdateStatus)
/// are safe. Not distributed; not persistent across restarts.
/// </summary>
public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<string, JobRecord> _jobs = new();

    public string CreateJob()
    {
        var jobId = Guid.NewGuid().ToString();
        _jobs[jobId] = new JobRecord(
            jobId,
            JobStatus.Queued,
            DownloadToken: null,
            ErrorMessage:  null,
            ErrorCode:     null,
            SafeToRetry:   null);
        return jobId;
    }

    // True no-op on unknown id: the background service and controller only ever
    // call UpdateStatus after CreateJob seeded the record, so an unknown id
    // means something is wrong upstream — not an invitation to plant a ghost
    // record. A lost-update retry loop guards against concurrent replaces.
    //
    // Story 3.5 code-review P1 — Ready / Failed are terminal. Refuse any
    // subsequent transition so a late host-shutdown drain cannot wipe a job
    // that already reached Ready (token+document already stored) or convert
    // one Failed code to another. Without this, the CAS retry loop re-reads
    // the latest snapshot and unconditionally clobbers downloadToken/code.
    public void UpdateStatus(
        string    jobId,
        JobStatus status,
        string?   downloadToken,
        string?   errorMessage,
        string?   errorCode   = null,
        bool?     safeToRetry = null)
    {
        while (_jobs.TryGetValue(jobId, out var existing))
        {
            if (existing.Status == JobStatus.Ready || existing.Status == JobStatus.Failed)
            {
                // Terminal — caller racing a finished job; nothing to do.
                return;
            }

            var updated = existing with
            {
                Status        = status,
                DownloadToken = downloadToken,
                ErrorMessage  = errorMessage,
                ErrorCode     = errorCode,
                SafeToRetry   = safeToRetry,
            };

            if (_jobs.TryUpdate(jobId, updated, existing))
            {
                return;
            }
        }
    }

    public JobRecord? GetJob(string jobId) => _jobs.TryGetValue(jobId, out var record) ? record : null;
}
