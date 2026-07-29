using FCPModUpdater.Commands.Settings;
using FCPModUpdater.Services;
using FCPModUpdater.UI;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

[UsedImplicitly]
public class ScanCommand(
    IGitService gitService,
    IGitHubApiService gitHubApiService,
    IModDiscoveryService modDiscoveryService,
    UpdateCheckService updateCheckService) : AsyncCommand<ModPathSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ModPathSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            TerminalTheme.WriteHeader();
            var modsDirectory = ModsDirectoryResolver.Resolve(settings.ModDirectory?.FullName, interactive: true);

            TerminalTheme.WriteMessage($"MOD DIRECTORY // {modsDirectory}", TerminalMessageKind.Muted);
            AnsiConsole.WriteLine();

            Task<UpdateCheckResult?> updateCheckTask = updateCheckService.CheckForUpdateAsync(cancellationToken);

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
            TerminalTheme.WriteMessage("OPERATION CANCELLED", TerminalMessageKind.Muted);
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, TerminalTheme.ExceptionSettings);
            return 1;
        }
    }
}
