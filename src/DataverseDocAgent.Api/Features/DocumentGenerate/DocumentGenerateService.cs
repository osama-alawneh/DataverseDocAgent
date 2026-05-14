// F-001, F-002, F-003, F-011, F-013, F-036 — Mode 1 generation pipeline (Story 3.5)
// NFR-001 — elapsed time captured for baseline comparison
// NFR-007 — credentials never logged; raw SDK/Anthropic messages never surfaced
using System.Diagnostics;
using System.ServiceModel;
using System.Text.Json;
using Anthropic.SDK;
using DataverseDocAgent.Api.Agent;
using DataverseDocAgent.Api.Agent.Tools;
using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Jobs;
using DataverseDocAgent.Api.Storage;
using DataverseDocAgent.Shared.Dataverse;

namespace DataverseDocAgent.Api.Features.DocumentGenerate;

/// <summary>
/// Implements <see cref="IGenerationPipeline"/> for Mode 1. Drives the request
/// from credential-validated connection through tool-equipped Claude
/// orchestration, deterministic complexity rating, and .docx assembly to the
/// document store. Failure modes are surfaced as
/// <see cref="GenerationFailureException"/> so the background service can
/// translate them into structured job records (NFR-014).
/// </summary>
public sealed class DocumentGenerateService : IGenerationPipeline
{
    private readonly IDataverseConnectionFactory _connectionFactory;
    private readonly Func<AgentOrchestrator>     _orchestratorFactory;
    private readonly IDocumentStore              _documentStore;
    private readonly ILogger<DocumentGenerateService> _logger;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public DocumentGenerateService(
        IDataverseConnectionFactory connectionFactory,
        Func<AgentOrchestrator>     orchestratorFactory,
        IDocumentStore              documentStore,
        ILogger<DocumentGenerateService> logger)
    {
        _connectionFactory   = connectionFactory;
        _orchestratorFactory = orchestratorFactory;
        _documentStore       = documentStore;
        _logger              = logger;
    }

