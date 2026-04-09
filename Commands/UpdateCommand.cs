using FCPModUpdater.Commands.Settings;
using FCPModUpdater.Models;
using FCPModUpdater.Services;
using FCPModUpdater.UI;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

[UsedImplicitly]
public class UpdateCommand(
    IGitService gitService,
    IModDiscoveryService modDiscoveryService,
    UpdateCheckService updateCheckService) : AsyncCommand<ModPathSettings>
{
    public static IReadOnlyList<InstalledMod> GetUpdateableMods(IReadOnlyList<InstalledMod> mods)
        => mods.Where(m => m.Source == ModSource.Git && m.Status == ModStatus.Behind).ToList();

    protected override async Task<int> ExecuteAsync(CommandContext context, ModPathSettings settings,
        CancellationToken ct)
    {
        try
        {
            var modsDirectory = ModsDirectoryResolver.Resolve(settings.ModDirectory?.FullName, interactive: false);

            AnsiConsole.MarkupLine($"[grey]Using mods directory: {modsDirectory}[/]");
            AnsiConsole.WriteLine();

            Task<UpdateCheckResult?> updateCheckTask = updateCheckService.CheckForUpdateAsync(ct);

            var mods = await ProgressReporter.WithStatusAsync(
                "Scanning mods directory...",
                async () => await modDiscoveryService.DiscoverModsAsync(modsDirectory, ct: ct));

            var updateableMods = GetUpdateableMods(mods);

            if (updateableMods.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]All FCP mods are up to date![/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[yellow]Found {updateableMods.Count} mod(s) with updates available:[/]");
            foreach (InstalledMod mod in updateableMods)
            {
                AnsiConsole.MarkupLine($"  • {mod.Name} [grey]({mod.CommitsBehind} commits behind)[/]");
            }

            AnsiConsole.WriteLine();

            var results = await ProgressReporter.WithBatchProgressAsync(
                "Updating mods",
                updateableMods,
                installedMod => installedMod.Name,
                async (mod, progress) =>
                {
                    progress.Report(25);
                    var fetchOk = await gitService.FetchAsync(mod.Path, ct: ct);
                    if (!fetchOk)
                        return (false, "Fetch failed");

                    progress.Report(50);
                    var pullOk = await gitService.PullAsync(mod.Path, ct: ct);
                    progress.Report(100);

                    return (pullOk, pullOk ? null : "Pull failed");
                });

            ModTableRenderer.RenderUpdateSummary(results);

            UpdateCheckResult? updateResult = await updateCheckTask;
            if (updateResult != null)
            {
                AnsiConsole.WriteLine();
                var label = updateResult.IsPrerelease ? "Pre-release available" : "Update available";
                AnsiConsole.MarkupLine(
                    $"[yellow bold]{label}: v{updateResult.LatestVersion}[/] [grey](current: {updateResult.CurrentVersion})[/]");
                AnsiConsole.MarkupLine($"[grey]Download: {updateResult.ReleaseUrl}[/]");
            }

            var failCount = results.Count(r => !r.Success);
            return failCount > 0 ? 1 : 0;
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
