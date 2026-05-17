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
        var outcome   = "unknown";

        try
        {
            // ── Step 1: Connect ───────────────────────────────────────────────
            Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client;
            try
            {
                client = await _connectionFactory.ConnectAsync(task.Credentials, cancellationToken);
            }
            catch (DataverseConnectionException ex)
            {
                // AC-3 — connection rejection is the dedicated CREDENTIAL_REJECTED code.
                outcome = JobFailureCodes.CredentialRejected;
                throw new GenerationFailureException(
                    JobFailureCodes.CredentialRejected,
                    safeToRetry: false,
                    "Credentials were rejected by Dataverse.",
                    ex);
            }

            using (client)
            {
                try
                {
                    var downloadToken = await RunPipelineAsync(
                        client, task.Credentials.EnvironmentUrl, cancellationToken);
                    outcome = "ready";
                    return downloadToken;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    outcome = "cancelled";
                    throw;
                }
                catch (GenerationFailureException ex)
                {
                    outcome = ex.Code;
                    throw;
                }
                catch (FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> ex)
                {
                    _logger.LogWarning(
                        "Pipeline forensic dump (Dataverse FaultException) for job {JobId}:\n{Forensics}",
                        task.JobId, FormatExceptionForLog(ex));
                    outcome = JobFailureCodes.DataverseError;
                    throw new GenerationFailureException(
                        JobFailureCodes.DataverseError,
                        safeToRetry: true,
                        "Dataverse fault during environment scan.",
                        ex);
                }
                // E2E hotfix 2026-05-14 — `Anthropic.SDK.RateLimitsExceeded`
                // derives from `System.Net.Http.HttpRequestException`. The
                // network-fault filter below MUST sit AFTER this catch so
                // Anthropic 429s aren't mis-labelled `DATAVERSE_ERROR`.
                // Top-down catch resolution is the contract; type-ordering
                // beats `when`-pattern-matching for readability.
                catch (Anthropic.SDK.RateLimitsExceeded ex)
                {
                    _logger.LogWarning(
                        "Pipeline forensic dump (Anthropic 429) for job {JobId}:\n{Forensics}",
                        task.JobId, FormatExceptionForLog(ex));
                    outcome = JobFailureCodes.AiError;
                    throw new GenerationFailureException(
                        JobFailureCodes.AiError,
                        safeToRetry: true,
                        "Anthropic API rate limit exceeded.",
                        ex);
                }
                catch (Exception ex) when (ex is TimeoutException
                                                or CommunicationException
                                                or System.Net.Http.HttpRequestException)
                {
                    // R-HF-9 — split Anthropic-side network faults out of the
                    // DATAVERSE_ERROR bucket. Both SDKs throw HttpRequestException
                    // for transport errors; without this disambiguation an
                    // Anthropic 5xx is mis-labelled as a Dataverse fault.
                    var isAnthropic = IsAnthropicNetworkFault(ex);
                    var code = isAnthropic ? JobFailureCodes.AiError : JobFailureCodes.DataverseError;
                    var label = isAnthropic ? "Anthropic network failure" : "Dataverse network failure";
                    _logger.LogWarning(
                        "Pipeline forensic dump ({Label}) for job {JobId}:\n{Forensics}",
                        label, task.JobId, FormatExceptionForLog(ex));
                    outcome = code;
                    throw new GenerationFailureException(
                        code,
                        safeToRetry: true,
                        $"{label} during environment scan.",
                        ex);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Pipeline forensic dump (unclassified) for job {JobId}:\n{Forensics}",
                        task.JobId, FormatExceptionForLog(ex));
                    outcome = JobFailureCodes.AiError;
                    throw new GenerationFailureException(
                        JobFailureCodes.AiError,
                        safeToRetry: true,
                        "Agent orchestration failed.",
                        ex);
                }
            }
        }
        finally
        {
            // Story 3.5 code-review P11 — log elapsed time on ALL paths
            // (success + failure) so AC-10 baseline measurement reflects the
            // distribution of failure-mode durations, not only happy-path runs.
            // NFR-007: no credential data is in scope here.
            stopwatch.Stop();
            _logger.LogInformation(
                "Mode 1 generation finished for job {JobId} in {ElapsedSeconds:F1}s — outcome={Outcome}",
                task.JobId,
                stopwatch.Elapsed.TotalSeconds,
                outcome);
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
        AgentJsonModel parsed;
        try
        {
            parsed = ParseAgentJson(rawResponse);
        }
        catch (GenerationFailureException ex)
            when (ex.Code == JobFailureCodes.AiError && ex.InnerException is JsonException jex)
        {
            // E2E hotfix 2026-05-14 — emit a forensic head+tail of Claude's
            // raw response (with the JsonException line/byte offsets) so the
            // next AI_ERROR (JsonException) failure leaves a diagnosable
            // trail. NFR-007 holds: the orchestrator never sends credentials
            // to Claude (only the Mode 1 prompt + tool schemas; per
            // AgentOrchestrator.RunAsync), so the response cannot echo any
            // secret the tool didn't put there itself — and the tool surface
            // (5 Mode 1 tools) only emits public Dataverse metadata.
            var safe = TruncateForLog(rawResponse, headChars: 2000, tailChars: 500);
            _logger.LogWarning(
                "Mode 1 JSON parse failed (line={Line}, byte={Byte}, total={Total} chars); "
                + "raw head+tail: {RawSnippet}",
                jex.LineNumber,
                jex.BytePositionInLine,
                rawResponse?.Length ?? 0,
                safe);
            throw;
        }

        // ── Deterministic enrichment ──────────────────────────────────────────
        var tableCount        = parsed.Tables?.Count ?? 0;
        var fieldCount        = parsed.Fields?.Values.Sum(f => f?.Count ?? 0) ?? 0;
        var relationshipCount = parsed.Relationships?.Values.Sum(r => r?.Count ?? 0) ?? 0;
        var rating            = ComplexityRater.Rate(tableCount, fieldCount, relationshipCount);
        // Story 3.6 code-review P2 — strip null entries before the model is
        // built. JSON `[null, {...}]` deserialises to a real null in the list;
        // both PrefixAnalyzer and DocxBuilder would NRE on the element. Mirrors
        // the Story 3.5 P3 KeyObservations filter at the parse boundary.
        var tables = (IReadOnlyList<TableInfo>?)(
            parsed.Tables?.Where(t => t is not null).ToList())
            ?? Array.Empty<TableInfo>();

        // Story 3.6 — F-047 / FR-042. Deterministic publisher-prefix breakdown,
        // computed BEFORE the cancellation gate so a cancelled job emits no
        // blob and the analyzer cost is observable on the per-job log line.
        var prefixSummary = PrefixAnalyzer.Analyze(tables);

        // Story 3.7 — F-055 / FR-050. Defence-in-depth: a Claude response that
        // drops the `applicationUsers` key (e.g. Mode 1 prompt drift, or a stale
        // model that still emits the four-key Story 3.5 shape) parses as an
        // empty list rather than failing AI_ERROR. Null entries inside the
        // array are also filtered out at the parse boundary — mirrors the
        // Story 3.6 P2 null-entry filter for Tables.
        var applicationUsers = (IReadOnlyList<ApplicationUserInfo>?)(
            parsed.ApplicationUsers?.Where(u => u is not null).ToList())
            ?? Array.Empty<ApplicationUserInfo>();

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
                PrefixSummary     = prefixSummary,
            },
            Tables           = tables,
            Fields           = CoerceDict(parsed.Fields),
            Relationships    = CoerceDict(parsed.Relationships),
            ApplicationUsers = applicationUsers,
        };

        // ── Render + store ────────────────────────────────────────────────────
        var bytes = DocxBuilder.Build(model);

        // Story 3.5 code-review P9 — observe cancellation immediately before
        // committing the blob. `IDocumentStore.StoreAsync` itself does not
        // accept a CT (Phase 1), so if the per-task CTS just tripped we would
        // otherwise persist a 24-hour blob nobody can reach.
        cancellationToken.ThrowIfCancellationRequested();

        return await _documentStore.StoreAsync(bytes, TimeSpan.FromHours(24));
    }

    // ── Claude-JSON shape (internal) ─────────────────────────────────────────

    internal sealed class AgentJsonModel
    {
        public OrganisationDto?                              Organisation     { get; set; }
        public List<TableInfo>?                              Tables           { get; set; }
        public Dictionary<string, List<FieldInfo>?>?         Fields           { get; set; }
        public Dictionary<string, List<RelationshipInfo>?>?  Relationships    { get; set; }
        public List<string>?                                 KeyObservations  { get; set; }
        // Story 3.7 — F-055 / FR-050. Optional in the DTO so a Claude response
        // that omits the key (e.g. backwards-compatible four-key shape from
        // Story 3.5) deserialises cleanly and the safe-coalesce in
        // RunPipelineAsync renders the empty Section 5.
        public List<ApplicationUserInfo>?                    ApplicationUsers { get; set; }
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
        // E2E hotfix 2026-05-14 (R-HF-5) — Claude routinely prepends a
        // conversational preamble ("All data has been collected. Here is
        // the JSON…") and occasionally a trailing comment, despite the
        // explicit "JSON object only — no surrounding text" prompt rule.
        // StripCodeFences handles ```json wrappers; TrimToJsonObject
        // handles arbitrary prose around the JSON object by anchoring on
        // the first `{` and last `}`. Both run unconditionally — if the
        // input is already clean both helpers are near-no-ops.
        var trimmed = TrimToJsonObject(StripCodeFences(raw));
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

    // E2E hotfix 2026-05-14 (R-HF-5) — anchor on the first top-level
    // `{`/`[` and the last matching `}`/`]` so a Claude reply with prose
    // wrapping the JSON ("Here's what I found:\n\n{ … }\n\nLet me know if…")
    // still parses cleanly. The Mode 1 contract guarantees the root is an
    // object, but the bracket-fallback covers a future Mode where Claude
    // returns an array root. If neither bracket is found, returns the
    // input unchanged so existing error paths (empty-response, invalid-
    // JSON) surface as before.
    internal static string TrimToJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim();

        var firstObj = s.IndexOf('{');
        var firstArr = s.IndexOf('[');
        int start;
        char closer;
        if (firstObj < 0 && firstArr < 0) return s;
        if (firstObj < 0)                 { start = firstArr; closer = ']'; }
        else if (firstArr < 0)            { start = firstObj; closer = '}'; }
        else if (firstObj < firstArr)     { start = firstObj; closer = '}'; }
        else                              { start = firstArr; closer = ']'; }

        var end = s.LastIndexOf(closer);
        if (end < start) return s; // mismatched / partial — let JSON parser report.

        return s.Substring(start, end - start + 1);
    }

    internal static string StripCodeFences(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim();

        // Claude occasionally wraps JSON in ```json ... ``` despite explicit
        // instructions. Strip a single leading fence and the matching trailing
        // fence; leave any inner backticks alone.
        //
        // Story 3.5 code-review P4 — only strip the trailing fence when a
        // leading fence was actually found. Otherwise a fence-less JSON body
        // whose last meaningful character is part of a string ending in three
        // backticks (e.g. `{"x":"\`\`\`"}`) loses its closing chars and fails
        // to parse.
        const string fenceStart  = "```";
        const string fenceJson   = "```json";
        const string fenceEnd    = "```";

        var leadingFenceFound = false;
        if (s.StartsWith(fenceJson, StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(fenceJson.Length).TrimStart('\r', '\n', ' ', '\t');
            leadingFenceFound = true;
        }
        else if (s.StartsWith(fenceStart, StringComparison.Ordinal))
        {
            s = s.Substring(fenceStart.Length).TrimStart('\r', '\n', ' ', '\t');
            leadingFenceFound = true;
        }

        if (leadingFenceFound && s.EndsWith(fenceEnd, StringComparison.Ordinal))
        {
            s = s.Substring(0, s.Length - fenceEnd.Length).TrimEnd();
        }

        return s;
    }

    // E2E hotfix 2026-05-14 — bounded forensic dump for Mode 1 JSON parse
    // failures. Head + tail covers the two most common Claude failure modes:
    // (a) a malformed preamble (markdown fence, prose lead-in) at the head,
    // (b) a max-tokens truncation at the tail. Centre is collapsed to a
    // length marker so the log line stays bounded regardless of response
    // size. Never invoked outside the logging catch — pure helper.
    internal static string TruncateForLog(string? raw, int headChars, int tailChars)
    {
        if (string.IsNullOrEmpty(raw))            return "(empty)";
        if (raw.Length <= headChars + tailChars)  return raw;
        var elided = raw.Length - headChars - tailChars;
        return string.Concat(
            raw.AsSpan(0, headChars),
            $"… [{elided} chars elided] …",
            raw.AsSpan(raw.Length - tailChars, tailChars));
    }

    // E2E hotfix 2026-05-14 (R-HF-9) — full exception forensics for unexpected
    // pipeline failures. Prior logs surfaced only `inner=TypeName`, which made
    // mis-classified network faults (e.g. Anthropic 5xx looking like
    // Dataverse HttpRequestException) indistinguishable on the wire. Trade:
    // ex.Message and StackTrace may contain tenant/host fragments — acceptable
    // for a dev-time POC, must be scrubbed before production. NFR-007 still
    // holds for credentials: we never authenticate via URL params, so client
    // secrets never appear in URI strings, and ServiceClient does not echo
    // ClientSecret in exception text.
    internal static string FormatExceptionForLog(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var depth = 0;
        for (Exception? cur = ex; cur is not null && depth < 4; cur = cur.InnerException, depth++)
        {
            sb.Append("  [").Append(depth).Append("] ")
              .Append(cur.GetType().FullName)
              .Append(" (Source=").Append(cur.Source ?? "?").Append(")\n")
              .Append("      Message: ").Append(cur.Message).Append('\n');
            if (cur.StackTrace is { Length: > 0 } st)
            {
                sb.Append("      Stack:\n");
                foreach (var line in st.Split('\n'))
                {
                    sb.Append("        ").Append(line.TrimEnd('\r')).Append('\n');
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// R-HF-9 — heuristic to detect HttpRequestException that originated inside
    /// the Anthropic SDK rather than the Dataverse client. Inspects Source
    /// (assembly) and stack trace. Returns true when the network fault is
    /// AI-side, so the caller routes it to AI_ERROR instead of
    /// DATAVERSE_ERROR.
    /// </summary>
    internal static bool IsAnthropicNetworkFault(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur.Source?.StartsWith("Anthropic", StringComparison.OrdinalIgnoreCase) == true)
                return true;
            if (cur.StackTrace?.Contains("Anthropic.SDK", StringComparison.Ordinal) == true)
                return true;
        }
        return false;
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
