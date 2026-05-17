// F-011, FR-011 — Mode 1 organisation metadata tool (Story 3.5)
using System.Globalization;
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseDocAgent.Api.Agent.Tools;

/// <summary>
/// Returns environment-wide metadata used to populate the executive summary
/// of the generated document — environment name, environment URL, server
/// version, and the base language display name. Errors are converted to the
/// structured JSON shape <c>{ "error" }</c> so the agent loop receives a tool
/// result rather than an exception (NFR-007 / sibling tool consistency).
/// </summary>
public sealed class GetOrganisationMetadataTool : IDataverseTool
{
    private readonly IOrganizationService _service;
    private readonly string? _environmentUrl;

    private static readonly JsonElement s_inputSchema =
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GetOrganisationMetadataTool(IOrganizationService service, string? environmentUrl = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        // Environment URL comes from the caller's credentials, not the SDK — the
        // Organization entity itself does not expose the public URL. Optional so
        // unit tests can construct the tool without a credentials echo path.
        _environmentUrl = environmentUrl;
    }

    public string      Name        => "get_organisation_metadata";
    public string      Description => "Returns environment-wide metadata (name, version, base language, URL).";
    public JsonElement InputSchema => s_inputSchema;

    public Task<string> ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var who          = (WhoAmIResponse)_service.Execute(new WhoAmIRequest());
            var organisation = _service.Retrieve(
                "organization",
                who.OrganizationId,
                new ColumnSet("name", "languagecode"));

            var versionResponse = (RetrieveVersionResponse)_service.Execute(new RetrieveVersionRequest());

            var environmentName = organisation.GetAttributeValue<string>("name");
            var languageCode    = organisation.GetAttributeValue<int?>("languagecode");

            return Task.FromResult(JsonSerializer.Serialize(new OrganisationDto
            {
                EnvironmentName    = environmentName,
                EnvironmentUrl     = _environmentUrl,
                Version            = versionResponse.Version,
                BaseLanguageCode   = languageCode,
                BaseLanguageName   = ResolveLanguageName(languageCode),
            }, s_jsonOptions));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FaultException<OrganizationServiceFault>)
        {
            return Task.FromResult(SerializeError("Failed to retrieve organisation metadata"));
        }
        catch (Exception ex) when (ex is TimeoutException
                                       or CommunicationException
                                       or System.Net.Http.HttpRequestException)
        {
            return Task.FromResult(SerializeError("Dataverse call failed while retrieving organisation metadata"));
        }
        catch
        {
            // Story 3.5 code-review P7 — sibling-tool error-contract parity.
            // Anything not handled by the typed branches above (InvalidPluginExecutionException,
            // unexpected SDK shapes, etc.) must still return the structured `{ error }` JSON
            // so the orchestrator never sees a raw exception escape this tool.
            return Task.FromResult(SerializeError("Unexpected failure while retrieving organisation metadata"));
        }
    }

    private static string? ResolveLanguageName(int? lcid)
    {
        if (lcid is null or 0) return null;
        if (lcid.Value < 0) return $"LCID {lcid.Value}";
        try
        {
            return CultureInfo.GetCultureInfo(lcid.Value).EnglishName;
        }
        catch (Exception ex) when (ex is CultureNotFoundException or ArgumentOutOfRangeException)
        {
            // Story 3.5 code-review P6/P8 — corrupted or non-Windows LCIDs (negative
            // values; out-of-range positives) must not escape this tool. Surface the
            // raw LCID number to Claude rather than fabricating or omitting.
            return $"LCID {lcid.Value}";
        }
    }

    private static string SerializeError(string message) =>
        JsonSerializer.Serialize(new { error = message }, s_jsonOptions);

    private sealed class OrganisationDto
    {
        public string? EnvironmentName  { get; set; }
        public string? EnvironmentUrl   { get; set; }
        public string? Version          { get; set; }
        public int?    BaseLanguageCode { get; set; }
        public string? BaseLanguageName { get; set; }
    }
}
