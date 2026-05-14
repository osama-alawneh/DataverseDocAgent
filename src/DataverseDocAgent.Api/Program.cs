// DataverseDocAgent.Api — Production-ready API host (Story 2.1, 2.2, 3.0, 3.1)
// NFR-009 (HTTPS), NFR-014 (error handling), NFR-007 (logging), NFR-006 (health),
// NFR-018 (rate limiting), F-036 (async jobs)

using System.Threading.Channels;
using System.Threading.RateLimiting;
using Anthropic.SDK;
using DataverseDocAgent.Api.Agent;
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Features.DocumentGenerate;
using DataverseDocAgent.Api.Features.SecurityCheck;
using DataverseDocAgent.Shared.Dataverse;
using DataverseDocAgent.Api.Jobs;
using DataverseDocAgent.Api.Middleware;
using DataverseDocAgent.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Serilog configuration — reads MinimumLevel from appsettings (Serilog:MinimumLevel)
// NFR-007 — Credential logging prohibition
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    // NFR-007 — third-party SDKs can emit authority URLs, tenant IDs, or request
    // payloads at Info/Debug. Clamp to Warning so privacy-policy credential claim
    // holds even in Development (MinimumLevel=Debug).
    .MinimumLevel.Override("Microsoft.PowerPlatform.Dataverse.Client", LogEventLevel.Warning)
    .MinimumLevel.Override("Anthropic", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Destructure.With<CredentialDestructuringPolicy>()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

// Add services to the container
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // NFR-014 — Model validation failures return StructuredErrorResponse, not ProblemDetails
        options.InvalidModelStateResponseFactory = context =>
        {
            var firstError = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .FirstOrDefault()?.ErrorMessage
                ?? "One or more required fields are missing or invalid.";

            return new BadRequestObjectResult(new StructuredErrorResponse
            {
                Error       = firstError,
                Code        = "INVALID_REQUEST",
                SafeToRetry = false,
            });
        };
    });
builder.Services.AddEndpointsApiExplorer();

// F-029, F-030, F-031 — Permission checker services (Story 2.2).
// IDataverseConnectionFactory is Singleton because the implementation is
// stateless (no instance fields; constructs a fresh ServiceClient per
// ConnectAsync call). Singleton lifetime is required so the Singleton
// `IGenerationPipeline` / hosted `GenerationBackgroundService` can consume
// it without tripping the .NET 8 ValidateScopes check at startup
// (Scoped → Singleton capture is the classic captive-dependency bug).
// `SecurityCheckService` stays Scoped — it has request-scoped logger state
// and consumes the now-Singleton factory cleanly (Scoped → Singleton is OK).
builder.Services.AddSingleton<IDataverseConnectionFactory, DataverseConnectionFactory>();
builder.Services.AddScoped<SecurityCheckService>();

// F-036 — Async job infrastructure (Story 3.1).
// Channel and IJobStore are singletons so the HTTP controller (producer) and the
// background service (consumer) share the same instance for the lifetime of the process.
// IGenerationPipeline is the stub in Phase 2; Story 3.5 swaps this registration.
builder.Services.AddSingleton<IJobStore, InMemoryJobStore>();
// Factory form so the channel is created by the DI container once the host is built,
// not captured at module-load time. Prevents a stale, Complete()'d channel surviving
// across WebApplicationFactory instances in tests.
builder.Services.AddSingleton(_ => Channel.CreateUnbounded<GenerationTask>());
// Story 3.5 — DocumentGenerateService is the real pipeline; replaces StubGenerationPipeline.
builder.Services.AddSingleton<IGenerationPipeline, DocumentGenerateService>();
builder.Services.AddHostedService<GenerationBackgroundService>();

