// F-034 / NFR-007 — Abstraction for testable credential handling.
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseDocAgent.Shared.Dataverse;

public interface IDataverseConnectionFactory
{
    Task<ServiceClient> ConnectAsync(
        EnvironmentCredentials credentials,
        CancellationToken cancellationToken = default);
}
