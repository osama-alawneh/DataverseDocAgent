// F-047 — FR-042 — Publisher Prefix Intelligence (Story 3.6)
using System.Text.RegularExpressions;
using DataverseDocAgent.Api.Documents;

namespace DataverseDocAgent.Api.Features.DocumentGenerate;

/// <summary>
/// Deterministic publisher-prefix bucketing for the Mode 1 Executive Summary.
/// Mirrors the <see cref="ComplexityRater"/> pattern — runs in-process after
/// Claude returns its structured JSON so the rendered breakdown is reproducible
/// and Claude is never asked to classify prefixes (FR-011, FR-042).
///
/// Algorithm (AC-1 through AC-3):
///   1. Prefix = segment of <c>logicalName</c> before the first underscore.
///      Names with no underscore are routed into the <c>Unprefixed</c> bucket.
///   2. Prefix is classified <c>Microsoft</c> if it exact-matches one of the
///      known Microsoft owners (<c>msdyn</c>, <c>msft</c>, <c>adx</c>) OR
///      matches the regex <c>^cr[a-z0-9]*$</c> — which covers both bare
///      <c>cr_</c> and the Power Apps default-environment auto-generated
///      <c>crXXXX_</c> family. Everything else goes into <c>Client/ISV</c>.
///   3. Primary client prefix = Client/ISV bucket prefix with the highest
///      component count, ties broken alphabetically (ordinal) on the
///      lowercase prefix string. Empty Client/ISV bucket sets
///      <see cref="PublisherPrefixSummary.NoClientPrefixDetected"/> = true and
///      leaves <see cref="PublisherPrefixSummary.PrimaryClientPrefix"/> null.
/// </summary>
public static class PrefixAnalyzer
{
    // Shared label for the Unprefixed bucket row. Renderer keys off this exact
    // literal to skip the trailing-underscore decoration, so the constant must
    // live next to the value that produces it. Story 3.6 code-review P5.
    public const string UnprefixedLabel = "(no prefix)";

    // Microsoft-owned prefixes that are exact-matched against the lowercase
    // prefix segment. Trailing underscore is omitted here because the
    // segmentation step strips it; DocxBuilder re-attaches it at render time.
    private static readonly HashSet<string> s_microsoftExactPrefixes =
        new(StringComparer.Ordinal) { "msdyn", "msft", "adx" };

    // ^cr[a-z0-9]*$ — matches the bare legacy "cr" prefix AND the Power Apps
    // default-environment auto-generated "crXXXX_" family (e.g. cr3a7_, crf2b_).
    // The match is applied against the prefix SEGMENT (without trailing _),
    // so "cr"/"cr3a7"/"crf2b" all match. Documented inline because the success
    // on auto-generated cr-prefixes is non-obvious to a future reader.
    private static readonly Regex s_microsoftCrPrefix =
        new(@"^cr[a-z0-9]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PublisherPrefixSummary Analyze(IReadOnlyList<TableInfo> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var microsoftCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var clientCounts    = new Dictionary<string, int>(StringComparer.Ordinal);
        var unprefixedCount = 0;

        foreach (var table in tables)
        {
            // Story 3.6 code-review P1 — JSON `[null, ...]` deserialises to a
            // real null entry; mirrors Story 3.5 P3 (KeyObservations null
            // filtering). Skip rather than NRE on `table.LogicalName`.
            if (table is null) continue;

            var logical = table.LogicalName;
            if (string.IsNullOrWhiteSpace(logical))
            {
                // Defensive: a logical-name-less entry is not a real custom table —
                // skip rather than emit an empty-string prefix.
                continue;
            }

            // Dataverse normalises logical names to lowercase; defensively
            // re-lower so a quirky test payload cannot break determinism.
            var ln = logical.ToLowerInvariant();
            var underscoreIndex = ln.IndexOf('_');
            if (underscoreIndex <= 0)
            {
                // No underscore (or leading underscore) → no recognisable prefix.
                // Bucket as Unprefixed per AC-2 / Dev Notes.
                unprefixedCount++;
                continue;
            }

            var prefix = ln.Substring(0, underscoreIndex);

            if (IsMicrosoftPrefix(prefix))
            {
                microsoftCounts[prefix] = microsoftCounts.GetValueOrDefault(prefix) + 1;
            }
            else
            {
                clientCounts[prefix] = clientCounts.GetValueOrDefault(prefix) + 1;
            }
        }

        var microsoftList = OrderByCountThenAlpha(microsoftCounts);
        var clientList    = OrderByCountThenAlpha(clientCounts);

        var unprefixedList = unprefixedCount > 0
            ? (IReadOnlyList<PrefixCount>)new[] { new PrefixCount(UnprefixedLabel, unprefixedCount) }
            : Array.Empty<PrefixCount>();

        var primary = clientList.Count > 0 ? clientList[0].Prefix : null;

        return new PublisherPrefixSummary
        {
            PrimaryClientPrefix    = primary,
            MicrosoftPrefixes      = microsoftList,
            ClientPrefixes         = clientList,
            UnprefixedTables       = unprefixedList,
            NoClientPrefixDetected = primary is null,
        };
    }

    private static bool IsMicrosoftPrefix(string prefix)
        => s_microsoftExactPrefixes.Contains(prefix) || s_microsoftCrPrefix.IsMatch(prefix);

    private static IReadOnlyList<PrefixCount> OrderByCountThenAlpha(Dictionary<string, int> counts)
    {
        if (counts.Count == 0) return Array.Empty<PrefixCount>();
        return counts
            .Select(kv => new PrefixCount(kv.Key, kv.Value))
            .OrderByDescending(p => p.ComponentCount)
            .ThenBy(p => p.Prefix, StringComparer.Ordinal)
            .ToList();
    }
}