// Story 3.5 — Anthropic client + orchestrator factory.
// AnthropicClient is registered lazily so a missing key does not block startup —
// the failure surfaces on first generation request as AI_ERROR, where it can be
// observed and retried, rather than as a fatal host startup crash that blocks
// the security-check endpoint (which has no Anthropic dependency).
//
// E2E hotfix 2026-05-14 (R-HF-6) — the SDK's default HttpClient has
// `Timeout = 100s`. Mode 1 final-iteration completions on real environments
// routinely exceed that (one call observed at 123.7s on user's env →
// AI_ERROR inner=TaskCanceledException). Inject a longer-timeout HttpClient
// so per-Anthropic-call timeout fits within the NFR-001 envelope (Large = 10
// min total Mode 1 budget). The outer per-task token in
// GenerationBackgroundService still bounds wall-clock for the whole job.
builder.Services.AddSingleton<AnthropicClient>(sp =>
{
    var apiKey = sp.GetRequiredService<IConfiguration>()["Anthropic:ApiKey"] ?? string.Empty;
    var http   = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    return new AnthropicClient(new Anthropic.SDK.APIAuthentication(apiKey), http, null!);
});
builder.Services.AddSingleton<Func<AgentOrchestrator>>(sp => () =>
    new AgentOrchestrator(
        sp.GetRequiredService<AnthropicClient>(),
        maxIterations: AgentOrchestrator.Mode1MaxIterations));

// F-040, NFR-013 — Document store (Story 3.2, Phase 1).
// IMemoryCache is itself a singleton; the store holds a reference to it, so the
// store must also be a singleton. AddScoped here would silently leak per-request
// instances while appearing to work because IMemoryCache state is still shared.
// Phase 2+: swap to BlobDocumentStore here — no other code changes required (AC-3).
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();

// NFR-018, NFR-014, NFR-007 — Rate limiting on credential-accepting endpoints (Story 3.0)
// Fail-fast config binding: ValidateOnStart surfaces out-of-range PermitLimit/WindowSeconds
// at startup rather than on the first rejection (else FixedWindowRateLimiter throws at 500).
builder.Services.AddOptions<CredentialEndpointsRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(CredentialEndpointsRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(CredentialEndpointsRateLimitOptions.PolicyName, httpContext =>
    {
        var opts = httpContext.RequestServices
            .GetRequiredService<IOptions<CredentialEndpointsRateLimitOptions>>().Value;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = opts.PermitLimit,
                Window               = TimeSpan.FromSeconds(opts.WindowSeconds),
                QueueLimit           = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment    = true,
            });
    });

    options.OnRejected = async (context, ct) =>
    {
        // Lease ownership transfers here per RateLimiter contract — dispose so metadata buffers
        // are released even if the current limiter swaps to one with non-trivial lease state.
        using var lease = context.Lease;

        // Fallback to operator-configured window (not a magic 60) so short/long window tunings
        // produce coherent client retry behaviour when the lease yields no metadata.
        var opts = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<CredentialEndpointsRateLimitOptions>>().Value;
        var rawSeconds = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait)
            ? (int)wait.TotalSeconds
            : opts.WindowSeconds;
        var retryAfter = Math.Max(1, rawSeconds);

        await RateLimitRejection.WriteAsync(context.HttpContext, retryAfter, ct);
    };
});

var app = builder.Build();

// NFR-014 — Exception handling middleware registered FIRST (outermost)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// NFR-009 — HTTPS enforcement
app.UseHttpsRedirection();

app.UseRouting();

// NFR-018 — Must sit between UseRouting (endpoint metadata bound) and MapControllers
// so [EnableRateLimiting("credential-endpoints")] is resolved on the matched endpoint.
app.UseRateLimiter();

app.MapControllers();

// NFR-006 — Health endpoint for uptime measurement (no auth required)
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// Story 3.5 code-review P5 — surface a missing Anthropic key at startup so
// generation requests don't silently end as AI_ERROR forever. The DI singleton
// itself is registered lazily; this is observability only.
var anthropicKey = builder.Configuration["Anthropic:ApiKey"];
if (string.IsNullOrWhiteSpace(anthropicKey))
{
    Log.Warning(
        "Anthropic:ApiKey is not configured — all /api/document/generate requests will fail with AI_ERROR. " +
        "Set it via dotnet user-secrets or appsettings.{Environment}.json.");
}

// Log startup confirmation
Log.Information("DataverseDocAgent.Api started successfully");

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
