// F-003, FR-003 — GetRelationshipsTool: per-table custom relationship discovery (Story 3.4)
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace DataverseDocAgent.Api.Agent.Tools;

/// <summary>
/// Returns all custom relationships (1:N where the supplied table is referencing or
/// referenced, and N:N where it is Entity1 or Entity2) for the supplied table.
/// 1:N entries carry a <c>cascadeConfiguration</c> object whose four fields
/// (delete / assign / share / unshare) are always present as <see cref="CascadeType"/>
/// strings — null cascade slots are reported as <c>"NoCascade"</c> so consumers see a
/// uniform shape. Any SDK fault is converted into the structured error JSON shape
/// <c>{ "error", "tableName" }</c> so the agent loop receives a tool result rather
/// than an exception (NFR-007 / AC-5).
/// </summary>
public sealed class GetRelationshipsTool : IDataverseTool
{
    private readonly IOrganizationService _service;

    private static readonly JsonElement s_inputSchema = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type": "object",
          "properties": {
            "tableName": {
              "type": "string",
              "description": "Logical name of the table whose custom relationships should be returned"
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

    // Logical-name shape per Dataverse rules: lowercase letter + underscore/digits/letters.
    // Stricter than IsNullOrWhiteSpace — rejects unicode-NBSP, RTL marks, mixed case,
    // and SQL/JSON-injection-shaped payloads before they hit the SDK and surface as
    // a misleading "table not found" fault.
    private static readonly Regex s_logicalNameShape = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    public GetRelationshipsTool(IOrganizationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public string      Name        => "get_relationships";
    public string      Description => "Returns all custom relationships for a specified table";
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
            var entity = FetchEntityWithRelationships(tableName);
            if (entity is null)
            {
                // Distinguish "table missing" from "table has zero custom relationships":
                // empty success would mislead the agent into accepting silence as truth.
                return Task.FromResult(SerializeError(
                    $"Table '{tableName}' not found or returned no metadata", tableName));
            }
            return Task.FromResult(BuildResultJson(entity, tableName));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation must short-circuit, not be sanitized into a tool result —
            // otherwise the orchestrator's cancellation contract is silently broken.
            throw;
        }
        catch (FaultException<OrganizationServiceFault>)
        {
            // NFR-007 — sanitize SDK fault. The raw message can echo connection-string
            // fragments under some failure modes; replace with a fixed string keyed
            // on tableName before handing back to Claude.
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

    private EntityMetadata? FetchEntityWithRelationships(string tableName)
    {
        // EntityFilters.Relationships pulls 1:N + N:1 + N:N in a single round-trip
        // — cheaper than three RetrieveRelationship calls and gives a stable
        // snapshot (no risk of inconsistency between filtered subsets).
        // RetrieveAsIfPublished=true so customizers see edges they just created in
        // the same session — matches the "current state for documentation" intent.
        var request = new RetrieveEntityRequest
        {
            LogicalName    = tableName,
            EntityFilters  = EntityFilters.Relationships,
            RetrieveAsIfPublished = true,
        };
        var response = (RetrieveEntityResponse)_service.Execute(request);
        return response.EntityMetadata;
    }

    private static string BuildResultJson(EntityMetadata entity, string tableName)
    {
        // Dedupe by SchemaName: the SDK returns self-referencing 1:N edges in BOTH
        // OneToManyRelationships AND ManyToOneRelationships; without dedup the agent
        // sees the same edge twice and may double-count.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relationships = new List<object>();

        AppendOneToMany(relationships, seen, entity.OneToManyRelationships, tableName);
        AppendOneToMany(relationships, seen, entity.ManyToOneRelationships, tableName);
        AppendManyToMany(relationships, seen, entity.ManyToManyRelationships, tableName);

        return JsonSerializer.Serialize(new { relationships }, s_jsonOptions);
    }

    private static void AppendOneToMany(
        List<object> sink,
        HashSet<string> seen,
        OneToManyRelationshipMetadata[]? rels,
        string tableName)
    {
        if (rels is null) return;
        foreach (var r in rels)
        {
            if (r.IsCustomRelationship != true) continue;
            // Defensive: SDK should already restrict to edges touching the supplied
            // table, but the AC names this filter explicitly. Cheap guard prevents a
            // future SDK or query refactor silently widening the result set.
            if (!string.Equals(r.ReferencingEntity, tableName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.ReferencedEntity, tableName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (r.SchemaName is not null && !seen.Add(r.SchemaName)) continue;

            sink.Add(new OneToManyDto
            {
                RelationshipType    = "OneToMany",
                SchemaName          = r.SchemaName,
                ReferencingEntity   = r.ReferencingEntity,
                ReferencedEntity    = r.ReferencedEntity,
                CascadeConfiguration = MapCascade(r.CascadeConfiguration),
            });
        }
    }

    private static void AppendManyToMany(
        List<object> sink,
        HashSet<string> seen,
        ManyToManyRelationshipMetadata[]? rels,
        string tableName)
    {
        if (rels is null) return;
        foreach (var r in rels)
        {
            if (r.IsCustomRelationship != true) continue;
            if (!string.Equals(r.Entity1LogicalName, tableName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(r.Entity2LogicalName, tableName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (r.SchemaName is not null && !seen.Add(r.SchemaName)) continue;

            sink.Add(new ManyToManyDto
            {
                RelationshipType   = "ManyToMany",
                SchemaName         = r.SchemaName,
                Entity1LogicalName = r.Entity1LogicalName,
                Entity2LogicalName = r.Entity2LogicalName,
            });
        }
    }

    private static CascadeDto MapCascade(CascadeConfiguration? cfg) =>
        // AC-3 says all four cascade behaviours must be present as strings. SDK
        // returns null for any unset field; collapse to "NoCascade" so consumers
        // never see a missing cascade slot.
        new()
        {
            Delete  = (cfg?.Delete  ?? CascadeType.NoCascade).ToString(),
            Assign  = (cfg?.Assign  ?? CascadeType.NoCascade).ToString(),
            Share   = (cfg?.Share   ?? CascadeType.NoCascade).ToString(),
            Unshare = (cfg?.Unshare ?? CascadeType.NoCascade).ToString(),
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

    private sealed class OneToManyDto
    {
        public string?      RelationshipType     { get; set; }
        public string?      SchemaName           { get; set; }
        public string?      ReferencingEntity    { get; set; }
        public string?      ReferencedEntity     { get; set; }
        public CascadeDto?  CascadeConfiguration { get; set; }
    }

    private sealed class ManyToManyDto
    {
        public string? RelationshipType   { get; set; }
        public string? SchemaName         { get; set; }
        public string? Entity1LogicalName { get; set; }
        public string? Entity2LogicalName { get; set; }
    }

    private sealed class CascadeDto
    {
        public string? Delete  { get; set; }
        public string? Assign  { get; set; }
        public string? Share   { get; set; }
        public string? Unshare { get; set; }
    }
}
