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

    // ── AC-3 (R-HF-10): 1:N from referenced-side emits relatedEntity = referencing ─

    [Fact]
    public async Task ExecuteAsync_OneToMany_FromReferencedSide_RelatedEntityIsReferencing()
    {
        // The supplied table ("account") is the REFERENCED (parent) side, so the
        // related (non-self) end is the REFERENCING (child) entity.
        var rel = BuildOneToMany(
            schemaName:        "new_account_contact",
            referencingEntity: "contact",
            referencedEntity:  "account",
            cascade:           BuildCascade(delete: CascadeType.Cascade));

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
        Assert.Equal("contact", r.GetProperty("referencingEntity").GetString());
        Assert.Equal("account", r.GetProperty("referencedEntity").GetString());
        Assert.Equal("new_lookupid", r.GetProperty("referencingAttribute").GetString());
        Assert.Equal("OneToMany",           r.GetProperty("relationshipType").GetString());
        Assert.Equal("new_account_contact", r.GetProperty("schemaName").GetString());
        Assert.Equal("contact",             r.GetProperty("relatedEntity").GetString());
        Assert.Equal("Cascade",             r.GetProperty("cascadeDelete").GetString());
    }

    // ── AC-3 (R-HF-10): same edge queried from the referencing side flips relatedEntity ─

    [Fact]
    public async Task ExecuteAsync_OneToMany_FromReferencingSide_RelatedEntityIsReferenced()
    {
        // SAME schemaName / endpoints as the previous test, but this time the
        // supplied table ("contact") is the REFERENCING (child) side, so the
        // related end is the REFERENCED (parent) entity. Verifies the
        // "owning-table-is-implicit" semantic is correctly oriented from BOTH
        // sides of the edge.
        var rel = BuildOneToMany(
            schemaName:        "new_account_contact",
            referencingEntity: "contact",
            referencedEntity:  "account",
            cascade:           BuildCascade(delete: CascadeType.Cascade));

        // Surfaces under ManyToOneRelationships from the child's perspective.
        var entity = BuildEntityMetadata("contact",
            manyToOne: new[] { rel });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("contact"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.Equal("OneToMany",           r.GetProperty("relationshipType").GetString());
        Assert.Equal("new_account_contact", r.GetProperty("schemaName").GetString());
        Assert.Equal("account",             r.GetProperty("relatedEntity").GetString());
        Assert.Equal("Cascade",             r.GetProperty("cascadeDelete").GetString());
    }

    // ── AC-3: N:N relationships ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ManyToMany_FromEntity1Side_RelatedEntityIsEntity2()
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
        Assert.Equal("user", r.GetProperty("entity1LogicalName").GetString());
        Assert.Equal("group", r.GetProperty("entity2LogicalName").GetString());
        Assert.Equal("new_users_groups", r.GetProperty("schemaName").GetString());
        Assert.Equal("group",            r.GetProperty("relatedEntity").GetString());
    }

    // ── AC-3 (R-HF-10): N:N flip — when queried from entity2, relatedEntity = entity1 ─

    [Fact]
    public async Task ExecuteAsync_ManyToMany_FromEntity2Side_RelatedEntityIsEntity1()
    {
        var rel = BuildManyToMany(
            schemaName:          "new_users_groups",
            entity1LogicalName:  "user",
            entity2LogicalName:  "group");

        var entity = BuildEntityMetadata("group",
            manyToMany: new[] { rel });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("group"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.Equal("user", r.GetProperty("relatedEntity").GetString());
    }

    // ── AC-3 (R-HF-10): negative — cascadeConfiguration NEVER appears ─────────

    [Fact]
    public async Task ExecuteAsync_OneToMany_NeverEmitsCascadeConfigurationObject()
    {
        var rel = BuildOneToMany(
            "new_a", "child", "parent",
            BuildCascade(
                delete:  CascadeType.Cascade,
                assign:  CascadeType.Cascade,
                share:   CascadeType.Cascade,
                unshare: CascadeType.Cascade));

        var entity = BuildEntityMetadata("parent", oneToMany: new[] { rel });
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("parent"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.False(r.TryGetProperty("cascadeConfiguration", out _),
            "R-HF-10: cascadeConfiguration object must not appear in the slimmed payload");
        Assert.False(r.TryGetProperty("assign",  out _), "assign slot must be dropped");
        Assert.False(r.TryGetProperty("share",   out _), "share slot must be dropped");
        Assert.False(r.TryGetProperty("unshare", out _), "unshare slot must be dropped");
    }

    // Deterministic evidence preserves relationship direction and lookup identity.

    [Fact]
    public async Task ExecuteAsync_OneToMany_PreservesReferencingAndReferencedEntity()
    {
        var rel = BuildOneToMany("new_a", "child", "parent", BuildCascade());
        var entity = BuildEntityMetadata("parent", oneToMany: new[] { rel });
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("parent"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.Equal("child", r.GetProperty("referencingEntity").GetString());
        Assert.Equal("parent", r.GetProperty("referencedEntity").GetString());
        Assert.Equal("new_lookupid", r.GetProperty("referencingAttribute").GetString());
    }

    // Deterministic evidence retains both N:N endpoints.

    [Fact]
    public async Task ExecuteAsync_ManyToMany_PreservesEntity1AndEntity2()
    {
        var rel = BuildManyToMany("new_users_groups", "user", "group");
        var entity = BuildEntityMetadata("user", manyToMany: new[] { rel });
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("user"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.Equal("user", r.GetProperty("entity1LogicalName").GetString());
        Assert.Equal("group", r.GetProperty("entity2LogicalName").GetString());
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

    // ── Code-review patches ───────────────────────────────────────────────────

    // Patch P3 — null EntityMetadata response must surface as a structured error
    // rather than `relationships:[]` which would be indistinguishable from
    // "table exists but has zero custom relationships".
    [Fact]
    public async Task ExecuteAsync_NullEntityMetadata_ReturnsStructuredErrorJson()
    {
        var svc = new Mock<IOrganizationService>();
        var response = new RetrieveEntityResponse();
        // EntityMetadata not set — typed property returns null.
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns(response);

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("missing"));

        var root = JsonDocument.Parse(json).RootElement;
        Assert.True(root.TryGetProperty("error", out _));
        Assert.Equal("missing", root.GetProperty("tableName").GetString());
    }

    // Patch P2 — TimeoutException sanitized into structured error (NFR-007).
    [Fact]
    public async Task ExecuteAsync_TimeoutException_ReturnsStructuredErrorJson()
    {
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Throws(new TimeoutException("network slow"));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("anything"));

        Assert.True(JsonDocument.Parse(json).RootElement.TryGetProperty("error", out _));
    }

    // Patch P10 — schema dedup across OneToMany + ManyToOne for self-referencing
    // relationships (SDK returns the same schemaName in both arrays).
    [Fact]
    public async Task ExecuteAsync_SelfReferencingOneToMany_DeduplicatedBySchemaName()
    {
        var rel = BuildOneToMany("new_self_ref", "self_table", "self_table", BuildCascade());
        // Self-ref edges appear in BOTH OneToMany AND ManyToOne arrays from RetrieveEntity.
        var entity = BuildEntityMetadata("self_table",
            oneToMany: new[] { rel },
            manyToOne: new[] { rel });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("self_table"));

        var rels = JsonDocument.Parse(json).RootElement.GetProperty("relationships");
        Assert.Equal(1, rels.GetArrayLength());
    }

    // Patch P9 / AC-3 — defensive: an SDK-returned 1:N edge whose endpoints don't
    // touch the requested table must be filtered out.
    [Fact]
    public async Task ExecuteAsync_OneToManyNotTouchingTable_IsFilteredOut()
    {
        var stray = BuildOneToMany("new_stray", "other_a", "other_b", BuildCascade());
        var entity = BuildEntityMetadata("requested_table", oneToMany: new[] { stray });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("requested_table"));

        Assert.Equal(0, JsonDocument.Parse(json).RootElement.GetProperty("relationships").GetArrayLength());
    }

    // Patch P8 / AC-3 — defensive N:N filter.
    [Fact]
    public async Task ExecuteAsync_ManyToManyNotTouchingTable_IsFilteredOut()
    {
        var stray = BuildManyToMany("new_stray_nn", "other_a", "other_b");
        var entity = BuildEntityMetadata("requested_table", manyToMany: new[] { stray });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("requested_table"));

        Assert.Equal(0, JsonDocument.Parse(json).RootElement.GetProperty("relationships").GetArrayLength());
    }

    // Patch P11 (R-HF-10 retained) — null CascadeConfiguration must still produce
    // a deterministic "NoCascade" string on cascadeDelete so Claude never sees a
    // missing cascade slot. Verifies the null-fallback semantic survived the
    // collapse of the four-field quad to a single cascadeDelete.
    [Fact]
    public async Task ExecuteAsync_NullCascadeConfiguration_DefaultsCascadeDeleteToNoCascade()
    {
        var rel = BuildOneToMany("new_null_cascade", "child", "parent", cascade: null);

        var entity = BuildEntityMetadata("parent", oneToMany: new[] { rel });
        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("parent"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.Equal("NoCascade", r.GetProperty("cascadeDelete").GetString());
    }

    // R-HF-10 review patch — CascadeType enum-roundtrip coverage. The slim collapsed
    // the four-field cascade quad to a single string, so the cascadeDelete value
    // depends on Enum.ToString() for whichever CascadeType the customizer set.
    // Previously only NoCascade was exercised (via the null-CascadeConfiguration
    // fallback). This pins the round-trip for every defined CascadeType so a future
    // SDK upgrade that flips the enum spelling fails noisily here, not in the
    // generated docx.
    [Theory]
    [InlineData(CascadeType.NoCascade,  "NoCascade")]
    [InlineData(CascadeType.Cascade,    "Cascade")]
    [InlineData(CascadeType.Active,     "Active")]
    [InlineData(CascadeType.UserOwned,  "UserOwned")]
    [InlineData(CascadeType.RemoveLink, "RemoveLink")]
    [InlineData(CascadeType.Restrict,   "Restrict")]
    public async Task ExecuteAsync_OneToMany_CascadeDelete_RoundtripsEveryCascadeType(
        CascadeType delete, string expected)
    {
        var rel = BuildOneToMany("new_a", "child", "parent", BuildCascade(delete: delete));
        var entity = BuildEntityMetadata("parent", oneToMany: new[] { rel });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("parent"));

        var r = JsonDocument.Parse(json).RootElement.GetProperty("relationships")[0];
        Assert.Equal(expected, r.GetProperty("cascadeDelete").GetString());
    }

    // R-HF-10 review patch — self-referencing N:N parity with the existing self-1:N
    // dedup test at ExecuteAsync_SelfReferencingOneToMany_DeduplicatedBySchemaName.
    // Comments in GetRelationshipsTool.cs:202 claim "Self-N:N collapses" but no test
    // pinned that behaviour. If the SDK ever returns the same self-N:N edge in
    // a second collection, the seen-by-schemaName dedup must still produce one row
    // and relatedEntity must equal the self table.
    [Fact]
    public async Task ExecuteAsync_SelfReferencingManyToMany_DeduplicatedAndCollapses()
    {
        var rel = BuildManyToMany("new_self_nn", "self_table", "self_table");
        var entity = BuildEntityMetadata("self_table",
            manyToMany: new[] { rel });

        var svc = new Mock<IOrganizationService>();
        svc.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
           .Returns(BuildRetrieveEntityResponse(entity));

        var tool = new GetRelationshipsTool(svc.Object);
        var json = await tool.ExecuteAsync(InputFor("self_table"));

        var rels = JsonDocument.Parse(json).RootElement.GetProperty("relationships");
        Assert.Equal(1, rels.GetArrayLength());
        var r = rels[0];
        Assert.Equal("ManyToMany",  r.GetProperty("relationshipType").GetString());
        Assert.Equal("new_self_nn", r.GetProperty("schemaName").GetString());
        Assert.Equal("self_table",  r.GetProperty("relatedEntity").GetString());
    }

    // Patch P14 — distinguish missing param from wrong type.
    [Fact]
    public async Task ExecuteAsync_NumericTableName_ReturnsTypeMismatchError()
    {
        var tool = new GetRelationshipsTool(new Mock<IOrganizationService>().Object);
        var json = await tool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>("{\"tableName\":123}"));
        var error = JsonDocument.Parse(json).RootElement.GetProperty("error").GetString();
        Assert.Contains("must be a string", error, StringComparison.OrdinalIgnoreCase);
    }

    // Patch P15 — invalid logical-name shapes rejected at validation, not at SDK.
    [Theory]
    [InlineData("Bad_Casing")]
    [InlineData("has space")]
    [InlineData("special!chars")]
    public async Task ExecuteAsync_InvalidLogicalName_ReturnsValidationError(string raw)
    {
        var tool = new GetRelationshipsTool(new Mock<IOrganizationService>().Object);
        var json = await tool.ExecuteAsync(
            JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(new { tableName = raw })));
        Assert.True(JsonDocument.Parse(json).RootElement.TryGetProperty("error", out _));
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
        CascadeConfiguration? cascade,
        bool isCustom = true)
    {
        var rel = new OneToManyRelationshipMetadata
        {
            SchemaName           = schemaName,
            ReferencingEntity    = referencingEntity,
            ReferencedEntity     = referencedEntity,
            ReferencingAttribute = "new_lookupid",
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
