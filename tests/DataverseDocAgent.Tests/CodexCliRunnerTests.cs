using System.ComponentModel;
using System.Diagnostics;
using DataverseDocAgent.Api.Pipeline;

namespace DataverseDocAgent.Tests;

public sealed class CodexCliRunnerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "codex-runner-tests-" + Guid.NewGuid().ToString("N"));
    public CodexCliRunnerTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "schema.json"), "{\"type\":\"object\"}");
    }

    private AnalysisRequest Request => new("table-analysis", "Untrusted ; $(echo secret)\n--model other", _directory,
        Path.Combine(_directory, "schema.json"), Path.Combine(_directory, "result.json"));

    [Fact]
    public async Task Uses_stdin_and_safe_arguments_and_returns_only_final_file()
    {
        var factory = new FakeFactory();
        var result = await new CodexCliRunner(new CodexOptions { Model = "name with spaces;literal" }, factory).RunAsync(Request);
        Assert.Equal("{\"business\":true}", result);
        var exec = factory.Starts[1];
        Assert.False(exec.UseShellExecute);
        Assert.True(exec.CreateNoWindow);
        Assert.True(exec.RedirectStandardInput && exec.RedirectStandardOutput && exec.RedirectStandardError);
        Assert.Empty(exec.Arguments);
        Assert.Contains("name with spaces;literal", exec.ArgumentList);
        Assert.DoesNotContain(Request.Prompt, exec.ArgumentList);
        Assert.Equal(Request.Prompt, factory.Children[1].Input.ToString());
        Assert.Contains("--ignore-user-config", exec.ArgumentList);
        Assert.Contains("--ignore-rules", exec.ArgumentList);
        Assert.Contains("read-only", exec.ArgumentList);
        Assert.Contains("forced_login_method=\"chatgpt\"", exec.ArgumentList);
        Assert.Contains("model_provider=\"openai\"", exec.ArgumentList);
        Assert.Contains("features.multi_agent=false", exec.ArgumentList);
        Assert.Contains("features.hooks=false", exec.ArgumentList);
        Assert.Contains("features.shell_tool=false", exec.ArgumentList);
        Assert.Equal("-", exec.ArgumentList.Last());
        Assert.NotEqual(Request.OutputPath, factory.LastMessagePath);
        Assert.False(File.Exists(factory.LastMessagePath));
        Assert.False(File.Exists(Request.OutputPath));
    }

    [Fact]
    public async Task Removes_api_credentials_and_endpoint_overrides_but_preserves_login_home()
    {
        var factory = new FakeFactory();
        // Inject environment at the process construction seam without mutating the test host.
        var environment = new Dictionary<string, string?>
        {
            ["OPENAI_API_KEY"] = "secret", ["CODEX_API_KEY"] = "secret", ["ANTHROPIC_API_KEY"] = "secret",
            ["CUSTOM_API_KEY"] = "secret", ["AZURE_OPENAI_API_KEY"] = "secret", ["OPENAI_BASE_URL"] = "https://other.invalid",
            ["DATAVERSE_CLIENT_SECRET"] = "secret", ["DATAVERSE_CLIENT_ID"] = "private-id", ["AZURE_CLIENT_SECRET"] = "secret",
            ["CONNECTIONSTRINGS__DATAVERSE"] = "secret", ["UNRECOGNIZED_SECRET"] = "secret", ["CODEX_AUTH_JSON"] = "secret", ["LC_SECRET"] = "secret",
            ["CODEX_HOME"] = "existing-auth-home", ["PATH"] = "existing-path"
        };
        await new CodexCliRunner(new CodexOptions(), factory, environment).RunAsync(Request);
        foreach (var start in factory.Starts)
        {
            Assert.DoesNotContain(start.Environment.Keys, name => name.Contains("API_KEY", StringComparison.OrdinalIgnoreCase));
            Assert.False(start.Environment.ContainsKey("OPENAI_BASE_URL"));
            Assert.Equal("existing-auth-home", start.Environment["CODEX_HOME"]);
            Assert.Equal("existing-path", start.Environment["PATH"]);
            Assert.Equal(2, start.Environment.Count);
        }
    }

    [Theory]
    [InlineData("Logged in using an API key", 0, "subscription_required")]
    [InlineData("Not logged in", 1, "login_required")]
    [InlineData("Could not find home directory", 1, "configuration")]
    [InlineData("unexpected status", 0, "login_required")]
    public async Task Rejects_unusable_login_without_starting_analysis(string status, int exit, string expected)
    {
        var factory = new FakeFactory { LoginStatus = status, LoginExit = exit };
        var error = await Assert.ThrowsAsync<AnalysisRunException>(() => new CodexCliRunner(new CodexOptions(), factory).RunAsync(Request));
        Assert.Equal(expected, error.Code);
        Assert.False(error.Retryable);
        Assert.Single(factory.Starts);
    }

    [Fact]
    public async Task Missing_executable_is_safe_nonretryable_error()
    {
        var factory = new FakeFactory { StartError = new Win32Exception(2, "secret path") };
        var error = await Assert.ThrowsAsync<AnalysisRunException>(() => new CodexCliRunner(new CodexOptions(), factory).RunAsync(Request));
        Assert.Equal("executable_missing", error.Code);
        Assert.False(error.Retryable);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData("You have hit your usage limit. secret", "usage_limit", false)]
    [InlineData("401 Unauthorized secret", "login_required", false)]
    [InlineData("invalid configuration secret", "configuration", false)]
    [InlineData("Access is denied. (os error 5) secret", "configuration", false)]
    [InlineData("connection reset secret", "transport", true)]
    [InlineData("unknown secret", "process_exit", false)]
    public async Task Nonzero_exit_is_classified_without_exposing_child_diagnostics(string stderr, string code, bool retryable)
    {
        var factory = new FakeFactory { ExecExit = 1, ExecError = stderr };
        var error = await Assert.ThrowsAsync<AnalysisRunException>(() => new CodexCliRunner(new CodexOptions(), factory).RunAsync(Request));
        Assert.Equal(code, error.Code);
        Assert.Equal(retryable, error.Retryable);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.False(File.Exists(factory.LastMessagePath));
    }

    [Theory]
    [InlineData(null, "output_missing")]
    [InlineData("", "invalid_output")]
    [InlineData("progress then {broken", "invalid_output")]
    [InlineData("null", "invalid_output")]
    public async Task Rejects_missing_or_malformed_last_message_even_with_json_progress(string? output, string expected)
    {
        var factory = new FakeFactory { Output = output };
        var error = await Assert.ThrowsAsync<AnalysisRunException>(() => new CodexCliRunner(new CodexOptions(), factory).RunAsync(Request));
        Assert.Equal(expected, error.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public async Task Caller_cancellation_kills_child_and_never_publishes_output()
    {
        using var cancel = new CancellationTokenSource();
        var factory = new FakeFactory { OnExecWait = () => cancel.Cancel(), BlockExec = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CodexCliRunner(new CodexOptions(), factory).RunAsync(Request, cancel.Token));
        Assert.True(factory.Children[1].Killed);
        Assert.False(File.Exists(factory.LastMessagePath));
    }

    [Fact]
    public async Task Timeout_kills_child_and_is_distinct_from_caller_cancellation()
    {
        var factory = new FakeFactory { BlockExec = true };
        var error = await Assert.ThrowsAsync<AnalysisRunException>(() => new CodexCliRunner(new CodexOptions { TimeoutSeconds = 1 }, factory).RunAsync(Request));
        Assert.Equal("timeout", error.Code);
        Assert.True(factory.Children[1].Killed);
        Assert.False(File.Exists(factory.LastMessagePath));
    }

    [Fact]
    public async Task Large_progress_is_drained_without_becoming_business_output()
    {
        var factory = new FakeFactory { ExecProgress = new string('x', 256_000) };
        Assert.Equal("{\"business\":true}", await new CodexCliRunner(new CodexOptions(), factory).RunAsync(Request));
    }

    [Fact]
    public async Task Rejects_oversized_response_and_removes_the_raw_file()
    {
        var factory = new FakeFactory { Output = "{\"text\":\"" + new string('x', 2 * 1024 * 1024) + "\"}" };
        var error = await Assert.ThrowsAsync<AnalysisRunException>(() => new CodexCliRunner(new CodexOptions(), factory).RunAsync(Request));
        Assert.Equal("output_too_large", error.Code);
        Assert.True(error.Retryable);
        Assert.False(File.Exists(factory.LastMessagePath));
    }

    [Fact]
    public async Task Never_uses_stale_requested_output_when_child_produces_no_file()
    {
        File.WriteAllText(Request.OutputPath, "{\"stale\":true}");
        var factory = new FakeFactory { Output = null };
        var error = await Assert.ThrowsAsync<AnalysisRunException>(() => new CodexCliRunner(new CodexOptions(), factory).RunAsync(Request));
        Assert.Equal("output_missing", error.Code);
        Assert.Equal("{\"stale\":true}", File.ReadAllText(Request.OutputPath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public async Task Invalid_timeout_fails_before_a_child_is_started(int seconds)
    {
        var factory = new FakeFactory();
        var error = await Assert.ThrowsAsync<AnalysisRunException>(() => new CodexCliRunner(new CodexOptions { TimeoutSeconds = seconds }, factory).RunAsync(Request));
        Assert.Equal("configuration", error.Code);
        Assert.Empty(factory.Starts);
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class FakeFactory : IChildProcessFactory
    {
        public List<ProcessStartInfo> Starts { get; } = [];
        public List<FakeChild> Children { get; } = [];
        public string LoginStatus { get; init; } = "Logged in using ChatGPT";
        public int LoginExit { get; init; }
        public int ExecExit { get; init; }
        public string ExecError { get; init; } = "";
        public string ExecProgress { get; init; } = "{\"type\":\"progress\"}";
        public string? Output { get; init; } = "{\"business\":true}";
        public Exception? StartError { get; init; }
        public bool BlockExec { get; init; }
        public Action? OnExecWait { get; init; }
        public string? LastMessagePath { get; private set; }
        public IChildProcess Start(ProcessStartInfo start)
        {
            Starts.Add(start);
            if (StartError is not null) throw StartError;
            var login = start.ArgumentList.Contains("login");
            if (!login)
            {
                LastMessagePath = start.ArgumentList[start.ArgumentList.IndexOf("-o") + 1];
                if (Output is not null) File.WriteAllText(LastMessagePath, Output);
            }
            var child = new FakeChild(login ? "" : ExecProgress, login ? LoginStatus : ExecError,
                login ? LoginExit : ExecExit, !login && BlockExec, login ? null : OnExecWait);
            Children.Add(child);
            return child;
        }
    }

    private sealed class FakeChild(string stdout, string stderr, int exit, bool block, Action? onWait) : IChildProcess
    {
        public StringWriter Input { get; } = new();
        public TextWriter StandardInput => Input;
        public TextReader StandardOutput { get; } = new StringReader(stdout);
        public TextReader StandardError { get; } = new StringReader(stderr);
        public int ExitCode => exit;
        public bool Killed { get; private set; }
        public async Task WaitForExitAsync(CancellationToken token)
        {
            onWait?.Invoke();
            if (block && !Killed) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested();
        }
        public void KillTree() => Killed = true;
        public void Dispose() { }
    }
}
