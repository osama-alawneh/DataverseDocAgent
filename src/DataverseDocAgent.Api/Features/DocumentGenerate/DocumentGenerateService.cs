using System.Diagnostics;
using DataverseDocAgent.Api.Agent.Tools;
using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Features.SecurityCheck;
using DataverseDocAgent.Api.Jobs;
using DataverseDocAgent.Api.Pipeline;
using DataverseDocAgent.Api.Storage;
using DataverseDocAgent.Shared.Dataverse;

namespace DataverseDocAgent.Api.Features.DocumentGenerate;

/// <summary>.NET owns collection, evidence persistence, bounded local analysis and document publication.</summary>
public sealed class DocumentGenerateService(
    IDataverseConnectionFactory connectionFactory,
    AnalysisPipeline analysisPipeline,
    PipelineOptions options,
    IDocumentStore documentStore,
    ILogger<DocumentGenerateService> logger) : IGenerationPipeline
{
    public async Task<string> RunAsync(GenerationTask task, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var phase = "connection";
        try
        {
            var environmentUrl = task.Credentials.EnvironmentUrl;
            Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client;
            try { client = await connectionFactory.ConnectAsync(task.Credentials, cancellationToken); }
            finally { task.ReleaseCredentials(); }
            EvidenceSnapshot snapshot;
            using (client)
            {
                phase = "permissions";
                var permission = await SecurityCheckService.CheckConnectedAsync(client, cancellationToken);
                if (!permission.SafeToRun)
                    throw new GenerationFailureException("PERMISSION_DENIED", false, "Required Dataverse read permissions are missing.");
                phase = "collection";
                snapshot = await new EvidenceCollector().CollectAsync(
                    DataverseToolFactory.CreateMode1Tools(client, environmentUrl), cancellationToken);
            }
            phase = "analysis";
            // No live Dataverse connection or credential envelope is needed during analysis.
            var runDirectory = Path.Combine(Path.GetFullPath(options.RunRoot), Guid.NewGuid().ToString("N"));
            return await ProcessSnapshotAsync(snapshot, runDirectory, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (GenerationFailureException) { throw; }
        catch (DataverseConnectionException)
        {
            throw new GenerationFailureException(JobFailureCodes.CredentialRejected, false, "Dataverse credentials were rejected.");
        }
        catch (AnalysisRunException ex)
        {
            throw new GenerationFailureException(ex.Code, ex.Retryable, "Local Codex analysis could not complete.");
        }
        catch (Exception)
        {
            throw new GenerationFailureException(
                phase is "connection" or "permissions" or "collection" ? JobFailureCodes.DataverseError : JobFailureCodes.GenerationFailed,
                true, "Document generation could not complete.");
        }
        finally
        {
            task.ReleaseCredentials();
            logger.LogInformation("Generation job {JobId} finished in {Seconds:F1}s", task.JobId, timer.Elapsed.TotalSeconds);
        }
    }

    internal async Task<string> ProcessSnapshotAsync(EvidenceSnapshot snapshot, string runDirectory, CancellationToken ct)
    {
        await SnapshotStore.SaveAsync(snapshot, Path.Combine(runDirectory, "evidence"), ct);
        var analysis = await analysisPipeline.RunAsync(snapshot, runDirectory, ct);
        ct.ThrowIfCancellationRequested();
        var bytes = DocxBuilder.Build(ReportAssembler.Build(snapshot, analysis));
        ct.ThrowIfCancellationRequested();
        return await documentStore.StoreAsync(bytes, TimeSpan.FromHours(24));
    }
}
