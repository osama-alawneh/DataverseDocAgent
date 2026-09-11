using System.Text.Json;
using System.Text.Json.Nodes;
using DataverseDocAgent.Api.Pipeline;

namespace DataverseDocAgent.Tests;

public sealed class AnalysisPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "analysis-pipeline-tests-" + Guid.NewGuid().ToString("N"));
    private readonly PipelineOptions _options;
    private readonly string _run;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AnalysisPipelineTests()
    {
        Directory.CreateDirectory(_root);
        _run = Path.Combine(_root, "run");
        var repo = FindRepository();
        var skills = Path.Combine(_root, "skills");
        foreach (var source in Directory.EnumerateFiles(Path.Combine(repo, ".agents", "skills"), "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(skills, Path.GetRelativePath(Path.Combine(repo, ".agents", "skills"), source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        _options = new PipelineOptions { SkillsRoot = skills, BatchSize = 2 };
    }

    private static EvidenceSnapshot Snapshot(string runId = "run-one") => new(1, runId, new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc),
        [new("organisation", "organisation", null, JsonSerializer.SerializeToElement(new { environmentName = "Fixture" })),
         new("table:new_case", "table", null, JsonSerializer.SerializeToElement(new { logicalName = "new_case", displayName = "Case" })),
         new("field:new_case:new_name", "field", "table:new_case", JsonSerializer.SerializeToElement(new { logicalName = "new_name", attributeType = "String" }))],
        [new("plugins", "excluded", "Not extracted.")]);

    [Fact]
    public async Task Successful_stages_cover_exact_targets_and_resume_without_runner_calls()
    {
        var runner = new FakeRunner();
        var pipeline = new AnalysisPipeline(runner, _options);
        var report = await pipeline.RunAsync(Snapshot(), _run);
        Assert.Equal(3, report.Stages.Count);
        Assert.Contains(report.Coverage, c => c.Scope == "plugins" && c.Status == "excluded");
        var synthesis = report.Stages.Where(s => s.StageId.StartsWith("synthesis-")).SelectMany(s => s.CoveredComponentIds).ToArray();
        Assert.Equal(new[] { "field:new_case:new_name", "organisation", "table:new_case" }, synthesis.Order().ToArray());
        var calls = runner.Calls;
        var resumed = await pipeline.RunAsync(Snapshot(), _run);
        Assert.Equal(calls, runner.Calls);
        Assert.Equal(report.Stages.Count, resumed.Stages.Count);
        Assert.True(File.Exists(Path.Combine(_run, "analysis-report.json")));
    }

    [Theory]
    [InlineData("omission")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    [InlineData("hash")]
    [InlineData("version")]
    [InlineData("reference")]
    [InlineData("extra")]
    [InlineData("no-finding")]
    public async Task Invalid_output_is_retried_once_and_never_published(string mutation)
    {
        var runner = new FakeRunner { Mutation = mutation };
        var report = await new AnalysisPipeline(runner, _options).RunAsync(Snapshot(), _run);
        Assert.Empty(report.Stages);
        Assert.Equal(6, runner.Calls);
        Assert.Empty(Directory.EnumerateFiles(_run, "checkpoint.json", SearchOption.AllDirectories));
        Assert.Contains(report.Coverage, c => c.Scope.StartsWith("analysis:") && c.Status == "failed");
    }

    [Fact]
    public async Task Corrupt_checkpoint_forces_reanalysis_and_valid_dependency_invalidation()
    {
        var runner = new FakeRunner();
        var pipeline = new AnalysisPipeline(runner, _options);
        await pipeline.RunAsync(Snapshot(), _run);
        var checkpoint = Directory.EnumerateFiles(_run, "checkpoint.json", SearchOption.AllDirectories).First();
        await File.WriteAllTextAsync(checkpoint, "{broken");
        var calls = runner.Calls;
        await pipeline.RunAsync(Snapshot(), _run);
        Assert.True(runner.Calls > calls);
    }

    [Theory]
    [InlineData("reference.md")]
    [InlineData("SKILL.md")]
    [InlineData("output-schema.json")]
    public async Task Changed_skill_artifact_invalidates_checkpoint(string artifact)
    {
        var runner = new FakeRunner();
        var pipeline = new AnalysisPipeline(runner, _options);
        await pipeline.RunAsync(Snapshot(), _run);
        var path = Path.Combine(_options.SkillsRoot, "dataverse-table-analysis", artifact);
        await File.AppendAllTextAsync(path, "\n ");
        var calls = runner.Calls;
        await pipeline.RunAsync(Snapshot(), _run);
        Assert.True(runner.Calls > calls);
    }

    [Fact]
    public async Task Changed_run_identity_invalidates_all_checkpoints()
    {
        var runner = new FakeRunner();
        var pipeline = new AnalysisPipeline(runner, _options);
        await pipeline.RunAsync(Snapshot(), _run);
        var calls = runner.Calls;
        await pipeline.RunAsync(Snapshot("another-run"), _run);
        Assert.Equal(calls * 2, runner.Calls);
    }

    [Fact]
    public async Task Fatal_auth_failure_is_attempted_once_and_disclosed_for_remaining_stages()
    {
        var runner = new FakeRunner { Failure = new AnalysisRunException("login_required", "safe") };
        var report = await new AnalysisPipeline(runner, _options).RunAsync(Snapshot(), _run);
        Assert.Equal(1, runner.Calls);
        Assert.Empty(report.Stages);
        Assert.Contains(report.Coverage, c => c.Detail.Contains("login_required"));
    }

    [Fact]
    public async Task Cancellation_publishes_no_checkpoint_or_report()
    {
        using var cancel = new CancellationTokenSource();
        var runner = new FakeRunner { BeforeReturn = () => cancel.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AnalysisPipeline(runner, _options).RunAsync(Snapshot(), _run, cancel.Token));
        Assert.Empty(Directory.EnumerateFiles(_run, "checkpoint.json", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(_run, "analysis-report.json")));
    }

    [Fact]
    public async Task Oversized_evidence_is_disclosed_and_no_prompt_exceeds_bound()
    {
        var snapshot = Snapshot();
        snapshot = snapshot with { Components = snapshot.Components.Select(c => c.Kind == "table"
            ? c with { Data = JsonSerializer.SerializeToElement(new { logicalName = "new_case", description = new string('x', 30000) }) } : c).ToArray() };
        var runner = new FakeRunner();
        var report = await new AnalysisPipeline(runner, _options).RunAsync(snapshot, _run);
        Assert.All(runner.Prompts, p => Assert.True(p.Length <= _options.MaxInputCharacters));
        Assert.Contains(report.Coverage, c => c.Detail.Contains("input_limit"));
    }

    [Fact]
    public async Task Large_environment_coverage_does_not_prevent_bounded_analysis_of_small_targets()
    {
        var tables = Enumerable.Range(0, 150).Select(i => new EvidenceComponent("table:new_t" + i, "table", null,
            JsonSerializer.SerializeToElement(new { logicalName = "new_t" + i }))).ToArray();
        var coverage = Enumerable.Range(0, 150).SelectMany(i => new[]
        {
            new CoverageEntry("fields:new_t" + i, "complete", "Read records successfully; " + new string('x', 100)),
            new CoverageEntry("relationships:new_t" + i, "complete", "Read records successfully; " + new string('x', 100))
        }).ToArray();
        var snapshot = Snapshot() with { Components = tables, Coverage = coverage };
        var runner = new FakeRunner();
        var report = await new AnalysisPipeline(runner, _options).RunAsync(snapshot, _run);
        Assert.Equal(150, report.Stages.Count);
        Assert.All(runner.Prompts, p => Assert.True(p.Length <= _options.MaxInputCharacters));
        Assert.Equal(300, report.Coverage.Count(c => c.Scope.StartsWith("fields:") || c.Scope.StartsWith("relationships:")));
        Assert.DoesNotContain(report.Coverage, c => c.Detail.Contains("input_limit"));
    }

    [Fact]
    public async Task Prior_finding_citations_are_included_as_raw_synthesis_context()
    {
        var snapshot = Snapshot() with { Components = [Snapshot().Components[0],
            .. Enumerable.Range(0, 3).Select(i => new EvidenceComponent("table:new_t" + i, "table", null,
                JsonSerializer.SerializeToElement(new { logicalName = "new_t" + i })))] };
        // Table analysis groups t0/t1; synthesis groups organisation/t0, so t1's citation
        // must be added as context when the partition boundary shifts.
        var runner = new FakeRunner { CiteOtherTarget = true };
        await new AnalysisPipeline(runner, _options).RunAsync(snapshot, _run);
        foreach (var input in runner.Inputs.Where(i => i["stageId"]!.GetValue<string>().StartsWith("synthesis-")))
        {
            var known = input["targets"]!.AsArray().Concat(input["context"]!.AsArray()).Select(c => c!["id"]!.GetValue<string>()).ToHashSet();
            Assert.All(input["previousFindings"]!.AsArray(), finding => Assert.Contains(finding!["evidenceId"]!.GetValue<string>(), known));
        }
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static string FindRepository()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "DataverseDocAgent.sln"))) return current.FullName;
        throw new InvalidOperationException("Repository root unavailable.");
    }

    private sealed class FakeRunner : IAnalysisRunner
    {
        public int Calls { get; private set; }
        public List<string> Prompts { get; } = [];
        public List<JsonNode> Inputs { get; } = [];
        public bool CiteOtherTarget { get; init; }
        public string? Mutation { get; init; }
        public AnalysisRunException? Failure { get; init; }
        public Action? BeforeReturn { get; init; }
        public async Task<string> RunAsync(AnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Prompts.Add(request.Prompt);
            if (Failure is not null) throw Failure;
            var input = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(request.WorkingDirectory, "input.json"), cancellationToken))!;
            Inputs.Add(input);
            var ids = input["targets"]!.AsArray().Select(t => t!["id"]!.GetValue<string>()).ToArray();
            var citation = CiteOtherTarget ? ids.Last() : null;
            var output = JsonSerializer.SerializeToNode(new StageAnalysis(1, input["stageId"]!.GetValue<string>(),
                input["skillVersion"]!.GetValue<string>(), input["inputHash"]!.GetValue<string>(), ids,
                ids.Select(id => new AnalysisFinding(id, citation ?? id, "inference", "Business purpose is uncertain; metadata alone does not establish intent.")).ToArray(), []), Json)!;
            switch (Mutation)
            {
                case "omission": output["coveredComponentIds"]!.AsArray().RemoveAt(0); break;
                case "unknown": output["coveredComponentIds"]![0] = "unknown"; break;
                case "duplicate": output["coveredComponentIds"]!.AsArray().Add(ids[0]); break;
                case "malformed": return "{broken";
                case "hash": output["inputHash"] = new string('0', 64); break;
                case "version": output["version"] = 42; break;
                case "reference": output["findings"]![0]!["evidenceId"] = "unknown"; break;
                case "extra": output["inventedInventory"] = true; break;
                case "no-finding": output["findings"]!.AsArray().RemoveAt(0); break;
            }
            BeforeReturn?.Invoke();
            return output.ToJsonString();
        }
    }
}
