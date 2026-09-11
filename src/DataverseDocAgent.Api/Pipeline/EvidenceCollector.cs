using System.Text.Json;
using DataverseDocAgent.Api.Agent.Tools;

namespace DataverseDocAgent.Api.Pipeline;

/// <summary>Calls the supported extractors directly; no model selects, summarizes, or creates inventory.</summary>
public sealed class EvidenceCollector
{
    public async Task<EvidenceSnapshot> CollectAsync(IReadOnlyList<IDataverseTool> tools, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ct.ThrowIfCancellationRequested();
        var components = new Dictionary<string, EvidenceComponent>(StringComparer.OrdinalIgnoreCase);
        var coverage = new List<CoverageEntry>();
        var emptyInput = JsonSerializer.SerializeToElement(new { });

        async Task<JsonElement?> Call(string name, string scope, JsonElement input)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var matching = tools.Where(t => t.Name == name).ToArray();
                if (matching.Length != 1) throw new InvalidDataException();
                var output = await matching[0].ExecuteAsync(input, ct);
                ct.ThrowIfCancellationRequested();
                var root = JsonSerializer.Deserialize<JsonElement>(output);
                if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _)
                    || root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
                    throw new InvalidDataException();
                return root;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                coverage.Add(new(scope, "failed", "Extraction failed or returned invalid evidence; no error payload was retained."));
                return null;
            }
        }

        bool Add(string kind, JsonElement raw, string? parent, out bool duplicate)
        {
            duplicate = false;
            if (raw.ValueKind != JsonValueKind.Object) return false;
            if (raw.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != raw.EnumerateObject().Count()) return false;
            var data = JsonSerializer.SerializeToElement(raw.EnumerateObject()
                .Where(p => SnapshotStore.AllowedProperties(kind).Contains(p.Name, StringComparer.Ordinal))
                .OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => p.Value.Clone()));
            var identity = kind switch
            {
                "organisation" => "organisation",
                "table" => "table:" + SnapshotStore.String(data, "logicalName"),
                "field" => "field:" + parent![6..] + ":" + SnapshotStore.String(data, "logicalName"),
                "relationship" => "relationship:" + SnapshotStore.String(data, "schemaName"),
                "application-user" => "application-user:" + (SnapshotStore.String(data, "systemUserId") ?? SnapshotStore.String(data, "applicationId")),
                _ => throw new InvalidDataException()
            };
            var component = new EvidenceComponent(identity, kind, parent, data);
            try { SnapshotStore.ValidateComponent(component); }
            catch (InvalidDataException) { return false; }
            if (components.TryGetValue(identity, out var existing))
            {
                duplicate = true;
                // The relatedEntity varies with the queried endpoint. Direction and lookup metadata must agree.
                return kind == "relationship" && RelationshipFacts(existing.Data) == RelationshipFacts(data);
            }
            components.Add(identity, component);
            return true;
        }

        async Task CollectArray(string tool, string scope, string property, string kind, string? parent, JsonElement input)
        {
            var result = await Call(tool, scope, input);
            if (result is null) return;
            if (!result.Value.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                coverage.Add(new(scope, "failed", "Extractor response did not contain the expected inventory array."));
                return;
            }
            var incomplete = false;
            var accepted = 0;
            foreach (var raw in array.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                if (!Add(kind, raw, parent, out var duplicate)) incomplete = true;
                else accepted++;
                if (duplicate && kind != "relationship") incomplete = true;
                if (kind == "application-user" && raw.ValueKind == JsonValueKind.Object)
                {
                    if (SnapshotStore.String(raw, "systemUserId") is null) incomplete = true;
                    if (!raw.TryGetProperty("roles", out var roleList) || roleList.ValueKind != JsonValueKind.Array) incomplete = true;
                    if (raw.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array
                        && roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == GetApplicationUsersTool.RoleLookupUnavailableSentinel))
                        incomplete = true;
                }
                if (kind == "relationship" && raw.ValueKind == JsonValueKind.Object
                    && SnapshotStore.String(raw, "relationshipType") == "OneToMany" && SnapshotStore.String(raw, "referencingAttribute") is null)
                    incomplete = true;
            }
            coverage.Add(new(scope, incomplete ? "partial" : "complete", incomplete
                ? $"Read {array.GetArrayLength()} records; {accepted} valid records observed. Invalid/duplicate metadata or unavailable role/identifier/lookup metadata limits coverage."
                : $"Read {array.GetArrayLength()} records successfully (including zero); shared relationships are stored once by schema name."));
        }

        var organisation = await Call("get_organisation_metadata", "organisation", emptyInput);
        if (organisation is not null)
        {
            var accepted = organisation.Value.EnumerateObject().Any(p => SnapshotStore.AllowedProperties("organisation").Contains(p.Name))
                && Add("organisation", organisation.Value, null, out _);
            coverage.Add(new("organisation", accepted ? "complete" : "failed", accepted
                ? "Organisation metadata retrieved." : "Organisation metadata was invalid."));
        }
        await CollectArray("list_custom_tables", "tables", "tables", "table", null, emptyInput);
        var tables = components.Values.Where(c => c.Kind == "table").OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        foreach (var table in tables)
        {
            var name = SnapshotStore.String(table.Data, "logicalName")!;
            var input = JsonSerializer.SerializeToElement(new { tableName = name });
            await CollectArray("get_table_fields", "fields:" + name, "fields", "field", table.Id, input);
            await CollectArray("get_relationships", "relationships:" + name, "relationships", "relationship", table.Id, input);
        }
        await CollectArray("get_application_users", "application-users", "applicationUsers", "application-user", null, emptyInput);
        coverage.AddRange(new[] {
            new CoverageEntry("standard-table-customizations", "excluded", "Custom fields and relationships confined to standard tables are not enumerated; extraction visits returned custom tables only. Relationships touching a custom table may reference a standard table outside inventory."),
            new CoverageEntry("plugins-flows-javascript", "excluded", "Plugin assemblies/steps, cloud flows, JavaScript/web resources, forms, views, business rules, and automation dependencies are not implemented by these extractors."),
            new CoverageEntry("solution-membership", "limitation", "The custom table extractor does not query solution membership; missing solution names are unavailable, not evidence of no solution."),
            new CoverageEntry("sdk-cancellation", "limitation", "Synchronous IOrganizationService calls cannot be interrupted; cancellation is checked before and after calls and between records."),
            new CoverageEntry("application-user-scope", "limitation", "The extractor queries enabled, unlicensed users with an application ID. It does not paginate the SDK result; large inventories may be truncated. Role lookup failures are retained as an explicit sentinel and partial coverage."),
            new CoverageEntry("snapshot-consistency", "limitation", "Metadata is read sequentially, not in a transactional environment snapshot. Extractor null SDK collections can appear as empty results; complete means the extractor returned a valid response, not independent proof of server completeness.")
        });
        var snapshot = new EvidenceSnapshot(SnapshotStore.CurrentVersion, Guid.NewGuid().ToString("N"), DateTime.UtcNow,
            Array.AsReadOnly(components.Values.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray()), Array.AsReadOnly(coverage.ToArray()));
        SnapshotStore.Validate(snapshot);
        return snapshot;
    }

    private static string RelationshipFacts(JsonElement data) => JsonSerializer.Serialize(data.EnumerateObject()
        .Where(p => p.Name != "relatedEntity").OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToDictionary(p => p.Name, p => p.Value));
}
