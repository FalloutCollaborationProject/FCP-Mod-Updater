using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace FCPModUpdater.Tests.Services;

public class DetermineStatusTests
{
    [Fact]
    public void DetachedHead_ReturnsUnknown()
    {
        Assert.Equal(ModStatus.Unknown, ModDiscoveryService.DetermineStatus(0, 0, false, null));
    }

    [Fact]
    public void UpToDate_ReturnsUpToDate()
    {
        Assert.Equal(ModStatus.UpToDate, ModDiscoveryService.DetermineStatus(0, 0, false, "main"));
    }

    [Fact]
    public void LocalChanges_TrumpsEverything()
    {
        Assert.Equal(ModStatus.LocalChanges, ModDiscoveryService.DetermineStatus(5, 3, true, "main"));
    }

    [Fact]
    public void BehindOnly_ReturnsBehind()
    {
        Assert.Equal(ModStatus.Behind, ModDiscoveryService.DetermineStatus(3, 0, false, "main"));
    }

    [Fact]
    public void AheadOnly_ReturnsAhead()
    {
        Assert.Equal(ModStatus.Ahead, ModDiscoveryService.DetermineStatus(0, 2, false, "main"));
    }

    [Fact]
    public void Diverged_ReturnsDiverged()
    {
        Assert.Equal(ModStatus.Diverged, ModDiscoveryService.DetermineStatus(3, 2, false, "main"));
    }

    [Fact]
    public void LocalChangesAndBehind_ReturnsLocalChanges()
    {
        Assert.Equal(ModStatus.LocalChanges, ModDiscoveryService.DetermineStatus(3, 0, true, "main"));
    }

    [Fact]
    public void LocalChangesAndDiverged_ReturnsLocalChanges()
    {
        Assert.Equal(ModStatus.LocalChanges, ModDiscoveryService.DetermineStatus(3, 2, true, "main"));
    }

    [Fact]
    public void DetachedHeadWithChanges_ReturnsUnknown()
    {
        // null branch check comes first
        Assert.Equal(ModStatus.Unknown, ModDiscoveryService.DetermineStatus(0, 0, true, null));
    }
}

