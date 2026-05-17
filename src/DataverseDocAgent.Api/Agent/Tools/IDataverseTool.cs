// F-001 — Custom tool contract for the Claude agent loop
using System.Text.Json;

namespace DataverseDocAgent.Api.Agent.Tools;

/// <summary>
/// Contract for a Dataverse-backed tool that the Claude agent loop can invoke.
/// Tools are pre-authenticated via constructor injection — credentials are not
/// passed per-call.
/// </summary>
public interface IDataverseTool
{
    /// <summary>Unique tool name registered with the Claude API (snake_case).</summary>
    string Name { get; }

    /// <summary>Human-readable description Claude uses to decide when to call the tool.</summary>
    string Description { get; }

    /// <summary>JSON Schema (object) describing the tool's input parameters.</summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// Executes the tool and returns a JSON string result.
    /// </summary>
    /// <param name="input">Parsed JSON input matching <see cref="InputSchema"/>.</param>
    /// <param name="cancellationToken">
    /// Propagated from the orchestrator's request scope. Current SDK
    /// (<c>Microsoft.PowerPlatform.Dataverse.Client</c> 1.2.10) exposes no
    /// cancellable <c>IOrganizationService.Execute</c> overload, so observation
    /// at the SDK boundary is deferred (story 1.3 F4) — the parameter is required
    /// here for pipeline symmetry and future-proofing (story 3.8 / PREP-4).
    /// </param>
    Task<string> ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default);
}
