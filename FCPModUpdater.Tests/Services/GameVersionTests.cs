using CliWrap;
using CliWrap.Buffered;
using FCPModUpdater.Settings;
using FCPModUpdater.UI;
using NSubstitute;

namespace FCPModUpdater.Tests.Services;

public sealed class GameVersionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_gv_{Guid.NewGuid():N}");

    public GameVersionTests() => Directory.CreateDirectory(Path.Combine(_dir, "Mods"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Theory]
    [InlineData("1.6.4630 rev467", 1, 6, 4630)]
    [InlineData("1.5", 1, 5, null)]
    [InlineData("  1.6.0\n", 1, 6, 0)]
    public void Parse_ReadsMajorMinorBuild(string text, int major, int minor, int? build)
    {
        GameVersion? v = GameVersion.Parse(text);
        Assert.NotNull(v);
        Assert.Equal((major, minor, build), (v.Major, v.Minor, v.Build));
        Assert.Equal($"{major}.{minor}", v.Short);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void Parse_RejectsNonVersions(string? text) => Assert.Null(GameVersion.Parse(text));

    [Fact]
    public void Detect_ReadsVersionTxtNextToMods()
    {
        File.WriteAllText(Path.Combine(_dir, "Version.txt"), "1.6.4630 rev467\n");
        Assert.Equal("1.6", GameVersionService.Detect(Path.Combine(_dir, "Mods"))?.Short);
        Assert.Equal("1.6", GameVersionService.Detect(Path.Combine(_dir, "Mods") + Path.DirectorySeparatorChar)?.Short);
    }

    [Fact]
    public void Detect_MissingOrGarbageFile_IsNull()
    {
        Assert.Null(GameVersionService.Detect(Path.Combine(_dir, "Mods")));
        File.WriteAllText(Path.Combine(_dir, "Version.txt"), "not a version");
        Assert.Null(GameVersionService.Detect(Path.Combine(_dir, "Mods")));
    }

    [Fact]
    public void Resolve_SavedOverrideWins()
    {
        File.WriteAllText(Path.Combine(_dir, "Version.txt"), "1.6.1");
        var mods = Path.Combine(_dir, "Mods");
        Assert.Equal(("1.6", GameVersionSource.Detected), Short(GameVersionService.Resolve(mods, new AppSettings())));
        Assert.Equal(("1.5", GameVersionSource.Settings),
            Short(GameVersionService.Resolve(mods, new AppSettings { GameVersionOverride = "1.5" })));
        Assert.Equal((null, GameVersionSource.None), Short(GameVersionService.Resolve(_dir, null)));
    }

    private static (string?, GameVersionSource) Short((GameVersion? V, GameVersionSource S) r) => (r.V?.Short, r.S);

    [Theory]
    [InlineData("FCP-Core", "1.6", "rimworld-1.6")]
    [InlineData("FCP-Tools", "1.6", "rimworld-1.6-snapshot")]
    [InlineData("fcp-tools", "1.6", "rimworld-1.6-snapshot")]
    [InlineData("FCP-Tools", "1.7", "rimworld-1.7")]
    [InlineData("FCP-Core", "1.5", "rimworld-1.5")]
    public void VersionBranchName(string repo, string version, string expected) =>
        Assert.Equal(expected, ModCatalog.GetVersionBranchName(repo, GameVersion.Parse(version)!));

    [Fact]
    public void UpdateableMods_SkipWrongBranchUnlessAsked()
    {
        InstalledMod ok = new() { Name = "a", Path = "a", Source = ModSource.Git, Status = ModStatus.Behind };
        InstalledMod wrong = ok with { Name = "b", VersionBranch = "rimworld-1.6", VersionMismatch = VersionMismatch.OnMainline };
        Assert.Equal(["a"], ModCatalog.GetUpdateableMods([ok, wrong]).Select(m => m.Name));
        Assert.Equal(["a", "b"], ModCatalog.GetUpdateableMods([ok, wrong], includeMismatched: true).Select(m => m.Name));
        Assert.Equal(["b"], ModCatalog.GetVersionMismatchedMods([ok, wrong]).Select(m => m.Name));
    }

    [Theory]
    [InlineData("rimworld-1.6", "1.6")]
    [InlineData("rimworld-1.6-snapshot", "1.6")]
    [InlineData("main", null)]
    [InlineData("rimworld-1.6-wip", null)]
    public void ParseVersionBranch(string branch, string? expected) =>
        Assert.Equal(expected, ModCatalog.ParseVersionBranch(branch)?.Short);

    [Fact]
    public void OlderVersionBranch_StillUpdateable()
    {
        InstalledMod mod = new() { Name = "a", Path = "a", Source = ModSource.Git, Status = ModStatus.Behind,
            VersionBranch = "main", VersionMismatch = VersionMismatch.OnOlderVersionBranch };
        Assert.Single(ModCatalog.GetUpdateableMods([mod]));
        Assert.Single(ModCatalog.GetVersionMismatchedMods([mod]));
    }

    [Fact]
    public void SettingsEditor_GameVersion()
    {
        Assert.Equal("1.6", SettingsEditor.Apply(new AppSettings(), SettingsEditor.GameVersion, "1.6.4630").Result?.GameVersionOverride);
        Assert.Null(SettingsEditor.Apply(new AppSettings { GameVersionOverride = "1.6" }, SettingsEditor.GameVersion, null).Result?.GameVersionOverride);
        Assert.NotNull(SettingsEditor.Apply(new AppSettings(), SettingsEditor.GameVersion, "abc").Error);
    }
}

/// <summary>Real repos: origin has main (moved on to 1.7) and rimworld-1.6.</summary>
[Trait("Category", "Integration")]
public sealed class VersionBranchIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_vb_{Guid.NewGuid():N}");
    private readonly string _mods;
    private readonly string _origin;
    private readonly GitService _git = new();

    public VersionBranchIntegrationTests()
    {
        _mods = Path.Combine(_dir, "RimWorld", "Mods");
        Directory.CreateDirectory(_mods);
        File.WriteAllText(Path.Combine(_dir, "RimWorld", "Version.txt"), "1.6.4630 rev467");
        _origin = Path.Combine(_dir, "origin", "FCP-Test");
        Directory.CreateDirectory(_origin);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static async Task<string> Git(string workingDir, string args)
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

    private static async Task Commit(string repo, string message)
    {
        await File.AppendAllTextAsync(Path.Combine(repo, "file.txt"), message + "\n");
        await Git(repo, "add .");
        await Git(repo, $"commit -m \"{message}\"");
    }

    private async Task<string> SetUpAsync(bool withVersionBranch = true)
    {
        await Git(_origin, "init -b main");
        await Commit(_origin, "for 1.6");
        if (withVersionBranch)
            await Git(_origin, "branch rimworld-1.6");
        await Commit(_origin, "for 1.7");
        await Git(_mods, $"clone \"{_origin}\" FCP-Test");
        return Path.Combine(_mods, "FCP-Test");
    }

    private async Task<InstalledMod> ScanAsync()
    {
        var gitHub = Substitute.For<IGitHubApiService>();
        gitHub.GetOrganizationReposAsync(Arg.Any<CancellationToken>()).Returns(
            [new RemoteRepo("FCP-Test", _origin, "main", null, "", [])]);
        var service = new ModDiscoveryService(_git, gitHub, SettingsStore.InMemory());
        IReadOnlyList<InstalledMod> mods = await service.DiscoverModsAsync(_mods, ct: TestContext.Current.CancellationToken);
        return Assert.Single(mods);
    }

    [Fact]
    public async Task OnMain_IsOnMainline()
    {
        await SetUpAsync();
        InstalledMod mod = await ScanAsync();
        Assert.Equal(("rimworld-1.6", VersionMismatch.OnMainline), (mod.VersionBranch, mod.VersionMismatch));
    }

    [Fact]
    public async Task OnVersionBranch_IsFine_AndSwitchFixesMainline()
    {
        var clone = await SetUpAsync();
        InstalledMod onMain = await ScanAsync();

        var (ok, error) = await ModOperations.SwitchToVersionBranchAsync(_git, onMain);
        Assert.True(ok, error);
        Assert.Equal("for 1.6", await Git(clone, "log -1 --format=%s"));

        InstalledMod after = await ScanAsync();
        Assert.Equal(("rimworld-1.6", VersionMismatch.None), (after.Branch, after.VersionMismatch));
    }

    [Fact]
    public async Task OnVersionBranchWithNewerCommit_IsAhead()
    {
        var clone = await SetUpAsync();
        await Git(clone, "checkout rimworld-1.6");
        await Git(clone, "reset --hard origin/main");
        InstalledMod mod = await ScanAsync();
        Assert.Equal(VersionMismatch.AheadOfVersionBranch, mod.VersionMismatch);
    }

    [Fact]
    public async Task NoVersionBranch_NoWarning()
    {
        await SetUpAsync(withVersionBranch: false);
        InstalledMod mod = await ScanAsync();
        Assert.Equal((null, VersionMismatch.None), (mod.VersionBranch, mod.VersionMismatch));
    }

    [Fact]
    public async Task GameUpdatedPastBranch_PointsAtMain()
    {
        var clone = await SetUpAsync();
        await Git(clone, "checkout rimworld-1.6");
        File.WriteAllText(Path.Combine(_dir, "RimWorld", "Version.txt"), "1.7.5000");

        InstalledMod mod = await ScanAsync();
        Assert.Equal(("main", VersionMismatch.OnOlderVersionBranch), (mod.VersionBranch, mod.VersionMismatch));

        var (ok, error) = await ModOperations.SwitchToVersionBranchAsync(_git, mod);
        Assert.True(ok, error);
        Assert.Equal("for 1.7", await Git(clone, "log -1 --format=%s"));
        Assert.Equal(VersionMismatch.None, (await ScanAsync()).VersionMismatch);
    }

    [Fact]
    public async Task GameUpdatedPastBranch_PrefersNewerVersionBranch()
    {
        var clone = await SetUpAsync();
        await Git(_origin, "branch rimworld-1.7");
        await Git(clone, "fetch");
        await Git(clone, "checkout rimworld-1.6");
        File.WriteAllText(Path.Combine(_dir, "RimWorld", "Version.txt"), "1.7.5000");

        InstalledMod mod = await ScanAsync();
        Assert.Equal(("rimworld-1.7", VersionMismatch.OnOlderVersionBranch), (mod.VersionBranch, mod.VersionMismatch));
    }

    [Fact]
    public async Task Install_ChecksOutVersionBranch()
    {
        await SetUpAsync();
        var repo = new RemoteRepo("Fresh", _origin, "main", null, "", []);
        var (ok, error) = await ModOperations.InstallAsync(_git, repo, _mods, gameVersion: GameVersion.Parse("1.6"),
            ct: TestContext.Current.CancellationToken);
        Assert.True(ok, error);
        Assert.Equal("rimworld-1.6", await Git(Path.Combine(_mods, "Fresh"), "rev-parse --abbrev-ref HEAD"));
    }
}
