// F-036, FR-036 — Generation pipeline seam (Story 3.1 stub, Story 3.5 real impl)
namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Encapsulates the work performed per <see cref="GenerationTask"/>. Extracted as a seam so
/// <see cref="GenerationBackgroundService"/> is testable without the real pipeline and so
/// Story 3.5 can substitute the real implementation without touching the background loop.
/// </summary>
public interface IGenerationPipeline
{
    /// <summary>Runs the pipeline and returns a download token on success.</summary>
    Task<string> RunAsync(GenerationTask task, CancellationToken cancellationToken);
}
