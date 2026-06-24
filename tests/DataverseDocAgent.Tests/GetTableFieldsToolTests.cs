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

    // ── AC-2 (R-HF-10): returns the five PromptBuilder-consumed fields per attribute ─

    [Fact]
    public async Task ExecuteAsync_WithCustomAttributes_ReturnsExpectedFields()
    {
        var attr = new StringAttributeMetadata { LogicalName = "new_name" };
        SetAttributeType(attr, AttributeTypeCode.String);
        SetRequiredLevel(attr, AttributeRequiredLevel.ApplicationRequired);
        SetLabel(attr, nameof(AttributeMetadata.DisplayName), "Name");
        SetLabel(attr, nameof(AttributeMetadata.Description), "Customer display name");

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
        Assert.Equal("Customer display name", f.GetProperty("description").GetString());
    }

    // ── AC-2 (R-HF-10): negative — picklist columns NEVER emit options[] ──────

    [Fact]
    public async Task ExecuteAsync_PicklistAttribute_NeverEmitsOptionsArray()
    {
        // Set up a real PicklistAttributeMetadata WITH a populated OptionSet so we
        // would notice if the slim regressed and started emitting `options` again.
        var attr = new PicklistAttributeMetadata { LogicalName = "new_status" };
        SetAttributeType(attr, AttributeTypeCode.Picklist);
        attr.OptionSet = BuildOptionSet(("Active", 1), ("Inactive", 2));

        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Returns(BuildMetadataResponse("new_mytable", attr));

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("new_mytable"));

        var f = JsonDocument.Parse(json).RootElement.GetProperty("fields")[0];
        // Positive anchor: confirms BuildField still emits the core key before
        // asserting absence of options[]; otherwise a hypothetical BuildField
        // regression that produces an empty field object would pass tautologically.
        Assert.Equal("new_status", f.GetProperty("logicalName").GetString());
        Assert.False(f.TryGetProperty("options", out _),
            "R-HF-10: picklist attributes must not emit an options array");
    }

    // ── AC-2 (R-HF-10): negative — boolean OptionSet must NOT surface as options[] ─

    [Fact]
    public async Task ExecuteAsync_BooleanAttribute_NeverEmitsOptionsArray()
    {
        var attr = new BooleanAttributeMetadata { LogicalName = "new_active" };
        SetAttributeType(attr, AttributeTypeCode.Boolean);
        attr.OptionSet = new BooleanOptionSetMetadata(
            new OptionMetadata(new Label("Yes", 1033), 1),
            new OptionMetadata(new Label("No",  1033), 0));

        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Returns(BuildMetadataResponse("new_mytable", attr));

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("new_mytable"));

        var f = JsonDocument.Parse(json).RootElement.GetProperty("fields")[0];
        Assert.Equal("new_active", f.GetProperty("logicalName").GetString());
        Assert.False(f.TryGetProperty("options", out _),
            "R-HF-10: boolean attributes must not emit True/False options");
    }

    // ── AC-2 (R-HF-10): negative — defaultValue NEVER emitted, even when SDK has one ─

    [Fact]
    public async Task ExecuteAsync_BooleanAttributeWithDefault_NeverEmitsDefaultValue()
    {
        var attr = new BooleanAttributeMetadata { LogicalName = "new_active" };
        SetAttributeType(attr, AttributeTypeCode.Boolean);
        SetNonPublic(attr, nameof(BooleanAttributeMetadata.DefaultValue), (bool?)true);

        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Returns(BuildMetadataResponse("new_mytable", attr));

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("new_mytable"));

        var f = JsonDocument.Parse(json).RootElement.GetProperty("fields")[0];
        Assert.Equal("new_active", f.GetProperty("logicalName").GetString());
        Assert.False(f.TryGetProperty("defaultValue", out _),
            "R-HF-10: defaultValue must never appear in the slimmed payload");
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

    // ── Code-review patches: not-found / paging / fault discrimination ────────

    // Patch P3 — empty EntityMetadata collection must surface as a structured
    // error, not as an empty `fields:[]` success that would mislead the agent.
    [Fact]
    public async Task ExecuteAsync_EmptyEntityMetadata_ReturnsStructuredErrorJson()
    {
        var svcMock = new Mock<IOrganizationService>();
        var emptyResponse = new RetrieveMetadataChangesResponse();
        emptyResponse.Results["EntityMetadata"] = new EntityMetadataCollection();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns(emptyResponse);

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("missing_table"));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out _));
        Assert.Equal("missing_table", root.GetProperty("tableName").GetString());
    }

    // Patch P2 — TimeoutException must be sanitized into structured error,
    // not bubble out and unwind the agent loop.
    [Fact]
    public async Task ExecuteAsync_TimeoutException_ReturnsStructuredErrorJson()
    {
        var svcMock = new Mock<IOrganizationService>();
        svcMock.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
               .Throws(new TimeoutException("network slow"));

        var tool = new GetTableFieldsTool(svcMock.Object);
        var json = await tool.ExecuteAsync(InputFor("anything"));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out _));
        Assert.Equal("anything", root.GetProperty("tableName").GetString());
    }

    // Patch P14 — distinguish missing param from wrong type so debug logs
    // for malformed Claude tool calls are not misleading.
    [Fact]
    public async Task ExecuteAsync_NumericTableName_ReturnsTypeMismatchError()
    {
        var tool = new GetTableFieldsTool(new Mock<IOrganizationService>().Object);
        var json = await tool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>("{\"tableName\":123}"));
        var error = JsonDocument.Parse(json).RootElement.GetProperty("error").GetString();
        Assert.Contains("must be a string", error, StringComparison.OrdinalIgnoreCase);
    }

    // Patch P15 — unicode-NBSP and uppercase must be rejected at the validation
    // layer, not after a misleading "table not found" round-trip.
    [Theory]
    [InlineData("Bad_Casing")]
    [InlineData("has space")]
    [InlineData("name\u00A0with\u00A0nbsp")]
    [InlineData("special!chars")]
    public async Task ExecuteAsync_InvalidLogicalName_ReturnsValidationError(string raw)
    {
        var tool = new GetTableFieldsTool(new Mock<IOrganizationService>().Object);
        var json = await tool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(new { tableName = raw })));
        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out _));
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

    private static void SetNonPublic(object target, string propertyName, object? value)
    {
        var prop = target.GetType().GetProperty(propertyName);
        Assert.NotNull(prop);
        prop!.SetValue(target, value);
    }
}
