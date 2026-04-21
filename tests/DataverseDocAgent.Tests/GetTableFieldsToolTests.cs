// F-002, FR-002 — Story 3.4 GetTableFieldsTool unit tests
using System.Text.Json;
using DataverseDocAgent.Api.Agent.Tools;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Moq;

namespace DataverseDocAgent.Tests;

public class GetTableFieldsToolTests
{
    // ── Contract tests ────────────────────────────────────────────────────────

    [Fact]
    public void Name_Is_get_table_fields()
    {
        var tool = new GetTableFieldsTool(new Mock<IOrganizationService>().Object);
        Assert.Equal("get_table_fields", tool.Name);
    }

    [Fact]
    public void Description_IsNonEmpty()
    {
        var tool = new GetTableFieldsTool(new Mock<IOrganizationService>().Object);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public void InputSchema_DeclaresTableNameAsRequired()
    {
        var tool = new GetTableFieldsTool(new Mock<IOrganizationService>().Object);
        var schema = tool.InputSchema;

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.Equal("object", schema.GetProperty("type").GetString());

        // tableName under properties
        var props = schema.GetProperty("properties");
        Assert.True(props.TryGetProperty("tableName", out _));

        // tableName must appear in the required array — drives Claude tool-use schema validation
        Assert.True(schema.TryGetProperty("required", out var required));
        Assert.Equal(JsonValueKind.Array, required.ValueKind);
        var requiredNames = required.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("tableName", requiredNames);
    }

    // ── AC-2: returns expected fields per attribute ───────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithCustomAttributes_ReturnsExpectedFields()
    {
        var attr = new StringAttributeMetadata { LogicalName = "new_name" };
        SetAttributeType(attr, AttributeTypeCode.String);
        SetRequiredLevel(attr, AttributeRequiredLevel.ApplicationRequired);
        SetLabel(attr, nameof(AttributeMetadata.DisplayName), "Name");

        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Returns(BuildMetadataResponse("new_mytable", attr));

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("new_mytable"));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("fields", out var fields));
        Assert.Equal(1, fields.GetArrayLength());

