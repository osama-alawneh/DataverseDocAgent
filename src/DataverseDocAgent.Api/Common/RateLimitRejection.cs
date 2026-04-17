// NFR-018, NFR-014, NFR-007 — Structured 429 writer for rate-limit rejections
using System.Net.Mime;

namespace DataverseDocAgent.Api.Common;

/// <summary>
/// Writes a <see cref="StructuredErrorResponse"/> to the HTTP response on rate-limit rejection.
/// Does not read the incoming request body — prevents credential material from echoing back
/// into response or Serilog sinks (AC-9 of story 3.0).
/// </summary>
public static class RateLimitRejection
{
    public const string ErrorCode = "RATE_LIMIT_EXCEEDED";

    public static async Task WriteAsync(HttpContext context, int retryAfterSeconds, CancellationToken ct = default)
    {
        // Mirror ExceptionHandlingMiddleware's guard: if the response has already started,
        // header/status mutation throws InvalidOperationException and the client sees a split
        // response. Silently skip — the client's in-flight body is already committed.
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = MediaTypeNames.Application.Json;

        await context.Response.WriteAsJsonAsync(new StructuredErrorResponse
        {
            Error = $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds.",
            Code = ErrorCode,
            SafeToRetry = true,
        }, ct);
    }
}
