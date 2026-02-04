using FCPModUpdater.Models;
using Spectre.Console;

namespace FCPModUpdater.UI;

public static class ModTableRenderer
{
    public static void RenderModTable(IReadOnlyList<InstalledMod> mods, int? rateLimit = null,
        DateTimeOffset? rateLimitReset = null)
    {
        if (mods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No FCP mods found in the specified directory.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold blue]FCP Mod Status[/]")
            .AddColumn(new TableColumn("[bold]Mod Name[/]").NoWrap())
            .AddColumn(new TableColumn("[bold]Source[/]").Centered())
            .AddColumn(new TableColumn("[bold]Branch[/]").Centered())
            .AddColumn(new TableColumn("[bold]Commit[/]").Centered())
            .AddColumn(new TableColumn("[bold]Status[/]").Centered());

        foreach (var mod in mods)
        {
            table.AddRow(
                FormatModName(mod),
                FormatSource(mod.Source),
                FormatBranch(mod.Branch),
                FormatCommit(mod.CurrentCommit),
                FormatStatus(mod)
            );
        }

        AnsiConsole.Write(table);

        // Status summary
        var gitMods = mods.Where(m => m.Source == ModSource.Git).ToList();
        var upToDate = gitMods.Count(m => m.Status == ModStatus.UpToDate);
        var behind = gitMods.Count(m => m.Status == ModStatus.Behind);
        var localChanges = gitMods.Count(m => m.Status == ModStatus.LocalChanges);
        var nonGit = mods.Count(m => m.Source != ModSource.Git);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[grey]Summary:[/] [green]{upToDate} up to date[/] | [yellow]{behind} updates available[/] | [cyan]{localChanges} with local changes[/] | [grey]{nonGit} non-git[/]");

        if (rateLimit.HasValue)
        {
            var resetTime = rateLimitReset.HasValue
                ? $" (resets {rateLimitReset.Value.ToLocalTime():HH:mm})"
                : "";
            var color = rateLimit.Value < 10 ? "yellow" : "grey";
            AnsiConsole.MarkupLine($"[{color}]GitHub API: {rateLimit.Value} requests remaining{resetTime}[/]");
        }
    }

    private static string FormatModName(InstalledMod mod)
    {
        var name = mod.Name;
        if (mod.HasLocalChanges)
        {
            name += " [yellow]*[/]";
        }

        return name;
    }

    private static string FormatSource(ModSource source)
    {
        return source switch
        {
            ModSource.Git => "[green]Git[/]",
            ModSource.Local => "[grey]Local[/]",
            ModSource.Workshop => "[blue]Workshop[/]",
            _ => "[grey]?[/]"
        };
    }

    private static string FormatBranch(string? branch)
    {
        if (string.IsNullOrEmpty(branch))
            return "[grey]-[/]";

        return branch == "main" || branch == "master"
            ? $"[green]{branch}[/]"
            : $"[yellow]{branch}[/]";
    }

    private static string FormatCommit(GitCommitInfo? commit)
    {
        if (commit == null)
            return "[grey]-[/]";

        return $"[grey]{commit.ShortHash}[/]";
    }

    private static string FormatStatus(InstalledMod mod)
    {
        return mod.Status switch
        {
            ModStatus.UpToDate => "[green]✓ Up to date[/]",
            ModStatus.Behind => $"[yellow]↓ {mod.CommitsBehind} behind[/]",
            ModStatus.Ahead => $"[cyan]↑ {mod.CommitsAhead} ahead[/]",
            ModStatus.Diverged => $"[red]⇅ Diverged ({mod.CommitsBehind}↓ {mod.CommitsAhead}↑)[/]",
            ModStatus.LocalChanges => "[cyan]~ Modified[/]",
            ModStatus.NonGit => "[grey]— Not Git[/]",
            ModStatus.Error => $"[red]✗ Error[/]",
            ModStatus.Unknown => "[grey]? Unknown[/]",
            _ => "[grey]?[/]"
        };
    }

    public static void RenderUpdateSummary(IReadOnlyList<(string Name, bool Success, string? Error)> results)
    {
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold]Update Results[/]")
            .AddColumn("[bold]Mod[/]")
            .AddColumn("[bold]Result[/]");

        foreach (var (name, success, error) in results)
        {
            var result = success
                ? "[green]✓ Updated[/]"
                : $"[red]✗ Failed: {Markup.Escape(error ?? "Unknown error")}[/]";

            table.AddRow(name, result);
        }

        AnsiConsole.Write(table);

        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);

        AnsiConsole.WriteLine();
        if (failCount == 0)
        {
            AnsiConsole.MarkupLine($"[green]Successfully updated {successCount} mod(s).[/]");
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Updated {successCount} mod(s), {failCount} failed.[/]");
        }
    }

    public static void RenderIncomingCommits(InstalledMod mod, IReadOnlyList<GitCommitInfo> commits)
    {
        if (commits.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]No incoming commits for {mod.Name}[/]");
            return;
        }

        var tree = new Tree($"[bold]{mod.Name}[/] [grey]({commits.Count} incoming commits)[/]");

        foreach (var commit in commits)
        {
            var dateStr = commit.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            tree.AddNode(
                $"[yellow]{commit.ShortHash}[/] [grey]{dateStr}[/] {Markup.Escape(commit.Message)} [grey]— {Markup.Escape(commit.Author)}[/]");
        }

        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();
    }
}