        var f = fields[0];
        Assert.Equal("new_name", f.GetProperty("logicalName").GetString());
        Assert.Equal("Name",     f.GetProperty("displayName").GetString());
        Assert.Equal("String",   f.GetProperty("attributeType").GetString());
        Assert.Equal("ApplicationRequired", f.GetProperty("requiredLevel").GetString());
    }

    // ── AC-2: PicklistAttributeMetadata emits options[] ───────────────────────

    [Fact]
    public async Task ExecuteAsync_PicklistAttribute_EmitsOptionsArray()
    {
        var attr = new PicklistAttributeMetadata { LogicalName = "new_status" };
        SetAttributeType(attr, AttributeTypeCode.Picklist);
        attr.OptionSet = BuildOptionSet(("Active", 1), ("Inactive", 2));

        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Returns(BuildMetadataResponse("new_mytable", attr));

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("new_mytable"));

        var f = JsonDocument.Parse(json).RootElement.GetProperty("fields")[0];
        Assert.True(f.TryGetProperty("options", out var options));
        Assert.Equal(2, options.GetArrayLength());

        // Order from OptionSet must be preserved — Claude relies on stable schema for diff/audit.
        Assert.Equal("Active",   options[0].GetProperty("label").GetString());
        Assert.Equal(1,          options[0].GetProperty("value").GetInt32());
        Assert.Equal("Inactive", options[1].GetProperty("label").GetString());
        Assert.Equal(2,          options[1].GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_MultiSelectPicklistAttribute_EmitsOptionsArray()
    {
        var attr = new MultiSelectPicklistAttributeMetadata { LogicalName = "new_tags" };
        SetAttributeType(attr, AttributeTypeCode.Virtual);
        attr.OptionSet = BuildOptionSet(("Red", 10), ("Green", 20), ("Blue", 30));

        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Returns(BuildMetadataResponse("new_mytable", attr));

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("new_mytable"));

        var f = JsonDocument.Parse(json).RootElement.GetProperty("fields")[0];
        Assert.Equal(3, f.GetProperty("options").GetArrayLength());
    }

    // ── AC-2: non-picklist attributes do not include options[] ────────────────

    [Fact]
    public async Task ExecuteAsync_StringAttribute_DoesNotEmitOptionsArray()
    {
        var attr = new StringAttributeMetadata { LogicalName = "new_name" };
        SetAttributeType(attr, AttributeTypeCode.String);

        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Returns(BuildMetadataResponse("new_mytable", attr));

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("new_mytable"));

        var f = JsonDocument.Parse(json).RootElement.GetProperty("fields")[0];
        Assert.False(f.TryGetProperty("options", out _),
            "String attributes must not include an options array");
    }

    // ── AC-5: table-not-found returns structured error JSON ───────────────────

    [Fact]
    public async Task ExecuteAsync_TableNotFound_ReturnsStructuredErrorJson()
    {
        var fault = new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { Message = "Could not find an entity with the name does_not_exist" });

        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Throws(fault);

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("does_not_exist"));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out var err));
        Assert.False(string.IsNullOrWhiteSpace(err.GetString()));
        Assert.Equal("does_not_exist", root.GetProperty("tableName").GetString());
    }

    // ── AC-6: NFR-007 — exception messages do not leak credential-shaped data ─

    [Fact]
    public async Task ExecuteAsync_TableNotFound_ErrorJsonExposesNoCredentialFields()
    {
        var fault = new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { Message = "Authentication failed for tenant abc-123" });
        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Throws(fault);

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("anything"));

        // Whatever the underlying SDK message contains, callers must never receive
        // tenant ids, secrets, or connection-string fragments. We assert the shape
        // is the structured-error contract, not the raw SDK message.
        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out _));
        Assert.True(root.TryGetProperty("tableName", out _));
        // No properties leaking SDK internals
        Assert.False(root.TryGetProperty("clientId", out _));
        Assert.False(root.TryGetProperty("tenantId", out _));
        Assert.False(root.TryGetProperty("clientSecret", out _));
    }

    // ── Input validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingTableName_ReturnsStructuredErrorJson()
    {
        var tool = new GetTableFieldsTool(new Mock<IOrganizationService>().Object);

        var json = await tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>("{}"));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out _));
    }

    // ── Cancellation propagation (mirrors story 3.8 PREP-4) ───────────────────

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelledToken_Throws()
    {
        var tool = new GetTableFieldsTool(new Mock<IOrganizationService>().Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(InputFor("anything"), cts.Token));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonElement InputFor(string tableName) =>
        JsonSerializer.Deserialize<JsonElement>($"{{\"tableName\":\"{tableName}\"}}");

    private static RetrieveMetadataChangesResponse BuildMetadataResponse(
        string tableLogicalName, params AttributeMetadata[] attrs)
    {
        var entity = new EntityMetadata { LogicalName = tableLogicalName };
        // Attributes property is internal-set on EntityMetadata — assigned via reflection.
        SetEntityAttributes(entity, attrs);

        var collection = new EntityMetadataCollection { entity };
        var response = new RetrieveMetadataChangesResponse();
        response.Results["EntityMetadata"] = collection;
        return response;
    }

    private static void SetEntityAttributes(EntityMetadata entity, AttributeMetadata[] attrs)
    {
        var prop = typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes));
        Assert.NotNull(prop);
        // Setter is non-public on the SDK type — reflection bypasses it for tests.
        prop!.SetValue(entity, attrs);
    }

    private static void SetAttributeType(AttributeMetadata attr, AttributeTypeCode type)
    {
        var prop = typeof(AttributeMetadata).GetProperty(nameof(AttributeMetadata.AttributeType));
        Assert.NotNull(prop);
        prop!.SetValue(attr, type);
    }

    private static void SetRequiredLevel(AttributeMetadata attr, AttributeRequiredLevel level)
    {
        var prop = typeof(AttributeMetadata).GetProperty(nameof(AttributeMetadata.RequiredLevel));
        Assert.NotNull(prop);
        prop!.SetValue(attr, new AttributeRequiredLevelManagedProperty(level));
    }

    private static OptionSetMetadata BuildOptionSet(params (string Label, int Value)[] options)
    {
        var set = new OptionSetMetadata();
        foreach (var (label, value) in options)
        {
            var opt = new OptionMetadata(new Label(label, 1033), value);
            set.Options.Add(opt);
        }
        return set;
    }

    private static void SetLabel(AttributeMetadata attr, string propertyName, string text)
    {
        var prop = typeof(AttributeMetadata).GetProperty(propertyName);
        Assert.NotNull(prop);
        var labelObj = Activator.CreateInstance(prop!.PropertyType) as Label;
        Assert.NotNull(labelObj);
        labelObj!.UserLocalizedLabel = new LocalizedLabel(text, 1033);
        prop.SetValue(attr, labelObj);
    }
}
