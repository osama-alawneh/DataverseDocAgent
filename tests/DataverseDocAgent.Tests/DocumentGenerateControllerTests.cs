// F-036, NFR-007, NFR-014 — Story 3.5 POST /api/document/generate controller tests
using System.Threading.Channels;
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Features.DocumentGenerate;
using DataverseDocAgent.Api.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DataverseDocAgent.Tests;

public class DocumentGenerateControllerTests
{
    [Fact]
    public async Task Generate_ValidRequest_EnqueuesTaskAndReturns202WithJobId()
    {
        var store    = new InMemoryJobStore();
        var channel  = Channel.CreateUnbounded<GenerationTask>();
        var controller = new DocumentGenerateController(store, channel);
        var request  = ValidRequest();

        var result = await controller.Generate(request, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, status.StatusCode);
        var payload = Assert.IsType<DocumentGenerateResponse>(status.Value);
        Assert.True(Guid.TryParse(payload.JobId, out _));

        Assert.True(channel.Reader.TryRead(out var task));
        Assert.NotNull(task);
        Assert.Equal(payload.JobId, task!.JobId);
        Assert.Equal(request.EnvironmentUrl, task.Credentials.EnvironmentUrl);
        Assert.Equal(request.ClientSecret,    task.Credentials.ClientSecret);

        // Job was created in the store with the same id.
        Assert.NotNull(store.GetJob(payload.JobId));
    }

    [Theory]
    [InlineData("", "tenant", "client", "secret")]
    [InlineData("https://env", "",       "client", "secret")]
    [InlineData("https://env", "tenant", "",       "secret")]
    [InlineData("https://env", "tenant", "client", "")]
    [InlineData(" ",           "tenant", "client", "secret")] // whitespace-only
    public async Task Generate_MissingCredentialFields_ReturnsBadRequestInvalidRequest(
        string url, string tenant, string client, string secret)
    {
        var store    = new InMemoryJobStore();
        var channel  = Channel.CreateUnbounded<GenerationTask>();
        var controller = new DocumentGenerateController(store, channel);
        var request  = new DocumentGenerateRequest
        {
            EnvironmentUrl = url,
            TenantId       = tenant,
            ClientId       = client,
            ClientSecret   = secret,
        };

        var result = await controller.Generate(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<StructuredErrorResponse>(bad.Value);
        Assert.Equal("INVALID_REQUEST", payload.Code);
        Assert.False(payload.SafeToRetry);

        // No job created, no task enqueued.
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Generate_NullBody_ReturnsBadRequest()
    {
        var store    = new InMemoryJobStore();
        var channel  = Channel.CreateUnbounded<GenerationTask>();
        var controller = new DocumentGenerateController(store, channel);

        var result = await controller.Generate(null, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<StructuredErrorResponse>(bad.Value);
        Assert.Equal("INVALID_REQUEST", payload.Code);
    }

    [Fact]
    public void Generate_DecoratedWithRateLimitingPolicy_CredentialEndpoints()
    {
        // NFR-018 — the action must carry the credential-endpoints policy
        // (Story 3.0 → done gate). Asserted via reflection so a refactor
        // away from [EnableRateLimiting] would fail this test.
        var method = typeof(DocumentGenerateController).GetMethod(nameof(DocumentGenerateController.Generate))!;
        var attr   = (Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute?)
            method.GetCustomAttributes(
                typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute),
                inherit: false).FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal(CredentialEndpointsRateLimitOptions.PolicyName, attr!.PolicyName);
    }

    private static DocumentGenerateRequest ValidRequest() => new()
    {
        EnvironmentUrl = "https://example.crm.dynamics.com",
        TenantId       = "11111111-1111-1111-1111-111111111111",
        ClientId       = "22222222-2222-2222-2222-222222222222",
        ClientSecret   = "super-secret",
    };
}
