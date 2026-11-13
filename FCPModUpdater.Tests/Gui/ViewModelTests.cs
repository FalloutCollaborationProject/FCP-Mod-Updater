using FCPModUpdater.Gui.Services;
using FCPModUpdater.Gui.ViewModels;
using NSubstitute;

namespace FCPModUpdater.Tests.Gui;

/// <summary>
/// Builds a shell over substituted services; everything "posted to the UI thread" runs inline.
/// </summary>
internal sealed class ShellFixture
{
    public IGitService Git { get; } = Substitute.For<IGitService>();
    public IGitHubApiService GitHub { get; } = Substitute.For<IGitHubApiService>();
    public IModDiscoveryService Discovery { get; } = Substitute.For<IModDiscoveryService>();
    public GitOutputLog Log { get; } = new(action => action());
    public List<InstalledMod> Mods { get; } = [];
    public List<RemoteRepo> Repos { get; } = [];
    public MainWindowViewModel Shell { get; }

    public FCPModUpdater.Settings.SettingsStore Settings { get; }

    public ShellFixture(string? modsDirectory, FCPModUpdater.Settings.SettingsStore? settings = null)
    {
        Settings = settings ?? FCPModUpdater.Settings.SettingsStore.InMemory();
        Discovery.DiscoverModsAsync(Arg.Any<string>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Mods.ToList());
        GitHub.GetOrganizationReposAsync(Arg.Any<CancellationToken>()).Returns(_ => Repos.ToList());
        Git.GetIncomingCommitsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var updateCheck = new UpdateCheckService(new HttpClient(new OfflineHandler())
            { BaseAddress = new Uri("https://api.github.invalid") });

        Shell = new MainWindowViewModel(Git, GitHub, Discovery, updateCheck, new PathDiscoveryService(), Log,
            new StartupOptions(modsDirectory), action => action(), () => Task.FromResult(true),
            settings: Settings, gitInfo: () => Task.FromResult<(string?, string?)>(("git version 2.47.1", "/usr/bin/git")));
    }

    public static InstalledMod GitMod(string name, ModStatus status, string directory, int behind = 0,
        bool dirty = false) => new()
    {
        Name = name,
        Path = Path.Combine(directory, name),
        Source = ModSource.Git,
        Branch = "main",
        CurrentCommit = new GitCommitInfo("aaaaaaa1111", "aaaaaaa", "msg", "kaz", DateTimeOffset.UtcNow),
        Status = status,
        CommitsBehind = behind,
        HasLocalChanges = dirty
    };

    public static InstalledMod LocalMod(string name, string repo, string directory) => new()
    {
        Name = name,
        Path = Path.Combine(directory, name),
        Source = ModSource.Local,
        Status = ModStatus.NonGit,
        MatchedRepoName = repo
    };

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("offline");
    }
}

