// F-036, FR-036, NFR-007 — Async job task envelope (Story 3.1)
using DataverseDocAgent.Shared.Dataverse;

namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Work item placed on the generation channel by controllers and consumed by the
/// background service. Holds <see cref="EnvironmentCredentials"/> by reference so the
/// credential material stays in memory only (NFR-007) and is released when the task
/// goes out of scope in <see cref="GenerationBackgroundService"/>'s per-iteration
/// local — no explicit zeroing required.
/// </summary>
public sealed record GenerationTask(string JobId, EnvironmentCredentials Credentials);
