// F-003, FR-003 — Story 3.4 GetRelationshipsTool unit tests
using System.Text.Json;
using DataverseDocAgent.Api.Agent.Tools;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Moq;

namespace DataverseDocAgent.Tests;

public class GetRelationshipsToolTests
{
    // ── Contract tests ────────────────────────────────────────────────────────

    [Fact]
    public void Name_Is_get_relationships()
    {
        var tool = new GetRelationshipsTool(new Mock<IOrganizationService>().Object);
        Assert.Equal("get_relationships", tool.Name);
    }

    [Fact]
    public void Description_IsNonEmpty()
    {
        var tool = new GetRelationshipsTool(new Mock<IOrganizationService>().Object);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    [Fact]
    public void InputSchema_DeclaresTableNameAsRequired()
    {
        var tool = new GetRelationshipsTool(new Mock<IOrganizationService>().Object);
        var schema = tool.InputSchema;

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.GetProperty("properties").TryGetProperty("tableName", out _));
        var requiredNames = schema.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Contains("tableName", requiredNames);
    }

    // ── AC-3: 1:N relationships are returned with cascade config ──────────────

    [Fact]
    public async Task ExecuteAsync_OneToMany_ReturnsRelationshipsWithCascadeConfig()
    {
        var rel = BuildOneToMany(
            schemaName:        "new_account_contact",
            referencingEntity: "contact",
            referencedEntity:  "account",
            cascade:           BuildCascade(
                                   delete:  CascadeType.Cascade,
                                   assign:  CascadeType.NoCascade,
                                   share:   CascadeType.Cascade,
                                   unshare: CascadeType.Cascade));

        var entity = BuildEntityMetadata("account",
            oneToMany: new[] { rel });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("account"));

        var rels = JsonDocument.Parse(json).RootElement.GetProperty("relationships");
        Assert.Equal(1, rels.GetArrayLength());
        var r = rels[0];
        Assert.Equal("OneToMany",          r.GetProperty("relationshipType").GetString());
        Assert.Equal("new_account_contact", r.GetProperty("schemaName").GetString());
        Assert.Equal("contact",             r.GetProperty("referencingEntity").GetString());
        Assert.Equal("account",             r.GetProperty("referencedEntity").GetString());

