// F-001 — Claude agent tool-use loop
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using DataverseDocAgent.Api.Agent.Tools;

namespace DataverseDocAgent.Api.Agent;

/// <summary>
/// Runs the Claude tool-use loop: send prompt → receive tool_use → execute tool
/// → return result → repeat until end_turn (or max iterations guard).
/// </summary>
public sealed class AgentOrchestrator
{
    /// <summary>Default iteration ceiling used by the POC console host.</summary>
    public const int DefaultMaxIterations = 10;

    /// <summary>
    /// Iteration ceiling for Mode 1 in the API host. Large environments with 50+
    /// custom tables trigger 100+ tool calls (2 calls per table for fields and
    /// relationships, plus organisation metadata + list_custom_tables) — the POC
    /// limit of 10 is insufficient. 200 covers realistic environments with
    /// headroom while still preventing runaway loops.
    /// </summary>
    public const int Mode1MaxIterations = 200;

    private readonly int _maxIterations;

    /// <summary>
    /// Returned when the agent loop exhausts all iterations without reaching end_turn.
    /// Callers can check for this value to detect an incomplete response.
    /// </summary>
    public const string MaxIterationsSentinel =
        "(Agent loop reached the maximum iteration limit without a final response.)";

    private readonly Func<MessageParameters, CancellationToken, Task<MessageResponse>> _sendMessage;

    // ── Public constructor (production) ───────────────────────────────────────

    public AgentOrchestrator(AnthropicClient client, int maxIterations = DefaultMaxIterations)
        : this((p, ct) => client.Messages.GetClaudeMessageAsync(p, ct), maxIterations)
    { }

    // ── Delegate constructor (testing / advanced DI) ──────────────────────────

