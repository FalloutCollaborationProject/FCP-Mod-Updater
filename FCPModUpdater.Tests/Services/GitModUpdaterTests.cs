using NSubstitute;

namespace FCPModUpdater.Tests.Services;

public class GitModUpdaterTests
{
    private readonly IGitService _gitService = Substitute.For<IGitService>();

    private static InstalledMod MakeMod() => new()
    {
        Name = "TestMod",
        Path = "/mods/TestMod",
        Source = ModSource.Git,
        Status = ModStatus.Behind,
        CommitsBehind = 1
    };

    [Fact]
    public async Task UpdateAsync_FetchFailure_ReturnsModSpecificError()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        InstalledMod mod = MakeMod();
        _gitService.FetchAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(new GitOperationResult(false, 128, "fatal: fetch failed"));
        _gitService.GetCommitDifferenceAsync(mod.Path, token)
            .Returns((Behind: 1, Ahead: 0));

        var result = await GitModUpdater.UpdateAsync(_gitService, mod, ct: token);

        Assert.False(result.Success);
        Assert.Equal("Fetch failed for TestMod: fatal: fetch failed", result.Error);
    }

    [Fact]
    public async Task UpdateAsync_PullFailure_ReturnsModSpecificError()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        InstalledMod mod = MakeMod();
        _gitService.FetchAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(new GitOperationResult(true, 0, null));
        _gitService.PullAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(new GitOperationResult(false, 128, "fatal: pull failed"));
        _gitService.GetCommitDifferenceAsync(mod.Path, token)
            .Returns((Behind: 1, Ahead: 0));

        var result = await GitModUpdater.UpdateAsync(_gitService, mod, ct: token);

        Assert.False(result.Success);
        Assert.Equal("Pull failed for TestMod: fatal: pull failed", result.Error);
    }

    [Fact]
    public async Task UpdateAsync_PullFailureButNoLongerBehind_ReturnsSuccess()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        InstalledMod mod = MakeMod();
        _gitService.FetchAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(new GitOperationResult(true, 0, null));
        _gitService.PullAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(new GitOperationResult(false, 1, "fatal: transient failure"));
        _gitService.GetCommitDifferenceAsync(mod.Path, token)
            .Returns((Behind: 0, Ahead: 0));

        var result = await GitModUpdater.UpdateAsync(_gitService, mod, ct: token);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task UpdateAsync_ExitCodeOneFourOne_ReturnsSuccessWhenNoLongerBehind()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        InstalledMod mod = MakeMod();
        _gitService.FetchAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(GitOperationResult.FromExitCode(141, "progress pipe closed"));
        _gitService.PullAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(GitOperationResult.FromExitCode(141, "progress pipe closed"));
        _gitService.GetCommitDifferenceAsync(mod.Path, token)
            .Returns((Behind: 0, Ahead: 0));

        var result = await GitModUpdater.UpdateAsync(_gitService, mod, ct: token);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        await _gitService.Received(1).GetCommitDifferenceAsync(mod.Path, token);
    }

    [Fact]
    public async Task UpdateAsync_PullSuccessButStillBehind_ReturnsFailure()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        InstalledMod mod = MakeMod();
        _gitService.FetchAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(new GitOperationResult(true, 0, null));
        _gitService.PullAsync(mod.Path, Arg.Any<IProgress<string>>(), token)
            .Returns(new GitOperationResult(true, 0, null));
        _gitService.GetCommitDifferenceAsync(mod.Path, token)
            .Returns((Behind: 2, Ahead: 0));

        var result = await GitModUpdater.UpdateAsync(_gitService, mod, ct: token);

        Assert.False(result.Success);
        Assert.Equal("Pull completed for TestMod, but it is still 2 commit(s) behind", result.Error);
    }
}
