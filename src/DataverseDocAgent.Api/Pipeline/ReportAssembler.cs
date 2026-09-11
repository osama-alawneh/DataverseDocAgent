using System.Text.Json;
using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Features.DocumentGenerate;

namespace DataverseDocAgent.Api.Pipeline;

/// <summary>Inventory and counts come exclusively from the saved SDK evidence.</summary>
public static class ReportAssembler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static bool HasIncompleteCoverage(EvidenceSnapshot snapshot, AnalysisReport analysis) =>
        analysis.Stages.Count == 0 || snapshot.Coverage.Concat(analysis.Coverage)
            .Any(c => c.Status is not ("complete" or "excluded" or "limitation"));

    public static GeneratedDocumentModel Build(EvidenceSnapshot snapshot, AnalysisReport analysis)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(analysis);
        SnapshotStore.Validate(snapshot);
        var components = snapshot.Components.DistinctBy(c => c.Id, StringComparer.Ordinal)
            .OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        var byId = components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var tables = components.Where(c => c.Kind == "table").Select(c => Read<TableInfo>(c)).ToArray();
        var fields = Group<FieldInfo>("field");
        var relationships = Group<RelationshipInfo>("relationship");
        var org = components.FirstOrDefault(c => c.Kind == "organisation")?.Data;
        var coverage = snapshot.Coverage.Concat(analysis.Coverage).Distinct().ToList();
        var covered = analysis.Stages.SelectMany(s => s.CoveredComponentIds).ToHashSet(StringComparer.Ordinal);
        foreach (var component in components.Where(c => !covered.Contains(c.Id)))
            coverage.Add(new CoverageEntry(component.Id, "analysis-missing", "No validated analysis covers this component; raw inventory is retained."));
        foreach (var stage in analysis.Stages)
            foreach (var limitation in stage.Limitations)
                coverage.Add(new CoverageEntry(stage.StageId, "analysis-limitation", limitation));
        if (analysis.Stages.Count == 0)
            coverage.Add(new CoverageEntry("analysis", "unavailable", "No validated analysis was produced. This report contains collected facts only."));
        if (snapshot.Coverage.Count == 0)
            coverage.Add(new CoverageEntry("collection", "unknown", "No collection coverage was supplied; empty inventories do not prove absence."));

        var observations = new List<string>();
        foreach (var finding in analysis.Stages.SelectMany(s => s.Findings))
        {
            if (!byId.ContainsKey(finding.ComponentId) || !byId.ContainsKey(finding.EvidenceId))
                throw new InvalidDataException("Analysis references evidence that is absent from the snapshot.");
            observations.Add($"AI {finding.Basis}: {finding.Text} [component: {finding.ComponentId}; evidence: {finding.EvidenceId}]");
        }
        var fieldCount = components.Count(c => c.Kind == "field");
        var relationshipCount = components.Count(c => c.Kind == "relationship");
        return new GeneratedDocumentModel
        {
            Summary = new ExecutiveSummary
            {
                EnvironmentName = Value(org, "environmentName"), EnvironmentUrl = Value(org, "environmentUrl"),
                Version = Value(org, "version"), BaseLanguageName = Value(org, "baseLanguageName"),
                ScanDate = snapshot.CreatedUtc, TableCount = tables.Length, FieldCount = fieldCount,
                RelationshipCount = relationshipCount,
                ComplexityRating = ComplexityRater.Rate(tables.Length, fieldCount, relationshipCount),
                PrefixSummary = PrefixAnalyzer.Analyze(tables), KeyObservations = observations
            },
            Tables = tables, Fields = fields, Relationships = relationships,
            ApplicationUsers = components.Where(c => c.Kind == "application-user").Select(Read<ApplicationUserInfo>).ToArray(),
            Coverage = coverage.Select(c => $"{c.Scope}: {c.Status} — {c.Detail}").ToArray(),
            Evidence = components.Select(c => new DocumentEvidence(c.Id, c.Kind, c.ParentId, c.Data.GetRawText())).ToArray()
        };

        IReadOnlyDictionary<string, IReadOnlyList<T>> Group<T>(string kind)
        {
            return components.Where(c => c.Kind == kind)
                .GroupBy(c => c.ParentId is not null && byId.TryGetValue(c.ParentId, out var parent)
                    ? Value(parent.Data, "logicalName") ?? c.ParentId : c.ParentId ?? "(unassigned)")
                .ToDictionary(g => g.Key, g => (IReadOnlyList<T>)g.Select(Read<T>).ToArray(), StringComparer.Ordinal);
        }
    }

    private static T Read<T>(EvidenceComponent component) => component.Data.Deserialize<T>(JsonOptions)
        ?? throw new InvalidDataException("Evidence component cannot be deserialized.");
    private static string? Value(JsonElement? data, string key) => data is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
