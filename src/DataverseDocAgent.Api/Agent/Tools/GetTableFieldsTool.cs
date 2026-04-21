// F-002, FR-002 — GetTableFieldsTool: per-table custom-attribute discovery (Story 3.4)
using System.Reflection;
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseDocAgent.Api.Agent.Tools;

/// <summary>
/// Returns custom attributes (IsCustomAttribute = true) for the supplied table.
/// Picklist / MultiSelectPicklist attributes carry an additional <c>options</c>
/// array of <c>{ label, value }</c>. Any SDK fault is converted into the
/// structured error JSON shape <c>{ "error", "tableName" }</c> so the agent
/// loop receives a tool result rather than an exception (NFR-007 / AC-5).
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

        if (!TryReadTableName(input, out var tableName))
        {
            // Schema-level required field is enforced by Claude, but the tool itself
            // must still degrade gracefully if a malformed call slips through.
            return Task.FromResult(SerializeError("Missing required parameter 'tableName'", tableName: null));
        }

        try
        {
            var attrs = FetchCustomAttributes(tableName);
            var json  = BuildResultJson(attrs);
            return Task.FromResult(json);
        }
        catch (FaultException<OrganizationServiceFault>)
        {
            // NFR-007 — surface a sanitized message, never the raw SDK fault. The
            // SDK message can echo connection-string fragments under some failure
            // modes, so we replace it with a fixed string keyed on tableName.
            return Task.FromResult(SerializeError($"Table '{tableName}' not found or inaccessible", tableName));
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private IReadOnlyList<AttributeMetadata> FetchCustomAttributes(string tableName)
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

        var entity = response.EntityMetadata?.FirstOrDefault();
        return entity?.Attributes ?? Array.Empty<AttributeMetadata>();
    }

    private static string BuildResultJson(IReadOnlyList<AttributeMetadata> attrs)
    {
        var fields = attrs.Select(BuildField).ToArray();
        return JsonSerializer.Serialize(new { fields }, s_jsonOptions);
    }

    private static FieldDto BuildField(AttributeMetadata attr)
    {
        var dto = new FieldDto
        {
            DisplayName   = attr.DisplayName?.UserLocalizedLabel?.Label,
            LogicalName   = attr.LogicalName,
            AttributeType = attr.AttributeType?.ToString(),
            RequiredLevel = attr.RequiredLevel?.Value.ToString(),
        };

        // Options are only meaningful for enum-shaped types. Picklist /
        // MultiSelectPicklist / State / Status all expose OptionSet, but each derived
        // type may shadow the base property with `new`, so a base-type cast can return
        // null when the derived setter was used. Reflect on the runtime type instead.
        var optionSet = attr.GetType()
            .GetProperty("OptionSet", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(attr) as OptionSetMetadata;

        if (optionSet?.Options is { Count: > 0 } options)
        {
            dto.Options = options.Select(o => new OptionDto
            {
                // Prefer UserLocalizedLabel; fall back to first LocalizedLabel so
                // option labels still surface when only LocalizedLabels were populated
                // (some SDK code paths and the Label(string,int) ctor don't set
                // UserLocalizedLabel).
                Label = o.Label?.UserLocalizedLabel?.Label
                        ?? o.Label?.LocalizedLabels?.FirstOrDefault()?.Label,
                // OptionSet integer; Value is nullable on the SDK type but is always set
                // for Picklist/MultiSelect options in practice — coerce with default 0
                // rather than emit null and break Claude's downstream consumption.
                Value = o.Value ?? 0,
            }).ToArray();
        }

        return dto;
    }

    private static bool TryReadTableName(JsonElement input, out string tableName)
    {
        tableName = string.Empty;
        if (input.ValueKind != JsonValueKind.Object) return false;
        if (!input.TryGetProperty("tableName", out var prop)) return false;
        if (prop.ValueKind != JsonValueKind.String) return false;
        var value = prop.GetString();
        if (string.IsNullOrWhiteSpace(value)) return false;
        tableName = value;
        return true;
    }

    private static string SerializeError(string message, string? tableName) =>
        JsonSerializer.Serialize(new { error = message, tableName }, s_jsonOptions);

    // ── DTOs ──────────────────────────────────────────────────────────────────

    private sealed class FieldDto
    {
        public string?             DisplayName   { get; set; }
        public string?             LogicalName   { get; set; }
        public string?             AttributeType { get; set; }
        public string?             RequiredLevel { get; set; }
        public IReadOnlyList<OptionDto>? Options { get; set; }
    }

    private sealed class OptionDto
    {
        public string? Label { get; set; }
        public int     Value { get; set; }
    }
}
