using CliWrap;
using CliWrap.Buffered;
using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public class GitService : IGitService
{
    private bool? _gitInstalled;

    public async Task<bool> IsGitInstalledAsync(CancellationToken ct = default)
    {
        if (_gitInstalled.HasValue)
            return _gitInstalled.Value;

        var (exitCode, _, _) = await RunGitCommandAsync(".", "--version", ct);
        _gitInstalled = exitCode == 0;
        return _gitInstalled.Value;
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

    public async Task<bool> FetchAsync(string path, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Fetching from remote...");
        var (exitCode, _, error) = await RunGitCommandAsync(path, "fetch --all --prune", ct);

        if (exitCode != 0)
            progress?.Report($"Fetch failed: {error}");

        return exitCode == 0;
    }

    public async Task<bool> PullAsync(string path, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Pulling changes...");
        var (exitCode, _, error) = await RunGitCommandAsync(path, "pull --ff-only", ct);

        if (exitCode != 0)
            progress?.Report($"Pull failed: {error}");

        return exitCode == 0;
    }

    public async Task<bool> CloneAsync(string url, string targetPath, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"Cloning {url}...");

        var parentDir = Path.GetDirectoryName(targetPath) ?? ".";
        var folderName = Path.GetFileName(targetPath);

        var (exitCode, _, error) = await RunGitCommandAsync(parentDir, $"clone \"{url}\" \"{folderName}\"", ct,
            timeoutMs: 300000); // 5 minute timeout for clone

        if (exitCode != 0)
            progress?.Report($"Clone failed: {error}");

        return exitCode == 0;
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

    public async Task<bool> CheckoutAsync(string path, string branchOrCommit, CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunGitCommandAsync(path, $"checkout \"{branchOrCommit}\"", ct);
        return exitCode == 0;
    }

    public async Task<bool> HasLocalChangesAsync(string path, CancellationToken ct = default)
    {
        var (exitCode, output, _) = await RunGitCommandAsync(path, "status --porcelain", ct);
        return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
    }

    private static IReadOnlyList<GitCommitInfo> ParseCommitLog(string output)
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

    private static async Task<(int ExitCode, string Output, string Error)> RunGitCommandAsync(
        string workingDirectory, string arguments, CancellationToken ct, int timeoutMs = 30000)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            BufferedCommandResult result = await Cli.Wrap("git")
                .WithArguments(arguments)
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cts.Token);

            return (result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (OperationCanceledException)
        {
            return (ExitCode: -1, Output: "", Error: "Operation timed out or was cancelled");
        }
    }
}
