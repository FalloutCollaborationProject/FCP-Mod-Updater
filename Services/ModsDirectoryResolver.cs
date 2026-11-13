using FCPModUpdater.Settings;
using Spectre.Console;

namespace FCPModUpdater.Services;

public static class ModsDirectoryResolver
{
    /// <summary>
    /// Resolves the mods directory: an explicit path, then the saved folder, then auto-discovery
    /// or a prompt. A folder the user picks or types is saved for next time.
    /// </summary>
    /// <param name="explicitPath">Explicit path provided via --directory, or null.</param>
    /// <param name="interactive">If true, prompts user to select when multiple paths found. If false, uses first path.</param>
    /// <param name="settings">Saved settings; null skips reading and saving them.</param>
    public static string Resolve(string? explicitPath, bool interactive, SettingsStore? settings = null)
    {
        if (explicitPath != null)
            return explicitPath;

        var saved = settings?.Current.ModsDirectory;
        if (!string.IsNullOrWhiteSpace(saved))
        {
            if (Directory.Exists(saved))
                return saved;
            AnsiConsole.MarkupLine($"[yellow]Saved mods folder no longer exists: {Markup.Escape(saved)}[/]");
        }

        var pathDiscovery = new PathDiscoveryService();
        var paths = pathDiscovery.DiscoverModPaths();

        switch (paths.Count)
        {
            case 0:
                return Remember(settings, PromptForPath());
            case 1:
                return paths[0];
        }

        // Multiple paths found
        if (interactive)
        {
            return Remember(settings, AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Multiple RimWorld installations found. Select one:[/]")
                    .PageSize(10)
                    .AddChoices(paths)));
        }

        // Non-interactive: use first one with warning
        AnsiConsole.MarkupLine($"[yellow]Multiple installations found, using: {paths[0]}[/]");
        AnsiConsole.MarkupLine("[grey]Use --directory or `settings set mods-directory` to choose a different path[/]");
        return paths[0];
    }

    private static string Remember(SettingsStore? settings, string path)
    {
        if (settings is null)
            return path;
        try
        {
            settings.Update(s => s with { ModsDirectory = path });
            AnsiConsole.MarkupLine($"[grey]Saved as your mods folder ({Markup.Escape(settings.FilePath)})[/]");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine($"[yellow]Could not save settings: {Markup.Escape(ex.Message)}[/]");
        }

        return path;
    }

    private static string PromptForPath()
    {
        AnsiConsole.MarkupLine("[yellow]Could not auto-detect RimWorld Mods folder.[/]");
        AnsiConsole.MarkupLine("[grey]Tip: the folder you enter is saved; --directory overrides it[/]");
        AnsiConsole.WriteLine();

        return AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Enter the path to your RimWorld Mods folder (ctrl + c to exit):[/]")
                .ValidationErrorMessage("[red]That directory does not exist[/]")
                .Validate(path => Directory.Exists(path)
                    ? ValidationResult.Success()
                    : ValidationResult.Error()));
    }
}
