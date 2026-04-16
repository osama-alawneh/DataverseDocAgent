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
    Task<string> ExecuteAsync(JsonElement input);
}
