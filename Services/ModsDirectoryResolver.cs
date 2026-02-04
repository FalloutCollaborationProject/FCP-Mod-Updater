using Spectre.Console;

namespace FCPModUpdater.Services;

public static class ModsDirectoryResolver
{
    /// <summary>
    /// Resolves the mods directory from an explicit path, auto-discovery, or user prompt.
    /// </summary>
    /// <param name="explicitPath">Explicit path provided via --directory, or null for auto-discovery.</param>
    /// <param name="interactive">If true, prompts user to select when multiple paths found. If false, uses first path.</param>
    /// <returns>The resolved path, or null if resolution failed.</returns>
    public static string Resolve(string? explicitPath, bool interactive)
    {
        if (explicitPath != null)
            return explicitPath;

        var pathDiscovery = new PathDiscoveryService();
        var paths = pathDiscovery.DiscoverModPaths();

        switch (paths.Count)
        {
            case 0:
                return PromptForPath();
            case 1:
                return paths[0];
        }

        // Multiple paths found
        if (interactive)
        {
            return AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Multiple RimWorld installations found. Select one:[/]")
                    .PageSize(10)
                    .AddChoices(paths));
        }

        // Non-interactive: use first one with warning
        AnsiConsole.MarkupLine($"[yellow]Multiple installations found, using: {paths[0]}[/]");
        AnsiConsole.MarkupLine("[grey]Use --directory to specify a different path[/]");
        return paths[0];
    }

    private static string PromptForPath()
    {
        AnsiConsole.MarkupLine("[yellow]Could not auto-detect RimWorld Mods folder.[/]");
        AnsiConsole.MarkupLine("[grey]Tip: Use --directory to skip this prompt in the future[/]");
        AnsiConsole.WriteLine();

        return AnsiConsole.Prompt(
            new TextPrompt<string>("[bold]Enter the path to your RimWorld Mods folder (ctrl + c to exit):[/]")
                .ValidationErrorMessage("[red]That directory does not exist[/]")
                .Validate(path => Directory.Exists(path)
                    ? ValidationResult.Success()
                    : ValidationResult.Error()));
    }
}
