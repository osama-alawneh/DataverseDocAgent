// F-047 — FR-042 — Publisher Prefix Intelligence (Story 3.6)
namespace DataverseDocAgent.Api.Documents;

/// <summary>Collected inventory and naming counts; ownership is not established by naming prefixes.</summary>
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
