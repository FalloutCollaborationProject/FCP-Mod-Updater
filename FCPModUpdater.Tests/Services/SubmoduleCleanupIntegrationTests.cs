using CliWrap;
using CliWrap.Buffered;

namespace FCPModUpdater.Tests.Services;

/// <summary>origin: "old" branch has a submodule, main removed it (like FCP-Tools' FCP-UnityAssets).</summary>
[Trait("Category", "Integration")]
public sealed class SubmoduleCleanupIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_sub_{Guid.NewGuid():N}");
    private readonly GitService _git = new();

    public SubmoduleCleanupIntegrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir))
            return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, true);
    }

    private static async Task<string> Git(string cwd, string args)
    {
        BufferedCommandResult r = await Cli.Wrap("git")
            .WithArguments($"-c commit.gpgsign=false -c protocol.file.allow=always {args}")
            .WithWorkingDirectory(cwd)
            .WithEnvironmentVariables(e => e
                .Set("GIT_AUTHOR_NAME", "T").Set("GIT_AUTHOR_EMAIL", "t@t")
                .Set("GIT_COMMITTER_NAME", "T").Set("GIT_COMMITTER_EMAIL", "t@t")
                // Submodules cloned from local paths need this for every nested git call.
                .Set("GIT_CONFIG_COUNT", "1").Set("GIT_CONFIG_KEY_0", "protocol.file.allow").Set("GIT_CONFIG_VALUE_0", "always"))
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
        if (r.ExitCode != 0)
            throw new Exception($"git {args}: {r.StandardError}");
        return r.StandardOutput.Trim();
    }

    private async Task<string> SetUpAsync()
    {
        var sub = Path.Combine(_dir, "assets");
        Directory.CreateDirectory(sub);
        await Git(sub, "init -b main");
        File.WriteAllText(Path.Combine(sub, "a.txt"), "a");
        await Git(sub, "add .");
        await Git(sub, "commit -m assets");

        var origin = Path.Combine(_dir, "origin");
        Directory.CreateDirectory(origin);
        await Git(origin, "init -b main");
        File.WriteAllText(Path.Combine(origin, "f.txt"), "1");
        await Git(origin, "add .");
        await Git(origin, $"submodule add \"{sub}\" Assets");
        await Git(origin, "commit -m with-submodule");
        await Git(origin, "branch old");
        await Git(origin, "rm -q Assets");
        await Git(origin, "commit -m remove-submodule");

        await Git(_dir, "clone -q origin clone");
        var clone = Path.Combine(_dir, "clone");
        await Git(clone, "checkout -q old");
        await Git(clone, "submodule update --init --recursive");
        Assert.True(File.Exists(Path.Combine(clone, "Assets", "a.txt")));
        return clone;
    }

    [Fact]
    public async Task Checkout_BranchWithoutSubmodule_LeavesCleanTree()
    {
        var clone = await SetUpAsync();
        File.WriteAllText(Path.Combine(clone, "mine.txt"), "user file");

        Assert.True(await _git.CheckoutAsync(clone, "main", TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(Path.Combine(clone, "Assets")));
        Assert.Equal("?? mine.txt", await Git(clone, "status --porcelain"));   // unrelated untracked files survive
    }

    [Fact]
    public async Task Reset_ToCommitWithoutSubmodule_LeavesCleanTree()
    {
        var clone = await SetUpAsync();
        Assert.True(await _git.ResetToCommitAsync(clone, "origin/main", TestContext.Current.CancellationToken));
        Assert.False(await _git.HasLocalChangesAsync(clone, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OrdinaryUntrackedRepo_IsNotTouched()
    {
        var clone = await SetUpAsync();
        var other = Path.Combine(clone, "Other");
        Directory.CreateDirectory(other);
        await Git(other, "init -q");   // a real nested repo with a .git folder, not a submodule leftover

        await _git.RemoveStaleSubmodulesAsync(clone, TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(Path.Combine(other, ".git")));
    }
}
