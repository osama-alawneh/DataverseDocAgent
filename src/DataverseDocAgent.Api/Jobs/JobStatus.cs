// F-036, FR-036 — Async job model (Story 3.1)
namespace DataverseDocAgent.Api.Jobs;

/// <summary>Lifecycle status of a generation job.</summary>
public enum JobStatus
{
    Queued,
    Running,
    Ready,
    Failed,
}
