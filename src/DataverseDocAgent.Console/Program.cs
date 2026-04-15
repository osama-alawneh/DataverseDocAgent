// F-001–013 — Phase 1 POC console host
// Story 1.3: Claude agent tool-use loop — ListCustomTables POC
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
Microsoft.PowerPlatform.Dataverse.Client.ServiceClient serviceClient;
try
{
    serviceClient = await factory.ConnectAsync(credentials);
    Console.WriteLine("Connected.");
}
catch (DataverseConnectionException ex)
{
    Console.WriteLine($"Connection failed: {ex.Message}");
    return;
}

// ── Set up tools and orchestrator ─────────────────────────────────────────────
var listTablesTool = new ListCustomTablesTool(serviceClient);
IReadOnlyList<IDataverseTool> tools = [listTablesTool];

var anthropicClient = new AnthropicClient(anthropicApiKey);
var orchestrator    = new AgentOrchestrator(anthropicClient);

const string Prompt =
    "You are a Dataverse environment analyst. " +
    "Use the available tools to list all custom tables in the environment and provide a summary.";

// ── Run agent loop and print result ───────────────────────────────────────────
Console.WriteLine("\nRunning Claude agent loop...\n");
try
{
    var result = await orchestrator.RunAsync(Prompt, tools, credentials);
    Console.WriteLine("── Claude's response ──────────────────────────────────────");
    Console.WriteLine(result);
    Console.WriteLine("───────────────────────────────────────────────────────────");
}
catch (Exception ex)
{
    Console.WriteLine($"Agent loop failed: {ex.Message}");
}
