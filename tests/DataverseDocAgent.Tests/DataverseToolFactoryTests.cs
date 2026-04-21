// F-001, F-002, F-003 — Story 3.4 Mode 1 tool registration tests
using DataverseDocAgent.Api.Agent.Tools;
using Microsoft.Xrm.Sdk;
using Moq;

namespace DataverseDocAgent.Tests;

public class DataverseToolFactoryTests
{
    [Fact]
    public void CreateMode1Tools_ReturnsExactlyThreeNamedTools()
    {
        var svc = new Mock<IOrganizationService>().Object;
        var tools = DataverseToolFactory.CreateMode1Tools(svc);

        var names = tools.Select(t => t.Name).ToArray();
        // Exact-set assertion: a regression that swaps or drops a tool changes Mode 1's
        // capability surface. Story 3.5's Mode 1 generation depends on these three.
        Assert.Equal(
            new[] { "list_custom_tables", "get_table_fields", "get_relationships" },
            names);
    }

    [Fact]
    public void CreateMode1Tools_AllToolsBoundToSameService()
    {
        var svc = new Mock<IOrganizationService>().Object;
        var tools = DataverseToolFactory.CreateMode1Tools(svc);

        // Each tool stores _service privately; assert via reflection that all three
        // received the SAME instance — guards against future code that constructs
        // tools with three different ServiceClients (re-auth-per-tool regression).
        foreach (var tool in tools)
        {
            var serviceField = tool.GetType().GetField("_service",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(serviceField);
            Assert.Same(svc, serviceField!.GetValue(tool));
        }
    }

    [Fact]
    public void CreateMode1Tools_NullService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DataverseToolFactory.CreateMode1Tools(null!));
    }

    [Fact]
    public void CreateMode1Tools_EachToolHasNonEmptySchemaAndDescription()
    {
        var svc = new Mock<IOrganizationService>().Object;
        var tools = DataverseToolFactory.CreateMode1Tools(svc);

        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            Assert.Equal(System.Text.Json.JsonValueKind.Object, tool.InputSchema.ValueKind);
        }
    }
}
