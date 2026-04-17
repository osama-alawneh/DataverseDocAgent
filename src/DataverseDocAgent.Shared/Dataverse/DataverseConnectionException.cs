// F-034 / NFR-007 — Typed connection failure; message must never include credential values.
namespace DataverseDocAgent.Shared.Dataverse;

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
