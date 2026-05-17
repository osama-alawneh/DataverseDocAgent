// F-001, FR-001 — Custom table discovery tool (Story 1.3 baseline + Story 3.4 hardening)
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
/// Lists all custom tables (IsCustomEntity = true) in the connected Dataverse environment.
/// Returns a JSON object containing an array of table summaries, or an explanatory message
/// when no custom tables are found.
/// </summary>
public sealed class ListCustomTablesTool : IDataverseTool
{
    private readonly IOrganizationService _service;

    private static readonly JsonElement s_inputSchema =
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    public ListCustomTablesTool(IOrganizationService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    // ── IDataverseTool ────────────────────────────────────────────────────────

    public string Name        => "list_custom_tables";
    public string Description => "Returns all custom tables in the connected Dataverse environment";
    public JsonElement InputSchema => s_inputSchema;

    /// <summary>
    /// Queries Dataverse for all custom entities and returns the result as JSON.
    /// </summary>
    public Task<string> ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default)
    {
        // F4 (story 1.3 deferred) — SDK does not expose async/cancellable Execute overload in
        // v1.2.10. Token accepted for pipeline symmetry; not observed at SDK boundary until
        // IOrganizationServiceAsync2 adoption lands.
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var entities = FetchCustomEntities();
            var json     = BuildResultJson(entities);
            return Task.FromResult(json);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation must short-circuit, not be sanitized into a tool result.
            throw;
        }
        catch (FaultException<OrganizationServiceFault>)
        {
            // NFR-007 / Story 3.4 sibling consistency — never let the raw SDK message
            // escape to Claude (it can echo connection-string fragments). Same JSON
            // contract as GetTableFieldsTool / GetRelationshipsTool: { error }.
            return Task.FromResult(SerializeError("Failed to list custom tables"));
        }
        catch (Exception ex) when (ex is TimeoutException
                                       or CommunicationException
                                       or System.Net.Http.HttpRequestException)
        {
            return Task.FromResult(SerializeError("Dataverse call failed while listing custom tables"));
        }
    }

    private static string SerializeError(string message) =>
        JsonSerializer.Serialize(new { error = message }, s_jsonOptions);

    // ── Internal helpers ──────────────────────────────────────────────────────

    internal IReadOnlyList<EntityMetadata> FetchCustomEntities()
    {
        var query = new EntityQueryExpression
        {
            Criteria = new MetadataFilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new MetadataConditionExpression(
                        "IsCustomEntity",
                        MetadataConditionOperator.Equals,
                        true),
                },
            },
            Properties = new MetadataPropertiesExpression(
                nameof(EntityMetadata.DisplayName),
                nameof(EntityMetadata.LogicalName),
                nameof(EntityMetadata.SchemaName),
                nameof(EntityMetadata.Description)),
        };

        var request  = new RetrieveMetadataChangesRequest { Query = query };
        var response = (RetrieveMetadataChangesResponse)_service.Execute(request);
        if (response.EntityMetadata is null)
            Console.Error.WriteLine("[ListCustomTablesTool] Warning: EntityMetadata was null in RetrieveMetadataChangesResponse.");
        return response.EntityMetadata ?? [];
    }

    internal static string BuildResultJson(IReadOnlyList<EntityMetadata> entities)
    {
        if (entities.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                tables  = Array.Empty<object>(),
                message = "No custom tables found in this environment",
            }, s_jsonOptions);
        }

        var tables = entities.Select(e => new TableDto
        {
            DisplayName  = e.DisplayName?.UserLocalizedLabel?.Label,
            LogicalName  = e.LogicalName,
            SchemaName   = e.SchemaName,
            // Phase 2 limitation (story 3.4 dev notes): solution membership requires a
            // secondary `solutioncomponent` query. Returning null keeps the JSON shape
            // forward-compatible — Phase 3 can populate without changing the contract.
            SolutionName = null,
            Description  = e.Description?.UserLocalizedLabel?.Label,
        }).ToArray();

        return JsonSerializer.Serialize(new { tables }, s_jsonOptions);
    }

    // ── DTO ───────────────────────────────────────────────────────────────────

    private sealed class TableDto
    {
        public string? DisplayName  { get; set; }
        public string? LogicalName  { get; set; }
        public string? SchemaName   { get; set; }
        public string? SolutionName { get; set; }
        public string? Description  { get; set; }
    }
}
