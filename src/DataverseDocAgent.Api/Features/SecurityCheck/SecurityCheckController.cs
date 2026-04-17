// F-029, F-030, F-031 — FR-029, FR-039
// NFR-018 — Credential-accepting endpoint — rate limited via "credential-endpoints" policy (Story 3.0)
// Permission pre-flight checker endpoint
using DataverseDocAgent.Api.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DataverseDocAgent.Api.Features.SecurityCheck;

[ApiController]
[Route("api/security")]
public sealed class SecurityCheckController : ControllerBase
{
    private readonly SecurityCheckService _service;

    public SecurityCheckController(SecurityCheckService service)
    {
        _service = service;
    }

    /// <summary>
    /// Verifies that the supplied service account holds exactly the required Dataverse privileges.
    /// Always returns HTTP 200 — semantic failures are encoded in the response body (AC-5).
    /// </summary>
    // F-029, F-030, F-031 — FR-029, FR-039
    // NFR-018 — Action-level rate limit so future non-credential actions on this controller don't inherit it.
    [HttpPost("check")]
    [EnableRateLimiting(CredentialEndpointsRateLimitOptions.PolicyName)]
    public async Task<IActionResult> Check([FromBody] SecurityCheckRequest request)
    {
        // [ApiController] validates [Required] annotations before reaching here.
        // InvalidModelStateResponseFactory in Program.cs maps failures to StructuredErrorResponse.
        var response = await _service.CheckAsync(request, HttpContext.RequestAborted);
        return Ok(response);
    }
}
