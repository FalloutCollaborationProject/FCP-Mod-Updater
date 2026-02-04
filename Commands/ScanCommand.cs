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
            var modsDirectory = ModsDirectoryResolver.Resolve(settings.ModDirectory?.FullName, interactive: true);
            if (modsDirectory == null)
            {
                return 1;
            }

            AnsiConsole.MarkupLine($"[grey]Using mods directory: {modsDirectory}[/]");
            AnsiConsole.WriteLine();

            // Initialize services
            var gitService = new GitService();
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
}
