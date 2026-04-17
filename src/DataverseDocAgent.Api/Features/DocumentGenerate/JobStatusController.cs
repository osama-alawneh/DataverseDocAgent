// F-036, FR-036, NFR-014 — GET /api/jobs/{jobId} (Story 3.1)
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace DataverseDocAgent.Api.Features.DocumentGenerate;

[ApiController]
public sealed class JobStatusController : ControllerBase
{
    private readonly IJobStore _jobStore;

    public JobStatusController(IJobStore jobStore)
    {
        _jobStore = jobStore;
    }

    /// <summary>
    /// Returns job status. HTTP 200 for both hit and miss — per PRD error contract, unknown
    /// ids are encoded in the body, not via HTTP 404.
    /// </summary>
    [HttpGet("/api/jobs/{jobId}")]
    public IActionResult Get(string jobId)
    {
        var record = _jobStore.GetJob(jobId);
        if (record is null)
        {
            return Ok(new StructuredErrorResponse
            {
                Error = "Job not found",
                Code = "JOB_NOT_FOUND",
                SafeToRetry = false,
            });
        }

        return Ok(new JobStatusResponse
        {
            JobId = record.JobId,
            Status = record.Status.ToString().ToLowerInvariant(),
            DownloadToken = record.DownloadToken,
            Error = record.ErrorMessage,
        });
    }
}
