// F-029, F-030, F-031 — FR-029, FR-039
// Permission pre-flight checker endpoint
using Microsoft.AspNetCore.Mvc;

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
    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] SecurityCheckRequest request)
    {
        // [ApiController] validates [Required] annotations before reaching here.
        // InvalidModelStateResponseFactory in Program.cs maps failures to StructuredErrorResponse.
        var response = await _service.CheckAsync(request, HttpContext.RequestAborted);
        return Ok(response);
    }
}
