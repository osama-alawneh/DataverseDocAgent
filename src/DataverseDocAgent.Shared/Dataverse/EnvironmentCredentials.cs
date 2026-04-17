// F-034 / NFR-007 — Credential in-memory guarantee
namespace DataverseDocAgent.Shared.Dataverse;

/// <summary>
/// Holds Dataverse client-credentials in memory only.
/// ClientSecret is hidden from debugger inspection to reduce accidental exposure.
/// This class must never be serialized (JSON, XML, ToString) in a way that emits property values.
/// </summary>
public sealed class EnvironmentCredentials
{
    public required string EnvironmentUrl { get; init; }
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }

    [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.Never)]
    public required string ClientSecret { get; init; }
}
