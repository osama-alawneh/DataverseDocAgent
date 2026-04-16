// NFR-014 — Global exception handling middleware
using DataverseDocAgent.Api.Common;
using System.Net.Mime;

namespace DataverseDocAgent.Api.Middleware;

/// <summary>
/// Catches all unhandled exceptions and returns structured error responses.
/// Prevents stack trace and internal details leakage.
/// Must be registered first in the middleware pipeline.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(ex, "Exception after response started — cannot write error response");
                throw;
            }

            _logger.LogError(ex, "Unhandled exception occurred during request processing");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = MediaTypeNames.Application.Json;

        var response = new StructuredErrorResponse
        {
            Error = "An internal server error occurred. Please try again later.",
            Code = "INTERNAL_ERROR",
            SafeToRetry = false
        };

        return context.Response.WriteAsJsonAsync(response);
    }
}
