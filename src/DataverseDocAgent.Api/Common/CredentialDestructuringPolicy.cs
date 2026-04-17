// NFR-007 — Prevents credential logging via Serilog destructuring
using DataverseDocAgent.Api.Features.SecurityCheck;
using DataverseDocAgent.Shared.Dataverse;
using Serilog.Core;
using Serilog.Events;

namespace DataverseDocAgent.Api.Common;

/// <summary>
/// Serilog destructuring policy that redacts credential-bearing objects.
/// Covers both EnvironmentCredentials and SecurityCheckRequest (which carries ClientSecret).
/// Prevents accidental credential leakage in structured logs.
/// </summary>
public sealed class CredentialDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue result)
    {
        if (value is EnvironmentCredentials or SecurityCheckRequest)
        {
            result = new ScalarValue("[REDACTED]");
            return true;
        }

        result = null!;
        return false;
    }
}
