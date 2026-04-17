// F-036, NFR-014 — Story 3.1 JobStatusController unit tests
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Features.DocumentGenerate;
using DataverseDocAgent.Api.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace DataverseDocAgent.Tests;

public class JobStatusControllerTests
{
    [Fact]
    public void Get_KnownJob_ReturnsOkWithJobStatusResponse()
    {
        var store = new InMemoryJobStore();
        var id = store.CreateJob();
        store.UpdateStatus(id, JobStatus.Ready, "download-tok", null);
        var controller = new JobStatusController(store);

        var result = controller.Get(id) as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result!.StatusCode);
        var payload = Assert.IsType<JobStatusResponse>(result.Value);
        Assert.Equal(id, payload.JobId);
        Assert.Equal("ready", payload.Status);
        Assert.Equal("download-tok", payload.DownloadToken);
        Assert.Null(payload.Error);
    }

    [Fact]
    public void Get_UnknownJob_ReturnsOk200WithStructuredErrorJobNotFound()
    {
        var store = new InMemoryJobStore();
        var controller = new JobStatusController(store);

        var result = controller.Get("nonexistent-id") as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result!.StatusCode);
        var payload = Assert.IsType<StructuredErrorResponse>(result.Value);
        Assert.Equal("JOB_NOT_FOUND", payload.Code);
        Assert.False(payload.SafeToRetry);
        Assert.Equal("Job not found", payload.Error);
    }

    [Fact]
    public void Get_FailedJob_ExposesSanitizedErrorMessage()
    {
        var store = new InMemoryJobStore();
        var id = store.CreateJob();
        store.UpdateStatus(id, JobStatus.Failed, downloadToken: null,
            errorMessage: "Generation failed. Check server logs for details.");
        var controller = new JobStatusController(store);

        var result = controller.Get(id) as OkObjectResult;
        var payload = Assert.IsType<JobStatusResponse>(result!.Value);

        Assert.Equal("failed", payload.Status);
        Assert.Equal("Generation failed. Check server logs for details.", payload.Error);
        Assert.Null(payload.DownloadToken);
    }
}
