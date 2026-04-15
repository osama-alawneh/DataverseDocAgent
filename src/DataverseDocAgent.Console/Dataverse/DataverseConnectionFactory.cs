// F-034 — Credential in-memory guarantee (NFR-007)
// POC duplicate — Console does not reference Api; proper shared project is Phase 2 work.
using DataverseDocAgent.ConsoleApp.Common;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Crm.Sdk.Messages;

namespace DataverseDocAgent.ConsoleApp.Dataverse;

/// <summary>
/// Creates authenticated ServiceClient instances against a Dataverse environment.
/// Never logs, surfaces, or wraps credential values in any form.
/// Inner exceptions are stripped to prevent SDK exception messages from leaking OAuth details.
/// </summary>
public sealed class DataverseConnectionFactory
{
    private const string SafeErrorMessage =
        "Failed to connect to Dataverse environment. Verify credentials and environment URL.";

    public async Task<ServiceClient> ConnectAsync(EnvironmentCredentials credentials)
    {
        ServiceClient client;

        try
        {
            client = new ServiceClient(
                new Uri(credentials.EnvironmentUrl),
                credentials.ClientId,
                credentials.ClientSecret,
                true);
        }
        catch
        {
            // Inner exception stripped — SDK exceptions can embed OAuth details (tenant IDs,
            // authority URLs). Callers receive only the safe diagnostic message.
            throw new DataverseConnectionException(SafeErrorMessage);
        }

        if (!client.IsReady)
        {
            client.Dispose();
            throw new DataverseConnectionException(SafeErrorMessage);
        }

        try
        {
            await client.ExecuteAsync(new WhoAmIRequest());
        }
        catch
        {
            client.Dispose();
            throw new DataverseConnectionException(SafeErrorMessage);
        }

        return client;
    }
}
