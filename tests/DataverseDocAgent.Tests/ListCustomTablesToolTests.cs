using System.Text.Json;
using DataverseDocAgent.Api.Agent.Tools;
using DataverseDocAgent.Api.Common;
using Microsoft.PowerPlatform.Dataverse.Client;
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

        var tool = new ListCustomTablesTool(svcMock.Object);
        var creds = MakeCredentials();
        var result = await tool.ExecuteAsync(EmptyInput(), creds);

        var doc = JsonDocument.Parse(result);
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

        var tool = new ListCustomTablesTool(svcMock.Object);
        var result = await tool.ExecuteAsync(EmptyInput(), MakeCredentials());

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

    // ── AC-6: credentials never appear in output ──────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DoesNotLeakCredentials()
    {
        var svcMock = new Mock<IOrganizationService>();
        svcMock
            .Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
            .Returns(BuildMetadataResponse());

        var tool = new ListCustomTablesTool(svcMock.Object);
        var creds = new EnvironmentCredentials
        {
            EnvironmentUrl = "https://secret-env.crm.dynamics.com",
            TenantId       = "secret-tenant-id",
            ClientId       = "secret-client-id",
            ClientSecret   = "super-secret-password",
        };

        var result = await tool.ExecuteAsync(EmptyInput(), creds);

        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EnvironmentCredentials MakeCredentials() => new()
    {
        EnvironmentUrl = "https://test.crm.dynamics.com",
        TenantId       = "tid",
        ClientId       = "cid",
        ClientSecret   = "cs",
    };

    private static JsonElement EmptyInput()
        => JsonSerializer.Deserialize<JsonElement>("{}");

    private static RetrieveMetadataChangesResponse BuildMetadataResponse(
        params EntityMetadata[] entities)
    {
        var response = new RetrieveMetadataChangesResponse();
        // EntityMetadata collection is internal — set via reflection
        var collection = new EntityMetadataCollection();
        foreach (var e in entities)
            collection.Add(e);
        response.Results["EntityMetadata"] = collection;
        return response;
    }

    private static void SetLabel(EntityMetadata entity, string propertyName, string text)
    {
        var prop = typeof(EntityMetadata).GetProperty(propertyName);
        if (prop == null) return;

        var labelObj = Activator.CreateInstance(prop.PropertyType);
        if (labelObj is Label label)
        {
            label.UserLocalizedLabel = new LocalizedLabel(text, 1033);
            prop.SetValue(entity, label);
        }
    }
}
