// F-036, NFR-007 — Story 3.1 background service unit tests
using System.Threading.Channels;
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseDocAgent.Tests;

public class GenerationBackgroundServiceTests
{
    [Fact]
    public async Task ProcessTaskAsync_Success_TransitionsQueuedRunningReady()
    {
        var store = new InMemoryJobStore();
        var id = store.CreateJob();
        var pipeline = new FakePipeline(_ => Task.FromResult("tok-success"));
        var service = BuildService(store, pipeline);

        await service.ProcessTaskAsync(
            new GenerationTask(id, BuildCreds()),
            CancellationToken.None);

        var record = store.GetJob(id)!;
        Assert.Equal(JobStatus.Ready, record.Status);
        Assert.Equal("tok-success", record.DownloadToken);
        Assert.Null(record.ErrorMessage);
    }

    [Fact]
    public async Task ProcessTaskAsync_PipelineThrows_MarksFailed_AndDoesNotLeakExceptionMessage()
    {
        const string sensitiveMessage =
            "Failed: clientSecret=super-secret; tenant=11111111-1111-1111-1111-111111111111";

        var store = new InMemoryJobStore();
        var id = store.CreateJob();
        var pipeline = new FakePipeline(_ => throw new InvalidOperationException(sensitiveMessage));
        var service = BuildService(store, pipeline);

        await service.ProcessTaskAsync(
            new GenerationTask(id, BuildCreds()),
            CancellationToken.None);

        var record = store.GetJob(id)!;
        Assert.Equal(JobStatus.Failed, record.Status);
        Assert.Null(record.DownloadToken);
        // NFR-007 — Exception message must not be echoed on the job record; a generic
        // sanitized message is surfaced instead.
        Assert.NotNull(record.ErrorMessage);
        Assert.DoesNotContain("super-secret",                           record.ErrorMessage!);
        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111",   record.ErrorMessage!);
    }

    [Fact]
    public async Task ExecuteAsync_OneTaskFailure_DoesNotStopLoop_SubsequentTaskSucceeds()
    {
        var store = new InMemoryJobStore();
        var failedId  = store.CreateJob();
        var succeedId = store.CreateJob();

        // Keep the channel open until we observe task-1 reaching Failed, then write
        // task-2 and complete. Proves ExecuteAsync survives a per-task fault across
        // real inter-task boundaries, not just within a pre-closed channel.
        var pipeline = new FakePipeline(task =>
            task.JobId == failedId
                ? throw new InvalidOperationException("boom")
                : Task.FromResult("tok-after-fault"));

        var channel = Channel.CreateUnbounded<GenerationTask>();
        var service = new GenerationBackgroundService(
            channel,
            store,
            pipeline,
            NullLogger<GenerationBackgroundService>.Instance);

        using var hostCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await service.StartAsync(hostCts.Token);

        await channel.Writer.WriteAsync(new GenerationTask(failedId, BuildCreds()));

        var pollDeadline = DateTime.UtcNow.AddSeconds(5);
        while (store.GetJob(failedId)!.Status != JobStatus.Failed)
        {
            Assert.True(DateTime.UtcNow < pollDeadline,
                "background service did not record task-1 failure within deadline");
            await Task.Delay(25, hostCts.Token);
        }

        await channel.Writer.WriteAsync(new GenerationTask(succeedId, BuildCreds()));
        channel.Writer.Complete();

        await service.ExecuteTask!;
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(JobStatus.Failed, store.GetJob(failedId)!.Status);
        var succeeded = store.GetJob(succeedId)!;
        Assert.Equal(JobStatus.Ready, succeeded.Status);
        Assert.Equal("tok-after-fault", succeeded.DownloadToken);
    }

    [Fact]
    public async Task ProcessTaskAsync_HostShutdownCancellation_PropagatesWithoutMarkingFailed()
    {
        // AC-3 + Dev Notes: OperationCanceledException on host shutdown must rethrow so
        // the BackgroundService base class stops cleanly — it must NOT collapse into a
        // generic Failed job record. Pins the `when stoppingToken.IsCancellationRequested`
        // catch filter against accidental removal.
        var store = new InMemoryJobStore();
        var id = store.CreateJob();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var pipeline = new FakePipeline(_ => throw new OperationCanceledException(cts.Token));
        var service = BuildService(store, pipeline);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ProcessTaskAsync(new GenerationTask(id, BuildCreds()), cts.Token));

        var record = store.GetJob(id)!;
        Assert.NotEqual(JobStatus.Failed, record.Status);
        Assert.Null(record.ErrorMessage);
    }

    private static GenerationBackgroundService BuildService(IJobStore store, IGenerationPipeline pipeline)
        => new(
            Channel.CreateUnbounded<GenerationTask>(),
            store,
            pipeline,
            NullLogger<GenerationBackgroundService>.Instance);

    private static EnvironmentCredentials BuildCreds() => new()
    {
        EnvironmentUrl = "https://example.crm.dynamics.com",
        TenantId       = "11111111-1111-1111-1111-111111111111",
        ClientId       = "22222222-2222-2222-2222-222222222222",
        ClientSecret   = "super-secret",
    };

    private sealed class FakePipeline : IGenerationPipeline
    {
        private readonly Func<GenerationTask, Task<string>> _behaviour;

        public FakePipeline(Func<GenerationTask, Task<string>> behaviour)
        {
            _behaviour = behaviour;
        }

        public Task<string> RunAsync(GenerationTask task, CancellationToken cancellationToken)
            => _behaviour(task);
    }
}
