using FCPModUpdater.Gui.Services;
using FCPModUpdater.Gui.ViewModels;
using NSubstitute;

namespace FCPModUpdater.Tests.Gui;

/// <summary>
/// <see cref="GitService.Executable"/> is global and every shell start-up resets it, so the tests that check it
/// run on their own, after the parallel tests.
/// </summary>
[CollectionDefinition(nameof(GitExecutableCollection), DisableParallelization = true)]
public sealed class GitExecutableCollection;

[Collection(nameof(GitExecutableCollection))]
public sealed class GitExecutableTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_gui_test_{Guid.NewGuid():N}");
    private readonly ShellFixture _f;

    public GitExecutableTests()
    {
        Directory.CreateDirectory(_dir);
        _f = new ShellFixture(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            GitService.DeleteDirectory(_dir);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GitMissingDialog_ValidPath_SavesItAndStartsScanning()
    {
        // The real git, since it becomes GitService.Executable for the rest of the test.
        var gitFile = GitService.FindOnPath(OperatingSystem.IsWindows() ? "git.exe" : "git");
        Assert.NotNull(gitFile);
        var settings = FCPModUpdater.Settings.SettingsStore.InMemory();
        var shell = new MainWindowViewModel(_f.Git, _f.GitHub, _f.Discovery,
            new UpdateCheckService(new HttpClient()), new PathDiscoveryService(), _f.Log,
            new StartupOptions(_dir), action => action(), () => Task.FromResult(false), settings: settings,
            gitInfo: () => Task.FromResult<(string?, string?)>(("git version 2.47.1", gitFile)),
            probeGit: _ => Task.FromResult<string?>("git version 2.47.1"));
        try
        {
            await shell.InitializeAsync();
            var dialog = Assert.IsType<GitMissingDialogViewModel>(shell.Dialog);

            dialog.PathText = Path.GetDirectoryName(gitFile)!; // the folder works too
            await dialog.UsePathCommand.ExecuteAsync(null);

            Assert.Null(dialog.Error);
            Assert.Equal(gitFile, settings.Current.GitPath);
            Assert.Equal(gitFile, GitService.Executable);
            Assert.False(shell.GitMissing);
            Assert.Null(shell.Dialog);
            await _f.Discovery.ReceivedWithAnyArgs()
                .DiscoverModsAsync(default!, default, TestContext.Current.CancellationToken);
        }
        finally
        {
            GitService.UseExecutable(null);
        }
    }

    [Fact]
    public async Task GitMissingDialog_MissingPath_ShowsErrorAndStaysOpen()
    {
        var shell = new MainWindowViewModel(_f.Git, _f.GitHub, _f.Discovery,
            new UpdateCheckService(new HttpClient()), new PathDiscoveryService(), _f.Log,
            new StartupOptions(_dir), action => action(), () => Task.FromResult(false));

        await shell.InitializeAsync();
        var dialog = Assert.IsType<GitMissingDialogViewModel>(shell.Dialog);
        dialog.PathText = Path.Combine(_dir, "nope", "git.exe");
        await dialog.UsePathCommand.ExecuteAsync(null);

        Assert.NotNull(dialog.Error);
        Assert.Same(dialog, shell.Dialog);
        Assert.Equal("git", GitService.Executable);
    }
}
