using FCPModUpdater.Services;
using Spectre.Console;

namespace FCPModUpdater.UI;

public static class AppUpdateNoticeRenderer
{
    public static void Render(UpdateCheckResult updateResult)
    {
        var label = updateResult.IsPrerelease ? "Pre-release available" : "Update available";
        var lines = new List<Markup>
        {
            new(
                $"[yellow bold]{label}: v{Markup.Escape(updateResult.LatestVersion)}[/] [Grey66](current: [/][Grey66 dim bold]{Markup.Escape(updateResult.CurrentVersion)}[/][Grey66])[/]"),
        };
        // The updater script is a separate download; only suggest it when it is next to the app.
        if (UpdateCheckService.GetUpdaterCommand() is { } updateCommand)
            lines.Add(new Markup($"[Grey66]Close this app before updating and run:[/] [underline]{Markup.Escape(updateCommand)}[/]"));
        lines.Add(new Markup($"[Grey66]Download:[/] [link]{Markup.Escape(updateResult.ReleaseUrl)}[/]"));
        var content = new Rows(lines);

        var panel = new Panel(content)
        {
            Border = BoxBorder.Square,
            BorderStyle = new Style(Color.Grey, null, Decoration.Dim)
        };
        panel.Padding = new Padding(1, 0, 1, 0);
        panel.Expand = false;

        AnsiConsole.Write(panel);
    }
}
