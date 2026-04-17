// F-036, FR-036 — In-memory job store (Story 3.1)
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
        _jobs[jobId] = new JobRecord(jobId, JobStatus.Queued, DownloadToken: null, ErrorMessage: null);
        return jobId;
    }

    public void UpdateStatus(string jobId, JobStatus status, string? downloadToken, string? errorMessage)
    {
        _jobs.AddOrUpdate(
            jobId,
            addValueFactory: _ => new JobRecord(jobId, status, downloadToken, errorMessage),
            updateValueFactory: (_, existing) => existing with
            {
                Status = status,
                DownloadToken = downloadToken,
                ErrorMessage = errorMessage,
            });
    }

    public JobRecord? GetJob(string jobId) => _jobs.TryGetValue(jobId, out var record) ? record : null;
}
