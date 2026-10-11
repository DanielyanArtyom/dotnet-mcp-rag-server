using System.ComponentModel;

namespace Lumen.Eval.Agents;

public class ClaudeCliAgentClient : IAgentClient
{
    // The user's own Claude Code hooks (e.g. plugin SessionStart hooks) would inject context into every run, and the
    // user-level CLAUDE.md overrides --append-system-prompt (a "respond in Russian" rule broke number scoring in a real run).
    private const string IsolatedSettings = """{"disableAllHooks":true}""";
    private const string ProjectSettingsOnly = "project";
    private const int StderrTailLength = 500;

    private readonly AgentSettings _settings;
    private readonly string _mcpUrl;

    public ClaudeCliAgentClient(AgentSettings settings, string mcpUrl)
    {
        _settings = settings;
        _mcpUrl = mcpUrl;
    }

    public string ConfiguredModel => _settings.Model;

    public async Task<string> GetVersionAsync(CancellationToken ct)
    {
        var (exitCode, stdout, _) = await RunAsync(["--version"], Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(30), ct);
        return exitCode == 0 ? stdout.Trim() : throw new InvalidOperationException($"'{_settings.ClaudePath} --version' failed — is Claude Code installed?");
    }

    public async Task<AgentTranscript> AskAsync(AgentRequest request, CancellationToken ct)
    {
        // An empty working directory keeps any project CLAUDE.md out of the agent's context, and the config file holds a
        // bearer token, so the whole directory is deleted however the run ends.
        var workDir = Directory.CreateTempSubdirectory("lumen-agent-").FullName;
        try
        {
            var config = McpConfigWriter.Write(workDir, _mcpUrl, request.Token);
            string[] args =
            [
                "-p", request.Question,
                "--output-format", "stream-json", "--verbose",
                "--mcp-config", config, "--strict-mcp-config",
                "--tools", "",
                "--allowedTools", "mcp__lumen__*",
                "--model", _settings.Model,
                "--append-system-prompt", AgentPromptConstants.Rules,
                "--settings", IsolatedSettings,
                "--setting-sources", ProjectSettingsOnly,
                "--no-session-persistence"
            ];

            var (exitCode, stdout, stderr) = await RunAsync(args, workDir, TimeSpan.FromSeconds(_settings.TimeoutSeconds), ct);
            return exitCode is { } code
                ? ReadOutput(code, stdout, stderr, request.Token)
                : ReadTimedOutOutput(stdout);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }

    public static AgentTranscript ReadOutput(int exitCode, string stdout, string stderr, string token)
    {
        AgentTranscript? transcript = null;
        string? parseError = null;
        try
        {
            transcript = StreamJsonParser.Parse(stdout.Split('\n'));
        }
        catch (InvalidOperationException ex)
        {
            parseError = ex.Message;
        }

        if (exitCode == 0 && transcript is not null)
        {
            return transcript;
        }

        var detail = transcript is not null ? $"result: {transcript.Answer}" : parseError;
        var tail = stderr.Length <= StderrTailLength ? stderr : stderr[^StderrTailLength..];
        var message = $"The claude CLI failed (exit code {exitCode}). {detail} stderr: {tail.Trim()}";
        throw new InvalidOperationException(token.Length == 0 ? message : message.Replace(token, "***"));
    }

    // A killed run never emits a result event, but tool results streamed before the timeout still need the isolation check.
    public static AgentTranscript ReadTimedOutOutput(string stdout)
    {
        var transcript = StreamJsonParser.Parse(stdout.Split('\n'), requireResult: false);
        transcript.TimedOut = true;
        return transcript;
    }

    private async Task<(int? ExitCode, string Stdout, string Stderr)> RunAsync(string[] args, string workDir, TimeSpan timeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo(_settings.ClaudePath)
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start '{_settings.ClaudePath}'.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Could not run '{_settings.ClaudePath}': {ex.Message}", ex);
        }

        using (process)
        {
            // Without a closed stdin the CLI waits for piped input before it starts.
            process.StandardInput.Close();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
                return (process.ExitCode, await stdoutTask, await stderrTask);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                if (ct.IsCancellationRequested)
                {
                    throw;
                }

                return (null, await DrainAsync(stdoutTask), await DrainAsync(stderrTask));
            }
        }
    }

    private static async Task<string> DrainAsync(Task<string> read)
    {
        try
        {
            return await read.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            return string.Empty;
        }
    }
}
