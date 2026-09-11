using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DataverseDocAgent.Api.Pipeline;

public sealed class CodexCliRunner : IAnalysisRunner
{
    private const int CaptureCharacters = 64 * 1024;
    private const int MaximumOutputBytes = 2 * 1024 * 1024;
    private readonly CodexOptions _options;
    private readonly IChildProcessFactory _factory;
    private readonly IReadOnlyDictionary<string, string?>? _environment;

    public CodexCliRunner(CodexOptions options) : this(options, new ChildProcessFactory()) { }

    internal CodexCliRunner(CodexOptions options, IChildProcessFactory factory,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        _options = options;
        _factory = factory;
        _environment = environment;
    }

    public async Task<string> RunAsync(AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        string? temporaryOutput = null;
        try
        {
            var login = BuildStartInfo(request.WorkingDirectory);
            Add(login, "login", "status");
            var status = await ExecuteAsync(login, "", linked.Token);
            var statusText = status.StandardOutput + "\n" + status.StandardError;
            if (ContainsAny(statusText, "api key", "api_key", "api-key"))
                throw Failure("subscription_required");
            if (ContainsAny(statusText, "home directory", "codex_home", "configuration", "config.toml"))
                throw Failure("configuration");
            if (status.ExitCode != 0 || !statusText.Contains("Logged in using ChatGPT", StringComparison.OrdinalIgnoreCase))
                throw Failure("login_required");

            var parent = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath))!;
            Directory.CreateDirectory(parent);
            temporaryOutput = Path.Combine(parent, ".codex-final-" + Guid.NewGuid().ToString("N") + ".json");
            var start = BuildStartInfo(request.WorkingDirectory);
            Add(start, "exec", "--ignore-user-config", "--ignore-rules", "--ephemeral", "--skip-git-repo-check",
                "-s", "read-only", "--json", "--color", "never", "--output-schema", Path.GetFullPath(request.SchemaPath),
                "-o", temporaryOutput, "-C", Path.GetFullPath(request.WorkingDirectory));

            // Verified against `codex exec --help`, `codex features list`, and the official
            // configuration reference (2026-09-11). Do not load profiles or inherited integrations.
            // The scheduler embeds the one selected skill in stdin; no ambient AGENTS is needed.
            foreach (var setting in new[]
            {
                "forced_login_method=\"chatgpt\"", "model_provider=\"openai\"", "approval_policy=\"never\"",
                "project_doc_max_bytes=0", "web_search=\"disabled\"", "mcp_servers={}",
                "agents.enabled=false", "features.multi_agent=false", "features.multi_agent_v2=false",
                "features.hooks=false", "features.plugins=false", "features.remote_plugin=false", "features.apps=false",
                "features.browser_use=false", "features.computer_use=false", "features.image_generation=false",
                "features.shell_tool=false", "features.unified_exec=false", "features.code_mode=false",
                "features.code_mode_host=false", "features.memories=false", "features.goals=false",
                "features.skill_mcp_dependency_install=false", "features.skip_host_skill_discovery=true",
                "features.unbounded_connection_retries=false"
            }) Add(start, "-c", setting);
            // An untrusted working root also suppresses project-local config, hooks, and rules.
            // JSON quoted strings are valid TOML basic strings for normal filesystem paths.
            Add(start, "-c", "projects." + JsonSerializer.Serialize(Path.GetFullPath(request.WorkingDirectory)) + ".trust_level=\"untrusted\"");
            if (!string.IsNullOrWhiteSpace(_options.Model)) Add(start, "-m", _options.Model);
            Add(start, "-");
            var result = await ExecuteAsync(start, request.Prompt, linked.Token);
            if (result.ExitCode != 0) throw Classify(result.StandardOutput + "\n" + result.StandardError);
            linked.Token.ThrowIfCancellationRequested();
            return await ReadFinalAsync(temporaryOutput, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw Failure("timeout", retryable: true);
        }
        catch (Win32Exception ex)
        {
            throw Failure(ex.NativeErrorCode is 2 or 3 ? "executable_missing" : "process_start");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Failure("local_io");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw Failure("configuration");
        }
        finally
        {
            // Only this invocation's random raw response is removed; published checkpoints are untouched.
            if (temporaryOutput is not null)
                try { File.Delete(temporaryOutput); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void Validate(AnalysisRequest request)
    {
        if (string.IsNullOrWhiteSpace(_options.Executable) || _options.TimeoutSeconds is < 1 or > 3600
            || string.IsNullOrWhiteSpace(request.SkillName) || string.IsNullOrWhiteSpace(request.Prompt)
            || request.Prompt.Length > MaximumOutputBytes || !Directory.Exists(request.WorkingDirectory)
            || !File.Exists(request.SchemaPath) || string.IsNullOrWhiteSpace(request.OutputPath))
            throw Failure("configuration");
    }

    private ProcessStartInfo BuildStartInfo(string workingDirectory)
    {
        var start = new ProcessStartInfo
        {
            FileName = _options.Executable, WorkingDirectory = Path.GetFullPath(workingDirectory),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (_environment is not null)
        {
            start.Environment.Clear();
            foreach (var pair in _environment) start.Environment[pair.Key] = pair.Value;
        }
        foreach (var key in start.Environment.Keys.ToArray())
        {
            // The parent also owns Dataverse credentials and connection strings. Do not pass its
            // application environment to an analysis child. Preserve only OS/runtime necessities,
            // network routing/certificate settings, and the existing subscription login location.
            if (!AllowedEnvironment.Contains(key))
                start.Environment.Remove(key);
        }
        return start;
    }

    private async Task<ProcessResult> ExecuteAsync(ProcessStartInfo start, string stdin, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var child = _factory.Start(start);
        using var streamsCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Begin draining both pipes before writing stdin so neither process can deadlock on a full pipe.
        var stdout = ReadBoundedAsync(child.StandardOutput, streamsCancellation.Token);
        var stderr = ReadBoundedAsync(child.StandardError, streamsCancellation.Token);
        try
        {
            await child.StandardInput.WriteAsync(stdin.AsMemory(), token);
            await child.StandardInput.FlushAsync(token);
            child.StandardInput.Close();
            await child.WaitForExitAsync(token);
            await Task.WhenAll(stdout, stderr).WaitAsync(token);
            return new(child.ExitCode, await stdout, await stderr);
        }
        catch
        {
            streamsCancellation.Cancel();
            try { child.KillTree(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            // Cleanup is bounded independently of the canceled request. Dispose closes remaining pipes.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await child.WaitForExitAsync(cleanup.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr).WaitAsync(cleanup.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(TextReader reader, CancellationToken token)
    {
        var retained = new StringBuilder(CaptureCharacters);
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            retained.Append(buffer, 0, read);
            if (retained.Length > CaptureCharacters) retained.Remove(0, retained.Length - CaptureCharacters);
        }
        return retained.ToString();
    }

    private static async Task<string> ReadFinalAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) throw Failure("output_missing", true);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaximumOutputBytes) throw Failure("output_too_large", true);
        // Bound the read itself, not only a racy file-length check.
        var bytes = new byte[MaximumOutputBytes + 1];
        var count = 0;
        int read;
        while (count < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(count), token)) > 0) count += read;
        if (count > MaximumOutputBytes) throw Failure("output_too_large", true);
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes, 0, count).TrimStart('\uFEFF');
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw Failure("invalid_output", true);
            return text;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        {
            throw Failure("invalid_output", true);
        }
    }

