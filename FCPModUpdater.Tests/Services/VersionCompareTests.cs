using FCPModUpdater.Services;

namespace FCPModUpdater.Tests.Services;

public sealed class VersionCompareTests
{
    [Theory]
    [InlineData("1.0.0-updatertest.2", "1.0.0-updatertest.1")]
    [InlineData("1.0.0", "1.0.0-alpha.3")]
    [InlineData("1.0.0-alpha.10", "1.0.0-alpha.3")]
    [InlineData("1.0.0-beta", "1.0.0-alpha.3")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.a", "1.0.0-alpha.1")]
    [InlineData("1.0.1-alpha.1", "1.0.0")]
    [InlineData("1.0.0-alpha.3", "1.0.0-alpha.2+g123")]
    public void Newer_version_compares_greater(string newer, string older)
    {
        Assert.True(UpdateCheckService.CompareVersions(newer, older) > 0);
        Assert.True(UpdateCheckService.CompareVersions(older, newer) < 0);
    }

    [Fact]
    public void Build_metadata_is_ignored() =>
        Assert.Equal(0, UpdateCheckService.CompareVersions("1.0.0-rc.1+gabc", "1.0.0-rc.1"));

    [Theory]
    [InlineData("1.0.0-dev.4", "1.0.0")]
    [InlineData("1.0.0-alpha.3.dev.14", "1.0.0-alpha.3")]
    [InlineData("1.0.0-alpha.3", "1.0.0-alpha.3")]
    [InlineData("1.0.0", "1.0.0")]
    public void Dev_build_compares_as_its_tag(string version, string tag) =>
        Assert.Equal(tag, UpdateCheckService.BuiltFromTag(version));
}