public class DiscoverModsAsyncTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IGitService _gitService;
    private readonly IGitHubApiService _gitHubApiService;
    private readonly ModDiscoveryService _service;

    public DiscoverModsAsyncTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fcp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gitService = Substitute.For<IGitService>();
        _gitHubApiService = Substitute.For<IGitHubApiService>();
        _service = new ModDiscoveryService(_gitService, _gitHubApiService);
    }

    void IDisposable.Dispose()
    {
        GC.SuppressFinalize(this);
        
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private void CreateModFolder(string name)
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, name));
    }

    private static RemoteRepo MakeRepo(string name)
    {
        return new RemoteRepo(
            Name: name, 
            CloneUrl: $"https://github.com/FalloutCollaborationProject/{name}.git",
            DefaultBranch: "main", 
            Description: null, 
            HtmlUrl: $"https://github.com/FalloutCollaborationProject/{name}",
            Topics: ["rimworld-mod"]);
    }

    [Fact]
    public async Task NonExistentDirectory_ReturnsEmpty()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync("/nonexistent/path/xyz", ct: token);
        Assert.Empty(result);
    }

    [Fact]
    public async Task NonGitFolderMatchingOrg_ReturnsNonGitMod()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("FCP-Weapons");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Weapons")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);

        Assert.Single(result);
        Assert.Equal("FCP-Weapons", result[0].Name);
        Assert.Equal(ModSource.Local, result[0].Source);
        Assert.Equal(ModStatus.NonGit, result[0].Status);
    }

    [Fact]
    public async Task NonGitFolderNotMatching_ReturnsEmpty()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("SomeRandomMod");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Weapons")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GitRepoWithFcpRemote_ReturnsFullMod()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("FCP-Weapons");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Weapons")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetRemoteUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/FalloutCollaborationProject/FCP-Weapons.git");
        _gitService.GetCurrentBranchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("main");
        _gitService.GetCurrentCommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommitInfo("abc123", "abc1", "Init", "Author", DateTimeOffset.UtcNow));
        _gitService.HasLocalChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _gitService.FetchAsync(Arg.Any<string>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetCommitDifferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Behind: 0, Ahead: 0));

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);

        Assert.Single(result);
        Assert.Equal(ModSource.Git, result[0].Source);
        Assert.Equal(ModStatus.UpToDate, result[0].Status);
    }

    [Fact]
    public async Task GitRepoWithNoRemote_MatchesOrg_ReturnsError()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("FCP-Weapons");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Weapons")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetRemoteUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);

        Assert.Single(result);
        Assert.Equal(ModStatus.Error, result[0].Status);
        Assert.Contains("no remote", result[0].ErrorMessage!);
    }

    [Fact]
    public async Task GitRepoNonFcpRemote_FolderMatchesOrg_ReturnsFullMod()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("FCP-Weapons");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Weapons")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetRemoteUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/SomeUser/FCP-Weapons.git");
        _gitService.GetCurrentBranchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("main");
        _gitService.GetCurrentCommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommitInfo("abc123", "abc1", "Init", "Author", DateTimeOffset.UtcNow));
        _gitService.HasLocalChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _gitService.FetchAsync(Arg.Any<string>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetCommitDifferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Behind: 0, Ahead: 0));

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);
        Assert.Single(result);
        Assert.Equal(ModSource.Git, result[0].Source);
    }

    [Fact]
    public async Task GitRepoNonFcpRemote_NoMatch_ReturnsEmpty()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("SomeRandomMod");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Weapons")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetRemoteUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/SomeUser/SomeRandomMod.git");

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GitOperationThrows_ReturnsErrorMod()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("FCP-Weapons");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Weapons")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetRemoteUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/FalloutCollaborationProject/FCP-Weapons.git");
        _gitService.GetCurrentBranchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("git broke"));
        _gitService.GetCurrentCommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommitInfo("abc123", "abc1", "Init", "Author", DateTimeOffset.UtcNow));
        _gitService.HasLocalChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _gitService.FetchAsync(Arg.Any<string>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);

        Assert.Single(result);
        Assert.Equal(ModStatus.Error, result[0].Status);
        Assert.Contains("git broke", result[0].ErrorMessage!);
    }

    [Fact]
    public async Task MultipleMods_SortedByName()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("FCP-Zebra");
        CreateModFolder("FCP-Alpha");
        CreateModFolder("FCP-Middle");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Zebra"), MakeRepo("FCP-Alpha"), MakeRepo("FCP-Middle")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);

        Assert.Equal(3, result.Count);
        Assert.Equal("FCP-Alpha", result[0].Name);
        Assert.Equal("FCP-Middle", result[1].Name);
        Assert.Equal("FCP-Zebra", result[2].Name);
    }

    [Fact]
    public async Task StatusCorrectlyComputed_Behind()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        CreateModFolder("FCP-Weapons");
        _gitHubApiService.GetOrganizationReposAsync(Arg.Any<CancellationToken>())
            .Returns([MakeRepo("FCP-Weapons")]);
        _gitService.IsGitRepositoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetRemoteUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/FalloutCollaborationProject/FCP-Weapons.git");
        _gitService.GetCurrentBranchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("main");
        _gitService.GetCurrentCommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GitCommitInfo("abc123", "abc1", "Init", "Author", DateTimeOffset.UtcNow));
        _gitService.HasLocalChangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _gitService.FetchAsync(Arg.Any<string>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _gitService.GetCommitDifferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Behind: 3, Ahead: 0));

        IReadOnlyList<InstalledMod> result = await _service.DiscoverModsAsync(_tempDir, ct: token);

        Assert.Single(result);
        Assert.Equal(ModStatus.Behind, result[0].Status);
        Assert.Equal(3, result[0].CommitsBehind);
    }
}