    public AgentOrchestrator(
        Func<MessageParameters, CancellationToken, Task<MessageResponse>> sendMessage,
        int maxIterations = DefaultMaxIterations)
    {
        _sendMessage = sendMessage ?? throw new ArgumentNullException(nameof(sendMessage));
        if (maxIterations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxIterations), maxIterations, "must be >= 1");
        _maxIterations = maxIterations;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full agent loop and returns Claude's final text response.
    /// </summary>
    /// <param name="prompt">User prompt sent in the first message.</param>
    /// <param name="tools">Available tools Claude may call.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> RunAsync(
        string                      prompt,
        IEnumerable<IDataverseTool> tools,
        CancellationToken           ct = default)
    {
        var toolList = tools.ToList();
        var sdkTools = BuildSdkTools(toolList);
        var messages = new List<Message>
        {
            new() { Role = RoleType.User, Content = [new TextContent { Text = prompt }] },
        };

        // E2E hotfix 2026-05-14 (R-HF-8) — per-iteration progress log.
        // Mode 1 in 200+ table envs runs hundreds of round-trips; without
        // a heartbeat we cannot tell whether a >10 min generation is
        // legitimately progressing (many tool calls) or stuck. Logging
        // iteration number, tool name(s), and elapsed time gives a clear
        // forensic trail when the job timeout fires.
        var loopStart = DateTimeOffset.UtcNow;
        for (int iteration = 0; iteration < _maxIterations; iteration++)
        {
            var parameters = new MessageParameters
            {
                Model     = AnthropicModels.Claude46Sonnet,
                // E2E hotfix 2026-05-14 (R-HF-7) — 16384 was still too low
                // for envs with 200+ tables: a 67k-char Mode 1 final JSON
                // hit the cap mid-stream and surfaced as AI_ERROR with
                // inner JsonException. Sonnet 4.6 caps max_tokens at
                // 64000 for non-thinking output; that ceiling covers the
                // largest realistic Phase 1 environment with headroom.
                // Token cost is metered by output actually produced, not
                // the budget, so raising the cap does not raise spend on
                // smaller envs.
                MaxTokens = 64000,
                Messages  = messages,
                Tools     = sdkTools,
            };

            var response = await _sendMessage(parameters, ct).ConfigureAwait(false);

            var elapsedSec = (DateTimeOffset.UtcNow - loopStart).TotalSeconds;
            Console.Error.WriteLine(
                $"[AgentOrchestrator] iter={iteration + 1}/{_maxIterations} stop={response.StopReason} elapsed={elapsedSec:F1}s");

            if (string.Equals(response.StopReason, "tool_use", StringComparison.Ordinal))
            {
                var toolUseBlocks = response.Content?.OfType<ToolUseContent>().ToList();
                if (toolUseBlocks is null || toolUseBlocks.Count == 0)
                {
                    // Malformed: stop_reason is tool_use but no tool blocks present — treat as end_turn
                    Console.Error.WriteLine("[AgentOrchestrator] Warning: StopReason is 'tool_use' but Content contains no ToolUseContent blocks. Treating as end_turn.");
                    return ExtractText(response);
                }

                // Append assistant message (built from response content)
                messages.Add(new Message
                {
                    Role    = RoleType.Assistant,
                    Content = response.Content,
                });

                // Execute every tool_use block and collect results
                var toolNames = string.Join(",", toolUseBlocks.Select(b => b.Name));
                Console.Error.WriteLine(
                    $"[AgentOrchestrator]   tools=[{toolNames}] count={toolUseBlocks.Count}");
                var toolResults = new List<ContentBase>();
                foreach (var block in toolUseBlocks)
                {
                    var tool         = FindTool(toolList, block.Name);
                    var inputElement = ToJsonElement(block.Input);
                    string resultJson;
                    if (tool is null)
                    {
                        resultJson = JsonSerializer.Serialize(new { error = $"Unknown tool: {block.Name}" });
                    }
                    else
                    {
                        try
                        {
                            resultJson = await tool.ExecuteAsync(inputElement, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            // Caller cancellation must short-circuit the loop — swallowing it
                            // into a tool_result JSON would keep the agent pumping tokens past
                            // the deadline and defeat the orchestrator's cancellation contract.
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // R-HF-9 — surface tool-level failures with type +
                            // message + first stack frame. Previously these were
                            // silently swallowed into the JSON returned to Claude;
                            // the user only ever saw `Tool 'X' failed: TypeName`
                            // in the agent transcript and never the SDK reason.
                            var firstFrame = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "(no stack)";
                            Console.Error.WriteLine(
                                $"[AgentOrchestrator]   tool '{block.Name}' threw {ex.GetType().FullName}: {ex.Message} @ {firstFrame}");
                            resultJson = JsonSerializer.Serialize(new { error = $"Tool '{block.Name}' failed: {ex.GetType().Name}" });
                        }
                    }

                    toolResults.Add(new ToolResultContent
                    {
                        ToolUseId = block.Id,
                        Content   = [new TextContent { Text = resultJson }],
                    });
                }

                messages.Add(new Message
                {
                    Role    = RoleType.User,
                    Content = toolResults,
                });
                continue;
            }

            // end_turn (or any other stop reason) — return final text (AC-7).
            // E2E hotfix 2026-05-14 (R-HF-7) — surface non-end_turn stop
            // reasons explicitly. "max_tokens" means Claude hit MaxTokens
            // mid-response and the JSON is truncated; downstream parse
            // will fail with JsonException. Logging here names the cause
            // without waiting for the forensic dump in DocumentGenerateService.
            if (!string.Equals(response.StopReason, "end_turn", StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"[AgentOrchestrator] Warning: final response StopReason='{response.StopReason}' (expected 'end_turn'). " +
                    "If 'max_tokens', the output was truncated — raise MaxTokens or shrink the prompt.");
            }
            return ExtractText(response);
        }

        // Max iterations reached — log warning and return sentinel
        Console.Error.WriteLine($"[AgentOrchestrator] Warning: agent loop reached the maximum iteration limit ({_maxIterations}).");
        return MaxIterationsSentinel;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IList<Anthropic.SDK.Common.Tool> BuildSdkTools(
        IEnumerable<IDataverseTool> tools)
    {
        return tools.Select(t =>
        {
            JsonNode? schemaNode;
            try
            {
                schemaNode = JsonNode.Parse(t.InputSchema.GetRawText());
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"[AgentOrchestrator] Warning: failed to parse InputSchema for tool '{t.Name}': {ex.Message}. Falling back to empty schema.");
                schemaNode = JsonNode.Parse("{}");
            }
            var function = new Anthropic.SDK.Common.Function(t.Name, t.Description, schemaNode);
            return (Anthropic.SDK.Common.Tool)function;
        }).ToList();
    }

    private static IDataverseTool? FindTool(
        IEnumerable<IDataverseTool> tools, string name)
        => tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    private static JsonElement ToJsonElement(JsonNode? node)
    {
        var json = node?.ToJsonString() ?? "{}";
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static string ExtractText(MessageResponse response)
    {
        var text = response.Content
            ?.OfType<TextContent>()
            .Where(b => b.Text != null)
            .Select(b => b.Text)
            .FirstOrDefault();

        return text ?? string.Empty;
    }
}
