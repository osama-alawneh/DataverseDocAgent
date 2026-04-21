// F-003, FR-003 — GetRelationshipsTool: per-table custom relationship discovery (Story 3.4)
using System.ServiceModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace DataverseDocAgent.Api.Agent.Tools;

/// <summary>
/// Returns all custom relationships (1:N where the supplied table is referencing or
/// referenced, and N:N where it is Entity1 or Entity2) for the supplied table.
/// 1:N entries carry a <c>cascadeConfiguration</c> object with delete / assign /
/// share / unshare strings drawn from <see cref="CascadeType"/>. Any SDK fault is
/// converted into the structured error JSON shape <c>{ "error", "tableName" }</c>
/// so the agent loop receives a tool result rather than an exception (NFR-007 / AC-5).
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

        if (!TryReadTableName(input, out var tableName))
        {
            return Task.FromResult(SerializeError("Missing required parameter 'tableName'", tableName: null));
        }

        try
        {
            var entity = FetchEntityWithRelationships(tableName);
            return Task.FromResult(BuildResultJson(entity));
        }
        catch (FaultException<OrganizationServiceFault>)
        {
            // NFR-007 — sanitize SDK fault. The raw message can echo connection-string
            // fragments under some failure modes; replace with a fixed string keyed
            // on tableName before handing back to Claude.
            return Task.FromResult(SerializeError($"Table '{tableName}' not found or inaccessible", tableName));
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private EntityMetadata? FetchEntityWithRelationships(string tableName)
    {
        // EntityFilters.Relationships pulls 1:N + N:1 + N:N in a single round-trip
        // — cheaper than three RetrieveRelationship calls and gives a stable
        // snapshot (no risk of inconsistency between filtered subsets).
        var request = new RetrieveEntityRequest
        {
            LogicalName    = tableName,
            EntityFilters  = EntityFilters.Relationships,
            RetrieveAsIfPublished = false,
        };
        var response = (RetrieveEntityResponse)_service.Execute(request);
        return response.EntityMetadata;
    }

    private static string BuildResultJson(EntityMetadata? entity)
    {
        var relationships = new List<object>();
        if (entity is not null)
        {
            // Both OneToMany (table is parent / referenced) and ManyToOne (table is
            // child / referencing) are 1:N relationships from different perspectives —
            // surface both as relationshipType "OneToMany" so Claude sees a uniform
            // view of the table's 1:N edges.
            AppendOneToMany(relationships, entity.OneToManyRelationships);
            AppendOneToMany(relationships, entity.ManyToOneRelationships);
            AppendManyToMany(relationships, entity.ManyToManyRelationships);
        }
        return JsonSerializer.Serialize(new { relationships }, s_jsonOptions);
    }

    private static void AppendOneToMany(List<object> sink, OneToManyRelationshipMetadata[]? rels)
    {
        if (rels is null) return;
        foreach (var r in rels)
        {
            if (r.IsCustomRelationship != true) continue;
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

    private static void AppendManyToMany(List<object> sink, ManyToManyRelationshipMetadata[]? rels)
    {
        if (rels is null) return;
        foreach (var r in rels)
        {
            if (r.IsCustomRelationship != true) continue;
            sink.Add(new ManyToManyDto
            {
                RelationshipType   = "ManyToMany",
                SchemaName         = r.SchemaName,
                Entity1LogicalName = r.Entity1LogicalName,
                Entity2LogicalName = r.Entity2LogicalName,
            });
        }
    }

    private static CascadeDto? MapCascade(CascadeConfiguration? cfg)
    {
        if (cfg is null) return null;
        return new CascadeDto
        {
            Delete  = cfg.Delete?.ToString(),
            Assign  = cfg.Assign?.ToString(),
            Share   = cfg.Share?.ToString(),
            Unshare = cfg.Unshare?.ToString(),
        };
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
