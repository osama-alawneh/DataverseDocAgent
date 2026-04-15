// F-001 — Claude agent tool-use loop
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using DataverseDocAgent.Api.Agent.Tools;
using DataverseDocAgent.Api.Common;

namespace DataverseDocAgent.Api.Agent;

/// <summary>
/// Runs the Claude tool-use loop: send prompt → receive tool_use → execute tool
/// → return result → repeat until end_turn (or max iterations guard).
/// </summary>
public sealed class AgentOrchestrator
{
    private const int MaxIterations = 10;

    private readonly Func<MessageParameters, CancellationToken, Task<MessageResponse>> _sendMessage;

    // ── Public constructor (production) ───────────────────────────────────────

    public AgentOrchestrator(AnthropicClient client)
        : this((p, ct) => client.Messages.GetClaudeMessageAsync(p, ct))
    { }

    // ── Delegate constructor (testing / advanced DI) ──────────────────────────

    public AgentOrchestrator(
        Func<MessageParameters, CancellationToken, Task<MessageResponse>> sendMessage)
    {
        _sendMessage = sendMessage ?? throw new ArgumentNullException(nameof(sendMessage));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full agent loop and returns Claude's final text response.
    /// </summary>
    /// <param name="prompt">User prompt sent in the first message.</param>
    /// <param name="tools">Available tools Claude may call.</param>
    /// <param name="credentials">
    ///   Live credentials passed to tools — never serialized or logged (AC-6).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> RunAsync(
        string                      prompt,
        IEnumerable<IDataverseTool> tools,
        EnvironmentCredentials      credentials,
        CancellationToken           ct = default)
    {
        var toolList   = tools.ToList();
        var sdkTools   = BuildSdkTools(toolList);
        var messages   = new List<Message>
        {
            new(RoleType.User, prompt, null!),
        };

        for (int iteration = 0; iteration < MaxIterations; iteration++)
        {
            var parameters = new MessageParameters
            {
                Model     = AnthropicModels.Claude46Sonnet,
                MaxTokens = 4096,
                Messages  = messages,
                Tools     = sdkTools,
            };

            var response = await _sendMessage(parameters, ct).ConfigureAwait(false);

            if (response.StopReason == "tool_use")
            {
                // Append assistant message (built from response content)
                messages.Add(new Message
                {
                    Role    = RoleType.Assistant,
                    Content = response.Content,
                });

                // Execute every tool_use block and collect results
                var toolResults = new List<ContentBase>();
                foreach (var block in response.Content.OfType<ToolUseContent>())
                {
                    var tool         = FindTool(toolList, block.Name);
                    var inputElement = ToJsonElement(block.Input);
                    // credentials passed through — never serialized (AC-6)
                    var resultJson   = tool is not null
                        ? await tool.ExecuteAsync(inputElement, credentials).ConfigureAwait(false)
                        : $"{{\"error\":\"Unknown tool: {block.Name}\"}}";

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

        // Max iterations reached — return whatever text we have
        return "(Agent loop reached the maximum iteration limit without a final response.)";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IList<Anthropic.SDK.Common.Tool> BuildSdkTools(
        IEnumerable<IDataverseTool> tools)
    {
        return tools.Select(t =>
        {
            var schemaNode = JsonNode.Parse(t.InputSchema.GetRawText());
            var function   = new Anthropic.SDK.Common.Function(t.Name, t.Description, schemaNode);
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
            .OfType<TextContent>()
            .Select(b => b.Text)
            .FirstOrDefault();

        return text ?? string.Empty;
    }
}
