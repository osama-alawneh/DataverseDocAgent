using DataverseDocAgent.Api.Agent.Tools;
using DataverseDocAgent.Api.Documents;
using DataverseDocAgent.Api.Features.SecurityCheck;
using DataverseDocAgent.Api.Pipeline;
using DataverseDocAgent.Shared.Dataverse;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (args.Length == 0 || args[0] is "--help" or "-h")
    {
        Console.WriteLine("analyze --snapshot <directory> --run <directory> [--skills <root>] [--codex <executable>] [--model <model>]");
        Console.WriteLine("collect --run <directory> | generate --run <directory> [--skills <root>] [--codex <executable>] [--model <model>]");
        Console.WriteLine("Live collection reads DATAVERSE_ENVIRONMENT_URL, DATAVERSE_TENANT_ID, DATAVERSE_CLIENT_ID, DATAVERSE_CLIENT_SECRET from the environment only.");
        return 0;
    }
    var command = args[0];
    if (command is not ("analyze" or "collect" or "generate")) throw new ArgumentException();
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    var allowed = new HashSet<string> { "--snapshot", "--run", "--skills", "--codex", "--model" };
    for (var i = 1; i < args.Length; i += 2)
    {
        if (!allowed.Contains(args[i]) || i + 1 >= args.Length || !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException();
    }
    if (!values.TryGetValue("--run", out var run) || string.IsNullOrWhiteSpace(run)) throw new ArgumentException();
    run = Path.GetFullPath(run);
    Directory.CreateDirectory(run);
    if ((File.GetAttributes(run) & FileAttributes.ReparsePoint) != 0) throw new IOException();
    // Hold the workflow lock through analysis AND final DOCX publication. The scheduler's
    // separate analysis lock alone ends too early to protect the complete report bundle.
    var workflowLockPath = Path.Combine(run, ".workflow.lock");
    if (File.Exists(workflowLockPath) && (File.GetAttributes(workflowLockPath) & FileAttributes.ReparsePoint) != 0)
        throw new IOException();
    await using var workflowLock = new FileStream(workflowLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    EvidenceSnapshot snapshot;
    if (command == "analyze")
    {
        if (!values.TryGetValue("--snapshot", out var source)) throw new ArgumentException();
        snapshot = await SnapshotStore.LoadAsync(source, cancellation.Token);
    }
    else
    {
        if (values.ContainsKey("--snapshot")) throw new ArgumentException();
        // Keep credentials in the connection helper and release its reference before analysis.
        snapshot = await CollectAsync(cancellation.Token);
        await SnapshotStore.SaveAsync(snapshot, Path.Combine(run, "evidence"), cancellation.Token);
        Console.WriteLine("Evidence snapshot saved.");
        if (command == "collect") return 0;
    }
    var options = new PipelineOptions
    {
        SkillsRoot = Path.GetFullPath(values.GetValueOrDefault("--skills") ?? FindSkills()),
        RunRoot = run
    };
    var runner = new CodexCliRunner(new CodexOptions
    {
        Executable = values.GetValueOrDefault("--codex") ?? "codex",
        Model = values.GetValueOrDefault("--model")
    });
    var analysis = await new AnalysisPipeline(runner, options).RunAsync(snapshot, run, cancellation.Token);
    var document = DocxBuilder.Build(ReportAssembler.Build(snapshot, analysis));
    Directory.CreateDirectory(run);
    var pending = Path.Combine(run, ".report-" + Guid.NewGuid().ToString("N") + ".tmp");
    try
    {
        await File.WriteAllBytesAsync(pending, document, cancellation.Token);
        cancellation.Token.ThrowIfCancellationRequested();
        File.Move(pending, Path.Combine(run, "report.docx"), overwrite: true);
    }
    finally { if (File.Exists(pending)) File.Delete(pending); }
    var partial = ReportAssembler.HasIncompleteCoverage(snapshot, analysis);
    Console.WriteLine(partial
        ? "Partial report saved as report.docx in the run directory. See coverage; rerun analysis to retry missing stages."
        : "Analysis complete for the supported scope. Report saved as report.docx in the run directory.");
    return partial ? 2 : 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Operation cancelled."); return 130;
}
catch (AnalysisRunException ex)
{
    Console.Error.WriteLine($"Local analysis failed: {ex.Code}. Check Codex login/configuration and retry the same run directory."); return 1;
}
catch (ArgumentException)
{
    Console.Error.WriteLine("Invalid command or configuration. Use --help for supported options."); return 2;
}
catch (Exception)
{
    // Raw SDK errors and model output can contain environment data and are never printed.
    Console.Error.WriteLine("Operation failed. Verify snapshot integrity, Dataverse permissions, and local output access."); return 1;
}

static string FindSkills()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
    {
        var path = Path.Combine(dir.FullName, ".agents", "skills");
        if (Directory.Exists(path)) return path;
    }
    throw new ArgumentException("Skills root is required.");
}

static async Task<EvidenceSnapshot> CollectAsync(CancellationToken ct)
{
    EnvironmentCredentials? credentials = new()
    {
        EnvironmentUrl = Environment.GetEnvironmentVariable("DATAVERSE_ENVIRONMENT_URL") ?? "",
        TenantId = Environment.GetEnvironmentVariable("DATAVERSE_TENANT_ID") ?? "",
        ClientId = Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_ID") ?? "",
        ClientSecret = Environment.GetEnvironmentVariable("DATAVERSE_CLIENT_SECRET") ?? ""
    };
    if (new[] { credentials.EnvironmentUrl, credentials.TenantId, credentials.ClientId, credentials.ClientSecret }.Any(string.IsNullOrWhiteSpace))
        throw new ArgumentException();
    var url = credentials.EnvironmentUrl;
    Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client;
    try { client = await new DataverseConnectionFactory().ConnectAsync(credentials, ct); }
    finally { credentials = null; }
    using (client)
    {
        var check = await SecurityCheckService.CheckConnectedAsync(client, ct);
        if (!check.SafeToRun) throw new InvalidOperationException("Required permissions are missing.");
        return await new EvidenceCollector().CollectAsync(DataverseToolFactory.CreateMode1Tools(client, url), ct);
    }
}
