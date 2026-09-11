using DataverseDocAgent.Shared.Dataverse;
namespace DataverseDocAgent.Api.Jobs;

/// <summary>Memory-only credentials are detached immediately after connection completes.</summary>
public sealed class GenerationTask(string jobId, EnvironmentCredentials credentials)
{
    private EnvironmentCredentials? _credentials = credentials;
    public string JobId { get; } = jobId;
    public EnvironmentCredentials Credentials => _credentials
        ?? throw new InvalidOperationException("Credentials have already been released.");
    // Managed immutable strings cannot be zeroed; removing this reference reduces their lifetime.
    public void ReleaseCredentials() => _credentials = null;
}
