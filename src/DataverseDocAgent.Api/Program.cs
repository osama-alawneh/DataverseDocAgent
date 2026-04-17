// DataverseDocAgent.Api — Production-ready API host (Story 2.1, 2.2, 3.0)
// NFR-009 (HTTPS), NFR-014 (error handling), NFR-007 (logging), NFR-006 (health), NFR-018 (rate limiting)

using System.Threading.RateLimiting;
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Dataverse;
using DataverseDocAgent.Api.Features.SecurityCheck;
using DataverseDocAgent.Api.Middleware;
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

// F-029, F-030, F-031 — Permission checker services (Story 2.2)
builder.Services.AddScoped<IDataverseConnectionFactory, DataverseConnectionFactory>();
builder.Services.AddScoped<SecurityCheckService>();

// NFR-018, NFR-014, NFR-007 — Rate limiting on credential-accepting endpoints (Story 3.0)
builder.Services.Configure<CredentialEndpointsRateLimitOptions>(
    builder.Configuration.GetSection(CredentialEndpointsRateLimitOptions.SectionName));

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
        var retryAfter = context.Lease.TryGetMetadata(
            MetadataName.RetryAfter, out TimeSpan wait) ? (int)wait.TotalSeconds : 60;
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
