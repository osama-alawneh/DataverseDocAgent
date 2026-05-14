// F-036 — FR-036 — POST /api/document/generate (Story 3.5)
// NFR-018 — Rate-limited via "credential-endpoints" policy (Story 3.0)
// NFR-007 — Credential bytes never logged on this path.
using System.Threading.Channels;
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Jobs;
using DataverseDocAgent.Shared.Dataverse;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataverseDocAgent.Api.Features.DocumentGenerate;

[ApiController]
public sealed class DocumentGenerateController : ControllerBase
{
    private readonly IJobStore _jobStore;
    private readonly Channel<GenerationTask> _channel;

    public DocumentGenerateController(IJobStore jobStore, Channel<GenerationTask> channel)
    {
        _jobStore = jobStore;
        _channel  = channel;
    }

    // F-036 — FR-036
    // NFR-018 — Action-level rate limit (not controller-level); flips Story 3.0 done gate.
    [HttpPost("/api/document/generate")]
    [EnableRateLimiting(CredentialEndpointsRateLimitOptions.PolicyName)]
    public async Task<IActionResult> Generate(
        [FromBody] DocumentGenerateRequest? request,
        CancellationToken cancellationToken)
    {
        // [ApiController] + InvalidModelStateResponseFactory in Program.cs surfaces
        // [Required]/[StringLength] failures as StructuredErrorResponse before reaching
        // here. Guard against a literal null body (no [Required] on the parameter itself).
        if (request is null)
        {
            return BadRequest(InvalidRequest("Request body is required."));
        }

        // Belt-and-braces — model binding already enforces [Required]; this is for the
        // case where the binder ran but every field is whitespace (StringLength only
        // catches empty strings, not " ").
        if (string.IsNullOrWhiteSpace(request.EnvironmentUrl) ||
            string.IsNullOrWhiteSpace(request.TenantId)       ||
            string.IsNullOrWhiteSpace(request.ClientId)       ||
            string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return BadRequest(InvalidRequest("One or more required credential fields are missing."));
        }

        // NFR-007 — Credentials reach the background task by reference. They never
        // touch a log scope, request-context bag, or framework-built telemetry.
        var credentials = new EnvironmentCredentials
        {
            EnvironmentUrl = request.EnvironmentUrl!,
            TenantId       = request.TenantId!,
            ClientId       = request.ClientId!,
            ClientSecret   = request.ClientSecret!,
        };

        var jobId = _jobStore.CreateJob();

        // WriteAsync respects the channel's bound (when configured). Unbounded today;
        // the bounded-channel decision is tracked in deferred-work.md.
        await _channel.Writer.WriteAsync(
            new GenerationTask(jobId, credentials),
            cancellationToken);

        return StatusCode(StatusCodes.Status202Accepted,
            new DocumentGenerateResponse { JobId = jobId });
    }

    private static StructuredErrorResponse InvalidRequest(string message) => new()
    {
        Error       = message,
        Code        = "INVALID_REQUEST",
        SafeToRetry = false,
    };
}
