using FCPModUpdater.Gui.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace FCPModUpdater.Tests.Gui;

public sealed class AppUpdateBannerTests
{
    private static readonly UpdateCheckResult Release = new("1.0.0", "1.1.0",
        "https://github.com/x/y/releases/tag/v1.1.0", "v1.1.0", DateTimeOffset.UtcNow, false);

    private static IAppUpdater Installed(string? available = "1.1.0")
    {
        var updater = Substitute.For<IAppUpdater>();
        updater.IsInstalled.Returns(true);
        updater.PrepareAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(available);
        return updater;
    }

    [Fact]
    public async Task InstalledApp_WithUpdate_OffersUpdateNow_AndNoScript()
    {
        var f = new ShellFixture(null, appUpdater: Installed());
        var banner = f.Shell.Installed;
        banner.AppUpdate = Release;
        await banner.PrepareInAppUpdateAsync(false);

        Assert.True(banner.ShowUpdateNow);
        Assert.False(banner.HasBannerCommand);
        Assert.False(banner.ShowGetInstaller);
    }

    [Fact]
    public async Task InstalledApp_VelopackFindsNothing_FallsBackToReleasePage()
    {
        var f = new ShellFixture(null, appUpdater: Installed(available: null));
        var banner = f.Shell.Installed;
        banner.AppUpdate = Release;
        await banner.PrepareInAppUpdateAsync(false);

        Assert.False(banner.ShowUpdateNow);
    }

    [Fact]
    public async Task PortableCopy_ShowsScriptAndInstallerOffer_NeverAsksVelopack()
    {
        var updater = Substitute.For<IAppUpdater>();
        var f = new ShellFixture(null, appUpdater: updater);
        var banner = f.Shell.Installed;
        banner.AppUpdate = Release;
        await banner.PrepareInAppUpdateAsync(true);

        Assert.False(banner.ShowUpdateNow);
        Assert.Equal(UpdateCheckService.GetUpdaterCommand() is not null, banner.HasBannerCommand);
        Assert.True(banner.ShowGetInstaller);
        await updater.DidNotReceiveWithAnyArgs().PrepareAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UpdateNow_ReportsProgress_ThenApplies()
    {
        IAppUpdater updater = Installed();
        updater.DownloadAsync(Arg.Any<Action<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => { ci.Arg<Action<int>>()(55); return Task.CompletedTask; });
        var f = new ShellFixture(null, appUpdater: updater);
        var banner = f.Shell.Installed;
        banner.AppUpdate = Release;
        await banner.PrepareInAppUpdateAsync(false);

        await banner.UpdateNowCommand.ExecuteAsync(null);

        Assert.Equal(55, banner.UpdateProgress);
        updater.Received(1).ApplyAndRestart();
        Assert.False(banner.IsUpdating);
    }

    [Fact]
    public async Task UpdateNow_DownloadFails_ShowsErrorAndDoesNotApply()
    {
        IAppUpdater updater = Installed();
        updater.DownloadAsync(Arg.Any<Action<int>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("offline"));
        var f = new ShellFixture(null, appUpdater: updater);
        var banner = f.Shell.Installed;
        banner.AppUpdate = Release;
        await banner.PrepareInAppUpdateAsync(false);

        await banner.UpdateNowCommand.ExecuteAsync(null);

        Assert.Contains("offline", banner.UpdateError);
        Assert.False(banner.IsUpdating);
        Assert.True(banner.ShowUpdateNow);
        updater.DidNotReceive().ApplyAndRestart();
    }

    [Fact]
    public async Task UpdateNow_WhileAnOperationRuns_IsRefused()
    {
        IAppUpdater updater = Installed();
        var f = new ShellFixture(null, appUpdater: updater);
        var banner = f.Shell.Installed;
        banner.AppUpdate = Release;
        await banner.PrepareInAppUpdateAsync(false);
        f.Shell.ShowUninstallDialog([ShellFixture.LocalMod("A", "A", Path.GetTempPath())]);

        await banner.UpdateNowCommand.ExecuteAsync(null);

        Assert.NotNull(banner.UpdateError);
        await updater.DidNotReceiveWithAnyArgs().DownloadAsync(null!, TestContext.Current.CancellationToken);
    }
}
