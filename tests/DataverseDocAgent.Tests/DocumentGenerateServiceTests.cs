using System.Text.Json;
using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Features.DocumentGenerate;
using DataverseDocAgent.Api.Jobs;
using DataverseDocAgent.Api.Pipeline;
using DataverseDocAgent.Api.Storage;
using DataverseDocAgent.Shared.Dataverse;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseDocAgent.Tests;

public class DocumentGenerateServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotPipeline_PersistsEvidenceAndPublishesOnlyAfterValidatedAnalysis(bool cancel)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "DataverseDocAgent.sln"))) repository = repository.Parent;
        var run = Path.Combine(Path.GetTempPath(), "dda-assembly-" + Guid.NewGuid().ToString("N"));
        using var ct = new CancellationTokenSource();
        var store = new CapturingStore();
        var options = new PipelineOptions { SkillsRoot = Path.Combine(repository!.FullName, ".agents", "skills") };
        var service = new DocumentGenerateService(null!, new AnalysisPipeline(new AssemblyRunner(cancel ? ct : null), options), options,
            store, NullLogger<DocumentGenerateService>.Instance);
        try
        {
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProcessSnapshotAsync(Snapshot(), run, ct.Token));
                Assert.Null(store.Bytes);
            }
            else
            {
                Assert.Equal("stored", await service.ProcessSnapshotAsync(Snapshot(), run, ct.Token));
                Assert.NotNull(store.Bytes);
                using var stream = new MemoryStream(store.Bytes!);
                using var doc = WordprocessingDocument.Open(stream, false);
                Assert.Contains("Original SDK description", doc.MainDocumentPart!.Document!.InnerText);
                Assert.Contains("AI inference:", doc.MainDocumentPart.Document.InnerText);
                var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToArray();
                Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Description + " " + e.Path?.XPath)));
            }
            var persisted = await SnapshotStore.LoadAsync(Path.Combine(run, "evidence"));
            Assert.Equal(Snapshot().Components.Count, persisted.Components.Count);
        }
        finally { if (Directory.Exists(run)) Directory.Delete(run, true); }
    }

    private sealed class AssemblyRunner(CancellationTokenSource? cancel) : IAnalysisRunner
    {
        public async Task<string> RunAsync(AnalysisRequest request, CancellationToken cancellationToken = default)
        {
            using var input = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(request.WorkingDirectory, "input.json"), cancellationToken));
            var root = input.RootElement;
            var ids = root.GetProperty("targets").EnumerateArray().Select(t => t.GetProperty("id").GetString()!).ToArray();
            cancel?.Cancel();
            return JsonSerializer.Serialize(new StageAnalysis(1, root.GetProperty("stageId").GetString()!, root.GetProperty("skillVersion").GetString()!,
                root.GetProperty("inputHash").GetString()!, ids, ids.Select(id => new AnalysisFinding(id, id, "inference", "Purpose is uncertain.")).ToArray(),
                Array.Empty<string>()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
    }

    private sealed class CapturingStore : IDocumentStore
    {
        public byte[]? Bytes { get; private set; }
        public Task<string> StoreAsync(byte[] documentBytes, TimeSpan ttl) { Bytes = documentBytes; return Task.FromResult("stored"); }
        public Task<byte[]?> RetrieveAsync(string token) => Task.FromResult(Bytes);
    }

    [Theory]
    [InlineData("complete", false)]
    [InlineData("excluded", false)]
    [InlineData("limitation", false)]
    [InlineData("partial", true)]
    [InlineData("failed", true)]
    [InlineData("unavailable", true)]
    public void ReportExitClassificationDistinguishesExpectedScopeFromFailure(string status, bool partial)
    {
        var snapshot = Snapshot() with { Coverage = new[] { new CoverageEntry("scope", status, "Detail") } };
        var analysis = new AnalysisReport(new[] { new StageAnalysis(1, "test", "1", "hash", Array.Empty<string>(),
            Array.Empty<AnalysisFinding>(), Array.Empty<string>()) }, snapshot.Coverage);
        Assert.Equal(partial, ReportAssembler.HasIncompleteCoverage(snapshot, analysis));
    }

    [Fact]
    public void Assembly_RawDescriptionsAndCountsCannotBeReplacedByAnalysis()
    {
        var snapshot = Snapshot();
        var analysis = new AnalysisReport(new[] { new StageAnalysis(1, "table-analysis-000", "1", "hash",
            new[] { "table:cr_order" }, new[] { new AnalysisFinding("table:cr_order", "field:cr_order:cr_name", "inference", "Likely orders; 900 tables is merely generated prose.") }, Array.Empty<string>()) }, Array.Empty<CoverageEntry>());
        var model = ReportAssembler.Build(snapshot, analysis);
        Assert.Equal(1, model.Summary.TableCount);
        Assert.Equal(1, model.Summary.FieldCount);
        Assert.Equal(1, model.Summary.RelationshipCount);
        Assert.Equal("Original SDK description", Assert.Single(model.Tables).Description);
        Assert.Null(Assert.Single(model.Tables).Purpose);
        Assert.Equal("Raw field text", Assert.Single(model.Fields["cr_order"]).Description);
        Assert.Contains("AI inference:", Assert.Single(model.Summary.KeyObservations));
        Assert.Contains("evidence: field:cr_order:cr_name", Assert.Single(model.Summary.KeyObservations));
        Assert.Contains(model.Evidence, e => e.RawJson.Contains("referencingAttribute"));
        Assert.Contains(model.Coverage, c => c.Contains("analysis-missing"));
    }

    [Fact]
    public void Assembly_MissingAnalysisAndFailedCollectionAreDisclosedInDocx()
    {
        var model = ReportAssembler.Build(Snapshot(), new AnalysisReport(Array.Empty<StageAnalysis>(), Array.Empty<CoverageEntry>()));
        var bytes = DocxBuilder.Build(model);
        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var text = doc.MainDocumentPart!.Document!.InnerText;
        Assert.Contains("No validated analysis was produced", text);
        Assert.Contains("application-users: failed", text);
        Assert.Contains("See collection coverage", text);
        Assert.DoesNotContain("No application users registered", text);
        Assert.DoesNotContain("No third-party ISV components detected", text);
        Assert.Contains("Publisher ownership is unknown", text);
        Assert.Contains("Original SDK description", text);
        Assert.Contains("relationship:cr_order_parent", text);
    }

    [Fact]
    public void Assembly_RejectsReferencesOutsideEvidence()
    {
        var analysis = new AnalysisReport(new[] { new StageAnalysis(1, "tables", "1", "hash", new[] { "table:cr_order" },
            new[] { new AnalysisFinding("table:cr_order", "table:invented", "inference", "Unsupported") }, Array.Empty<string>()) }, Array.Empty<CoverageEntry>());
        Assert.Throws<InvalidDataException>(() => ReportAssembler.Build(Snapshot(), analysis));
    }

    [Fact]
    public async Task ConnectionFailure_ReleasesCredentialsAndDoesNotExposeSdkMessage()
    {
        var task = new GenerationTask("job", new EnvironmentCredentials
        {
            EnvironmentUrl = "https://example.crm.dynamics.com", TenantId = "tenant", ClientId = "client", ClientSecret = "sentinel-secret"
        });
        var service = new DocumentGenerateService(new RejectConnection(), null!, null!, null!, NullLogger<DocumentGenerateService>.Instance);
        var error = await Assert.ThrowsAsync<GenerationFailureException>(() => service.RunAsync(task, CancellationToken.None));
        Assert.Equal(JobFailureCodes.CredentialRejected, error.Code);
        Assert.DoesNotContain("sentinel-secret", error.ToString());
        Assert.Throws<InvalidOperationException>(() => task.Credentials);
    }

    private sealed class RejectConnection : IDataverseConnectionFactory
    {
        public Task<ServiceClient> ConnectAsync(EnvironmentCredentials credentials, CancellationToken cancellationToken = default)
            => throw new DataverseConnectionException("SDK sentinel-secret");
    }

    internal static EvidenceSnapshot Snapshot() => new(1, "fixture", DateTime.UnixEpoch,
        new[]
        {
            Component("organisation", "organisation", null, new { environmentName = "Fixture", environmentUrl = "https://example.invalid", version = "9" }),
            Component("table:cr_order", "table", null, new { logicalName = "cr_order", description = "Original SDK description" }),
            Component("field:cr_order:cr_name", "field", "table:cr_order", new { logicalName = "cr_name", description = "Raw field text" }),
            Component("relationship:cr_order_parent", "relationship", "table:cr_order", new { schemaName = "cr_order_parent", relationshipType = "OneToMany", relatedEntity = "cr_parent", referencedEntity = "cr_parent", referencingEntity = "cr_order", referencingAttribute = "cr_parentid" })
        }, new[] { new CoverageEntry("application-users", "failed", "Read access unavailable.") });
    private static EvidenceComponent Component(string id, string kind, string? parent, object data)
        => new(id, kind, parent, JsonSerializer.SerializeToElement(data));
}
