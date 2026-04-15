using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK.Messaging;
using DataverseDocAgent.Api.Agent;
using DataverseDocAgent.Api.Agent.Tools;
using DataverseDocAgent.Api.Common;
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

        var result = await orchestrator.RunAsync(
            "list custom tables",
            [],
            MakeCredentials());

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
            .Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<EnvironmentCredentials>()))
            .ReturnsAsync(toolResult);

        var orchestrator = new AgentOrchestrator(Sender);
        var result = await orchestrator.RunAsync(
            "do something with my_tool",
            [toolMock.Object],
            MakeCredentials());

        // Tool must have been called exactly once
        toolMock.Verify(t => t.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<EnvironmentCredentials>()), Times.Once);
        Assert.Contains("Final answer", result);
        Assert.Equal(2, callCount);
    }

    // ── AC-4: infinite-loop guard — max 10 iterations ─────────────────────────

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
            .Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>(), It.IsAny<EnvironmentCredentials>()))
            .ReturnsAsync("{}");

        int callCount = 0;
        Task<MessageResponse> Sender(MessageParameters _p, CancellationToken _ct)
        {
            callCount++;
            return Task.FromResult(BuildToolUseResponse(toolId, toolName, "{}"));
        }

        var orchestrator = new AgentOrchestrator(Sender);
        // Should not throw — must return after hitting the guard
        var result = await orchestrator.RunAsync("loop forever", [toolMock.Object], MakeCredentials());

        Assert.True(callCount <= 10, $"Expected ≤10 Claude calls, got {callCount}");
    }

    // ── AC-6: credentials never forwarded in return value ────────────────────

    [Fact]
    public async Task RunAsync_DoesNotIncludeCredentialsInResult()
    {
        var response = BuildTextResponse("Clean result returned by Claude.", "end_turn");
        var orchestrator = new AgentOrchestrator(BuildSender(response));

        var creds = new EnvironmentCredentials
        {
            EnvironmentUrl = "https://xn--leak-test.crm.dynamics.com",
            TenantId       = "tid-abc123xyz",
            ClientId       = "cid-abc123xyz",
            ClientSecret   = "pw-abc123xyz",
        };

        var result = await orchestrator.RunAsync("test", [], creds);

        // Verify no credential VALUES appear in the result
        Assert.DoesNotContain(creds.EnvironmentUrl, result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(creds.TenantId,       result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(creds.ClientId,        result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pw-abc123xyz",        result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EnvironmentCredentials MakeCredentials() => new()
    {
        EnvironmentUrl = "https://test.crm.dynamics.com",
        TenantId       = "tid",
        ClientId       = "cid",
        ClientSecret   = "cs",
    };

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
