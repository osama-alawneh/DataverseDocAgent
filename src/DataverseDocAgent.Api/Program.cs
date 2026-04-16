// DataverseDocAgent.Api — Production-ready API host (Story 2.1)
// NFR-009 (HTTPS), NFR-014 (error handling), NFR-007 (logging), NFR-006 (health)

using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Middleware;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Serilog configuration — reads MinimumLevel from appsettings (Serilog:MinimumLevel)
// NFR-007 — Credential logging prohibition
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Destructure.With<CredentialDestructuringPolicy>()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// NFR-014 — Exception handling middleware registered FIRST (outermost)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// NFR-009 — HTTPS enforcement
app.UseHttpsRedirection();

app.UseRouting();
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
