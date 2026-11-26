using CliWrap;
using CliWrap.Buffered;

namespace FCPModUpdater.Tests.Services;

[Trait("Category", "Integration")]
public sealed class ApplyVersionIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_apply_{Guid.NewGuid():N}");
    private readonly GitService _git = new();

    public ApplyVersionIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            GitService.DeleteDirectory(_dir);
    }

    private async Task<string> Git(string workingDir, string args)
    {
        BufferedCommandResult result = await Cli.Wrap("git")
            .WithArguments($"-c commit.gpgsign=false {args}")
            .WithWorkingDirectory(workingDir)
            .WithEnvironmentVariables(env => env
                .Set("GIT_AUTHOR_NAME", "Test").Set("GIT_AUTHOR_EMAIL", "t@t")
                .Set("GIT_COMMITTER_NAME", "Test").Set("GIT_COMMITTER_EMAIL", "t@t"))
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
        if (result.ExitCode != 0)
            throw new Exception($"git {args}: {result.StandardError}");
        return result.StandardOutput.Trim();
    }

    private async Task Commit(string repo, string message)
    {
        await File.AppendAllTextAsync(Path.Combine(repo, "file.txt"), message + "\n");
        await Git(repo, "add .");
        await Git(repo, $"commit -m \"{message}\"");
    }

    /// <summary>origin: main with c1..c3, dev with d1 on top of c1. Clone reset back to c1.</summary>
    private async Task<(string Clone, InstalledMod Mod)> SetUpAsync()
    {
        var origin = Path.Combine(_dir, "origin");
        Directory.CreateDirectory(origin);
        await Git(origin, "init -b main");
        await Commit(origin, "c1");
        await Git(origin, "branch dev");
        await Commit(origin, "c2");
        await Commit(origin, "c3");
        await Git(origin, "checkout dev");
        await Commit(origin, "d1");
        await Git(origin, "checkout main");

        await Git(_dir, "clone origin clone");
        var clone = Path.Combine(_dir, "clone");
        await Git(clone, "reset --hard HEAD~2");

        return (clone, new InstalledMod { Name = "clone", Path = clone, Source = ModSource.Git, Branch = "main" });
    }

    private Task<string> Head(string repo) => Git(repo, "log -1 --format=%s");

    [Fact]
    public async Task FastForward_MovesToNewerCommit()
    {
        var (clone, mod) = await SetUpAsync();
        IReadOnlyList<GitCommitInfo> incoming = await _git.GetIncomingCommitsAsync(clone, 10, TestContext.Current.CancellationToken);
        Assert.Equal(["c3", "c2"], incoming.Select(c => c.Message));

        var (ok, error) = await ModOperations.ApplyVersionAsync(_git, mod, VersionAction.FastForward, incoming[1].Hash);

        Assert.True(ok, error);
        Assert.Equal("c2", await Head(clone));
    }

    [Fact]
    public async Task Reset_MovesToOlderCommit()
    {
        var (clone, mod) = await SetUpAsync();
        await ModOperations.ApplyVersionAsync(_git, mod, VersionAction.FastForward, "origin/main");
        IReadOnlyList<GitCommitInfo> history = await _git.GetCommitHistoryAsync(clone, 5, TestContext.Current.CancellationToken);

        var (ok, _) = await ModOperations.ApplyVersionAsync(_git, mod, VersionAction.Reset, history[1].Hash);

        Assert.True(ok);
        Assert.Equal("c2", await Head(clone));
    }

    [Fact]
    public async Task SwitchBranch_ChecksOutBranch_AndLandsOnPickedCommit()
    {
        var (clone, mod) = await SetUpAsync();
        IReadOnlyList<GitCommitInfo> dev = await _git.GetBranchCommitsAsync(clone, "dev", 5, TestContext.Current.CancellationToken);
        Assert.Equal(["d1", "c1"], dev.Select(c => c.Message));

        var (ok, error) = await ModOperations.ApplyVersionAsync(_git, mod, VersionAction.SwitchBranch, dev[0].Hash, "dev");

        Assert.True(ok, error);
        Assert.Equal("dev", await _git.GetCurrentBranchAsync(clone, TestContext.Current.CancellationToken));
        Assert.Equal("d1", await Head(clone));
    }

    [Fact]
    public async Task FastForward_ToUnrelatedCommit_Fails()
    {
        var (clone, mod) = await SetUpAsync();
        IReadOnlyList<GitCommitInfo> dev = await _git.GetBranchCommitsAsync(clone, "dev", 5, TestContext.Current.CancellationToken);
        await ModOperations.ApplyVersionAsync(_git, mod, VersionAction.FastForward, "origin/main");

        var (ok, error) = await ModOperations.ApplyVersionAsync(_git, mod, VersionAction.FastForward, dev[0].Hash);

        Assert.False(ok);
        Assert.Contains("Fast-forward", error);
    }

    [Fact]
    public async Task GitInfo_FindsGitOnPath()
    {
        var (version, path) = await GitService.GetGitInfoAsync();
        Assert.StartsWith("git version", version);
        Assert.NotNull(path);
    }
}
