using FCPModUpdater.Commands.Settings;
using FCPModUpdater.Models;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;
using FCPModUpdater.UI;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

[UsedImplicitly]
public class UpdateCommand(
    IGitService gitService,
    IModDiscoveryService modDiscoveryService,
    UpdateCheckService updateCheckService,
    SettingsStore settingsStore) : AsyncCommand<UpdateSettings>
{
    public static IReadOnlyList<InstalledMod> GetUpdateableMods(IReadOnlyList<InstalledMod> mods)
        => ModCatalog.GetUpdateableMods(mods);

    protected override async Task<int> ExecuteAsync(CommandContext context, UpdateSettings settings,
        CancellationToken ct)
    {
        try
        {
            var modsDirectory = ModsDirectoryResolver.Resolve(settings.ModDirectory?.FullName, interactive: false, settingsStore);

            AnsiConsole.MarkupLine($"[grey]Using mods directory: {modsDirectory}[/]");
            var (gameVersion, versionSource) = GameVersionService.Resolve(modsDirectory, settingsStore.Current);
            if (gameVersion is not null)
                AnsiConsole.MarkupLine($"[grey]RimWorld {Markup.Escape(gameVersion.Raw)} ({(versionSource == GameVersionSource.Settings ? "from settings" : "detected")})[/]");
            AnsiConsole.WriteLine();

            Task<UpdateCheckResult?> updateCheckTask = settingsStore.Current.CheckForUpdatesOnLaunch
                ? updateCheckService.CheckAndRecordAsync(settingsStore, ct)
                : Task.FromResult<UpdateCheckResult?>(null);

            var mods = await ProgressReporter.WithStatusAsync(
                "Scanning mods directory...",
                async () => await modDiscoveryService.DiscoverModsAsync(modsDirectory, ct: ct));

            var updateableMods = ModCatalog.GetUpdateableMods(mods, settings.AllowMainline);

            foreach (InstalledMod mod in ModCatalog.GetVersionMismatchedMods(mods))
                AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(ModTableRenderer.DescribeVersionMismatch(mod, gameVersion))}"
                                       + (settings.AllowMainline || mod.Status != ModStatus.Behind || !ModCatalog.BlocksUpdate(mod.VersionMismatch) ? "" : " · skipped (--allow-mainline to update anyway)") + "[/]");

            if (updateableMods.Count == 0)
            {
                AnsiConsole.MarkupLine(ModCatalog.GetUpdateableMods(mods, includeMismatched: true).Count > 0
                    ? "[yellow]Nothing else to update.[/]"
                    : "[green]All FCP mods are up to date![/]");
                await RenderAppUpdateNoticeAsync(updateCheckTask);
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
                async (mod, progress) => await GitModUpdater.UpdateAsync(gitService, mod, progress, ct));

            ModTableRenderer.RenderUpdateSummary(results);

            await RenderAppUpdateNoticeAsync(updateCheckTask);

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

    private static async Task RenderAppUpdateNoticeAsync(Task<UpdateCheckResult?> updateCheckTask)
    {
        UpdateCheckResult? updateResult = await updateCheckTask;
        if (updateResult is null)
            return;

        AnsiConsole.WriteLine();
        AppUpdateNoticeRenderer.Render(updateResult);
    }
}