    public async Task<string> RunAsync(GenerationTask task, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        // ── Step 1: Connect ───────────────────────────────────────────────────
        Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client;
        try
        {
            client = await _connectionFactory.ConnectAsync(task.Credentials, cancellationToken);
        }
        catch (DataverseConnectionException ex)
        {
            // AC-3 — connection rejection is the dedicated CREDENTIAL_REJECTED code.
            // The factory has already sanitised the message; credentials go out of
            // scope when this method returns (NFR-007).
            throw new GenerationFailureException(
                JobFailureCodes.CredentialRejected,
                safeToRetry: false,
                "Credentials were rejected by Dataverse.",
                ex);
        }

        // ── Steps 2–10: Run pipeline against the connected client ────────────
        using (client)
        {
            string downloadToken;
            try
            {
                downloadToken = await RunPipelineAsync(
                    client, task.Credentials.EnvironmentUrl, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Either per-task timeout (10 min) or host shutdown. Background service
                // disambiguates based on which token tripped. Propagate without
                // wrapping — OCE is the canonical signal.
                throw;
            }
            catch (GenerationFailureException)
            {
                // Already typed — surface as-is so the bg service preserves the code.
                throw;
            }
            catch (FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> ex)
            {
                throw new GenerationFailureException(
                    JobFailureCodes.DataverseError,
                    safeToRetry: true,
                    "Dataverse fault during environment scan.",
                    ex);
            }
            catch (Exception ex) when (ex is TimeoutException
                                            or CommunicationException
                                            or System.Net.Http.HttpRequestException)
            {
                throw new GenerationFailureException(
                    JobFailureCodes.DataverseError,
                    safeToRetry: true,
                    "Network failure during environment scan.",
                    ex);
            }
            catch (Anthropic.SDK.RateLimitsExceeded ex)
            {
                throw new GenerationFailureException(
                    JobFailureCodes.AiError,
                    safeToRetry: true,
                    "Anthropic API rate limit exceeded.",
                    ex);
            }
            catch (Exception ex)
            {
                // Anthropic SDK does not export a public common base for transport
                // errors in v5.10.0 — catch-all reaches AI_ERROR by default because
                // the most likely remaining failure mode after the typed branches
                // above is the agent loop. CONTEXT: the bg service still has a
                // generic catch that surfaces GENERATION_FAILED — that one fires
                // only if a non-Exception derived throw somehow reaches it.
                throw new GenerationFailureException(
                    JobFailureCodes.AiError,
                    safeToRetry: true,
                    "Agent orchestration failed.",
                    ex);
            }

            stopwatch.Stop();
            // NFR-001 — elapsed time only; no credential surface.
            _logger.LogInformation(
                "Mode 1 generation completed for job {JobId} in {ElapsedSeconds:F1}s",
                task.JobId,
                stopwatch.Elapsed.TotalSeconds);
            return downloadToken;
        }
    }

    private async Task<string> RunPipelineAsync(
        Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client,
        string environmentUrl,
        CancellationToken cancellationToken)
    {
        // ── Tools ─────────────────────────────────────────────────────────────
        var tools = DataverseToolFactory.CreateMode1Tools(client, environmentUrl);

        // ── Orchestrator ──────────────────────────────────────────────────────
        var orchestrator = _orchestratorFactory();
        var prompt       = PromptBuilder.BuildMode1Prompt();
        var rawResponse  = await orchestrator.RunAsync(prompt, tools, cancellationToken);

        if (string.Equals(rawResponse, AgentOrchestrator.MaxIterationsSentinel, StringComparison.Ordinal))
        {
            throw new GenerationFailureException(
                JobFailureCodes.AiError,
                safeToRetry: true,
                "Agent loop exceeded iteration ceiling without producing a final response.");
        }

        // ── Parse Claude JSON ─────────────────────────────────────────────────
        var parsed = ParseAgentJson(rawResponse);

        // ── Deterministic enrichment ──────────────────────────────────────────
        var tableCount        = parsed.Tables?.Count ?? 0;
        var fieldCount        = parsed.Fields?.Values.Sum(f => f?.Count ?? 0) ?? 0;
        var relationshipCount = parsed.Relationships?.Values.Sum(r => r?.Count ?? 0) ?? 0;
        var rating            = ComplexityRater.Rate(tableCount, fieldCount, relationshipCount);

        var model = new GeneratedDocumentModel
        {
            Summary = new ExecutiveSummary
            {
                EnvironmentName   = parsed.Organisation?.EnvironmentName,
                EnvironmentUrl    = parsed.Organisation?.EnvironmentUrl ?? environmentUrl,
                Version           = parsed.Organisation?.Version,
                BaseLanguageName  = parsed.Organisation?.BaseLanguageName,
                ScanDate          = DateTime.UtcNow,
                ComplexityRating  = rating,
                TableCount        = tableCount,
                FieldCount        = fieldCount,
                RelationshipCount = relationshipCount,
                KeyObservations   = (IReadOnlyList<string>?)parsed.KeyObservations ?? Array.Empty<string>(),
            },
            Tables        = (IReadOnlyList<TableInfo>?)parsed.Tables ?? Array.Empty<TableInfo>(),
            Fields        = CoerceDict(parsed.Fields),
            Relationships = CoerceDict(parsed.Relationships),
        };

        // ── Render + store ────────────────────────────────────────────────────
        var bytes = DocxBuilder.Build(model);
        return await _documentStore.StoreAsync(bytes, TimeSpan.FromHours(24));
    }

    // ── Claude-JSON shape (internal) ─────────────────────────────────────────

    internal sealed class AgentJsonModel
    {
        public OrganisationDto?                              Organisation    { get; set; }
        public List<TableInfo>?                              Tables          { get; set; }
        public Dictionary<string, List<FieldInfo>?>?         Fields          { get; set; }
        public Dictionary<string, List<RelationshipInfo>?>?  Relationships   { get; set; }
        public List<string>?                                 KeyObservations { get; set; }
    }

    internal sealed class OrganisationDto
    {
        public string? EnvironmentName  { get; set; }
        public string? EnvironmentUrl   { get; set; }
        public string? Version          { get; set; }
        public string? BaseLanguageName { get; set; }
    }

    internal static AgentJsonModel ParseAgentJson(string raw)
    {
        var trimmed = StripCodeFences(raw);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new GenerationFailureException(
                JobFailureCodes.AiError,
                safeToRetry: true,
                "Agent returned an empty response.");
        }

        try
        {
            var model = JsonSerializer.Deserialize<AgentJsonModel>(trimmed, s_jsonOptions);
            return model ?? throw new GenerationFailureException(
                JobFailureCodes.AiError,
                safeToRetry: true,
                "Agent JSON deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new GenerationFailureException(
                JobFailureCodes.AiError,
                safeToRetry: true,
                "Agent response was not valid JSON.",
                ex);
        }
    }

    internal static string StripCodeFences(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim();

        // Claude occasionally wraps JSON in ```json ... ``` despite explicit
        // instructions. Strip a single leading fence and the matching trailing
        // fence; leave any inner backticks alone.
        const string fenceStart  = "```";
        const string fenceJson   = "```json";
        const string fenceEnd    = "```";

        if (s.StartsWith(fenceJson, StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(fenceJson.Length).TrimStart('\r', '\n', ' ', '\t');
        }
        else if (s.StartsWith(fenceStart, StringComparison.Ordinal))
        {
            s = s.Substring(fenceStart.Length).TrimStart('\r', '\n', ' ', '\t');
        }

        if (s.EndsWith(fenceEnd, StringComparison.Ordinal))
        {
            s = s.Substring(0, s.Length - fenceEnd.Length).TrimEnd();
        }

        return s;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<T>> CoerceDict<T>(
        Dictionary<string, List<T>?>? source)
    {
        if (source is null) return new Dictionary<string, IReadOnlyList<T>>();
        var result = new Dictionary<string, IReadOnlyList<T>>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            result[kvp.Key] = (IReadOnlyList<T>?)kvp.Value ?? Array.Empty<T>();
        }
        return result;
    }
}
