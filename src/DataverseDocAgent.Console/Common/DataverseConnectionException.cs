// F-034 — Credential in-memory guarantee (NFR-007)
// POC duplicate — Console does not reference Api; proper shared project is Phase 2 work.
namespace DataverseDocAgent.ConsoleApp.Common;

/// <summary>
/// Thrown when a Dataverse connection attempt fails.
/// SECURITY CONTRACT: The Message must contain only safe diagnostic text — never credential
/// values. Inner exceptions are stripped by DataverseConnectionFactory to prevent SDK exception
/// messages from leaking OAuth details (tenant IDs, authority URLs).
/// </summary>
public sealed class DataverseConnectionException : Exception
{
    public DataverseConnectionException(string message) : base(message) { }

    public DataverseConnectionException(string message, Exception innerException)
        : base(message, innerException) { }
}
