using CliWrap;
using CliWrap.Buffered;

namespace FCPModUpdater.Tests.Services;

[Trait("Category", "Integration")]
public class GitServiceIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GitService _service;

    public GitServiceIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fcp_git_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new GitService();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private async Task RunGitAsync(string workingDir, string args)
    {
        var result = await Cli.Wrap("git")
            .WithArguments($"-c commit.gpgsign=false {args}")
            .WithWorkingDirectory(workingDir)
            .WithEnvironmentVariables(env => env
                .Set("GIT_TERMINAL_PROMPT", "0")
                .Set("GIT_AUTHOR_NAME", "Test")
                .Set("GIT_AUTHOR_EMAIL", "test@test.com")
                .Set("GIT_COMMITTER_NAME", "Test")
                .Set("GIT_COMMITTER_EMAIL", "test@test.com"))
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();

        if (result.ExitCode != 0)
            throw new Exception($"git {args} failed: {result.StandardError}");
    }

    private async Task<string> InitRepoWithCommit(string? dir = null)
    {
        dir ??= _tempDir;
        await RunGitAsync(dir, "init -b main");
        File.WriteAllText(Path.Combine(dir, "README.md"), "# Test");
        await RunGitAsync(dir, "add .");
        await RunGitAsync(dir, "commit -m \"Initial commit\"");
        return dir;
    }

    [Fact]
    public async Task IsGitRepositoryAsync_ValidRepo_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        Assert.True(await _service.IsGitRepositoryAsync(_tempDir, ct));
    }

    [Fact]
    public async Task IsGitRepositoryAsync_NotARepo_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.False(await _service.IsGitRepositoryAsync(_tempDir, ct));
    }

    [Fact]
    public async Task GetRemoteUrlAsync_HasRemote_ReturnsUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        await RunGitAsync(_tempDir, "remote add origin https://github.com/test/repo.git");

        var url = await _service.GetRemoteUrlAsync(_tempDir, ct);
        Assert.Equal("https://github.com/test/repo.git", url);
    }

    [Fact]
    public async Task GetRemoteUrlAsync_NoRemote_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        var url = await _service.GetRemoteUrlAsync(_tempDir, ct);
        Assert.Null(url);
    }

    [Fact]
    public async Task GetCurrentCommitAsync_HasCommits_ReturnsInfo()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        var commit = await _service.GetCurrentCommitAsync(_tempDir, ct);

        Assert.NotNull(commit);
        Assert.Equal(40, commit.Hash.Length);
        Assert.Equal("Initial commit", commit.Message);
        Assert.Equal("Test", commit.Author);
    }

    [Fact]
    public async Task GetCurrentCommitAsync_EmptyRepo_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunGitAsync(_tempDir, "init");
        var commit = await _service.GetCurrentCommitAsync(_tempDir, ct);
        Assert.Null(commit);
    }

    [Fact]
    public async Task GetCurrentBranchAsync_OnBranch_ReturnsBranchName()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        var branch = await _service.GetCurrentBranchAsync(_tempDir, ct);
        Assert.Equal("main", branch);
    }

    [Fact]
    public async Task GetCurrentBranchAsync_DetachedHead_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        // Get the commit hash and detach
        var commit = await _service.GetCurrentCommitAsync(_tempDir, ct);
        await RunGitAsync(_tempDir, $"checkout {commit!.Hash}");

        var branch = await _service.GetCurrentBranchAsync(_tempDir, ct);
        Assert.Null(branch);
    }

    [Fact]
    public async Task HasLocalChangesAsync_Clean_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        Assert.False(await _service.HasLocalChangesAsync(_tempDir, ct));
    }

    [Fact]
    public async Task HasLocalChangesAsync_ModifiedFile_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "modified");
        Assert.True(await _service.HasLocalChangesAsync(_tempDir, ct));
    }

    [Fact]
    public async Task HasLocalChangesAsync_UntrackedFile_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        File.WriteAllText(Path.Combine(_tempDir, "newfile.txt"), "new");
        Assert.True(await _service.HasLocalChangesAsync(_tempDir, ct));
    }

    [Fact]
    public async Task GetCommitDifferenceAsync_Behind()
    {
        var ct = TestContext.Current.CancellationToken;
        // Create a "remote" repo
        var remoteDir = Path.Combine(_tempDir, "remote");
        Directory.CreateDirectory(remoteDir);
        await InitRepoWithCommit(remoteDir);

        // Clone it
        var localDir = Path.Combine(_tempDir, "local");
        await RunGitAsync(_tempDir, $"clone \"{remoteDir}\" local");

        // Add a commit to the remote
        File.WriteAllText(Path.Combine(remoteDir, "file2.txt"), "new file");
        await RunGitAsync(remoteDir, "add .");
        await RunGitAsync(remoteDir, "commit -m \"Second commit\"");

        // Fetch in local
        await RunGitAsync(localDir, "fetch");

        var (behind, ahead) = await _service.GetCommitDifferenceAsync(localDir, ct);
        Assert.Equal(1, behind);
        Assert.Equal(0, ahead);
    }

    [Fact]
    public async Task GetRemoteBranchesAsync_ReturnsBranches()
    {
        var ct = TestContext.Current.CancellationToken;
        var remoteDir = Path.Combine(_tempDir, "remote");
        Directory.CreateDirectory(remoteDir);
        await InitRepoWithCommit(remoteDir);
        await RunGitAsync(remoteDir, "checkout -b feature");
        await RunGitAsync(remoteDir, "checkout main");

        var localDir = Path.Combine(_tempDir, "local");
        await RunGitAsync(_tempDir, $"clone \"{remoteDir}\" local");

        var branches = await _service.GetRemoteBranchesAsync(localDir, ct);
        Assert.Contains("main", branches);
        Assert.Contains("feature", branches);
        // Should not contain HEAD -> references
        Assert.DoesNotContain(branches, b => b.Contains("HEAD"));
    }

    [Fact]
    public async Task CheckoutAsync_SwitchesBranch()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        await RunGitAsync(_tempDir, "checkout -b feature");
        await RunGitAsync(_tempDir, "checkout main");

        var success = await _service.CheckoutAsync(_tempDir, "feature", ct);
        Assert.True(success);

        var branch = await _service.GetCurrentBranchAsync(_tempDir, ct);
        Assert.Equal("feature", branch);
    }

    [Fact]
    public async Task GetCommitHistoryAsync_ReturnsCommits()
    {
        var ct = TestContext.Current.CancellationToken;
        await InitRepoWithCommit();
        File.WriteAllText(Path.Combine(_tempDir, "file2.txt"), "second");
        await RunGitAsync(_tempDir, "add .");
        await RunGitAsync(_tempDir, "commit -m \"Second commit\"");

        var history = await _service.GetCommitHistoryAsync(_tempDir, ct: ct);
        Assert.Equal(2, history.Count);
        Assert.Equal("Second commit", history[0].Message);
        Assert.Equal("Initial commit", history[1].Message);
    }
}
