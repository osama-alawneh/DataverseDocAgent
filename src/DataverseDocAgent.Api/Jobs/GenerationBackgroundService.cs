// F-036, FR-036, NFR-007, NFR-014 — Async job worker (Story 3.1)
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Hosted background service that dequeues <see cref="GenerationTask"/> items from the
/// shared channel and drives each through <see cref="IGenerationPipeline"/>. Exceptions
/// in a single task are caught and recorded on the job; the loop continues so one failing
/// task cannot take down the whole worker (AC-6).
/// </summary>
public sealed class GenerationBackgroundService : BackgroundService
{
    private readonly Channel<GenerationTask> _channel;
    private readonly IJobStore _jobStore;
    private readonly IGenerationPipeline _pipeline;
    private readonly ILogger<GenerationBackgroundService> _logger;

    public GenerationBackgroundService(
        Channel<GenerationTask> channel,
        IJobStore jobStore,
        IGenerationPipeline pipeline,
        ILogger<GenerationBackgroundService> logger)
    {
        _channel = channel;
        _jobStore = jobStore;
        _pipeline = pipeline;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var task in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessTaskAsync(task, stoppingToken);
        }
    }

    // NFR-007 — Task-local variable released on next iteration; GC reclaims the
    // EnvironmentCredentials reference. No explicit zeroing (managed strings).
    internal async Task ProcessTaskAsync(GenerationTask task, CancellationToken stoppingToken)
    {
        _jobStore.UpdateStatus(task.JobId, JobStatus.Running, downloadToken: null, errorMessage: null);

        try
        {
            var downloadToken = await _pipeline.RunAsync(task, stoppingToken);
            _jobStore.UpdateStatus(task.JobId, JobStatus.Ready, downloadToken, errorMessage: null);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown — surface cancellation and let the base class stop cleanly.
            throw;
        }
        catch (Exception ex)
        {
            // NFR-007 — Do NOT surface ex.Message on the job record. SDK exceptions
            // can embed tenant ids, authority URLs, or payload bytes. The polling
            // client sees a generic reason; the full exception lands in Serilog
            // (which already clamps the Dataverse/Anthropic namespaces to Warning
            // and runs `CredentialDestructuringPolicy`).
            _logger.LogError(ex, "Generation task {JobId} failed", task.JobId);
            _jobStore.UpdateStatus(task.JobId, JobStatus.Failed, downloadToken: null,
                errorMessage: "Generation failed. Check server logs for details.");
        }
    }
}