    private static AnalysisRunException Classify(string diagnostic)
    {
        if (ContainsAny(diagnostic, "usage limit", "rate limit", "usage_limit", "rate_limit", "quota exceeded", "insufficient_quota"))
            return Failure("usage_limit");
        if (ContainsAny(diagnostic, "unauthorized", "not logged in", "authentication", "token expired", "401"))
            return Failure("login_required");
        if (ContainsAny(diagnostic, "configuration", "config.toml", "unknown variant", "unexpected argument", "invalid value", "unsupported model", "model not found", "access is denied", "permission denied"))
            return Failure("configuration");
        if (ContainsAny(diagnostic, "connection reset", "connection refused", "timed out", "stream disconnected", "temporarily unavailable"))
            return Failure("transport", true);
        return Failure("process_exit");
    }

    private static bool ContainsAny(string value, params string[] fragments)
        => fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static AnalysisRunException Failure(string code, bool retryable = false) => new(code, code switch
    {
        "executable_missing" => "Codex CLI was not found. Configure Codex:Executable or install it on PATH.",
        "subscription_required" => "Codex must use an existing ChatGPT subscription login; API-key authentication is disabled.",
        "login_required" => "Codex ChatGPT login is unavailable or expired. Run codex login using the same operating-system account.",
        "usage_limit" => "Codex usage is currently limited. Resume after the account limit resets.",
        "configuration" => "Codex configuration is invalid or unsupported. Check the installed CLI version, paths, and login home.",
        "timeout" => "Codex analysis exceeded its configured time limit.",
        "transport" => "Codex could not complete the request because its connection failed.",
        "output_missing" => "Codex did not produce a final response file.",
        "invalid_output" => "Codex final response was not a valid JSON object.",
        "output_too_large" => "Codex final response exceeded the allowed size.",
        "local_io" => "Codex analysis could not access a required local file.",
        "process_start" => "Codex CLI could not be started.",
        _ => "Codex analysis exited unsuccessfully. No checkpoint was published."
    }, retryable);

    private static void Add(ProcessStartInfo start, params string[] arguments)
    {
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static readonly HashSet<string> AllowedEnvironment = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "PATHEXT", "SYSTEMROOT", "WINDIR", "COMSPEC", "TEMP", "TMP", "TMPDIR",
        "USERPROFILE", "HOME", "HOMEDRIVE", "HOMEPATH", "LOCALAPPDATA", "APPDATA", "PROGRAMDATA",
        "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432", "COMMONPROGRAMFILES", "OS",
        "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS", "LANG", "TZ", "TERM", "NO_COLOR",
        "LC_ALL", "LC_CTYPE", "LC_MESSAGES", "LC_TIME", "LC_NUMERIC", "LC_COLLATE", "LC_MONETARY",
        "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "SSL_CERT_FILE", "SSL_CERT_DIR",
        "NODE_EXTRA_CA_CERTS", "CODEX_HOME"
    };
}

internal interface IChildProcessFactory
{
    IChildProcess Start(ProcessStartInfo startInfo);
}

internal interface IChildProcess : IDisposable
{
    TextWriter StandardInput { get; }
    TextReader StandardOutput { get; }
    TextReader StandardError { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void KillTree();
}

internal sealed class ChildProcessFactory : IChildProcessFactory
{
    public IChildProcess Start(ProcessStartInfo startInfo)
        => new ChildProcess(Process.Start(startInfo) ?? throw new AnalysisRunException("process_start", "Codex CLI could not be started."));
}

internal sealed class ChildProcess(Process process) : IChildProcess
{
    public TextWriter StandardInput => process.StandardInput;
    public TextReader StandardOutput => process.StandardOutput;
    public TextReader StandardError => process.StandardError;
    public int ExitCode => process.ExitCode;
    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
    public void KillTree() { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    public void Dispose() => process.Dispose();
}
