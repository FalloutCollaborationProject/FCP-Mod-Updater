using FCPModUpdater.Services;

namespace FCPModUpdater.Tests.Services;

public class UpdaterCommandTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("/home/user/Apps/FCPModManager-GUI-x86_64.AppImage", true)]
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
            Assert.Equal("update-fcp-mod-manager.bat", command);
        else if (OperatingSystem.IsLinux())
            Assert.Equal("./update-fcp-mod-manager.sh", command);
        else
            Assert.Null(command);
    }
}
