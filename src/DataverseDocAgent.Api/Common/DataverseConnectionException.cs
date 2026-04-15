// F-034 — Credential in-memory guarantee (NFR-007)
namespace DataverseDocAgent.Api.Common;

/// <summary>
/// Thrown when a Dataverse connection attempt fails.
/// Message must never include credential values; only safe diagnostic text is permitted.
/// </summary>
public sealed class DataverseConnectionException : Exception
{
    public DataverseConnectionException(string message) : base(message) { }

    public DataverseConnectionException(string message, Exception innerException)
        : base(message, innerException) { }
}
