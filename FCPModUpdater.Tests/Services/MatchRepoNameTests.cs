namespace FCPModUpdater.Tests.Services;

public class MatchRepoNameTests
{
    [Fact]
    public void DirectMatch_ReturnsFolder()
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FCP-Weapons" };
        Assert.Equal("FCP-Weapons", ModDiscoveryService.MatchRepoName("FCP-Weapons", repos));
    }

    [Fact]
    public void CaseInsensitiveMatch_ReturnsFolderName()
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FCP-Weapons" };
        Assert.Equal("fcp-weapons", ModDiscoveryService.MatchRepoName("fcp-weapons", repos));
    }

    [Theory]
    [InlineData("FCP-Weapons-main")]
    [InlineData("FCP-Weapons-master")]
    [InlineData("FCP-Weapons-develop")]
    public void StripBranchSuffix_ReturnsBaseName(string folderName)
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FCP-Weapons" };
        Assert.Equal("FCP-Weapons", ModDiscoveryService.MatchRepoName(folderName, repos));
    }

    [Fact]
    public void SuffixCaseInsensitive_ReturnsBaseName()
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FCP-Weapons" };
        Assert.Equal("FCP-Weapons", ModDiscoveryService.MatchRepoName("FCP-Weapons-MAIN", repos));
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FCP-Weapons" };
        Assert.Null(ModDiscoveryService.MatchRepoName("SomeOtherMod", repos));
    }

    [Fact]
    public void EmptyOrgRepos_ReturnsNull()
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Null(ModDiscoveryService.MatchRepoName("FCP-Weapons", repos));
    }

    [Fact]
    public void SuffixButBaseDoesNotMatch_ReturnsNull()
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FCP-Weapons" };
        Assert.Null(ModDiscoveryService.MatchRepoName("RandomMod-main", repos));
    }

    [Fact]
    public void NameContainsSuffixSubstring_DirectMatch()
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FCP-main-Weapons" };
        Assert.Equal("FCP-main-Weapons", ModDiscoveryService.MatchRepoName("FCP-main-Weapons", repos));
    }

    [Fact]
    public void DirectMatchTakesPriority_OverSuffixStripping()
    {
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FCP-Weapons", "FCP-Weapons-main" };
        // "FCP-Weapons-main" matches directly first
        Assert.Equal("FCP-Weapons-main", ModDiscoveryService.MatchRepoName("FCP-Weapons-main", repos));
    }
}