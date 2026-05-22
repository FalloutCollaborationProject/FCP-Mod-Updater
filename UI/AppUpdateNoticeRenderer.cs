using System.Runtime.InteropServices;
using FCPModUpdater.Services;
using Spectre.Console;

namespace FCPModUpdater.UI;

public static class AppUpdateNoticeRenderer
{
    public static void Render(UpdateCheckResult updateResult)
    {
        var label = updateResult.IsPrerelease ? "Pre-release available" : "Update available";
        var updateCommand = GetUpdateCommand();
        var content = new Rows(
            new Markup(
                $"[yellow bold]{label}: v{Markup.Escape(updateResult.LatestVersion)}[/] [Grey66](current: [/][Grey66 dim bold]{Markup.Escape(updateResult.CurrentVersion)}[/][Grey66])[/]"),
            new Markup($"[Grey66]Close this app before updating and run:[/] [underline]{Markup.Escape(updateCommand)}[/]"),
            new Markup($"[Grey66]Manual download:[/] [link]{Markup.Escape(updateResult.ReleaseUrl)}[/]"));

        var panel = new Panel(content)
        {
            Border = BoxBorder.Square,
            BorderStyle = new Style(Color.Grey, null, Decoration.Dim)
        };
        panel.Padding = new Padding(1, 0, 1, 0);
        panel.Expand = false;

        AnsiConsole.Write(panel);
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
