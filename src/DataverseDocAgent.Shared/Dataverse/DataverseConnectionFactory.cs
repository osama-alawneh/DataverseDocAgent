// F-034 / NFR-007 — Credential in-memory guarantee; inner exceptions stripped to prevent
// OAuth/tenant detail leakage through SDK exception messages.
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Crm.Sdk.Messages;

namespace DataverseDocAgent.Shared.Dataverse;

/// <summary>
/// Creates authenticated ServiceClient instances against a Dataverse environment.
/// Never logs, surfaces, or wraps credential values in any form.
/// Inner exceptions are stripped to prevent SDK exception messages from leaking OAuth details.
/// </summary>
public sealed class DataverseConnectionFactory : IDataverseConnectionFactory
{
    private const string SafeErrorMessage =
        "Failed to connect to Dataverse environment. Verify credentials and environment URL.";

    public async Task<ServiceClient> ConnectAsync(
        EnvironmentCredentials credentials,
        CancellationToken cancellationToken = default)
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
            await client.ExecuteAsync(new WhoAmIRequest(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation must propagate as OperationCanceledException so hosted
            // services and request-scoped callers can distinguish shutdown / timeout from
            // an actual connection failure. Inner exception is not wrapped — OCE carries
            // no credential content.
            client.Dispose();
            throw;
        }
        catch
        {
            client.Dispose();
            throw new DataverseConnectionException(SafeErrorMessage);
        }

        return client;
    }
}
