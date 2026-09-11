using DataverseDocAgent.Api.Documents;
namespace DataverseDocAgent.Api.Features.DocumentGenerate;

/// <summary>Counts naming prefixes. Names alone cannot establish publisher ownership.</summary>
public static class PrefixAnalyzer
{
    public const string UnprefixedLabel = "(no prefix)";
    public static PublisherPrefixSummary Analyze(IReadOnlyList<TableInfo> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var unprefixed = 0;
        foreach (var table in tables)
        {
            if (table is null || string.IsNullOrWhiteSpace(table.LogicalName)) continue;
            var logical = table.LogicalName.ToLowerInvariant();
            var index = logical.IndexOf('_');
            if (index <= 0) { unprefixed++; continue; }
            var prefix = logical[..index];
            counts[prefix] = counts.GetValueOrDefault(prefix) + 1;
        }
        var prefixes = counts.Select(c => new PrefixCount(c.Key, c.Value))
            .OrderByDescending(c => c.ComponentCount).ThenBy(c => c.Prefix, StringComparer.Ordinal).ToArray();
        return new PublisherPrefixSummary
        {
            // Legacy property names are retained for model compatibility, not ownership attribution.
            MicrosoftPrefixes = Array.Empty<PrefixCount>(), ClientPrefixes = prefixes,
            PrimaryClientPrefix = prefixes.FirstOrDefault()?.Prefix,
            NoClientPrefixDetected = prefixes.Length == 0,
            UnprefixedTables = unprefixed == 0 ? Array.Empty<PrefixCount>() : new[] { new PrefixCount(UnprefixedLabel, unprefixed) }
        };
    }
}
