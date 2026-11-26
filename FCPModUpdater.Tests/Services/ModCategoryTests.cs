using FCPModUpdater.Gui.ViewModels;
using FCPModUpdater.Settings;
using FCPModUpdater.Tests.Gui;
using FCPModUpdater.UI;
using NSubstitute;

namespace FCPModUpdater.Tests.Services;

public class ModCategoryResolveTests
{
    [Fact]
    public void Resolve_WithoutSettings_ReturnsBuiltIns()
    {
        IReadOnlyList<ModCategory> categories = ModCategories.Resolve(null);

        Assert.Equal(["fcp", "escp"], categories.Select(c => c.Id));
        Assert.Equal("rimworld-mod", categories[0].RequiredTopic);
        Assert.Null(categories[1].RequiredTopic);
        Assert.Equal("ElderScrollsCollaborationProject", categories[1].Organization);
    }

    [Fact]
    public void Resolve_SavedEntryOverridesBuiltIn_AndKeepsMissingNames()
    {
        var settings = new AppSettings
        {
            Categories = [new ModCategory("ESCP", "", "") { RequiredTopic = "rimworld-mod", Enabled = false }]
        };

        ModCategory escp = ModCategories.Resolve(settings)[1];

        Assert.Equal("escp", escp.Id);
        Assert.Equal("ESCP", escp.DisplayName);
        Assert.Equal("ElderScrollsCollaborationProject", escp.Organization);
        Assert.Equal("rimworld-mod", escp.RequiredTopic);
        Assert.False(escp.Enabled);
        Assert.Equal(["fcp"], ModCategories.Effective(settings).Select(c => c.Id));
    }

    [Fact]
    public void Resolve_UnknownId_AddsCategory_AndSkipsOnesWithoutOrganization()
    {
        var settings = new AppSettings
        {
            Categories = [new ModCategory("other", "", "SomeOrg"), new ModCategory("broken", "Broken", "")]
        };

        IReadOnlyList<ModCategory> categories = ModCategories.Resolve(settings);

        Assert.Equal(["fcp", "escp", "other"], categories.Select(c => c.Id));
        Assert.Equal("other", categories[2].DisplayName);
    }

    [Fact]
    public void WithEnabled_RoundTripsThroughJson()
    {
        AppSettings settings = ModCategories.WithEnabled(new AppSettings(), "escp", false);
        var json = JsonSerializer.Serialize(settings);
        AppSettings back = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(["fcp"], ModCategories.Effective(back).Select(c => c.Id));
        Assert.Equal(["fcp", "escp"], ModCategories.Effective(ModCategories.WithEnabled(back, "escp", true)).Select(c => c.Id));
    }

    [Fact]
    public void SettingsEditor_CategoryKey_TogglesCategory()
    {
        var (off, error) = SettingsEditor.Apply(new AppSettings(), "category.escp.enabled", "false");
        Assert.Null(error);
        Assert.False(ModCategories.Resolve(off)[1].Enabled);

        var (on, _) = SettingsEditor.Apply(off!, "category.escp.enabled", null);
        Assert.True(ModCategories.Resolve(on)[1].Enabled);

        Assert.NotNull(SettingsEditor.Apply(new AppSettings(), "category.nope.enabled", "true").Error);
        Assert.NotNull(SettingsEditor.Apply(new AppSettings(), "category.escp.enabled", "maybe").Error);
    }

    [Fact]
    public void OwnsRemoteUrl_MatchesOrgCaseInsensitively()
    {
        Assert.True(ModCategories.Escp.OwnsRemoteUrl("https://github.com/elderscrollscollaborationproject/ESCP-Core.git"));
        Assert.False(ModCategories.Escp.OwnsRemoteUrl("https://github.com/FalloutCollaborationProject/FCP-Core.git"));
        Assert.Equal("fcp", ModCategories.FindByRemoteUrl(ModCategories.BuiltIn,
            "git@github.com:FalloutCollaborationProject/FCP-Core.git".Replace(':', '/'))?.Id);
    }
}

public class ModCatalogCategoryTests
{
    private static RemoteRepo Repo(string name, string category, params string[] topics) =>
        new(name, $"https://github.com/x/{name}.git", "main", null, "", topics) { CategoryId = category };

    [Fact]
    public void GetAvailableInstallRepos_FiltersByCategory()
    {
        IReadOnlyList<RemoteRepo> repos = [Repo("FCP-A", "fcp", "rimworld-mod"), Repo("ESCP-A", "escp"), Repo("ESCP-B", "escp")];

        Assert.Equal(["ESCP-A", "ESCP-B"], ModCatalog.GetAvailableInstallRepos(repos, [], "escp").Select(r => r.Name));
        Assert.Equal(3, ModCatalog.GetAvailableInstallRepos(repos, []).Count);
    }

