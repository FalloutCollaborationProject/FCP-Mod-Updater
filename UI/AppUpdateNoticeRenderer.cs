using System.Runtime.InteropServices;
using FCPModUpdater.Services;
using Spectre.Console;

namespace FCPModUpdater.UI;

public static class AppUpdateNoticeRenderer
{
    public static void Render(UpdateCheckResult updateResult, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        var label = updateResult.IsPrerelease ? "PRE-RELEASE AVAILABLE" : "UPDATE AVAILABLE";
        var updateCommand = GetUpdateCommand();
        var content = new Rows(
            new Markup(
                $"[bold {TerminalTheme.Warning.ToMarkup()}]{label} // V{Markup.Escape(updateResult.LatestVersion)}[/] " +
                $"[{TerminalTheme.Dim.ToMarkup()}](CURRENT: V{Markup.Escape(updateResult.CurrentVersion)})[/]"),
            new Markup(
                $"[{TerminalTheme.Dim.ToMarkup()}]PROCEDURE> CLOSE APPLICATION AND EXECUTE:[/] " +
                $"[underline {TerminalTheme.Bright.ToMarkup()}]{Markup.Escape(updateCommand)}[/]"),
            new Markup(
                $"[{TerminalTheme.Dim.ToMarkup()}]MANUAL SOURCE>[/] " +
                $"[{TerminalTheme.Phosphor.ToMarkup()}]" +
                $"[link={Markup.Escape(updateResult.ReleaseUrl)}]{Markup.Escape(updateResult.ReleaseUrl)}[/][/]"));

        var panel = new Panel(content)
        {
            Header = new PanelHeader("[[ SYSTEM UPDATE BULLETIN ]]"),
            Border = console.Profile.Capabilities.Unicode ? BoxBorder.Heavy : BoxBorder.Ascii,
            BorderStyle = TerminalTheme.WarningStyle
        };
        panel.Padding = new Padding(1, 0, 1, 0);
        panel.Expand = true;

        console.Write(panel);
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
