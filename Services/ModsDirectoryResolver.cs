using Spectre.Console;
using FCPModUpdater.UI;

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
                TerminalTheme.Selection<string>("Multiple RimWorld installations detected")
                    .PageSize(10)
                    .AddChoices(paths));
        }

        // Non-interactive: use first one with warning
        TerminalTheme.WriteMessage($"MULTIPLE INSTALLATIONS DETECTED // USING {paths[0]}",
            TerminalMessageKind.Warning);
        TerminalTheme.WriteMessage("USE --DIRECTORY TO SPECIFY AN ALTERNATE PATH",
            TerminalMessageKind.Muted);
        return paths[0];
    }

    private static string PromptForPath()
    {
        TerminalTheme.WriteMessage("RIMWORLD MOD DIRECTORY AUTO-DETECTION FAILED",
            TerminalMessageKind.Warning);
        TerminalTheme.WriteMessage("USE --DIRECTORY TO BYPASS FUTURE PATH SCANS",
            TerminalMessageKind.Muted);
        AnsiConsole.WriteLine();

        return AnsiConsole.Prompt(
            TerminalTheme.TextPrompt("Enter RimWorld Mods directory (ctrl + c to exit):")
                .ValidationErrorMessage(
                    $"[{TerminalTheme.Failure.ToMarkup()}]DIRECTORY DOES NOT EXIST[/]")
                .Validate(path => Directory.Exists(path)
                    ? ValidationResult.Success()
                    : ValidationResult.Error()));
    }
}
