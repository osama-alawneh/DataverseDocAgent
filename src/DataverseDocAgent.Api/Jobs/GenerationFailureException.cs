// F-036, NFR-014 — Typed failure carrier between pipeline and background service (Story 3.5)
namespace DataverseDocAgent.Api.Jobs;

/// <summary>
/// Thrown by <c>DocumentGenerateService</c> when a step fails in a way the API
/// contract describes with a stable <see cref="Code"/>. The background service
/// translates it into a Failed <see cref="JobRecord"/> with the matching code
/// and <see cref="SafeToRetry"/> flag. The message is never echoed verbatim
/// back to the client (NFR-007) — only the code/flag are surfaced.
/// </summary>
public sealed class GenerationFailureException : Exception
{
    public string Code        { get; }
    public bool   SafeToRetry { get; }

    public GenerationFailureException(string code, bool safeToRetry, string? message = null, Exception? inner = null)
        : base(message ?? code, inner)
    {
        Code        = code;
        SafeToRetry = safeToRetry;
    }
}
