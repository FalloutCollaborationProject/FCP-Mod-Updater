using FCPModUpdater.Gui.ViewModels;
using FCPModUpdater.Settings;
using NSubstitute;

namespace FCPModUpdater.Tests.Gui;

/// <summary>Behaviour added by the v2 handoff: row popovers, filter bar, select-all, settings.</summary>
public sealed class V2ViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_gui_v2_{Guid.NewGuid():N}");

    public V2ViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    /// <summary>Navigation starts the rescan without awaiting it; discovery runs on the thread pool.</summary>
    private static async Task WaitForScanAsync(ShellFixture f)
    {
        for (var i = 0; i < 200 && (f.Shell.IsScanning || f.Shell.LastScanned is null); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
    }

    private static GitCommitInfo C(string hash, string message) =>
        new(hash + "0000000000", hash, message, "kaz", DateTimeOffset.UtcNow);

    private async Task<(ShellFixture F, ModRowViewModel Row)> RowAsync(bool dirty = false)
    {
        var f = new ShellFixture(_dir);
        InstalledMod mod = ShellFixture.GitMod("A", dirty ? ModStatus.LocalChanges : ModStatus.Behind, _dir, behind: 2, dirty: dirty);
        f.Mods.Add(mod);
        f.Git.GetIncomingCommitsAsync(mod.Path, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([C("ccccccc", "newest"), C("bbbbbbb", "newer")]);
        f.Git.GetCommitHistoryAsync(mod.Path, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([C("aaaaaaa", "current"), C("9999999", "older")]);
        f.Git.GetRemoteBranchInfoAsync(mod.Path, Arg.Any<CancellationToken>())
            .Returns([new GitBranchInfo("dev", DateTimeOffset.UtcNow.AddDays(-3)), new GitBranchInfo("main", DateTimeOffset.UtcNow)]);
        f.Git.GetBranchCommitsAsync(mod.Path, "dev", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([C("ddddddd", "dev tip"), C("eeeeeee", "dev older")]);
        await f.Shell.InitializeAsync();
        return (f, f.Shell.Installed.Rows.Single());
    }

    [Fact]
    public async Task CommitMenu_StartsOnHead_AndLabelsEachChoice()
    {
        var (_, row) = await RowAsync();
        VersionMenuViewModel menu = row.Menu!;

        await menu.OpenCommitsAsync();

        Assert.Equal(["latest", "new", "HEAD", ""], menu.Commits.Select(c => c.TagText));
        Assert.Equal("Already checked out", menu.Cta);
        Assert.False(menu.ApplyCommand.CanExecute(null));

        menu.Commits[0].PickCommand.Execute(null);
        Assert.Equal("Update to ccccccc", menu.Cta);
        Assert.Equal(VersionAction.FastForward, menu.Action);

        menu.Commits[3].PickCommand.Execute(null);
        Assert.Equal("Reset to 9999999", menu.Cta);
        Assert.True(menu.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task BranchMenu_CurrentFirst_ThenOtherBranchSwitches()
    {
        var (f, row) = await RowAsync();
        VersionMenuViewModel menu = row.Menu!;

        await menu.OpenBranchesAsync();
        Assert.Equal(["main", "dev"], menu.Branches.Select(b => b.Name));
        Assert.Equal("✓", menu.Branches[0].Mark);

        await menu.ShowCommitsAsync("dev", fromBranch: true);
        Assert.True(menu.FromBranch);
        Assert.Equal("Switch to dev @ ddddddd", menu.Cta);

        f.Git.CheckoutAsync(row.Path, "dev", Arg.Any<CancellationToken>()).Returns(true);
        f.Git.GetCurrentCommitAsync(row.Path, Arg.Any<CancellationToken>()).Returns(C("ddddddd", "dev tip"));
        await menu.ApplyCommand.ExecuteAsync(null);

        await f.Git.Received(1).CheckoutAsync(row.Path, "dev", Arg.Any<CancellationToken>());
        await f.Git.DidNotReceiveWithAnyArgs().ResetToCommitAsync(default!, default!, default);
        Assert.Null(menu.Error);
    }

    [Fact]
    public async Task CommitMenu_DirtyMod_NeedsAcknowledgement()
    {
        var (_, row) = await RowAsync(dirty: true);
        VersionMenuViewModel menu = row.Menu!;
        await menu.OpenCommitsAsync();
        menu.Commits[^1].PickCommand.Execute(null);

        Assert.False(menu.ApplyCommand.CanExecute(null));
        menu.DirtyAcknowledged = true;
        Assert.True(menu.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task CommitMenu_Failure_StaysOpenWithError()
    {
        var (f, row) = await RowAsync();
        VersionMenuViewModel menu = row.Menu!;
        await menu.OpenCommitsAsync();
        menu.Commits[0].PickCommand.Execute(null);
        f.Git.FastForwardToAsync(row.Path, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await menu.ApplyCommand.ExecuteAsync(null);

        Assert.True(menu.IsCommitOpen);
        Assert.Contains("Fast-forward", menu.Error);
    }

    [Fact]
    public async Task FilterBar_UsesShortLabels_AndHeaderCheckboxTogglesUpdateable()
    {
        var f = new ShellFixture(_dir);
        f.Mods.AddRange(
        [
            ShellFixture.GitMod("A", ModStatus.Behind, _dir, behind: 1),
            ShellFixture.GitMod("B", ModStatus.Behind, _dir, behind: 3),
            ShellFixture.GitMod("C", ModStatus.UpToDate, _dir)
        ]);
        await f.Shell.InitializeAsync();
        InstalledModsViewModel page = f.Shell.Installed;

        Assert.Equal(["All", "Updates", "Local changes", "Non-Git", "Errors"], page.Chips.Select(c => c.Label));
        Assert.True(page.AllChecked);

        page.AllChecked = false;
        Assert.Equal(0, page.CheckedCount);
        page.AllChecked = true;
        Assert.Equal(2, page.CheckedCount);
    }

    [Fact]
    public async Task Rows_ShowTagsAndGitHubLinks()
    {
        var f = new ShellFixture(_dir);
        f.Mods.Add(ShellFixture.GitMod("A", ModStatus.UpToDate, _dir) with
        {
            RemoteUrl = "git@github.com:FalloutCollaborationProject/FCP-A.git",
            Topics = [ModCatalog.NotRecommendedTopic]
        });
        f.Mods.Add(ShellFixture.LocalMod("B-main", "FCP-B", _dir));
        await f.Shell.InitializeAsync();

        ModRowViewModel a = f.Shell.Installed.Rows.Single(r => r.Name == "A");
        ModRowViewModel b = f.Shell.Installed.Rows.Single(r => r.Name == "B-main");
        Assert.True(a.IsNotRecommended);
        Assert.Equal("https://github.com/FalloutCollaborationProject/FCP-A", a.RepoUrl);
        Assert.True(b.IsLocal);
        Assert.Null(b.Menu);
        Assert.Equal("https://github.com/FalloutCollaborationProject/FCP-B", b.RepoUrl);
    }

    [Fact]
    public async Task Startup_UsesSavedFolder_WhenNoArgument()
    {
        var saved = Directory.CreateDirectory(Path.Combine(_dir, "saved")).FullName;
        var f = new ShellFixture(null, SettingsStore.InMemory(new AppSettings { ModsDirectory = saved }));

        await f.Shell.InitializeAsync();

        Assert.Equal(saved, f.Shell.ModsDirectory);
        Assert.Equal(FolderOrigin.Saved, f.Shell.FolderOrigin);
        Assert.Equal("Saved in settings", f.Shell.Settings.FolderSource);
    }

    [Fact]
    public async Task Startup_ArgumentBeatsSavedFolder()
    {
        var f = new ShellFixture(_dir, SettingsStore.InMemory(new AppSettings { ModsDirectory = "/elsewhere" }));

        await f.Shell.InitializeAsync();

        Assert.Equal(_dir, f.Shell.ModsDirectory);
        Assert.Equal(FolderOrigin.Argument, f.Shell.FolderOrigin);
    }

    [Fact]
    public async Task Settings_FolderCommit_SavesAndRescans()
    {
        var f = new ShellFixture(_dir);
        await f.Shell.InitializeAsync();
        var other = Directory.CreateDirectory(Path.Combine(_dir, "other")).FullName;

        f.Shell.Settings.FolderText = other;
        await f.Shell.Settings.CommitFolderAsync();

        Assert.Equal(other, f.Shell.ModsDirectory);
        Assert.Equal(other, f.Settings.Current.ModsDirectory);
        await f.Discovery.Received().DiscoverModsAsync(other, Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>());

        f.Shell.Settings.FolderText = Path.Combine(_dir, "missing");
        await f.Shell.Settings.CommitFolderAsync();
        Assert.Equal(other, f.Shell.ModsDirectory);
        Assert.False(f.Shell.Settings.FolderOk);
    }

    [Fact]
    public async Task Settings_TokenSavesImmediately_AndShowsActiveSource()
    {
        var f = new ShellFixture(_dir);
        await f.Shell.InitializeAsync();
        SettingsViewModel settings = f.Shell.Settings;
        Assert.Equal(TokenSource.None, settings.ActiveSource);
        Assert.True(f.Shell.ShowAddToken);

        settings.Token = "github_pat_x";

        Assert.Equal("github_pat_x", f.Settings.Current.GitHubToken);
        Assert.Equal("Saved in Settings", settings.ActiveSourceLabel);
        Assert.False(f.Shell.ShowAddToken);

        settings.RemoveTokenCommand.Execute(null);
        Assert.Null(f.Settings.Current.GitHubToken);
        Assert.Equal(TokenSource.None, settings.ActiveSource);
    }

    [Fact]
    public async Task Settings_TestToken_ReportsRemaining()
    {
        var f = new ShellFixture(_dir);
        f.GitHub.GetRateLimitAsync("tok", Arg.Any<CancellationToken>())
            .Returns(new RateLimitCheck(new RateLimitInfo(4982, 5000, DateTimeOffset.UtcNow), null));
        await f.Shell.InitializeAsync();
        f.Shell.Settings.Token = "tok";

        await f.Shell.Settings.TestTokenCommand.ExecuteAsync(null);

        Assert.True(f.Shell.Settings.TestOk);
        Assert.StartsWith("✓ Token valid · 4,982 of 5,000 requests left", f.Shell.Settings.TestResult);
    }

    [Fact]
    public async Task Settings_TestingTheTokenInUse_RefreshesSidebarCounter()
    {
        var f = new ShellFixture(_dir);
        var info = new RateLimitCheck(new RateLimitInfo(4982, 5000, DateTimeOffset.UtcNow), null);
        f.GitHub.GetRateLimitAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(info);
        f.GitHub.GetRateLimitAsync(null, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            f.GitHub.RemainingRateLimit.Returns(4982);
            f.GitHub.RateLimitTotal.Returns(5000);
            return info;
        });
        await f.Shell.InitializeAsync();

        f.Shell.Settings.Token = "tok";
        await f.Shell.Settings.TestTokenCommand.ExecuteAsync(null);

        await f.GitHub.Received(1).GetRateLimitAsync(null, Arg.Any<CancellationToken>());
        Assert.Equal("4,982 / 5,000", f.Shell.RateLimitText);
    }

    [Fact]
    public async Task Settings_NewToken_ClearsRepoCache_AndRescansOnLeavingSettings()
    {
        var f = new ShellFixture(_dir);
        await f.Shell.InitializeAsync();
        f.Shell.Navigate(Page.Settings);
        f.GitHub.ClearReceivedCalls();
        f.Discovery.ClearReceivedCalls();

        f.Shell.Settings.Token = "tok";
        f.GitHub.Received().ClearCache();

        f.Shell.Navigate(Page.Installed);
        await WaitForScanAsync(f);
        await f.Discovery.Received(1).DiscoverModsAsync(_dir, Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>());

        // No second rescan without another change.
        f.Shell.Navigate(Page.Settings);
        f.Shell.Navigate(Page.Installed);
        await WaitForScanAsync(f);
        await f.Discovery.Received(1).DiscoverModsAsync(_dir, Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Settings_TogglesSave_AndGitInfoLoads()
    {
        var f = new ShellFixture(_dir);
        await f.Shell.InitializeAsync();
        f.Shell.Navigate(Page.Settings);
        SettingsViewModel settings = f.Shell.Settings;

        settings.CheckOnLaunch = false;
        settings.IncludePrereleases = !settings.IncludePrereleases;

        Assert.False(f.Settings.Current.CheckForUpdatesOnLaunch);
        Assert.Equal(settings.IncludePrereleases, f.Settings.Current.IncludePrereleases);
        Assert.True(f.Shell.SettingsNav.IsActive);
        Assert.Equal("✓ git version 2.47.1", settings.GitVersion);
        Assert.Equal("/usr/bin/git", settings.GitPath);
    }

    [Fact]
    public async Task Settings_ReachableWithoutAModsFolder()
    {
        var f = new ShellFixture(null);
        await f.Shell.InitializeAsync();
        if (f.Shell.ModsDirectory is not null)
            return; // a real RimWorld install was discovered on this machine

        Assert.True(f.Shell.ShowChooseFolder);
        f.Shell.Navigate(Page.Settings);
        Assert.True(f.Shell.ShowPage);
        Assert.False(f.Shell.ShowChooseFolder);
    }
}
