// F-001–013 — Phase 1 POC console host
// Story 1.3: Claude agent tool-use loop — ListCustomTables POC
// Story 1.4: Timing instrumentation added
using System.Diagnostics;
using Anthropic.SDK;
using DataverseDocAgent.Api.Agent;
using DataverseDocAgent.Api.Agent.Tools;
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Dataverse;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

// ── Load Dataverse credentials ────────────────────────────────────────────────
var credentials = new EnvironmentCredentials
{
    EnvironmentUrl = config["Dataverse:EnvironmentUrl"] ?? string.Empty,
    TenantId       = config["Dataverse:TenantId"]       ?? string.Empty,
    ClientId       = config["Dataverse:ClientId"]       ?? string.Empty,
    ClientSecret   = config["Dataverse:ClientSecret"]   ?? string.Empty,
};

if (string.IsNullOrWhiteSpace(credentials.EnvironmentUrl) ||
    string.IsNullOrWhiteSpace(credentials.TenantId)       ||
    string.IsNullOrWhiteSpace(credentials.ClientId)       ||
    string.IsNullOrWhiteSpace(credentials.ClientSecret))
{
    Console.WriteLine("Missing Dataverse credentials in User Secrets. " +
                      "Run: dotnet user-secrets set \"Dataverse:EnvironmentUrl\" \"<url>\" (etc.)");
    return;
}

// ── Load Anthropic API key ────────────────────────────────────────────────────
var anthropicApiKey = config["Anthropic:ApiKey"] ?? string.Empty;
if (string.IsNullOrWhiteSpace(anthropicApiKey))
{
    Console.WriteLine("Missing Anthropic API key in User Secrets. " +
                      "Run: dotnet user-secrets set \"Anthropic:ApiKey\" \"<key>\"");
    return;
}

// ── Connect to Dataverse ──────────────────────────────────────────────────────
Console.WriteLine("Connecting to Dataverse...");
var factory = new DataverseConnectionFactory();
Microsoft.PowerPlatform.Dataverse.Client.ServiceClient serviceClient = null!;
var connectSw = Stopwatch.StartNew();
try
{
    serviceClient = await factory.ConnectAsync(credentials);
    connectSw.Stop();
    Console.WriteLine($"Connected. [connection time: {connectSw.ElapsedMilliseconds} ms]");
}
catch (DataverseConnectionException ex)
{
    connectSw.Stop();
    Console.WriteLine($"Connection failed: {ex.Message}");
    Environment.Exit(1);
}
finally
{
    connectSw.Stop(); // no-op if already stopped; guards non-DataverseConnectionException escapes
}

bool agentFailed = false;
using (serviceClient)
{
    // ── Set up tools and orchestrator ─────────────────────────────────────────
    var listTablesTool = new ListCustomTablesTool(serviceClient);
    IReadOnlyList<IDataverseTool> tools = [listTablesTool];

    var anthropicClient = new AnthropicClient(anthropicApiKey);
    var orchestrator    = new AgentOrchestrator(anthropicClient);

    const string Prompt =
        "You are a Dataverse environment analyst. " +
        "Use the available tools to list all custom tables in the environment and provide a summary.";

    // ── Run agent loop and print result ───────────────────────────────────────
    Console.WriteLine("\nRunning Claude agent loop...\n");
    var agentSw = Stopwatch.StartNew();
    try
    {
        var result = await orchestrator.RunAsync(Prompt, tools);
        agentSw.Stop();
        Console.WriteLine($"[agent loop time: {agentSw.ElapsedMilliseconds} ms]");
        Console.WriteLine("── Claude's response ──────────────────────────────────────");
        if (string.Equals(result, AgentOrchestrator.MaxIterationsSentinel, StringComparison.Ordinal))
            Console.WriteLine("[WARNING: agent loop hit iteration limit — response may be incomplete]");
        Console.WriteLine(result);
        Console.WriteLine("───────────────────────────────────────────────────────────");
    }
    catch (Exception ex)
    {
        agentSw.Stop();
        Console.WriteLine($"[agent loop time: {agentSw.ElapsedMilliseconds} ms] (failed)");
        Console.WriteLine($"Agent loop failed: {ex.Message}");
        agentFailed = true;
    }
}

if (agentFailed)
    Environment.Exit(1);
