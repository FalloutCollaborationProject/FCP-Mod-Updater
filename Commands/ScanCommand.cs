using FCPModUpdater.Commands.Settings;
using FCPModUpdater.Services;
using FCPModUpdater.UI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

public class ScanCommand : AsyncCommand<ModPathSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ModPathSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            // Resolve mods directory
            var modsDirectory = ResolveModsDirectory(settings);
            if (modsDirectory == null)
            {
                return 1;
            }

            AnsiConsole.MarkupLine($"[grey]Using mods directory: {modsDirectory}[/]");
            AnsiConsole.WriteLine();

            // Check for git
            var gitService = new GitService();
            if (!await gitService.IsGitInstalledAsync(cancellationToken))
            {
                AnsiConsole.MarkupLine("[yellow]Warning: Git is not installed or not in PATH.[/]");
                AnsiConsole.MarkupLine("[yellow]Git-based features will be limited.[/]");
                AnsiConsole.MarkupLine("[grey]Install git from: https://git-scm.com/downloads[/]");
                AnsiConsole.WriteLine();
            }

            // Initialize services
            var gitHubApiService = new GitHubApiService();
            var modDiscoveryService = new ModDiscoveryService(gitService, gitHubApiService);

            // Run interactive menu
            var menu = new InteractiveMenu(
                gitService,
                gitHubApiService,
                modDiscoveryService,
                modsDirectory);

            await menu.RunAsync(cancellationToken);

            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private static string? ResolveModsDirectory(ModPathSettings settings)
    {
        if (settings.ModDirectory != null)
        {
            return settings.ModDirectory.FullName;
        }

        // Auto-discover
        var pathDiscovery = new PathDiscoveryService();
        var paths = pathDiscovery.DiscoverModPaths();

        if (paths.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Error: Could not find RimWorld Mods folder.[/]");
            AnsiConsole.MarkupLine("[grey]Please specify the path using --directory[/]");
            return null;
        }

        if (paths.Count == 1)
        {
            return paths[0];
        }

        // Multiple paths found - let user choose
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Multiple RimWorld installations found. Select one:[/]")
                .PageSize(10)
                .AddChoices(paths));
    }
}
