// F-002, FR-002 — GetTableFieldsTool: per-table custom-attribute discovery (Story 3.4)
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseDocAgent.Api.Agent.Tools;

/// <summary>
/// Returns custom attributes (IsCustomAttribute = true) for the supplied table.
/// Emits only the five fields the Mode 1 prompt parser consumes
/// (<see cref="PromptBuilder"/> lines 52-65): <c>logicalName</c>,
/// <c>displayName</c>, <c>attributeType</c>, <c>requiredLevel</c>,
/// <c>description</c>. Picklist <c>options[]</c> and <c>defaultValue</c> are NOT
/// emitted (R-HF-10 scope reduction — see story-3.4 AC-2). Any SDK fault is
/// converted into the structured error JSON shape
/// <c>{ "error", "tableName" }</c> so the agent loop receives a tool result
/// rather than an exception (NFR-007 / AC-5).
/// </summary>
public sealed class GetTableFieldsTool : IDataverseTool
{
    private readonly IOrganizationService _service;

    private static readonly JsonElement s_inputSchema = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type": "object",
          "properties": {
            "tableName": {
              "type": "string",
              "description": "Logical name of the table whose custom attributes should be returned"
            }
          },
          "required": ["tableName"]
        }
        """);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Logical-name shape per Dataverse rules. Stricter than IsNullOrWhiteSpace —
    // rejects unicode-NBSP, RTL marks, mixed case, and SQL/JSON-injection-shaped
    // payloads before they hit the SDK and surface as a misleading "table not found".
    private static readonly Regex s_logicalNameShape = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    public GetTableFieldsTool(IOrganizationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public string      Name        => "get_table_fields";
    public string      Description => "Returns all custom fields for a specified table";
    public JsonElement InputSchema => s_inputSchema;

    public Task<string> ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var read = ReadTableName(input);
        if (read.Error is not null)
        {
            return Task.FromResult(SerializeError(read.Error, read.TableName));
        }
        var tableName = read.TableName!;

        try
        {
            var fetch = FetchCustomAttributes(tableName);
            if (!fetch.EntityFound)
            {
                // Distinguish "table missing" from "table has zero custom attributes":
                // an empty success would mislead the agent into accepting silence as truth.
                return Task.FromResult(SerializeError(
                    $"Table '{tableName}' not found", tableName));
            }
            return Task.FromResult(BuildResultJson(fetch.Attributes));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation must short-circuit, not be sanitized into a tool result —
            // otherwise the orchestrator's cancellation contract is silently broken.
            throw;
        }
        catch (FaultException<OrganizationServiceFault>)
        {
            // NFR-007 — surface a sanitized message, never the raw SDK fault. The
            // SDK message can echo connection-string fragments under some failure
            // modes, so we replace it with a fixed string keyed on tableName.
            return Task.FromResult(SerializeError($"Table '{tableName}' not found or inaccessible", tableName));
        }
        catch (Exception ex) when (ex is TimeoutException
                                       or CommunicationException
                                       or System.Net.Http.HttpRequestException)
        {
            // NFR-007 — non-FaultException SDK failures (timeouts, network resets, WCF
            // channel faults) must also produce a structured tool result rather than
            // unwinding into the orchestrator and losing the tableName correlation.
            return Task.FromResult(SerializeError(
                $"Dataverse call failed for table '{tableName}'", tableName));
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private record struct FetchResult(bool EntityFound, IReadOnlyList<AttributeMetadata> Attributes);

    private FetchResult FetchCustomAttributes(string tableName)
    {
        var query = new EntityQueryExpression
        {
            // Restrict to the requested entity by logical name.
            Criteria = new MetadataFilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new MetadataConditionExpression(
                        nameof(EntityMetadata.LogicalName),
                        MetadataConditionOperator.Equals,
                        tableName),
                },
            },
            // Pull only attribute metadata — fewer round-trip bytes than EntityFilters.All.
            AttributeQuery = new AttributeQueryExpression
            {
                Criteria = new MetadataFilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new MetadataConditionExpression(
                            nameof(AttributeMetadata.IsCustomAttribute),
                            MetadataConditionOperator.Equals,
                            true),
                    },
                },
            },
        };

        var request  = new RetrieveMetadataChangesRequest { Query = query };
        var response = (RetrieveMetadataChangesResponse)_service.Execute(request);

        // null OR empty EntityMetadata both mean "no entity matched the LogicalName
        // filter" — surface as not-found rather than emit an empty success.
        var entity = response.EntityMetadata?.FirstOrDefault();
        if (entity is null) return new FetchResult(EntityFound: false, Array.Empty<AttributeMetadata>());

        // Defensive copy: SDK collection could in principle be mutated under us; ToArray()
        // costs almost nothing for typical attribute counts and isolates downstream code.
        var attrs = entity.Attributes is null ? Array.Empty<AttributeMetadata>() : entity.Attributes.ToArray();
        return new FetchResult(EntityFound: true, attrs);
    }

    private static string BuildResultJson(IReadOnlyList<AttributeMetadata> attrs)
    {
        var fields = attrs.Select(BuildField).ToArray();
        return JsonSerializer.Serialize(new { fields }, s_jsonOptions);
    }

    private static FieldDto BuildField(AttributeMetadata attr) => new()
    {
        // R-HF-10: slimmed to the five PromptBuilder-consumed keys. `options[]`,
        // `defaultValue`, the BooleanOptionSetMetadata True/False branch, the
        // reflection cache for runtime OptionSet lookup, and the per-type
        // defaultValue extractor were all removed — PromptBuilder.cs:52-65 never
        // reads them and the picklist arrays inflated tool_result payloads
        // ~60 chars × ~8 options per column.
        DisplayName   = attr.DisplayName?.UserLocalizedLabel?.Label,
        LogicalName   = attr.LogicalName,
        AttributeType = attr.AttributeType?.ToString(),
        RequiredLevel = attr.RequiredLevel?.Value.ToString(),
        Description   = attr.Description?.UserLocalizedLabel?.Label,
    };

    private record struct TableNameRead(string? TableName, string? Error);

    private static TableNameRead ReadTableName(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return new(null, "Missing required parameter 'tableName'");
        if (!input.TryGetProperty("tableName", out var prop))
            return new(null, "Missing required parameter 'tableName'");
        if (prop.ValueKind == JsonValueKind.Null)
            return new(null, "Missing required parameter 'tableName'");
        if (prop.ValueKind != JsonValueKind.String)
            return new(null, "Parameter 'tableName' must be a string");

        var value = prop.GetString()?.Trim();
        if (string.IsNullOrEmpty(value))
            return new(null, "Missing required parameter 'tableName'");
        if (!s_logicalNameShape.IsMatch(value))
            return new(value, $"Invalid table name '{value}': must be lowercase letters, digits, and underscores only");
        return new(value, null);
    }

    private static string SerializeError(string message, string? tableName) =>
        JsonSerializer.Serialize(new { error = message, tableName }, s_jsonOptions);

    // ── DTOs ──────────────────────────────────────────────────────────────────

    private sealed class FieldDto
    {
        public string? DisplayName   { get; set; }
        public string? LogicalName   { get; set; }
        public string? AttributeType { get; set; }
        public string? RequiredLevel { get; set; }
        public string? Description   { get; set; }
    }
}
