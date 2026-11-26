using FCPModUpdater.Services;

namespace FCPModUpdater.Tests.Services;

public class UpdaterCommandTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("/home/user/Apps/CollabProjectUpdater-x86_64.AppImage", true)]
    public void IsAppImagePath_IsTrueOnlyWhenTheRuntimeSetAPath(string? value, bool expected)
    {
        Assert.Equal(expected, UpdateCheckService.IsAppImagePath(value));
    }

    [Fact]
    public void GetUpdaterCommand_InsideAppImage_SuggestsNoScript()
    {
        Assert.Null(UpdateCheckService.GetUpdaterCommand(isAppImage: true));
    }

    [Fact]
    public void GetUpdaterCommand_OutsideAppImage_NamesTheScriptForThisPlatform()
    {
        var command = UpdateCheckService.GetUpdaterCommand(isAppImage: false);

        if (OperatingSystem.IsWindows())
            Assert.Equal("update-collab-project-updater.bat", command);
        else if (OperatingSystem.IsLinux())
            Assert.Equal("./update-collab-project-updater.sh", command);
        else
            Assert.Null(command);
    }
}

public class ReleaseNotesTests
{
    [Fact]
    public void ChangelogPart_KeepsTheTextAboveTheFirstRule()
    {
        var body = "### Fixed\r\n- A fix\r\n\r\n---\r\n\r\n## Which file do I download?\n---\nGenerated";
        Assert.Equal("### Fixed\n- A fix", UpdateCheckService.ChangelogPart(body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n---\n\n## Which file do I download?")]
    public void ChangelogPart_IsNullWithoutChangelog(string? body)
    {
        Assert.Null(UpdateCheckService.ChangelogPart(body));
    }

    [Fact]
    public void ChangelogPart_WithoutRule_KeepsEverything()
    {
        Assert.Equal("Old release notes", UpdateCheckService.ChangelogPart("  Old release notes \n"));
    }
}