        var cascade = r.GetProperty("cascadeConfiguration");
        Assert.Equal("Cascade",   cascade.GetProperty("delete").GetString());
        Assert.Equal("NoCascade", cascade.GetProperty("assign").GetString());
        Assert.Equal("Cascade",   cascade.GetProperty("share").GetString());
        Assert.Equal("Cascade",   cascade.GetProperty("unshare").GetString());
    }

    // ── AC-3: ManyToOne (this table is the child) is also surfaced as OneToMany ──

    [Fact]
    public async Task ExecuteAsync_ManyToOne_AlsoIncludedAsOneToMany()
    {
        var rel = BuildOneToMany(
            schemaName:        "new_parent_child",
            referencingEntity: "child_table",
            referencedEntity:  "parent_table",
            cascade:           BuildCascade());

        var entity = BuildEntityMetadata("child_table",
            manyToOne: new[] { rel });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("child_table"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.Equal("OneToMany",      r.GetProperty("relationshipType").GetString());
        Assert.Equal("child_table",    r.GetProperty("referencingEntity").GetString());
        Assert.Equal("parent_table",   r.GetProperty("referencedEntity").GetString());
    }

    // ── AC-3: N:N relationships ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ManyToMany_ReturnsRelationship()
    {
        var rel = BuildManyToMany(
            schemaName:          "new_users_groups",
            entity1LogicalName:  "user",
            entity2LogicalName:  "group");

        var entity = BuildEntityMetadata("user",
            manyToMany: new[] { rel });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("user"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.Equal("ManyToMany",       r.GetProperty("relationshipType").GetString());
        Assert.Equal("new_users_groups", r.GetProperty("schemaName").GetString());
        Assert.Equal("user",             r.GetProperty("entity1LogicalName").GetString());
        Assert.Equal("group",            r.GetProperty("entity2LogicalName").GetString());
    }

    // ── AC-3: filter out non-custom relationships ─────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NonCustomRelationships_AreFilteredOut()
    {
        var custom = BuildOneToMany("new_a", "child", "parent", BuildCascade(), isCustom: true);
        var system = BuildOneToMany("system_b", "child", "parent", BuildCascade(), isCustom: false);

        var entity = BuildEntityMetadata("parent", oneToMany: new[] { custom, system });
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("parent"));

        var rels = JsonDocument.Parse(json).RootElement.GetProperty("relationships");
        Assert.Equal(1, rels.GetArrayLength());
        Assert.Equal("new_a", rels[0].GetProperty("schemaName").GetString());
    }

    // ── AC-3: empty case returns empty array ──────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoRelationships_ReturnsEmptyArray()
    {
        var entity = BuildEntityMetadata("alone");
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("alone"));

        var rels = JsonDocument.Parse(json).RootElement.GetProperty("relationships");
        Assert.Equal(0, rels.GetArrayLength());
    }

    // ── AC-5: Dataverse fault returns structured error JSON ───────────────────

    [Fact]
    public async Task ExecuteAsync_TableNotFound_ReturnsStructuredErrorJson()
    {
        var fault = new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { Message = "Could not find an entity with the name does_not_exist" });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Throws(fault);

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("does_not_exist"));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out var err));
        Assert.False(string.IsNullOrWhiteSpace(err.GetString()));
        Assert.Equal("does_not_exist", root.GetProperty("tableName").GetString());
    }

    // ── AC-6 / NFR-007: error JSON exposes only error+tableName ───────────────

    [Fact]
    public async Task ExecuteAsync_TableNotFound_ErrorJsonExposesNoCredentialFields()
    {
        var fault = new System.ServiceModel.FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { Message = "Authentication failed for tenant abc-123 client def-456" });
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Throws(fault);

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("anything"));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out _));
        Assert.True(root.TryGetProperty("tableName", out _));
        Assert.False(root.TryGetProperty("clientId", out _));
        Assert.False(root.TryGetProperty("tenantId", out _));
        Assert.False(root.TryGetProperty("clientSecret", out _));
    }

    // ── Input validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingTableName_ReturnsStructuredErrorJson()
    {
        var tool = new GetRelationshipsTool(new Mock<IOrganizationService>().Object);

        var json = await tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>("{}"));

        Assert.True(JsonDocument.Parse(json).RootElement.TryGetProperty("error", out _));
    }

    // ── Cancellation propagation ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AlreadyCancelledToken_Throws()
    {
        var tool = new GetRelationshipsTool(new Mock<IOrganizationService>().Object);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => tool.ExecuteAsync(InputFor("anything"), cts.Token));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonElement InputFor(string tableName) =>
        JsonSerializer.Deserialize<JsonElement>($"{{\"tableName\":\"{tableName}\"}}");

    private static RetrieveEntityResponse BuildRetrieveEntityResponse(EntityMetadata entity)
    {
        var response = new RetrieveEntityResponse();
        response.Results["EntityMetadata"] = entity;
        return response;
    }

    private static EntityMetadata BuildEntityMetadata(
        string logicalName,
        OneToManyRelationshipMetadata[]? oneToMany  = null,
        OneToManyRelationshipMetadata[]? manyToOne  = null,
        ManyToManyRelationshipMetadata[]? manyToMany = null)
    {
        var entity = new EntityMetadata { LogicalName = logicalName };
        SetNonPublic(entity, nameof(EntityMetadata.OneToManyRelationships),  oneToMany  ?? Array.Empty<OneToManyRelationshipMetadata>());
        SetNonPublic(entity, nameof(EntityMetadata.ManyToOneRelationships),  manyToOne  ?? Array.Empty<OneToManyRelationshipMetadata>());
        SetNonPublic(entity, nameof(EntityMetadata.ManyToManyRelationships), manyToMany ?? Array.Empty<ManyToManyRelationshipMetadata>());
        return entity;
    }

    private static OneToManyRelationshipMetadata BuildOneToMany(
        string schemaName,
        string referencingEntity,
        string referencedEntity,
        CascadeConfiguration cascade,
        bool isCustom = true)
    {
        var rel = new OneToManyRelationshipMetadata
        {
            SchemaName           = schemaName,
            ReferencingEntity    = referencingEntity,
            ReferencedEntity     = referencedEntity,
            CascadeConfiguration = cascade,
        };
        SetNonPublic(rel, nameof(OneToManyRelationshipMetadata.IsCustomRelationship), (bool?)isCustom);
        return rel;
    }

    private static ManyToManyRelationshipMetadata BuildManyToMany(
        string schemaName,
        string entity1LogicalName,
        string entity2LogicalName,
        bool isCustom = true)
    {
        var rel = new ManyToManyRelationshipMetadata
        {
            SchemaName         = schemaName,
            Entity1LogicalName = entity1LogicalName,
            Entity2LogicalName = entity2LogicalName,
        };
        SetNonPublic(rel, nameof(ManyToManyRelationshipMetadata.IsCustomRelationship), (bool?)isCustom);
        return rel;
    }

    private static CascadeConfiguration BuildCascade(
        CascadeType delete  = CascadeType.NoCascade,
        CascadeType assign  = CascadeType.NoCascade,
        CascadeType share   = CascadeType.NoCascade,
        CascadeType unshare = CascadeType.NoCascade) =>
        new()
        {
            Delete  = delete,
            Assign  = assign,
            Share   = share,
            Unshare = unshare,
        };

    private static void SetNonPublic(object target, string propertyName, object? value)
    {
        var prop = target.GetType().GetProperty(propertyName);
        Assert.NotNull(prop);
        prop!.SetValue(target, value);
    }
}