public sealed class ViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_gui_test_{Guid.NewGuid():N}");
    private readonly ShellFixture _f;

    public ViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _f = new ShellFixture(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static RemoteRepo Repo(string name, bool notRecommended = false) =>
        new(name, $"https://github.com/FalloutCollaborationProject/{name}.git", "main", "desc", "",
            notRecommended ? ["rimworld-mod", ModCatalog.NotRecommendedTopic] : ["rimworld-mod"]);

    [Fact]
    public async Task Initialize_WithoutGit_ShowsGitMissingAndDoesNotScan()
    {
        var shell = new MainWindowViewModel(_f.Git, _f.GitHub, _f.Discovery,
            new UpdateCheckService(new HttpClient()), new PathDiscoveryService(), _f.Log,
            new StartupOptions(_dir), action => action(), () => Task.FromResult(false));

        await shell.InitializeAsync();

        Assert.True(shell.GitMissing);
        await _f.Discovery.DidNotReceiveWithAnyArgs()
            .DiscoverModsAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Scan_FillsTableChipsAndNavCounts()
    {
        _f.Mods.AddRange(
        [
            ShellFixture.GitMod("A", ModStatus.UpToDate, _dir),
            ShellFixture.GitMod("B", ModStatus.Behind, _dir, behind: 4),
            ShellFixture.GitMod("C", ModStatus.LocalChanges, _dir, dirty: true),
            ShellFixture.GitMod("D", ModStatus.Diverged, _dir, behind: 2),
            ShellFixture.LocalMod("E-main", "E", _dir)
        ]);
        _f.Repos.AddRange([Repo("A"), Repo("B"), Repo("New1"), Repo("New2")]);

        await _f.Shell.InitializeAsync();

        InstalledModsViewModel page = _f.Shell.Installed;
        Assert.Equal(5, page.Rows.Count);
        Assert.Equal([5, 1, 2, 1, 0], page.Chips.Select(c => c.Count));
        Assert.Equal(["5", "2", "1", ""], _f.Shell.NavItems.Select(n => n.Count));

        // Only the cleanly-behind mod is updateable, and it starts ticked like the CLI's pre-selection.
        Assert.Equal(1, page.CheckedCount);
        Assert.Equal("Update 1 mod", page.UpdateLabel);
        Assert.False(page.Rows.Single(r => r.Name == "D").CanUpdate);

        // The diverged mod is reported as a warning in the Problems tab.
        Assert.Equal(1, page.ProblemCount);
    }

    [Fact]
    public async Task FilterChipAndSearch_NarrowRows()
    {
        _f.Mods.AddRange(
        [
            ShellFixture.GitMod("FCP Core", ModStatus.UpToDate, _dir),
            ShellFixture.GitMod("FCP Ghouls", ModStatus.Behind, _dir, behind: 1),
            ShellFixture.GitMod("FCP Creatures", ModStatus.Behind, _dir, behind: 12)
        ]);
        await _f.Shell.InitializeAsync();
        InstalledModsViewModel page = _f.Shell.Installed;

        page.Chips[1].PickCommand.Execute(null);
        Assert.Equal(["FCP Creatures", "FCP Ghouls"], page.Rows.Select(r => r.Name).Order());

        page.SearchText = "ghoul";
        Assert.Equal("FCP Ghouls", Assert.Single(page.Rows).Name);
        Assert.Equal("FCP Ghouls", page.SelectedRow?.Name);
    }

    [Fact]
    public async Task FetchFailure_ShowsAsErrorBadge()
    {
        _f.Mods.Add(ShellFixture.GitMod("A", ModStatus.UpToDate, _dir) with
        {
            FetchError = new GitOperationResult(false, 128, "fatal: Could not resolve host")
        });
        await _f.Shell.InitializeAsync();

        ModRowViewModel row = Assert.Single(_f.Shell.Installed.Rows);
        Assert.Equal(StatusKind.Error, row.Badge.Kind);
        Assert.Equal("Fetch failed", row.Badge.Text);
        Assert.Equal("Last error · exit 128", row.ErrorTitle);
        Assert.Equal(1, _f.Shell.Installed.Chips[4].Count);
    }

    [Fact]
    public async Task UpdateDialog_RunsSelectedMods_AndSummarisesFailures()
    {
        InstalledMod good = ShellFixture.GitMod("Good", ModStatus.Behind, _dir, behind: 1);
        InstalledMod bad = ShellFixture.GitMod("Bad", ModStatus.Behind, _dir, behind: 1);
        _f.Mods.AddRange([good, bad]);
        await _f.Shell.InitializeAsync();

        var ok = new GitOperationResult(true, 0, null);
        _f.Git.FetchAsync(Arg.Any<string>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>()).Returns(ok);
        _f.Git.PullAsync(good.Path, Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>()).Returns(ok);
        _f.Git.PullAsync(bad.Path, Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new GitOperationResult(false, 1, "error: local changes would be overwritten"));
        _f.Git.GetCommitDifferenceAsync(good.Path, Arg.Any<CancellationToken>()).Returns((0, 0));
        _f.Git.GetCommitDifferenceAsync(bad.Path, Arg.Any<CancellationToken>()).Returns((1, 0));
        _f.Git.GetCurrentCommitAsync(good.Path, Arg.Any<CancellationToken>())
            .Returns(new GitCommitInfo("bbbbbbb2222", "bbbbbbb", "new", "kaz", DateTimeOffset.UtcNow));

        _f.Shell.Installed.UpdateCheckedCommand.Execute(null);
        var dialog = Assert.IsType<OperationDialogViewModel>(_f.Shell.Dialog);
        Assert.True(dialog.IsReview);

        await dialog.StartAsync();

        Assert.True(dialog.IsResults);
        Assert.False(dialog.AllSucceeded);
        Assert.Equal("Updated 1 mod, 1 failed.", dialog.Summary);

        OperationItemViewModel goodItem = dialog.Items.Single(i => i.Name == "Good");
        Assert.Equal(ItemState.Done, goodItem.State);
        Assert.Equal("✓ Updated · aaaaaaa → bbbbbbb", goodItem.ResultText);

        OperationItemViewModel badItem = dialog.Items.Single(i => i.Name == "Bad");
        Assert.Equal(ItemState.Failed, badItem.State);
        Assert.Contains("local changes would be overwritten", badItem.Error);
    }

    [Fact]
    public async Task UpdateDialog_CancelRemaining_SkipsQueuedMods()
    {
        InstalledMod first = ShellFixture.GitMod("First", ModStatus.Behind, _dir, behind: 1);
        InstalledMod second = ShellFixture.GitMod("Second", ModStatus.Behind, _dir, behind: 1);
        _f.Mods.AddRange([first, second]);
        await _f.Shell.InitializeAsync();

        var gate = new TaskCompletionSource<GitOperationResult>();
        _f.Git.FetchAsync(first.Path, Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);
        _f.Git.PullAsync(Arg.Any<string>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new GitOperationResult(true, 0, null));
        _f.Git.GetCommitDifferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((0, 0));

        _f.Shell.ShowUpdateDialog([first, second]);
        var dialog = (OperationDialogViewModel)_f.Shell.Dialog!;

        Task run = dialog.StartAsync();
        dialog.CancelRemainingCommand.Execute(null);
        gate.SetResult(new GitOperationResult(true, 0, null));
        await run;

        Assert.Equal(ItemState.Done, dialog.Items[0].State);
        Assert.Equal(ItemState.Skipped, dialog.Items[1].State);
        Assert.Equal("Updated 1 mod, 1 skipped.", dialog.Summary);
        await _f.Git.DidNotReceive().FetchAsync(second.Path, Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_SelectAllRecommended_SkipsNotRecommended_AndClonesIntoModsFolder()
    {
        _f.Repos.AddRange([Repo("Chems"), Repo("Legacy", notRecommended: true), Repo("Vault")]);
        await _f.Shell.InitializeAsync();
        _f.Git.CloneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IProgress<string>?>(),
            Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>()).Returns((true, null));

        InstallViewModel page = _f.Shell.Install;
        Assert.Equal("3 repositories tagged rimworld-mod · 3 not installed", page.Subheading);
        Assert.False(page.ConfirmCommand.CanExecute(null));

        page.SelectAllRecommendedCommand.Execute(null);
        Assert.Equal("Install 2 mods", page.Cta);

        page.ConfirmCommand.Execute(null);
        var dialog = Assert.IsType<OperationDialogViewModel>(_f.Shell.Dialog);
        await WaitUntilAsync(() => dialog.IsResults);

        Assert.True(dialog.AllSucceeded);
        Assert.Equal("Installed 2 mods.", dialog.Summary);
        await _f.Git.Received(1).CloneAsync(Arg.Is<string>(u => u.EndsWith("/Chems.git")),
            Path.Combine(_dir, "Chems"), Arg.Any<IProgress<string>?>(), Arg.Any<IProgress<double>?>(),
            Arg.Any<CancellationToken>());
        await _f.Git.DidNotReceive().CloneAsync(Arg.Is<string>(u => u.Contains("Legacy")), Arg.Any<string>(),
            Arg.Any<IProgress<string>?>(), Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_RequiresTypingDelete_ThenRemovesFolders()
    {
        InstalledMod mod = ShellFixture.GitMod("Doomed", ModStatus.UpToDate, _dir);
        Directory.CreateDirectory(mod.Path);
        File.WriteAllText(Path.Combine(mod.Path, "About.xml"), "<x/>");
        _f.Mods.Add(mod);
        await _f.Shell.InitializeAsync();

        _f.Shell.Installed.UninstallCommand.Execute(null);
        var dialog = Assert.IsType<UninstallDialogViewModel>(_f.Shell.Dialog);
        Assert.Equal("Uninstall 1 mod?", dialog.Title);

        dialog.ConfirmText = "delete";
        Assert.False(dialog.DeleteCommand.CanExecute(null));
        Assert.True(Directory.Exists(mod.Path));

        dialog.ConfirmText = "DELETE";
        Assert.True(dialog.DeleteCommand.CanExecute(null));
        dialog.DeleteCommand.Execute(null);

        Assert.False(Directory.Exists(mod.Path));
        Assert.Null(_f.Shell.Dialog);
    }

    [Fact]
    public async Task Versions_DirtyMod_NeedsAcknowledgement_ThenChecksOutBranch()
    {
        InstalledMod mod = ShellFixture.GitMod("Apparel", ModStatus.LocalChanges, _dir, dirty: true);
        _f.Mods.Add(mod);
        _f.Git.FetchAsync(Arg.Any<string>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new GitOperationResult(true, 0, null));
        _f.Git.GetRemoteBranchInfoAsync(mod.Path, Arg.Any<CancellationToken>())
            .Returns([new GitBranchInfo("dev", null), new GitBranchInfo("main", null)]);
        _f.Git.CheckoutAsync(mod.Path, "dev", Arg.Any<CancellationToken>()).Returns(true);
        await _f.Shell.InitializeAsync();

        // Nothing is fetched until the page is opened.
        await _f.Git.DidNotReceive().GetRemoteBranchInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        _f.Shell.OpenVersions(mod.Path);
        VersionsViewModel page = _f.Shell.Versions;
        await WaitUntilAsync(() => page.Branches.Count == 2);

        Assert.Equal(["main", "dev"], page.Branches.Select(b => b.Name)); // current branch first
        Assert.True(page.Branches[0].IsCurrent);

        page.SelectedBranch = page.Branches[1];
        Assert.Equal("Switch to dev", page.Cta);
        Assert.False(page.ApplyCommand.CanExecute(null));

        page.DirtyAcknowledged = true;
        Assert.True(page.ApplyCommand.CanExecute(null));
        await page.ApplyCommand.ExecuteAsync(null);

        await _f.Git.Received(1).CheckoutAsync(mod.Path, "dev", Arg.Any<CancellationToken>());
        Assert.False(page.ResultIsError);
        Assert.Equal("Switched to branch 'dev'.", page.ResultMessage);
    }

    [Fact]
    public async Task Versions_ManualHash_ChecksOutTrimmedHash_AndReportsFailure()
    {
        InstalledMod mod = ShellFixture.GitMod("Core", ModStatus.UpToDate, _dir);
        _f.Mods.Add(mod);
        _f.Git.CheckoutAsync(mod.Path, "3c7d2b8", Arg.Any<CancellationToken>()).Returns(false);
        await _f.Shell.InitializeAsync();

        _f.Shell.OpenVersions(mod.Path);
        VersionsViewModel page = _f.Shell.Versions;
        page.Tabs[2].PickCommand.Execute(null);
        Assert.False(page.ApplyCommand.CanExecute(null));

        page.ManualHash = "  3c7d2b8 ";
        await page.ApplyCommand.ExecuteAsync(null);

        await _f.Git.Received(1).CheckoutAsync(mod.Path, "3c7d2b8", Arg.Any<CancellationToken>());
        Assert.True(page.ResultIsError);
    }

    [Fact]
    public void ParseArgs_ReadsDirectoryOption()
    {
        Assert.Null(FCPModUpdater.Gui.App.ParseArgs([]).ModsDirectory);
        Assert.Equal(Path.GetFullPath("/x/Mods"), FCPModUpdater.Gui.App.ParseArgs(["-d", "/x/Mods"]).ModsDirectory);
        Assert.Equal(Path.GetFullPath("/x/Mods"),
            FCPModUpdater.Gui.App.ParseArgs(["--directory=/x/Mods"]).ModsDirectory);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(condition(), "Timed out waiting for the view model.");
    }
}
