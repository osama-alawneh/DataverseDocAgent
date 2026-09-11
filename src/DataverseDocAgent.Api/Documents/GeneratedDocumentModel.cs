// F-013 — FR-013 — Structured input to the .docx builder (Story 3.5)
// F-055 — FR-050 — Top-level ApplicationUsers slot (Story 3.7)
namespace DataverseDocAgent.Api.Documents;

/// <summary>Collected inventory and naming counts; ownership is not established by naming prefixes.</summary>
public sealed class GeneratedDocumentModel
{
    public IReadOnlyList<string> Coverage { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DocumentEvidence> Evidence { get; init; } = Array.Empty<DocumentEvidence>();
    public required ExecutiveSummary                       Summary       { get; init; }
    public required IReadOnlyList<TableInfo>               Tables        { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<FieldInfo>>        Fields        { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<RelationshipInfo>> Relationships { get; init; }

    // Story 3.7 — F-055 / FR-050. `required` so a future refactor cannot
    // silently bypass the GetApplicationUsersTool result. Peer to Tables /
    // Fields / Relationships (NOT nested under Summary) so the renderer can
    // emit a top-level Section 5 without reaching into the executive summary.
    public required IReadOnlyList<ApplicationUserInfo> ApplicationUsers { get; init; }
}

public sealed record DocumentEvidence(string Id, string Kind, string? ParentId, string RawJson);

public sealed class ExecutiveSummary
{
    public required string?                EnvironmentName    { get; init; }
    public          string?                EnvironmentUrl     { get; init; }
    public          string?                Version            { get; init; }
    public          string?                BaseLanguageName   { get; init; }
    public required DateTime               ScanDate           { get; init; }
    public required string                 ComplexityRating   { get; init; }
    public required int                    TableCount         { get; init; }
    public required int                    FieldCount         { get; init; }
    public required int                    RelationshipCount  { get; init; }
    public required IReadOnlyList<string>  KeyObservations    { get; init; }

    // Story 3.6 — F-047 / FR-042. `required` so a future refactor cannot
    // silently bypass the deterministic PrefixAnalyzer step.
    public required PublisherPrefixSummary PrefixSummary      { get; init; }
}

public sealed class TableInfo
{
    public required string  LogicalName  { get; init; }
    public          string? DisplayName  { get; init; }
    public          string? SchemaName   { get; init; }
    public          string? SolutionName { get; init; }
    public          string? Description  { get; init; }
    public          string? Purpose      { get; init; }
}

public sealed class FieldInfo
{
    public required string  LogicalName   { get; init; }
    public          string? DisplayName   { get; init; }
    public          string? AttributeType { get; init; }
    public          string? RequiredLevel { get; init; }
    public          string? Description   { get; init; }
}

public sealed class RelationshipInfo
{
    public          string? SchemaName       { get; init; }
    public          string? RelationshipType { get; init; }
    public          string? RelatedEntity    { get; init; }
    public          string? CascadeDelete    { get; init; }
    public          string? BusinessMeaning  { get; init; }
}

// Null or missing roles mean lookup unavailable; an explicit empty list means no roles returned.
public sealed class ApplicationUserInfo
{
    public          string?                DisplayName   { get; init; }
    public          string?                ApplicationId { get; init; }
    public          string?                Email         { get; init; }
    public          IReadOnlyList<string>? Roles         { get; init; }
}
