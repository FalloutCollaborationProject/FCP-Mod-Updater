using FCPModUpdater.Commands.Settings;
using FCPModUpdater.Services;
using FCPModUpdater.UI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

public class ScanCommand(
    IGitService gitService,
    IGitHubApiService gitHubApiService,
    IModDiscoveryService modDiscoveryService,
    UpdateCheckService updateCheckService) : AsyncCommand<ModPathSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ModPathSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var modsDirectory = ModsDirectoryResolver.Resolve(settings.ModDirectory?.FullName, interactive: true);
            if (modsDirectory == null)
                return 1;

            AnsiConsole.MarkupLine($"[grey]Using mods directory: {modsDirectory}[/]");
            AnsiConsole.WriteLine();

            var updateCheckTask = updateCheckService.CheckForUpdateAsync(cancellationToken);

            var menu = new InteractiveMenu(
                gitService,
                gitHubApiService,
                modDiscoveryService,
                modsDirectory,
                updateCheckTask);

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
