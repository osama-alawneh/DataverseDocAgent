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
    public void UpdateStatus_ReplacesRecordFields()
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

        store.UpdateStatus(id, JobStatus.Failed, downloadToken: null, errorMessage: "boom");
        var failed = store.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Null(failed.DownloadToken);
        Assert.Equal("boom", failed.ErrorMessage);
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
