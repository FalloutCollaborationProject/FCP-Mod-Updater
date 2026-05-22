using System.Runtime.InteropServices;
using FCPModUpdater.Services;
using Spectre.Console;

namespace FCPModUpdater.UI;

public static class AppUpdateNoticeRenderer
{
    public static void Render(UpdateCheckResult updateResult)
    {
        var label = updateResult.IsPrerelease ? "Pre-release available" : "Update available";

        AnsiConsole.MarkupLine(
            $"[yellow bold]{label}: v{updateResult.LatestVersion}[/] [grey](current: {updateResult.CurrentVersion})[/]");
        AnsiConsole.MarkupLine("[grey]Close this app before updating.[/]");
        AnsiConsole.MarkupLine($"[grey]Run: {GetUpdateCommand()}[/]");
        AnsiConsole.MarkupLine($"[grey]Manual download: {updateResult.ReleaseUrl}[/]");
    }

    private static string GetUpdateCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return @"update-fcp-mod-manager.bat";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "./update-fcp-mod-manager.sh";

        return "download the latest release manually";
    }
}
