// F-001, F-002, F-003 — Story 3.4 Mode 1 tool registration tests
using DataverseDocAgent.Api.Agent.Tools;
using Microsoft.Xrm.Sdk;
using Moq;

namespace DataverseDocAgent.Tests;

public class DataverseToolFactoryTests
{
    [Fact]
    public void CreateMode1Tools_ReturnsExactlyFiveNamedTools()
    {
        var svc = new Mock<IOrganizationService>().Object;
        var tools = DataverseToolFactory.CreateMode1Tools(svc);

        var names = tools.Select(t => t.Name).ToArray();
        // Exact-ordered assertion: a regression that swaps, drops, or reorders
        // a tool changes Mode 1's capability surface. Story 3.5 added
        // get_organisation_metadata as the 4th tool. Story 3.7 adds
        // get_application_users as the 5th tool (FR-050) — the trailing slot
        // is pinned here so a future addition cannot silently shift the
        // application-users tool out of its documented position.
        Assert.Equal(
            new[]
            {
                "list_custom_tables",
                "get_table_fields",
                "get_relationships",
                "get_organisation_metadata",
                "get_application_users",
            },
            names);
    }

    [Fact]
    public void CreateMode1Tools_AllToolsBoundToSameService()
    {
        var svc = new Mock<IOrganizationService>().Object;
        var tools = DataverseToolFactory.CreateMode1Tools(svc);

        // Each tool stores _service privately; assert via reflection that all
        // five (Story 3.7 added the 5th) received the SAME instance — guards
        // against future code that constructs tools with separate
        // ServiceClients (re-auth-per-tool regression).
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
