using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK.Messaging;
using DataverseDocAgent.Api.Agent;
using DataverseDocAgent.Api.Agent.Tools;
using Moq;

namespace DataverseDocAgent.Tests;

public class AgentOrchestratorTests
{
    // ── AC-7: end_turn immediately — no crash ─────────────────────────────────

    [Fact]
    public async Task RunAsync_EndTurnImmediately_ReturnsTextWithoutCrashing()
    {
        var endTurnResponse = BuildTextResponse("No tools needed, here is the answer.", "end_turn");

        var sender = BuildSender(endTurnResponse);
        var orchestrator = new AgentOrchestrator(sender);

        var result = await orchestrator.RunAsync("list custom tables", []);

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("answer", result);
    }

    // ── AC-4: tool_use → execute → continue → end_turn ───────────────────────

    [Fact]
    public async Task RunAsync_ToolUse_DispatchesToCorrectTool()
    {
        const string toolId     = "call-001";
        const string toolName   = "my_tool";
        const string toolResult = """{"data":"hello"}""";

        // First call → tool_use, second call → end_turn
        var callCount = 0;
        Task<MessageResponse> Sender(MessageParameters _p, CancellationToken _ct)
        {
            callCount++;
            return Task.FromResult(callCount == 1
                ? BuildToolUseResponse(toolId, toolName, "{}")
                : BuildTextResponse("Final answer after tool.", "end_turn"));
        }

        var toolMock = new Mock<IDataverseTool>();
        toolMock.Setup(t => t.Name).Returns(toolName);
        toolMock.Setup(t => t.Description).Returns("A test tool");
        toolMock.Setup(t => t.InputSchema)
                .Returns(JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}"""));
        toolMock
            .Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>()))
            .ReturnsAsync(toolResult);

        var orchestrator = new AgentOrchestrator(Sender);
        var result = await orchestrator.RunAsync(
            "do something with my_tool",
            [toolMock.Object]);

        // Tool must have been called exactly once
        toolMock.Verify(t => t.ExecuteAsync(It.IsAny<JsonElement>()), Times.Once);
        Assert.Contains("Final answer", result);
        Assert.Equal(2, callCount);
    }

    // ── AC-4: infinite-loop guard — exactly 10 iterations ────────────────────

    [Fact]
    public async Task RunAsync_MaxIterationsGuard_StopsAt10()
    {
        const string toolId   = "call-loop";
        const string toolName = "loop_tool";

        // Always returns tool_use — simulate runaway agent
        var toolMock = new Mock<IDataverseTool>();
        toolMock.Setup(t => t.Name).Returns(toolName);
        toolMock.Setup(t => t.Description).Returns("loops forever");
        toolMock.Setup(t => t.InputSchema)
                .Returns(JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}"""));
        toolMock
            .Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>()))
            .ReturnsAsync("{}");

        int callCount = 0;
        Task<MessageResponse> Sender(MessageParameters _p, CancellationToken _ct)
        {
            callCount++;
            return Task.FromResult(BuildToolUseResponse(toolId, toolName, "{}"));
        }

        var orchestrator = new AgentOrchestrator(Sender);
        // Should not throw — must return sentinel after exactly MaxIterations calls
        var result = await orchestrator.RunAsync("loop forever", [toolMock.Object]);

        Assert.Equal(10, callCount);
        Assert.Contains("maximum iteration limit", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── P1: tool exception is caught and returned as error JSON, loop does not crash ──

    [Fact]
    public async Task RunAsync_ToolThrows_ReturnsErrorJsonAndContinues()
    {
        const string toolId   = "call-throw";
        const string toolName = "throwing_tool";

        var callCount = 0;
        Task<MessageResponse> Sender(MessageParameters _p, CancellationToken _ct)
        {
            callCount++;
            return Task.FromResult(callCount == 1
                ? BuildToolUseResponse(toolId, toolName, "{}")
                : BuildTextResponse("Recovered after tool error.", "end_turn"));
        }

        var toolMock = new Mock<IDataverseTool>();
        toolMock.Setup(t => t.Name).Returns(toolName);
        toolMock.Setup(t => t.Description).Returns("throws");
        toolMock.Setup(t => t.InputSchema)
                .Returns(JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}"""));
        toolMock
            .Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var orchestrator = new AgentOrchestrator(Sender);
        // Must not throw — exception is caught and surfaced as tool result error JSON
        var result = await orchestrator.RunAsync("call throwing tool", [toolMock.Object]);

        Assert.Contains("Recovered", result);
        Assert.Equal(2, callCount);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Func<MessageParameters, CancellationToken, Task<MessageResponse>> BuildSender(
        MessageResponse response)
        => (_, _) => Task.FromResult(response);

    private static MessageResponse BuildTextResponse(string text, string stopReason)
        => new()
        {
            StopReason = stopReason,
            Content    = [new TextContent { Text = text }],
            Role       = RoleType.Assistant,
        };

    private static MessageResponse BuildToolUseResponse(string id, string name, string inputJson)
        => new()
        {
            StopReason = "tool_use",
            Content    =
            [
                new ToolUseContent
                {
                    Id    = id,
                    Name  = name,
                    Input = JsonNode.Parse(inputJson),
                },
            ],
            Role = RoleType.Assistant,
        };
}
