using System.Text.Json;
using System.Text.Json.Nodes;
using DataverseDocAgent.Api.Agent.Tools;
using DataverseDocAgent.Api.Pipeline;

namespace DataverseDocAgent.Tests;

public sealed class EvidencePipelineTests
{
    [Fact]
    public async Task EmptySuccessIsDistinguishedFromFailedInventoryAndErrorsAreSanitized()
    {
        var empty = await new EvidenceCollector().CollectAsync(Tools());
        Assert.Contains(empty.Coverage, c => c.Scope == "tables" && c.Status == "complete");
        Assert.DoesNotContain(empty.Components, c => c.Kind == "table");
        var failed = await new EvidenceCollector().CollectAsync(Tools(tables: """{"error":"clientSecret=PRIVATE"}"""));
        Assert.Contains(failed.Coverage, c => c.Scope == "tables" && c.Status == "failed");
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(failed));
        Assert.Contains(failed.Coverage, c => c.Scope == "standard-table-customizations" && c.Status == "excluded");
    }

    [Fact]
    public async Task TablesAreSortedAndRelationshipsAcrossBothEndpointsCountOnce()
    {
        var calls = new List<string>();
        var snapshot = await new EvidenceCollector().CollectAsync(Tools(
            tables: """{"tables":[{"logicalName":"new_b"},{"logicalName":"new_a"}]}""",
            relationships: input => {
                var table = input.GetProperty("tableName").GetString();
                calls.Add(table!);
                return JsonSerializer.Serialize(new { relationships = new[] { new {
                    schemaName = "new_a_b", relationshipType = "OneToMany", relatedEntity = table == "new_a" ? "new_b" : "new_a",
                    referencingEntity = "new_b", referencedEntity = "new_a", referencingAttribute = "new_aid"
                } } });
            }));
        Assert.Equal(new[] { "new_a", "new_b" }, calls);
        var relationship = Assert.Single(snapshot.Components.Where(c => c.Kind == "relationship"));
        Assert.Equal("relationship:new_a_b", relationship.Id);
        Assert.Equal("table:new_a", relationship.ParentId);
        Assert.Equal("new_aid", relationship.Data.GetProperty("referencingAttribute").GetString());
        Assert.Equal(2, snapshot.Components.Count(c => c.Kind == "table"));
    }

    [Fact]
    public async Task DuplicateTablesAndInvalidFieldsAreReportedWithoutFabricatingComponents()
    {
        var snapshot = await new EvidenceCollector().CollectAsync(Tools(
            tables: """{"tables":[{"logicalName":"new_a"},{"logicalName":"new_a"}]}""",
            fields: """{"fields":[{"logicalName":"new_good","description":"Source description"},{"displayName":"Missing identifier"}]}"""));
        Assert.Single(snapshot.Components.Where(c => c.Kind == "table"));
        Assert.Single(snapshot.Components.Where(c => c.Kind == "field"));
        Assert.Contains(snapshot.Coverage, c => c.Scope == "tables" && c.Status == "partial");
        Assert.Contains(snapshot.Coverage, c => c.Scope == "fields:new_a" && c.Status == "partial");
    }

    [Fact]
    public async Task RoleLookupFailureIsPartialAndCancellationPropagates()
    {
        var snapshot = await new EvidenceCollector().CollectAsync(Tools(users: """{"applicationUsers":[{"applicationId":"abc","roles":["(role lookup unavailable)"]}]}"""));
        Assert.Contains(snapshot.Coverage, c => c.Scope == "application-users" && c.Status == "partial");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EvidenceCollector().CollectAsync(Tools(), cts.Token));
    }

    [Fact]
    public async Task DistinctSystemUsersSharingAnApplicationIdRemainSeparate()
    {
        var snapshot = await new EvidenceCollector().CollectAsync(Tools(users: """{"applicationUsers":[{"systemUserId":"user-a","applicationId":"app","roles":[]},{"systemUserId":"user-b","applicationId":"app","roles":[]}]}"""));
        Assert.Equal(new[] { "application-user:user-a", "application-user:user-b" }, snapshot.Components.Where(c => c.Kind == "application-user").Select(c => c.Id));
        Assert.Contains(snapshot.Coverage, c => c.Scope == "application-users" && c.Status == "complete");
    }

    [Fact]
    public async Task MalformedRecordsArePartialAndUnexpectedPropertiesAreNotPersisted()
    {
        var snapshot = await new EvidenceCollector().CollectAsync(Tools(
            tables: """{"tables":[{"logicalName":"new_a","clientSecret":"PRIVATE"},{"logicalName":42}]}""",
            fields: """{"fields":[{"logicalName":"new_aid","attributeType":42}]}"""));
        Assert.Single(snapshot.Components.Where(c => c.Kind == "table"));
        Assert.Empty(snapshot.Components.Where(c => c.Kind == "field"));
        Assert.Contains(snapshot.Coverage, c => c.Scope == "fields:new_a" && c.Status == "partial");
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public async Task DuplicateInventoryKeysFailRatherThanSelectingOneArray()
    {
        var snapshot = await new EvidenceCollector().CollectAsync(Tools(
            tables: """{"tables":[{"logicalName":"new_a"}],"tables":[]}"""));
        Assert.Contains(snapshot.Coverage, c => c.Scope == "tables" && c.Status == "failed");
        Assert.Empty(snapshot.Components.Where(c => c.Kind == "table"));
    }

    [Fact]
    public void DuplicateDataKeysCannotBeValidatedAsAuthoritativeEvidence()
    {
        var component = new EvidenceComponent("table:new_a", "table", null,
            Json("""{"logicalName":"new_b","logicalName":"new_a"}"""));
        var snapshot = new EvidenceSnapshot(1, "duplicate-data", DateTime.UtcNow, new[] { component }, Array.Empty<CoverageEntry>());
        Assert.Throws<InvalidDataException>(() => SnapshotStore.Validate(snapshot));
    }

    [Fact]
    public void ParentIdMustMatchExactStoredComponentId()
    {
        var snapshot = new EvidenceSnapshot(1, "case-sensitive-parent", DateTime.UtcNow, new[] {
            new EvidenceComponent("table:new_a", "table", null, Json("""{"logicalName":"new_a"}""")),
            new EvidenceComponent("field:NEW_A:new_name", "field", "table:NEW_A", Json("""{"logicalName":"new_name"}"""))
        }, Array.Empty<CoverageEntry>());
        Assert.Throws<InvalidDataException>(() => SnapshotStore.Validate(snapshot));
    }

    [Fact]
    public async Task ConflictingRelationshipEndpointsDoNotReplaceTheFirstSourceAndArePartial()
    {
        var snapshot = await new EvidenceCollector().CollectAsync(Tools(
            tables: """{"tables":[{"logicalName":"new_a"},{"logicalName":"new_b"}]}""",
            relationships: input => input.GetProperty("tableName").GetString() == "new_a"
                ? """{"relationships":[{"schemaName":"new_edge","relationshipType":"ManyToMany","entity1LogicalName":"new_a","entity2LogicalName":"new_b","relatedEntity":"new_b"}]}"""
                : """{"relationships":[{"schemaName":"new_edge","relationshipType":"ManyToMany","entity1LogicalName":"new_b","entity2LogicalName":"new_c","relatedEntity":"new_c"}]}"""));
        Assert.Single(snapshot.Components.Where(c => c.Kind == "relationship"));
        Assert.Contains(snapshot.Coverage, c => c.Scope == "relationships:new_b" && c.Status == "partial");
    }

    [Fact]
    public async Task SnapshotRoundTripsAndCannotOverwriteExistingEvidence()
    {
        var directory = NewDirectory();
        try
        {
            var snapshot = await new EvidenceCollector().CollectAsync(Tools());
            await SnapshotStore.SaveAsync(snapshot, directory);
            var loaded = await SnapshotStore.LoadAsync(directory);
            Assert.Equal(snapshot.RunId, loaded.RunId);
            Assert.Equal(snapshot.Components.Select(c => c.Id), loaded.Components.Select(c => c.Id));
            Assert.Equal(snapshot.Coverage, loaded.Coverage);
            await Assert.ThrowsAsync<IOException>(() => SnapshotStore.SaveAsync(snapshot, directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("mutation")]
    [InlineData("traversal")]
    [InlineData("version")]
    public async Task SnapshotRejectsTamperedFilesAndManifest(string tampering)
    {
        var directory = NewDirectory();
        try
        {
            await SnapshotStore.SaveAsync(await new EvidenceCollector().CollectAsync(Tools()), directory);
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
            if (tampering == "mutation")
                await File.AppendAllTextAsync(Path.Combine(directory, "coverage.json"), " ");
            else
            {
                if (tampering == "traversal") manifest["files"]![0]!["path"] = "../outside.json";
                else manifest["version"] = 900;
                await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => SnapshotStore.LoadAsync(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SnapshotRejectsUnknownKindsDuplicateIdsAndOrphanReferencesBeforeWriting()
    {
        var table = new EvidenceComponent("table:new_a", "table", null, Json("""{"logicalName":"new_a"}"""));
        foreach (var components in new[] {
            new[] { table, table },
            new[] { table with { Kind = "invented" } },
            new[] { new EvidenceComponent("field:new_b:new_x", "field", "table:new_b", Json("""{"logicalName":"new_x"}""")) }
        })
        {
            var directory = NewDirectory();
            var snapshot = new EvidenceSnapshot(1, Guid.NewGuid().ToString("N"), DateTime.UtcNow, components, Array.Empty<CoverageEntry>());
            await Assert.ThrowsAsync<InvalidDataException>(() => SnapshotStore.SaveAsync(snapshot, directory));
            Assert.False(Directory.Exists(directory));
        }
    }

    [Fact]
    public async Task BatchesRoundTripAllComponentsAndRejectIncorrectRelationshipEndpoints()
    {
        var directory = NewDirectory();
        try
        {
            var components = Enumerable.Range(0, 401).Select(i => new EvidenceComponent("table:new_" + i, "table", null,
                JsonSerializer.SerializeToElement(new { logicalName = "new_" + i }))).ToArray();
            var snapshot = new EvidenceSnapshot(1, "batch-test", DateTime.UtcNow, components, Array.Empty<CoverageEntry>());
            await SnapshotStore.SaveAsync(snapshot, directory);
            Assert.Equal(3, Directory.GetFiles(directory, "components-*.json").Length);
            Assert.Equal(401, (await SnapshotStore.LoadAsync(directory)).Components.Count);
            var badRelationship = new EvidenceComponent("relationship:new_edge", "relationship", "table:new_0",
                Json("""{"schemaName":"new_edge","relationshipType":"OneToMany","referencingEntity":"new_1","referencedEntity":"new_2","relatedEntity":"new_2"}"""));
            Assert.Throws<InvalidDataException>(() => SnapshotStore.Validate(snapshot with { Components = components.Append(badRelationship).ToArray() }));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "dataverse-evidence-tests-" + Guid.NewGuid().ToString("N"));
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private static IReadOnlyList<IDataverseTool> Tools(string tables = "{\"tables\":[]}",
        string fields = "{\"fields\":[]}", Func<JsonElement, string>? relationships = null,
        string users = "{\"applicationUsers\":[]}") => new IDataverseTool[] {
            new FakeTool("get_organisation_metadata", _ => "{\"environmentName\":\"Example\"}"),
            new FakeTool("list_custom_tables", _ => tables), new FakeTool("get_table_fields", _ => fields),
            new FakeTool("get_relationships", relationships ?? (_ => "{\"relationships\":[]}")),
            new FakeTool("get_application_users", _ => users)
        };
    private sealed class FakeTool(string name, Func<JsonElement, string> result) : IDataverseTool
    {
        public string Name => name;
        public string Description => name;
        public JsonElement InputSchema => Json("{}");
        public Task<string> ExecuteAsync(JsonElement input, CancellationToken cancellationToken = default) => Task.FromResult(result(input));
    }
}