    [Fact]
    public void IsNotRecommended_UsesTheRepoCategoryTopic()
    {
        Assert.True(ModCatalog.IsNotRecommended(Repo("A", "fcp", ModCatalog.NotRecommendedTopic)));
        // ESCP has no not-recommended topic, so the FCP tag means nothing there.
        Assert.False(ModCatalog.IsNotRecommended(Repo("B", "escp", ModCatalog.NotRecommendedTopic)));

        ModCategory custom = ModCategories.Escp with { NotRecommendedTopic = "escp-old" };
        Assert.True(ModCatalog.IsNotRecommended(Repo("C", "escp", "escp-old"), [custom]));
    }

    [Fact]
    public void GetVersionBranchName_ToolsSnapshotRuleIsFcpOnly()
    {
        GameVersion v16 = GameVersion.Parse("1.6")!;
        Assert.Equal("rimworld-1.6-snapshot", ModCatalog.GetVersionBranchName("FCP-Tools", v16, "fcp"));
        Assert.Equal("rimworld-1.6-snapshot", ModCatalog.GetVersionBranchName("FCP-Tools", v16));
        Assert.Equal("rimworld-1.6", ModCatalog.GetVersionBranchName("FCP-Tools", v16, "escp"));
    }
}

public class GitHubApiServiceCategoryTests
{
    private sealed class OrgHandler(Dictionary<string, object[]> byOrg) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requested.Add(path);
            var org = path.Split('/')[2];
            return Task.FromResult(byOrg.TryGetValue(org, out var repos)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(repos)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static object Json(string name, string[] topics, bool archived = false, bool fork = false) => new
    {
        name, clone_url = $"https://github.com/x/{name}.git", default_branch = "main", description = (string?)null,
        html_url = $"https://github.com/x/{name}", topics, archived, fork
    };

    private static (GitHubApiService Service, OrgHandler Handler) Make(IReadOnlyList<ModCategory> categories)
    {
        var handler = new OrgHandler(new()
        {
            ["FalloutCollaborationProject"] = [Json("FCP-Core", ["rimworld-mod"]), Json("FCP-Site", [])],
            ["ElderScrollsCollaborationProject"] =
                [Json("ESCP-Core", []), Json("ESCP-Old", [], archived: true), Json("ESCP-Fork", [], fork: true)],
        });
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") };
        return (new GitHubApiService(client, () => categories), handler);
    }

    [Fact]
    public async Task FetchesEveryCategory_FiltersEachByItsRules_AndTagsCategory()
    {
        var (service, handler) = Make(ModCategories.BuiltIn);

        IReadOnlyList<RemoteRepo> repos = await service.GetOrganizationReposAsync(TestContext.Current.CancellationToken);

        Assert.Equal([("FCP-Core", "fcp"), ("ESCP-Core", "escp")], repos.Select(r => (r.Name, r.CategoryId)));
        Assert.Equal(2, handler.Requested.Count);
    }

    [Fact]
    public async Task DisabledCategoryIsNotFetched()
    {
        var (service, handler) = Make([ModCategories.Escp]);

        IReadOnlyList<RemoteRepo> repos = await service.GetOrganizationReposAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["ESCP-Core"], repos.Select(r => r.Name));
        Assert.All(handler.Requested, p => Assert.Contains("ElderScrollsCollaborationProject", p));
    }

    [Fact]
    public async Task SettingsStoreDrivesCategories()
    {
        var store = SettingsStore.InMemory(ModCategories.WithEnabled(new AppSettings(), "fcp", false));
        var handler = new OrgHandler(new() { ["ElderScrollsCollaborationProject"] = [Json("ESCP-Core", [])] });
        var service = new GitHubApiService(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") }, store);

        IReadOnlyList<RemoteRepo> repos = await service.GetOrganizationReposAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["escp"], service.Categories.Select(c => c.Id));
        Assert.Equal("ESCP-Core", Assert.Single(repos).Name);
    }

    [Fact]
    public async Task GetRepoByName_PrefersRequestedCategory()
    {
        var handler = new OrgHandler(new()
        {
            ["FalloutCollaborationProject"] = [Json("Shared", ["rimworld-mod"])],
            ["ElderScrollsCollaborationProject"] = [Json("Shared", [])],
        });
        var service = new GitHubApiService(new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com") },
            () => ModCategories.BuiltIn);
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Equal("escp", (await service.GetRepoByNameAsync("shared", "escp", ct))?.CategoryId);
        Assert.Equal("fcp", (await service.GetRepoByNameAsync("shared", ct))?.CategoryId);
    }
}

