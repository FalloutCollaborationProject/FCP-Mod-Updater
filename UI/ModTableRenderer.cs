using FCPModUpdater.Models;
using Spectre.Console;

namespace FCPModUpdater.UI;

public static class ModTableRenderer
{
    public static void RenderModTable(IReadOnlyList<InstalledMod> mods, int? rateLimit = null,
        DateTimeOffset? rateLimitReset = null, IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;

        if (mods.Count == 0)
        {
            TerminalTheme.WriteMessage("NO FCP MOD RECORDS FOUND IN SPECIFIED DIRECTORY",
                TerminalMessageKind.Warning, console);
            return;
        }

        var table = new Table()
            .Border(console.Profile.Capabilities.Unicode ? TableBorder.Heavy : TableBorder.Ascii)
            .BorderColor(TerminalTheme.Dim)
            .Title($"[bold {TerminalTheme.Phosphor.ToMarkup()}][[ FCP MOD DATABASE ]][/]")
            .AddColumn(Header("MOD DESIGNATION").NoWrap())
            .AddColumn(Header("SOURCE").Centered())
            .AddColumn(Header("BRANCH").Centered())
            .AddColumn(Header("REVISION").Centered())
            .AddColumn(Header("STATUS").Centered());

        var glyphs = TerminalGlyphs.For(console);

        foreach (InstalledMod mod in mods)
        {
            table.AddRow(
                FormatModName(mod),
                FormatSource(mod.Source),
                FormatBranch(mod.Branch),
                FormatCommit(mod.CurrentCommit),
                FormatStatus(mod, glyphs)
            );
        }

        console.Write(table);

        // Status summary
        var gitMods = mods.Where(mod => mod.Source == ModSource.Git).ToList();
        var upToDate = gitMods.Count(mod => mod.Status == ModStatus.UpToDate);
        var behind = gitMods.Count(mod => mod.Status == ModStatus.Behind);
        var localChanges = gitMods.Count(mod => mod.Status == ModStatus.LocalChanges);
        var nonGit = mods.Count(mod => mod.Source != ModSource.Git);

        console.WriteLine();
        console.MarkupLine(
            $"[{TerminalTheme.Dim.ToMarkup()}]SYS> SUMMARY //[/] " +
            $"[{TerminalTheme.Phosphor.ToMarkup()}]{upToDate} CURRENT[/] " +
            $"[{TerminalTheme.Dim.ToMarkup()}]//[/] [{TerminalTheme.Warning.ToMarkup()}]{behind} UPDATE(S)[/] " +
            $"[{TerminalTheme.Dim.ToMarkup()}]//[/] [{TerminalTheme.Bright.ToMarkup()}]{localChanges} MODIFIED[/] " +
            $"[{TerminalTheme.Dim.ToMarkup()}]// {nonGit} LOCAL[/]");

        if (!rateLimit.HasValue) 
            return;
        
        var resetTime = rateLimitReset.HasValue
            ? $" (resets {rateLimitReset.Value.ToLocalTime():HH:mm})"
            : "";
        var style = rateLimit.Value < 10 ? TerminalTheme.Warning : TerminalTheme.Dim;
        console.MarkupLine(
            $"[{style.ToMarkup()}]NET> GITHUB API // {rateLimit.Value} REQUESTS REMAINING{Markup.Escape(resetTime.ToUpperInvariant())}[/]");
    }

    private static string FormatModName(InstalledMod mod)
    {
        var name = Markup.Escape(mod.Name);
        if (mod.HasLocalChanges)
        {
            name += $" [{TerminalTheme.Warning.ToMarkup()}]*[/]";
        }

        return name;
    }

    private static string FormatSource(ModSource source)
    {
        return source switch
        {
            ModSource.Git => Tag(TerminalTheme.PrimaryStyle, "GIT"),
            ModSource.Local => Tag(TerminalTheme.DimStyle, "LOCAL"),
            _ => Tag(TerminalTheme.DimStyle, "?")
        };
    }

    private static string FormatBranch(string? branch)
    {
        if (string.IsNullOrEmpty(branch))
            return Tag(TerminalTheme.DimStyle, "-");

        return branch == "main" || branch == "master"
            ? Tag(TerminalTheme.PrimaryStyle, branch)
            : Tag(TerminalTheme.WarningStyle, branch);
    }

    private static string FormatCommit(GitCommitInfo? commit)
    {
        return commit != null 
            ? Tag(TerminalTheme.DimStyle, commit.ShortHash)
            : Tag(TerminalTheme.DimStyle, "-");
    }

