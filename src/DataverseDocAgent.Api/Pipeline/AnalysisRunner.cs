namespace DataverseDocAgent.Api.Pipeline;

public sealed record AnalysisRequest(string SkillName, string Prompt, string WorkingDirectory,
    string SchemaPath, string OutputPath);

public interface IAnalysisRunner
{
    Task<string> RunAsync(AnalysisRequest request, CancellationToken cancellationToken = default);
}

public sealed class CodexOptions
{
    public string Executable { get; set; } = "codex";
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 180;
}

/// <summary>Only fixed, safe diagnostics cross the process boundary. Never attach child stderr.</summary>
public sealed class AnalysisRunException(string code, string message, bool retryable = false)
    : Exception(message)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}
