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

        for (int iteration = 0; iteration < _maxIterations; iteration++)
        {
            var parameters = new MessageParameters
            {
                Model     = AnthropicModels.Claude46Sonnet,
                // E2E hotfix 2026-05-14 — 4096 was the POC default and
                // truncated the Mode 1 final JSON for any realistic
                // environment (50 tables × 10 fields easily exceeds the
                // limit; Story 3.7's `applicationUsers` key tightened the
                // budget further). Truncation surfaced downstream as
                // AI_ERROR with inner JsonException because Claude's
                // partial response is no longer valid JSON. Sonnet 4.6
                // supports much higher output budgets; 16384 covers
                // realistic Phase 1 environments with headroom and still
                // bounds per-call cost.
                MaxTokens = 16384,
                Messages  = messages,
                Tools     = sdkTools,
            };

            var response = await _sendMessage(parameters, ct).ConfigureAwait(false);

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

            // end_turn (or any other stop reason) — return final text (AC-7)
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