public class DiscoveryCategoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_cat_test_{Guid.NewGuid():N}");
    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly IGitHubApiService _gitHub = Substitute.For<IGitHubApiService>();

    public DiscoveryCategoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            GitService.DeleteDirectory(_dir);
    }

    [Fact]
    public async Task EscpRemote_IsDetectedAndCategorised_LocalFolderGetsRepoCategory()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.Combine(_dir, "ESCP-Core"));
        Directory.CreateDirectory(Path.Combine(_dir, "ESCP-Races-main"));
        _gitHub.Categories.Returns(ModCategories.BuiltIn);
        _gitHub.GetOrganizationReposAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new RemoteRepo("ESCP-Core", "u", "main", null, "", []) { CategoryId = "escp" },
            new RemoteRepo("ESCP-Races", "u", "main", null, "", []) { CategoryId = "escp" },
        ]);
        _git.IsGitRepositoryAsync(Path.Combine(_dir, "ESCP-Core"), Arg.Any<CancellationToken>()).Returns(true);
        _git.GetRemoteUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/ElderScrollsCollaborationProject/ESCP-Core.git");
        _git.GetCurrentBranchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("main");
        _git.FetchAsync(Arg.Any<string>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(new GitOperationResult(true, 0, null));
        _git.GetCommitDifferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Behind: 1, Ahead: 0));

        IReadOnlyList<InstalledMod> mods = await new ModDiscoveryService(_git, _gitHub).DiscoverModsAsync(_dir, ct: ct);

        InstalledMod git = Assert.Single(mods, m => m.Name == "ESCP-Core");
        Assert.Equal((ModSource.Git, ModStatus.Behind, "escp"), (git.Source, git.Status, git.CategoryId));
        InstalledMod local = Assert.Single(mods, m => m.Name == "ESCP-Races-main");
        Assert.Equal(("ESCP-Races", "escp"), (local.MatchedRepoName, local.CategoryId));
    }
}

public sealed class InstallCategoryFilterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_cat_gui_{Guid.NewGuid():N}");
    private readonly ShellFixture _f;

    public InstallCategoryFilterTests()
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
    public async Task CategoryDropdown_FiltersReposAndSelection()
    {
        _f.Repos.AddRange(
        [
            new RemoteRepo("FCP-A", "u", "main", null, "", ["rimworld-mod"]) { CategoryId = "fcp" },
            new RemoteRepo("ESCP-A", "u", "main", null, "", []) { CategoryId = "escp" },
            new RemoteRepo("ESCP-B", "u", "main", null, "", []) { CategoryId = "escp" },
        ]);
        await _f.Shell.InitializeAsync();
        InstallViewModel page = _f.Shell.Install;

        Assert.True(page.HasCategories);
        Assert.Equal(["All categories", "FCP", "ESCP"], page.Categories.Select(c => c.Label));
        Assert.Equal(3, page.Repos.Count);
        Assert.Equal(["FCP", "ESCP"], page.RepoGroups.Select(g => g.Label));

        page.Repos.First(r => r.Name == "FCP-A").IsChecked = true;
        page.SelectedCategory = page.Categories.Single(c => c.Id == "escp");

        Assert.Equal(["ESCP-A", "ESCP-B"], page.Repos.Select(r => r.Name));
        Assert.Equal(["ESCP"], page.RepoGroups.Select(g => g.Label));
        Assert.Equal("2 mod repositories in ESCP · 2 not installed", page.Subheading);
        Assert.Equal(0, page.SelectedCount);

        // The choice survives a rescan.
        await _f.Shell.RescanAsync();
        Assert.Equal("escp", page.SelectedCategory.Id);
    }

    private static RemoteRepo R(string name, string category) =>
        new(name, "u", "main", null, $"https://github.com/org/{name}", []) { CategoryId = category };

    [Fact]
    public async Task CollapsedInstallGroup_StillInstallsItsCheckedCards_AndStaysCollapsed()
    {
        _f.Repos.AddRange([R("FCP-A", "fcp"), R("ESCP-A", "escp")]);
        _f.Git.CloneAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IProgress<string>?>(),
            Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>()).Returns((true, null));
        await _f.Shell.InitializeAsync();
        InstallViewModel page = _f.Shell.Install;

        page.Repos.Single(r => r.Name == "ESCP-A").IsChecked = true;
        page.RepoGroups.Single(g => g.Label == "ESCP").ToggleCommand.Execute(null);
        Assert.Equal(1, page.SelectedCount);

        await _f.Shell.RescanAsync();
        Assert.False(page.RepoGroups.Single(g => g.Label == "ESCP").IsExpanded);
        Assert.True(page.RepoGroups.Single(g => g.Label == "FCP").IsExpanded);

        page.ConfirmCommand.Execute(null);
        var dialog = Assert.IsType<OperationDialogViewModel>(_f.Shell.Dialog);
        Assert.Contains("ESCP-A", dialog.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task OpenRepo_OpensTheRepoPage()
    {
        _f.Repos.Add(R("ESCP-A", "escp"));
        await _f.Shell.InitializeAsync();
        var platform = Substitute.For<FCPModUpdater.Gui.Services.IPlatformServices>();
        _f.Shell.Platform = platform;

        _f.Shell.Install.OpenRepoCommand.Execute(_f.Shell.Install.Repos.Single());

        platform.Received(1).OpenUrl("https://github.com/org/ESCP-A");
        Assert.Equal("https://github.com/ElderScrollsCollaborationProject/X",
            new RepoCardViewModel(new RemoteRepo("X", "u", "main", null, "", []) { CategoryId = "escp" }).Url);
    }

    [Fact]
    public async Task InstalledTable_GroupsReverseAlphabetically_AndCollapseKeepsRowsCheckable()
    {
        InstalledMod Mod(string name, string? category) => ShellFixture.GitMod(name, ModStatus.Behind, _dir, behind: 1)
            with { CategoryId = category };
        _f.Mods.AddRange([Mod("E1", "escp"), Mod("F1", "fcp"), Mod("F2", "fcp"), Mod("X1", null)]);
        await _f.Shell.InitializeAsync();
        InstalledModsViewModel page = _f.Shell.Installed;

        Assert.Equal(["FCP", "F1", "F2", "ESCP", "E1", "Other", "X1"], page.TableItems.Select(Label));
        Assert.Equal(4, page.CheckedCount);

        var fcp = page.TableItems.OfType<ModGroupViewModel>().First();
        fcp.ToggleCommand.Execute(null);

        Assert.Equal(["FCP", "ESCP", "E1", "Other", "X1"], page.TableItems.Select(Label));
        Assert.Equal(4, page.Rows.Count);
        Assert.Equal(4, page.CheckedCount);

        // Headers can't be selected.
        ModRowViewModel? before = page.SelectedRow;
        page.SelectedTableItem = fcp;
        Assert.Same(before, page.SelectedRow);

        await _f.Shell.RescanAsync();
        Assert.False(page.TableItems.OfType<ModGroupViewModel>().First().IsExpanded);
    }

    private static string Label(object item) => item switch
    {
        CategoryGroupViewModel g => g.Label,
        ModRowViewModel r => r.Name,
        _ => "?"
    };

    [Fact]
    public void Group_PutsCustomCategoryOrderedByNameDescending_OtherLast()
    {
        ModCategory zeta = new("zeta", "Zeta", "ZetaOrg");
        var groups = CategoryGroups.Group(["a", "b", "c", "d"],
            s => s switch { "a" => "escp", "b" => "zeta", "c" => "fcp", _ => null },
            [ModCategories.Fcp, ModCategories.Escp, zeta]).Select(g => g.Category?.DisplayName ?? "Other");

        Assert.Equal(["Zeta", "FCP", "ESCP", "Other"], groups);
    }
}

public sealed class InstallSelectRecommendedPerCategoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_cat_sel_{Guid.NewGuid():N}");
    private readonly ShellFixture _f;

    public InstallSelectRecommendedPerCategoryTests()
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
    public async Task SelectRecommended_OnlyTouchesItsOwnCategory()
    {
        _f.Repos.AddRange(
        [
            new RemoteRepo("FCP-A", "u", "main", null, "", ["rimworld-mod"]) { CategoryId = "fcp" },
            new RemoteRepo("FCP-Old", "u", "main", null, "", ["rimworld-mod", ModCatalog.NotRecommendedTopic]) { CategoryId = "fcp" },
            new RemoteRepo("ESCP-A", "u", "main", null, "", []) { CategoryId = "escp" },
        ]);
        await _f.Shell.InitializeAsync();
        InstallViewModel page = _f.Shell.Install;

        page.RepoGroups.Single(g => g.Label == "FCP").SelectRecommendedCommand.Execute(null);

        Assert.Equal(["FCP-A"], page.Repos.Where(r => r.IsChecked).Select(r => r.Name));
        Assert.Equal("Install 1 mod", page.Cta);

        page.RepoGroups.Single(g => g.Label == "ESCP").SelectRecommendedCommand.Execute(null);
        Assert.Equal(["ESCP-A", "FCP-A"], page.Repos.Where(r => r.IsChecked).Select(r => r.Name).Order());

        // Pressing it again deselects that category only.
        RepoGroupViewModel fcp = page.RepoGroups.Single(g => g.Label == "FCP");
        Assert.Equal("Deselect all", fcp.SelectButtonText);
        fcp.SelectRecommendedCommand.Execute(null);
        Assert.Equal(["ESCP-A"], page.Repos.Where(r => r.IsChecked).Select(r => r.Name));
        Assert.Equal("Select all recommended", fcp.SelectButtonText);
    }
}
