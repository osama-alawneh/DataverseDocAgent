// F-034 — Credential in-memory guarantee (NFR-007)
// POC duplicate — Console does not reference Api; proper shared project is Phase 2 work.
namespace DataverseDocAgent.ConsoleApp.Common;

/// <summary>
/// Holds Dataverse client-credentials in memory only.
/// ClientSecret is hidden from debugger inspection to reduce accidental exposure.
/// SECURITY CONTRACT: This class must never be serialized (JSON, XML) or passed to ToString()
/// in a way that emits property values. Do not pass to JsonSerializer, XmlSerializer, or any
/// logging framework that enumerates object properties.
/// </summary>
public sealed class EnvironmentCredentials
{
    public required string EnvironmentUrl { get; init; }
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }

    [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.Never)]
    public required string ClientSecret { get; init; }
}
