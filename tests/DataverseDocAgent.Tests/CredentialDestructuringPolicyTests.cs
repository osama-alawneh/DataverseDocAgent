// NFR-007 — Serilog destructuring policy must redact credential-bearing objects
// regardless of their source assembly (story 3.8 consolidation).
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Features.SecurityCheck;
using DataverseDocAgent.Shared.Dataverse;
using Serilog.Core;
using Serilog.Events;

namespace DataverseDocAgent.Tests;

public class CredentialDestructuringPolicyTests
{
    [Fact]
    public void Destructure_SharedEnvironmentCredentials_RedactsToScalar()
    {
        var policy = new CredentialDestructuringPolicy();
        var credentials = new EnvironmentCredentials
        {
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
            TenantId       = "11111111-1111-1111-1111-111111111111",
            ClientId       = "22222222-2222-2222-2222-222222222222",
            ClientSecret   = "super-secret-do-not-log",
        };

        var handled = policy.TryDestructure(credentials, NoopFactory.Instance, out var value);

        Assert.True(handled);
        var scalar = Assert.IsType<ScalarValue>(value);
        Assert.Equal("[REDACTED]", scalar.Value);
    }

    [Fact]
    public void Destructure_SharedEnvironmentCredentials_SerializedValueDoesNotContainSecret()
    {
        var policy = new CredentialDestructuringPolicy();
        var credentials = new EnvironmentCredentials
        {
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
            TenantId       = "11111111-1111-1111-1111-111111111111",
            ClientId       = "22222222-2222-2222-2222-222222222222",
            ClientSecret   = "tenant-leak-probe-secret-value",
        };

        policy.TryDestructure(credentials, NoopFactory.Instance, out var value);

        using var writer = new StringWriter();
        value.Render(writer);
        var rendered = writer.ToString();

        Assert.DoesNotContain("tenant-leak-probe-secret-value", rendered);
        Assert.DoesNotContain(credentials.ClientSecret, rendered);
        Assert.DoesNotContain(credentials.TenantId, rendered);
        Assert.DoesNotContain(credentials.ClientId, rendered);
    }

    [Fact]
    public void Destructure_SecurityCheckRequest_RedactsToScalar()
    {
        var policy = new CredentialDestructuringPolicy();
        var request = new SecurityCheckRequest
        {
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
            TenantId       = "11111111-1111-1111-1111-111111111111",
            ClientId       = "22222222-2222-2222-2222-222222222222",
            ClientSecret   = "secret",
        };

        var handled = policy.TryDestructure(request, NoopFactory.Instance, out var value);

        Assert.True(handled);
        var scalar = Assert.IsType<ScalarValue>(value);
        Assert.Equal("[REDACTED]", scalar.Value);
    }

    [Fact]
    public void Destructure_UnrelatedObject_ReturnsFalse()
    {
        var policy = new CredentialDestructuringPolicy();

        var handled = policy.TryDestructure(new { Foo = "bar" }, NoopFactory.Instance, out _);

        Assert.False(handled);
    }

    private sealed class NoopFactory : ILogEventPropertyValueFactory
    {
        public static readonly NoopFactory Instance = new();

        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false)
            => new ScalarValue(value);
    }
}
