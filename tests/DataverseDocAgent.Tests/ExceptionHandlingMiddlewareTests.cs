// NFR-007, NFR-014 — Middleware must not leak credentials on exception paths
using System.Text;
using System.Text.Json;
using DataverseDocAgent.Api.Common;
using DataverseDocAgent.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseDocAgent.Tests;

public class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ThrowingDownstream_ReturnsStructuredError()
    {
        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new InvalidOperationException("boom"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance);

        var ctx = BuildHttpContextWithJsonBody(
            """{"environmentUrl":"https://x","clientSecret":"super-secret-value"}""");

        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
        ctx.Response.Body.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<StructuredErrorResponse>(ctx.Response.Body);
        Assert.NotNull(payload);
        Assert.Equal("INTERNAL_ERROR", payload!.Code);
        Assert.False(payload.SafeToRetry);
    }

    [Fact]
    public async Task InvokeAsync_ThrowingDownstream_DoesNotReadRequestBody()
    {
        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new InvalidOperationException("downstream failure"),
            logger: NullLogger<ExceptionHandlingMiddleware>.Instance);

        var ctx = BuildHttpContextWithJsonBody(
            """{"clientSecret":"super-secret-value"}""");
        var initialPosition = ctx.Request.Body.Position;

        await middleware.InvokeAsync(ctx);

        Assert.Equal(initialPosition, ctx.Request.Body.Position);
    }

    [Fact]
    public async Task InvokeAsync_ThrowingDownstream_LogsExceptionWithoutRequestBody()
    {
        var captured = new List<string>();
        var logger = new CapturingLogger(captured);

        var middleware = new ExceptionHandlingMiddleware(
            next: _ => throw new InvalidOperationException("downstream failure"),
            logger: logger);

        var ctx = BuildHttpContextWithJsonBody(
            """{"clientSecret":"super-secret-value","clientId":"11111111-1111-1111-1111-111111111111"}""");

        await middleware.InvokeAsync(ctx);

        var joined = string.Join("\n", captured);
        Assert.DoesNotContain("super-secret-value", joined);
        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", joined);
    }

    private static HttpContext BuildHttpContextWithJsonBody(string json)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private sealed class CapturingLogger : ILogger<ExceptionHandlingMiddleware>
    {
        private readonly List<string> _sink;

        public CapturingLogger(List<string> sink)
        {
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _sink.Add(formatter(state, exception));
            if (exception is not null)
            {
                _sink.Add(exception.ToString());
            }
        }
    }
}
