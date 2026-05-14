// F-036, FR-036, NFR-007, NFR-014 — Async job worker (Story 3.1, hardened in Story 3.5)
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Hosted background service that dequeues <see cref="GenerationTask"/> items from the
/// shared channel and drives each through <see cref="IGenerationPipeline"/>. Exceptions
/// in a single task are caught and recorded on the job; the loop continues so one failing
/// task cannot take down the whole worker (AC-6 / Story 3.1).
///
/// Story 3.5 additions:
///   - Per-task 10-minute timeout linked to the host stopping token (AC-9).
///   - Translates <see cref="GenerationFailureException"/> into structured Failed records
///     with stable error codes and SafeToRetry hints (NFR-014).
///   - Marks the in-flight job <c>HOST_SHUTDOWN</c>/safeToRetry=true on host stop so the
///     polling client sees a terminal state instead of an indefinite Running.
/// </summary>
public sealed class GenerationBackgroundService : BackgroundService
{
    private static readonly TimeSpan PerTaskTimeout = TimeSpan.FromMinutes(10);

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
        _jobStore.UpdateStatus(task.JobId, JobStatus.Running,
            downloadToken: null, errorMessage: null, errorCode: null, safeToRetry: null);

        // AC-9 — per-task 10-minute deadline. Linking with stoppingToken means host
        // shutdown also cancels in-flight pipeline work — disambiguated below.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(PerTaskTimeout);

        try
        {
            var downloadToken = await _pipeline.RunAsync(task, timeoutCts.Token);
            _jobStore.UpdateStatus(task.JobId, JobStatus.Ready,
                downloadToken, errorMessage: null, errorCode: null, safeToRetry: null);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown — flush the in-flight job so the polling client sees a
            // terminal state, then rethrow so the BackgroundService base class stops
            // cleanly. SafeToRetry=true because re-enqueuing after restart is safe —
            // no partial side-effects (the document was never stored, no DB writes).
            _logger.LogWarning("Generation task {JobId} interrupted by host shutdown", task.JobId);
            _jobStore.UpdateStatus(task.JobId, JobStatus.Failed,
                downloadToken: null,
                errorMessage:  "Generation interrupted by host shutdown.",
                errorCode:     JobFailureCodes.HostShutdown,
                safeToRetry:   true);
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // AC-9 — per-task timeout. SafeToRetry=true: timing-only failure, no
            // mutation occurred client-side.
            _logger.LogWarning("Generation task {JobId} exceeded {Minutes}-minute timeout",
                task.JobId, PerTaskTimeout.TotalMinutes);
            _jobStore.UpdateStatus(task.JobId, JobStatus.Failed,
                downloadToken: null,
                errorMessage:  "Generation exceeded the configured timeout.",
                errorCode:     JobFailureCodes.GenerationTimeout,
                safeToRetry:   true);
        }
        catch (GenerationFailureException ex)
        {
            // NFR-014 — typed pipeline failure. Inner-exception detail goes to the
            // structured log (CredentialDestructuringPolicy already redacts EnvironmentCredentials).
            _logger.LogError(ex, "Generation task {JobId} failed with code {Code}",
                task.JobId, ex.Code);
            _jobStore.UpdateStatus(task.JobId, JobStatus.Failed,
                downloadToken: null,
                errorMessage:  SanitisedClientMessage(ex.Code),
                errorCode:     ex.Code,
                safeToRetry:   ex.SafeToRetry);
        }
        catch (Exception ex)
        {
            // NFR-007 — Do NOT surface ex.Message on the job record. SDK exceptions
            // can embed tenant ids, authority URLs, or payload bytes. The polling
            // client sees a generic reason; the full exception lands in Serilog
            // (which already clamps the Dataverse/Anthropic namespaces to Warning
            // and runs `CredentialDestructuringPolicy`).
            _logger.LogError(ex, "Generation task {JobId} failed", task.JobId);
            _jobStore.UpdateStatus(task.JobId, JobStatus.Failed,
                downloadToken: null,
                errorMessage:  "Generation failed. Check server logs for details.",
                errorCode:     JobFailureCodes.GenerationFailed,
                safeToRetry:   false);
        }
    }

    private static string SanitisedClientMessage(string code) => code switch
    {
        JobFailureCodes.CredentialRejected => "Credentials were rejected by the target environment.",
        JobFailureCodes.DataverseError     => "The target environment returned an error.",
        JobFailureCodes.AiError            => "Document generation failed during AI orchestration.",
        JobFailureCodes.GenerationTimeout  => "Generation exceeded the configured timeout.",
        JobFailureCodes.HostShutdown       => "Generation interrupted by host shutdown.",
        _                                  => "Generation failed. Check server logs for details.",
    };
}
