using FCPModUpdater.Commands.Settings;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;
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
    UpdateCheckService updateCheckService,
    SettingsStore settingsStore,
    GitHubTokenProvider tokenProvider) : AsyncCommand<ModPathSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ModPathSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var modsDirectory = ModsDirectoryResolver.Resolve(settings.ModDirectory?.FullName, interactive: true, settingsStore);

            AnsiConsole.MarkupLine($"[grey]Using mods directory: {modsDirectory}[/]");
            AnsiConsole.WriteLine();

            Task<UpdateCheckResult?> updateCheckTask = settingsStore.Current.CheckForUpdatesOnLaunch
                ? updateCheckService.CheckAndRecordAsync(settingsStore, cancellationToken)
                : Task.FromResult<UpdateCheckResult?>(null);

            var menu = new InteractiveMenu(
                gitService,
                gitHubApiService,
                modDiscoveryService,
                modsDirectory,
                updateCheckTask,
                new SettingsMenu(settingsStore, tokenProvider, gitHubApiService),
                settingsStore);

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
