// NFR-018, NFR-014, NFR-007 — Story 3.0 rate limiter tests
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using DataverseDocAgent.Api.Common;
using Microsoft.AspNetCore.Http;

namespace DataverseDocAgent.Tests;

public class RateLimiterTests
{
    private static FixedWindowRateLimiterOptions BuildOptions(int permitLimit) => new()
    {
        PermitLimit          = permitLimit,
        Window               = TimeSpan.FromSeconds(60),
        QueueLimit           = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment    = true,
    };

    [Fact]
    public void FixedWindowLimiter_RejectsAfterPermitLimitExhausted()
    {
        using var limiter = new FixedWindowRateLimiter(BuildOptions(permitLimit: 3));

        for (var i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire(1);
            Assert.True(lease.IsAcquired, $"request {i + 1} should be permitted");
        }

        using var denied = limiter.AttemptAcquire(1);
        Assert.False(denied.IsAcquired);
    }

    [Fact]
    public async Task RateLimitRejection_WriteAsync_ProducesStructuredErrorBody()
    {
        var ctx = BuildContext();

        await RateLimitRejection.WriteAsync(ctx, retryAfterSeconds: 42);

        Assert.Equal(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<StructuredErrorResponse>(ctx.Response.Body);
        Assert.NotNull(payload);
        Assert.Equal("RATE_LIMIT_EXCEEDED", payload!.Code);
        Assert.True(payload.SafeToRetry);
        Assert.Contains("42", payload.Error);
    }

    [Fact]
    public async Task RateLimitRejection_WriteAsync_SetsRetryAfterHeader()
    {
        var ctx = BuildContext();

        await RateLimitRejection.WriteAsync(ctx, retryAfterSeconds: 17);

        Assert.Equal("17", ctx.Response.Headers["Retry-After"].ToString());
    }

    [Fact]
    public async Task RateLimitRejection_WriteAsync_DoesNotEchoCredentials()
    {
        const string secret   = "super-secret-value";
        const string clientId = "11111111-1111-1111-1111-111111111111";
        const string tenantId = "22222222-2222-2222-2222-222222222222";

        var ctx = BuildContext(
            $$"""{"clientSecret":"{{secret}}","clientId":"{{clientId}}","tenantId":"{{tenantId}}"}""");

        await RateLimitRejection.WriteAsync(ctx, retryAfterSeconds: 60);

        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        Assert.DoesNotContain(secret,   responseBody);
        Assert.DoesNotContain(clientId, responseBody);
        Assert.DoesNotContain(tenantId, responseBody);

        Assert.Equal(0, ctx.Request.Body.Position);
    }

    [Fact]
    public void PartitionedLimiter_IsolatesByRemoteIpAddress()
    {
        using var partitioned = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => BuildOptions(permitLimit: 2)));

        var ctxA = BuildContext(remoteIp: IPAddress.Parse("10.0.0.1"));
        var ctxB = BuildContext(remoteIp: IPAddress.Parse("10.0.0.2"));

        using (var a1 = partitioned.AttemptAcquire(ctxA, 1)) Assert.True(a1.IsAcquired);
        using (var a2 = partitioned.AttemptAcquire(ctxA, 1)) Assert.True(a2.IsAcquired);
        using (var a3 = partitioned.AttemptAcquire(ctxA, 1)) Assert.False(a3.IsAcquired);

        using var b1 = partitioned.AttemptAcquire(ctxB, 1);
        Assert.True(b1.IsAcquired);
    }

    private static HttpContext BuildContext(string? jsonBody = null, IPAddress? remoteIp = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method      = "POST";
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body        = jsonBody is null
            ? new MemoryStream()
            : new MemoryStream(Encoding.UTF8.GetBytes(jsonBody));
        ctx.Response.Body       = new MemoryStream();
        ctx.Connection.RemoteIpAddress = remoteIp;
        return ctx;
    }
}
