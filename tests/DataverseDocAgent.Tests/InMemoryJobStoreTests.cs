// F-036 — Story 3.1 InMemoryJobStore unit tests
using DataverseDocAgent.Api.Jobs;

namespace DataverseDocAgent.Tests;

public class InMemoryJobStoreTests
{
    [Fact]
    public void CreateJob_ReturnsUniqueGuid_AndInitialStatusQueued()
    {
        var store = new InMemoryJobStore();

        var id1 = store.CreateJob();
        var id2 = store.CreateJob();

        Assert.True(Guid.TryParse(id1, out _));
        Assert.NotEqual(id1, id2);

        var record = store.GetJob(id1);
        Assert.NotNull(record);
        Assert.Equal(JobStatus.Queued, record!.Status);
        Assert.Null(record.DownloadToken);
        Assert.Null(record.ErrorMessage);
    }

    [Fact]
    public void UpdateStatus_ReplacesRecordFields_UpToTerminalState()
    {
        var store = new InMemoryJobStore();
        var id = store.CreateJob();

        store.UpdateStatus(id, JobStatus.Running, downloadToken: null, errorMessage: null);
        Assert.Equal(JobStatus.Running, store.GetJob(id)!.Status);

        store.UpdateStatus(id, JobStatus.Ready, downloadToken: "tok-abc", errorMessage: null);
        var ready = store.GetJob(id)!;
        Assert.Equal(JobStatus.Ready, ready.Status);
        Assert.Equal("tok-abc", ready.DownloadToken);
        Assert.Null(ready.ErrorMessage);
    }

    [Fact]
    public void UpdateStatus_RunningToFailed_RecordsErrorAndCode()
    {
        // Story 3.5 — Failed and Ready are both terminal; verified separately so the
        // P1 guard cannot regress by silently overwriting either.
        var store = new InMemoryJobStore();
        var id = store.CreateJob();
        store.UpdateStatus(id, JobStatus.Running, null, null);

        store.UpdateStatus(id, JobStatus.Failed, downloadToken: null,
            errorMessage: "boom",
            errorCode:    "DATAVERSE_ERROR",
            safeToRetry:  true);

        var failed = store.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Null(failed.DownloadToken);
        Assert.Equal("boom", failed.ErrorMessage);
        Assert.Equal("DATAVERSE_ERROR", failed.ErrorCode);
        Assert.True(failed.SafeToRetry);
    }

    [Fact]
    public void UpdateStatus_TerminalReady_IgnoresSubsequentFailedTransition()
    {
        // Story 3.5 code-review P1 — host-shutdown drain must not wipe a job that
        // already reached Ready (token + document already stored).
        var store = new InMemoryJobStore();
        var id = store.CreateJob();
        store.UpdateStatus(id, JobStatus.Running, null, null);
        store.UpdateStatus(id, JobStatus.Ready, downloadToken: "tok-keep", errorMessage: null);

        store.UpdateStatus(id, JobStatus.Failed, downloadToken: null,
            errorMessage: "would clobber", errorCode: "HOST_SHUTDOWN", safeToRetry: true);

        var record = store.GetJob(id)!;
        Assert.Equal(JobStatus.Ready, record.Status);
        Assert.Equal("tok-keep", record.DownloadToken);
        Assert.Null(record.ErrorMessage);
        Assert.Null(record.ErrorCode);
    }

    [Fact]
    public void UpdateStatus_TerminalFailed_IgnoresSubsequentTransition()
    {
        var store = new InMemoryJobStore();
        var id = store.CreateJob();
        store.UpdateStatus(id, JobStatus.Running, null, null);
        store.UpdateStatus(id, JobStatus.Failed, null, "first failure",
            errorCode: "CREDENTIAL_REJECTED", safeToRetry: false);

        store.UpdateStatus(id, JobStatus.Failed, null, "second failure",
            errorCode: "AI_ERROR", safeToRetry: true);

        var record = store.GetJob(id)!;
        Assert.Equal("CREDENTIAL_REJECTED", record.ErrorCode);
        Assert.Equal("first failure", record.ErrorMessage);
        Assert.False(record.SafeToRetry);
    }

    [Fact]
    public void GetJob_UnknownId_ReturnsNull()
    {
        var store = new InMemoryJobStore();

        Assert.Null(store.GetJob("does-not-exist"));
    }

    [Fact]
    public void UpdateStatus_UnknownId_IsTrueNoOp_NoGhostRecordInserted()
    {
        var store = new InMemoryJobStore();

        store.UpdateStatus("ghost", JobStatus.Failed, null, "x");

        // Contract (IJobStore.UpdateStatus xmldoc): "No-op if the id is unknown."
        // Pins the fix for Story 3.1 code review P1 — previously AddOrUpdate silently
        // inserted, which would have let arbitrary callers plant records keyed on
        // untrusted strings.
        Assert.Null(store.GetJob("ghost"));
    }
}
