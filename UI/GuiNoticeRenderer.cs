using FCPModUpdater.Settings;
using Spectre.Console;

namespace FCPModUpdater.UI;

/// <summary>Tells CLI users, once, that a desktop GUI version is available.</summary>
public static class GuiNoticeRenderer
{
    private const string ReleasesUrl = "https://github.com/FalloutCollaborationProject/FCP-Mod-Updater/releases";

    /// <summary>Renders the notice if it has never been shown, then records that it was.</summary>
    public static void RenderOnce(SettingsStore? settingsStore)
    {
        if (settingsStore is null || settingsStore.Current.GuiNoticeShown)
            return;

        var content = new Rows(
            new Markup("[cyan bold]New: Collaboration Project Updater now has a GUI version[/]"),
            new Markup("[Grey66]Download[/] [underline]CollabProjectUpdater-win-x64-Setup.exe[/] [Grey66](Windows) or the[/] [underline]AppImage[/] [Grey66](Linux) from:[/]"),
            new Markup($"[link]{ReleasesUrl}[/]"),
            new Markup("[Grey66 dim]It shares this app's settings. This notice is only shown once.[/]"));

        AnsiConsole.Write(new Panel(content)
        {
            Border = BoxBorder.Square,
            BorderStyle = new Style(Color.Grey, null, Decoration.Dim),
            Padding = new Padding(1, 0, 1, 0),
            Expand = false
        });

        settingsStore.Update(s => s with { GuiNoticeShown = true });
    }
}
