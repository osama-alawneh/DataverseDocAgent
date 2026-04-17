// F-036, FR-036 — Pipeline stub, replaced by real impl in Story 3.5
namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Phase-2 placeholder pipeline. Returns a fresh GUID as the download token after a short delay.
/// Story 3.5 replaces this registration with the Mode-1 pipeline.
/// </summary>
public sealed class StubGenerationPipeline : IGenerationPipeline
{
    public async Task<string> RunAsync(GenerationTask task, CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        return Guid.NewGuid().ToString();
    }
}
