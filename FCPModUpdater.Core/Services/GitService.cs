using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public partial class GitService(IGitOutputSink? outputSink = null) : IGitService
{
    internal const int StandardCommandTimeoutMs = 30000;
    internal const int NetworkCommandIdleTimeoutMs = 300000;

    /// <summary>Error text reported when the caller cancelled a command (as opposed to it failing).</summary>
    public const string CancelledError = "Operation was cancelled";

    // Matches git progress output like: "Receiving objects:  45% (555/1234), 12.34 MiB | 1.23 MiB/s"
    [GeneratedRegex(@"(\d+)%\s*\((\d+)/(\d+)\)", RegexOptions.Compiled)]
    private static partial Regex GitProgressRegex();

    /// <summary>True when the git executable can be started from PATH.</summary>
    public static async Task<bool> IsGitInstalledAsync()
    {
        try
        {
            BufferedCommandResult result = await Cli.Wrap("git")
                .WithArguments("--version")
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsGitRepositoryAsync(string path, CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunGitCommandAsync(path, "rev-parse --is-inside-work-tree", ct);
        return exitCode == 0;
    }

    public async Task<string?> GetRemoteUrlAsync(string path, CancellationToken ct = default)
    {
        var (exitCode, output, _) = await RunGitCommandAsync(path, "remote get-url origin", ct);
        return exitCode == 0 ? output.Trim() : null;
    }

    public async Task<GitCommitInfo?> GetCurrentCommitAsync(string path, CancellationToken ct = default)
    {
        const string format = "%H%n%h%n%s%n%an%n%aI";
        var (exitCode, output, _) = await RunGitCommandAsync(path, $"log -1 --format=\"{format}\"", ct);

        if (exitCode != 0)
            return null;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 5)
            return null;

        return new GitCommitInfo(
            Hash: lines[0].Trim(),
            ShortHash: lines[1].Trim(),
            Message: lines[2].Trim(),
            Author: lines[3].Trim(),
            Date: DateTimeOffset.Parse(lines[4].Trim())
        );
    }

    public async Task<string?> GetCurrentBranchAsync(string path, CancellationToken ct = default)
    {
        var (exitCode, output, _) = await RunGitCommandAsync(path, "rev-parse --abbrev-ref HEAD", ct);
        if (exitCode != 0)
            return null;

        var branch = output.Trim();
        return branch == "HEAD" ? null : branch; // Detached HEAD
    }

    public async Task<(int Behind, int Ahead)> GetCommitDifferenceAsync(string path, CancellationToken ct = default)
    {
        var branch = await GetCurrentBranchAsync(path, ct);
        if (branch == null)
            return (Behind: 0, Ahead: 0);

        var (exitCode, output, _) = await RunGitCommandAsync(path,
            $"rev-list --left-right --count origin/{branch}...HEAD", ct);

        if (exitCode != 0)
            return (Behind: 0, Ahead: 0);

        var parts = output.Trim().Split('\t');
        if (parts.Length != 2)
            return (Behind: 0, Ahead: 0);

        int.TryParse(parts[0], out var behind);
        int.TryParse(parts[1], out var ahead);

        return (Behind: behind, Ahead: ahead);
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetIncomingCommitsAsync(string path, int limit = 10,
        CancellationToken ct = default)
    {
        var branch = await GetCurrentBranchAsync(path, ct);
        if (branch == null)
            return [];

        const string format = "%H%n%h%n%s%n%an%n%aI%n---";
        var (exitCode, output, _) = await RunGitCommandAsync(path,
            $"log HEAD..origin/{branch} --format=\"{format}\" -n {limit}", ct);

        if (exitCode != 0)
            return [];

        return ParseCommitLog(output);
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetCommitHistoryAsync(string path, int limit = 20,
        CancellationToken ct = default)
    {
        const string format = "%H%n%h%n%s%n%an%n%aI%n---";
        var (exitCode, output, _) = await RunGitCommandAsync(path,
            $"log --format=\"{format}\" -n {limit}", ct);

        if (exitCode != 0)
            return [];

        return ParseCommitLog(output);
    }

    public async Task<GitOperationResult> FetchAsync(string path, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Fetching from remote...");
        var (exitCode, error) = await RunGitCommandWithProgressAsync(
            path,
            "fetch --all --prune --progress --recurse-submodules",
            percentProgress: null,
            ct,
            idleTimeoutMs: NetworkCommandIdleTimeoutMs);

        var result = GitOperationResult.FromExitCode(exitCode, error);

        if (!result.Success)
            progress?.Report($"Fetch failed: {result.Error ?? $"git exited with code {result.ExitCode}"}");

        return result;
    }

    public async Task<GitOperationResult> PullAsync(string path, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Pulling changes...");
        var (exitCode, error) = await RunGitCommandWithProgressAsync(
            path,
            "pull --ff-only --progress --recurse-submodules",
            percentProgress: null,
            ct,
            idleTimeoutMs: NetworkCommandIdleTimeoutMs);

        var result = GitOperationResult.FromExitCode(exitCode, error);

        if (!result.Success)
            progress?.Report($"Pull failed: {result.Error ?? $"git exited with code {result.ExitCode}"}");

        return result;
    }

    public async Task<(bool Success, string? Error)> CloneAsync(string url, string targetPath, IProgress<string>? progress = null,
        IProgress<double>? percentProgress = null, CancellationToken ct = default)
    {
        progress?.Report($"Cloning {url}...");

        var parentDir = Path.GetDirectoryName(targetPath) ?? ".";
        var folderName = Path.GetFileName(targetPath);

        var (exitCode, error) = await RunGitCommandWithProgressAsync(
            parentDir,
            $"clone --progress --recurse-submodules \"{url}\" \"{folderName}\"",
            percentProgress,
            ct,
            idleTimeoutMs: NetworkCommandIdleTimeoutMs,
            logTarget: targetPath);

        var result = GitOperationResult.FromExitCode(exitCode, error);
        if (!result.Success)
        {
            var errorMessage = $"Clone failed for {targetPath}: {FormatGitFailure(result)}";
            progress?.Report(errorMessage);
            return (Success: false, Error: errorMessage);
        }

        // Verify the clone actually produced a valid git repo
        if (!Directory.Exists(targetPath) || !await IsGitRepositoryAsync(targetPath, ct))
        {
            var errorMessage =
                $"Clone appeared to finish but target is not a valid git repo: url={url}, target={targetPath}, git output: {result.Error}";
            progress?.Report(errorMessage);
            return (Success: false, Error: errorMessage);
        }

        return (Success: true, Error: null);
    }

    internal static string FormatGitFailure(GitOperationResult result)
    {
        return result.Error ?? $"git exited with code {result.ExitCode}";
    }

    public async Task<IReadOnlyList<string>> GetRemoteBranchesAsync(string path, CancellationToken ct = default)
    {
        var (exitCode, output, _) = await RunGitCommandAsync(path, "branch -r", ct);

        if (exitCode != 0)
            return [];

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !s.Contains("->")) // Filter out HEAD -> origin/main
            .Select(s => s.StartsWith("origin/") ? s[7..] : s)
            .ToList();
    }

    public async Task<IReadOnlyList<GitBranchInfo>> GetRemoteBranchInfoAsync(string path,
        CancellationToken ct = default)
    {
        var (exitCode, output, _) = await RunGitCommandAsync(path,
            "for-each-ref --sort=-committerdate --format=\"%(refname:short)%09%(committerdate:iso-strict)\" refs/remotes/origin",
            ct);

        return exitCode != 0 ? [] : ParseBranchInfo(output);
    }

    internal static IReadOnlyList<GitBranchInfo> ParseBranchInfo(string output)
    {
        var branches = new List<GitBranchInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('\t');
            var name = parts[0].Trim();

            // "origin" / "origin/HEAD" is the symbolic default-branch pointer, not a branch
            if (name is "origin" or "origin/HEAD" || name.Length == 0)
                continue;

            if (name.StartsWith("origin/"))
                name = name[7..];

            DateTimeOffset? date = parts.Length > 1 && DateTimeOffset.TryParse(parts[1].Trim(), out DateTimeOffset parsed)
                ? parsed
                : null;

            branches.Add(new GitBranchInfo(name, date));
        }

        return branches;
    }

    public async Task<bool> CheckoutAsync(string path, string branchOrCommit, CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunGitCommandAsync(path, $"checkout \"{branchOrCommit}\"", ct);
        if (exitCode != 0)
            return false;

        await RunGitCommandAsync(path, "submodule update --init --recursive", ct);
        return true;
    }

    public async Task<bool> ResetToCommitAsync(string path, string commitHash, CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunGitCommandAsync(path, $"reset --hard \"{commitHash}\"", ct);
        if (exitCode != 0)
            return false;

        await RunGitCommandAsync(path, "submodule update --init --recursive", ct);
        return true;
    }

    public async Task<bool> HasLocalChangesAsync(string path, CancellationToken ct = default)
    {
        var (exitCode, output, _) = await RunGitCommandAsync(path, "status --porcelain", ct);
        return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
    }

    private async Task<(int ExitCode, string Error)> RunGitCommandWithProgressAsync(
        string workingDirectory, string arguments, IProgress<double>? percentProgress,
        CancellationToken ct, int idleTimeoutMs = StandardCommandTimeoutMs, string? logTarget = null)
    {
        logTarget ??= workingDirectory;
        var started = System.Diagnostics.Stopwatch.StartNew();
        outputSink?.CommandStarted(logTarget, arguments);

        var (exitCode, error) = await RunCoreAsync();

        outputSink?.CommandFinished(logTarget, arguments, exitCode, started.Elapsed,
            GitOperationResult.FromExitCode(exitCode, error).Success ? null : error);
        return (exitCode, error);

        async Task<(int ExitCode, string Error)> RunCoreAsync()
        {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(idleTimeoutMs);

        var errorLines = new List<string>();

        try
        {
            var result = await Cli.Wrap("git")
                .WithArguments(arguments)
                .WithWorkingDirectory(workingDirectory)
                .WithEnvironmentVariables(env => env
                    .Set("GIT_TERMINAL_PROMPT", "0"))
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.Null)
                .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
                {
                    cts.CancelAfter(idleTimeoutMs);
                    errorLines.Add(line);
                    outputSink?.OutputLine(logTarget, arguments, line);

                    if (percentProgress == null)
                        return;

                    var match = GitProgressRegex().Match(line);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                    {
                        percentProgress.Report(percent);
                    }
                }))
                .ExecuteAsync(cts.Token);

            return (result.ExitCode, string.Join(Environment.NewLine, errorLines));
        }
        catch (OperationCanceledException)
        {
            return ct.IsCancellationRequested
                ? (ExitCode: -1, Error: CancelledError)
                : (ExitCode: -1,
                    Error: $"Git command made no progress for {TimeSpan.FromMilliseconds(idleTimeoutMs).TotalMinutes:0} minute(s)");
        }
        }
    }

    internal static IReadOnlyList<GitCommitInfo> ParseCommitLog(string output)
    {
        var commits = new List<GitCommitInfo>();
        var entries = output.Split("---", StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var lines = entry.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 5)
                continue;

            commits.Add(new GitCommitInfo(
                Hash: lines[0].Trim(),
                ShortHash: lines[1].Trim(),
                Message: lines[2].Trim(),
                Author: lines[3].Trim(),
                Date: DateTimeOffset.TryParse(lines[4].Trim(), out DateTimeOffset date) ? date : DateTimeOffset.MinValue
            ));
        }

        return commits;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunGitCommandAsync(
        string workingDirectory, string arguments, CancellationToken ct, int timeoutMs = StandardCommandTimeoutMs)
    {
        if (outputSink == null)
            return await RunGitCommandCoreAsync(workingDirectory, arguments, ct, timeoutMs);

        var started = System.Diagnostics.Stopwatch.StartNew();
        outputSink.CommandStarted(workingDirectory, arguments);

        var result = await RunGitCommandCoreAsync(workingDirectory, arguments, ct, timeoutMs);

        foreach (var line in (result.Output + "\n" + result.Error).Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length > 0)
                outputSink.OutputLine(workingDirectory, arguments, trimmed);
        }

        outputSink.CommandFinished(workingDirectory, arguments, result.ExitCode, started.Elapsed,
            result.ExitCode == 0 ? null : result.Error.Trim());
        return result;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunGitCommandCoreAsync(
        string workingDirectory, string arguments, CancellationToken ct, int timeoutMs)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            BufferedCommandResult result = await Cli.Wrap("git")
                .WithArguments(arguments)
                .WithWorkingDirectory(workingDirectory)
                .WithEnvironmentVariables(env => env
                    .Set("GIT_TERMINAL_PROMPT", "0"))  // Disable interactive prompts
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cts.Token);

            return (result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (OperationCanceledException)
        {
            return ct.IsCancellationRequested
                ? (ExitCode: -1, Output: "", Error: CancelledError)
                : (ExitCode: -1, Output: "",
                    Error: $"Git command timed out after {TimeSpan.FromMilliseconds(timeoutMs).TotalSeconds:0} second(s)");
        }
    }
}
