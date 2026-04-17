using System.Text.Json;
using DataverseDocAgent.Api.Agent.Tools;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Moq;

namespace DataverseDocAgent.Tests;

public class ListCustomTablesToolTests
{
    // ── Contract tests ────────────────────────────────────────────────────────

    [Fact]
    public void Name_Is_list_custom_tables()
    {
        var tool = new ListCustomTablesTool(new Mock<IOrganizationService>().Object);
        Assert.Equal("list_custom_tables", tool.Name);
    }

    [Fact]
    public void Description_IsNonEmpty()
    {
        var tool = new ListCustomTablesTool(new Mock<IOrganizationService>().Object);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public void InputSchema_IsObjectType_WithNoRequiredParams()
    {
        var tool = new ListCustomTablesTool(new Mock<IOrganizationService>().Object);
        var schema = tool.InputSchema;

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);

        // Must declare type = "object"
        Assert.True(schema.TryGetProperty("type", out var typeProp));
        Assert.Equal("object", typeProp.GetString());
    }

    // ── AC-3: empty result ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoCustomTables_ReturnsMessageObject()
    {
        var svcMock = new Mock<IOrganizationService>();
        svcMock
            .Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
            .Returns(BuildMetadataResponse());

        var tool   = new ListCustomTablesTool(svcMock.Object);
        var result = await tool.ExecuteAsync(EmptyInput());

        var doc  = JsonDocument.Parse(result);
        var root = doc.RootElement;

        // Must have "tables" array
        Assert.True(root.TryGetProperty("tables", out var tables));
        Assert.Equal(JsonValueKind.Array, tables.ValueKind);
        Assert.Equal(0, tables.GetArrayLength());

        // Must have "message" explaining the empty result
        Assert.True(root.TryGetProperty("message", out var msg));
        Assert.False(string.IsNullOrWhiteSpace(msg.GetString()));
    }

    // ── AC-2: table entries have the required fields ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithTables_ReturnsExpectedFields()
    {
        var entity = new EntityMetadata
        {
            LogicalName = "new_mytable",
            SchemaName  = "new_MyTable",
        };
        // DisplayName / Description use LocalizedLabels — set reflectively
        SetLabel(entity, nameof(EntityMetadata.DisplayName), "My Table");
        SetLabel(entity, nameof(EntityMetadata.Description), "A test table");

        var svcMock = new Mock<IOrganizationService>();
        svcMock
            .Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
            .Returns(BuildMetadataResponse(entity));

        var tool   = new ListCustomTablesTool(svcMock.Object);
        var result = await tool.ExecuteAsync(EmptyInput());

        var root = JsonDocument.Parse(result).RootElement;
        Assert.True(root.TryGetProperty("tables", out var tables));
        Assert.Equal(1, tables.GetArrayLength());

        var row = tables[0];
        Assert.True(row.TryGetProperty("logicalName",  out _));
        Assert.True(row.TryGetProperty("schemaName",   out _));
        Assert.True(row.TryGetProperty("displayName",  out _));
        Assert.True(row.TryGetProperty("description",  out _));

        Assert.Equal("new_mytable", row.GetProperty("logicalName").GetString());
        Assert.Equal("new_MyTable", row.GetProperty("schemaName").GetString());
        Assert.Equal("My Table",    row.GetProperty("displayName").GetString());
    }

    // ── AC-2: solutionName not in output until query is implemented ───────────

    [Fact]
    public async Task ExecuteAsync_WithTables_DoesNotIncludeSolutionName()
    {
        var entity = new EntityMetadata { LogicalName = "new_table", SchemaName = "new_Table" };

        var svcMock = new Mock<IOrganizationService>();
        svcMock
            .Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
            .Returns(BuildMetadataResponse(entity));

        var tool   = new ListCustomTablesTool(svcMock.Object);
        var result = await tool.ExecuteAsync(EmptyInput());

        var root = JsonDocument.Parse(result).RootElement;
        var row  = root.GetProperty("tables")[0];

        Assert.False(row.TryGetProperty("solutionName", out _),
            "solutionName must not appear in output until the solution query is implemented");
    }

    // ── AC-10 (story 3.8): CancellationToken propagation ──────────────────────

    [Fact]
    public async Task ExecuteAsync_WithCancellationToken_DoesNotThrowWhenTokenPassed()
    {
        var svcMock = new Mock<IOrganizationService>();
        svcMock
            .Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
            .Returns(BuildMetadataResponse());

        var tool = new ListCustomTablesTool(svcMock.Object);

        using var cts = new CancellationTokenSource();

        // A non-default, non-cancelled token must be accepted without throwing. SDK-level
        // cancellation is still a gap (story 1.3 F4), but the pipeline signature is now
        // CancellationToken-aware per story 3.8 PREP-4.
        var result = await tool.ExecuteAsync(EmptyInput(), cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public async Task ExecuteAsync_WithAlreadyCancelledToken_ThrowsOperationCanceled()
    {
        var svcMock = new Mock<IOrganizationService>();
        var tool = new ListCustomTablesTool(svcMock.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(EmptyInput(), cts.Token));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonElement EmptyInput()
        => JsonSerializer.Deserialize<JsonElement>("{}");

    private static RetrieveMetadataChangesResponse BuildMetadataResponse(
        params EntityMetadata[] entities)
    {
        var response   = new RetrieveMetadataChangesResponse();
        var collection = new EntityMetadataCollection();
        foreach (var e in entities)
            collection.Add(e);
        response.Results["EntityMetadata"] = collection;
        return response;
    }

    private static void SetLabel(EntityMetadata entity, string propertyName, string text)
    {
        var prop = typeof(EntityMetadata).GetProperty(propertyName);
        Assert.NotNull(prop); // property must exist on EntityMetadata SDK type

        var labelObj = Activator.CreateInstance(prop.PropertyType);
        Assert.IsType<Label>(labelObj); // must be a Label — fails fast if SDK changes internal structure

        if (labelObj is Label label)
        {
            label.UserLocalizedLabel = new LocalizedLabel(text, 1033);
            prop.SetValue(entity, label);
        }
    }
}
