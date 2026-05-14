// F-047 — FR-042 — Publisher Prefix Intelligence (Story 3.6)
namespace DataverseDocAgent.Api.Documents;

/// <summary>
/// Deterministic publisher-prefix breakdown computed in C# host code
/// (FR-011 / FR-042 — Claude is intentionally NOT trusted with this data).
/// Consumed by <see cref="DocxBuilder"/> to render the
/// "Publisher Prefix Summary" sub-section under the Executive Summary.
///
/// Buckets:
///   <list type="bullet">
///     <item><term>Microsoft</term><description>known Microsoft-owned prefixes (msdyn, msft, adx) and the Power Apps default-environment <c>cr[a-z0-9]*</c> family.</description></item>
///     <item><term>Client/ISV</term><description>everything else with a recognisable prefix segment.</description></item>
///     <item><term>Unprefixed</term><description>logical names without an underscore — never expected from <c>list_custom_tables</c>, surfaced as a single <c>(no prefix)</c> row when present.</description></item>
///   </list>
/// </summary>
public sealed class PublisherPrefixSummary
{
    public          string?                       PrimaryClientPrefix    { get; init; }
    public required IReadOnlyList<PrefixCount>    MicrosoftPrefixes      { get; init; }
    public required IReadOnlyList<PrefixCount>    ClientPrefixes         { get; init; }
    public required IReadOnlyList<PrefixCount>    UnprefixedTables       { get; init; }
    public required bool                          NoClientPrefixDetected { get; init; }
}

/// <summary>
/// One prefix observed across the scanned environment plus the number of
/// custom-table components carrying it. Prefix is stored without trailing
/// underscore; <see cref="DocxBuilder"/> appends the underscore at render
/// time so the analyzer surface stays orthogonal to the document format.
/// </summary>
public sealed record PrefixCount(string Prefix, int ComponentCount);
