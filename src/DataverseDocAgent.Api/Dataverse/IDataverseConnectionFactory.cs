// F-034 — Abstraction for testable credential handling
using DataverseDocAgent.Api.Common;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseDocAgent.Api.Dataverse;

public interface IDataverseConnectionFactory
{
    Task<ServiceClient> ConnectAsync(
        EnvironmentCredentials credentials,
        CancellationToken cancellationToken = default);
}