    private static string FormatStatus(InstalledMod mod, TerminalGlyphs glyphs)
    {
        return mod.Status switch
        {
            ModStatus.UpToDate => Tag(TerminalTheme.PrimaryStyle, $"{glyphs.Success} CURRENT"),
            ModStatus.Behind => Tag(TerminalTheme.WarningStyle, $"{glyphs.Behind} {mod.CommitsBehind} BEHIND"),
            ModStatus.Ahead => Tag(TerminalTheme.BrightStyle, $"{glyphs.Ahead} {mod.CommitsAhead} AHEAD"),
            ModStatus.Diverged => Tag(TerminalTheme.FailureStyle,
                $"{glyphs.Diverged} DIVERGED ({mod.CommitsBehind}{glyphs.Behind} {mod.CommitsAhead}{glyphs.Ahead})"),
            ModStatus.LocalChanges => Tag(TerminalTheme.BrightStyle, $"{glyphs.Modified} MODIFIED"),
            ModStatus.NonGit => Tag(TerminalTheme.DimStyle, $"{glyphs.Empty} NOT GIT"),
            ModStatus.Error => Tag(TerminalTheme.FailureStyle, $"{glyphs.Failure} ERROR"),
            ModStatus.Unknown => Tag(TerminalTheme.DimStyle, "? UNKNOWN"),
            _ => Tag(TerminalTheme.DimStyle, "?")
        };
    }

    public static void RenderUpdateSummary(
        IReadOnlyList<(string Name, bool Success, string? Error)> results,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;
        var glyphs = TerminalGlyphs.For(console);
        console.WriteLine();

        Table table = new Table()
            .Border(console.Profile.Capabilities.Unicode ? TableBorder.Heavy : TableBorder.Ascii)
            .BorderColor(TerminalTheme.Dim)
            .Title($"[bold {TerminalTheme.Phosphor.ToMarkup()}][[ OPERATION RESULTS ]][/]")
            .AddColumn(Header("MOD DESIGNATION"))
            .AddColumn(Header("RESULT"));

        foreach (var (name, success, error) in results)
        {
            var result = success
                ? Tag(TerminalTheme.PrimaryStyle, $"{glyphs.Success} COMPLETE")
                : Tag(TerminalTheme.FailureStyle,
                    $"{glyphs.Failure} FAILED: {error ?? "UNKNOWN ERROR"}");

            table.AddRow(Markup.Escape(name), result);
        }

        console.Write(table);

        var successCount = results.Count(r => r.Success);
        var failCount = results.Count(r => !r.Success);

        console.WriteLine();
        TerminalTheme.WriteMessage(
            failCount == 0
                ? $"OPERATION COMPLETE // {successCount} MOD(S) PROCESSED"
                : $"OPERATION PARTIAL // {successCount} COMPLETE // {failCount} FAILED",
            failCount == 0 ? TerminalMessageKind.Success : TerminalMessageKind.Warning,
            console);
    }

    public static void RenderIncomingCommits(
        InstalledMod mod,
        IReadOnlyList<GitCommitInfo> commits,
        IAnsiConsole? console = null)
    {
        console ??= AnsiConsole.Console;

        if (commits.Count == 0)
        {
            TerminalTheme.WriteMessage($"NO INCOMING REVISIONS FOR {mod.Name}",
                TerminalMessageKind.Muted, console);
            return;
        }

        var tree = new Tree(
                $"[bold {TerminalTheme.Bright.ToMarkup()}]{Markup.Escape(mod.Name)}[/] " +
                $"[{TerminalTheme.Dim.ToMarkup()}]({commits.Count} INCOMING REVISIONS)[/]")
            .Guide(TreeGuide.BoldLine)
            .Style(TerminalTheme.DimStyle);

        foreach (GitCommitInfo commit in commits)
        {
            var dateStr = commit.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            tree.AddNode(
                $"[{TerminalTheme.Warning.ToMarkup()}]{Markup.Escape(commit.ShortHash)}[/] " +
                $"[{TerminalTheme.Dim.ToMarkup()}]{dateStr}[/] " +
                $"[{TerminalTheme.Phosphor.ToMarkup()}]{Markup.Escape(commit.Message)}[/] " +
                $"[{TerminalTheme.Dim.ToMarkup()}]// {Markup.Escape(commit.Author)}[/]");
        }

        console.Write(tree);
        console.WriteLine();
    }

    private static TableColumn Header(string text) =>
        new($"[bold {TerminalTheme.Phosphor.ToMarkup()}]{text}[/]");

    private static string Tag(Style style, string text) =>
        $"[{style.ToMarkup()}]{Markup.Escape(text)}[/]";
}
