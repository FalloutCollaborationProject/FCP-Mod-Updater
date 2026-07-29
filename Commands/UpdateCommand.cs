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
            TerminalTheme.WriteHeader();
            var modsDirectory = ModsDirectoryResolver.Resolve(settings.ModDirectory?.FullName, interactive: false);

            TerminalTheme.WriteMessage($"MOD DIRECTORY // {modsDirectory}", TerminalMessageKind.Muted);
            AnsiConsole.WriteLine();

            Task<UpdateCheckResult?> updateCheckTask = updateCheckService.CheckForUpdateAsync(ct);

            var mods = await ProgressReporter.WithStatusAsync(
                "Scanning mods directory...",
                async () => await modDiscoveryService.DiscoverModsAsync(modsDirectory, ct: ct));

            var updateableMods = GetUpdateableMods(mods);

            if (updateableMods.Count == 0)
            {
                TerminalTheme.WriteMessage("ALL FCP MOD RECORDS CURRENT", TerminalMessageKind.Success);
                await RenderAppUpdateNoticeAsync(updateCheckTask);
                return 0;
            }

            TerminalTheme.WriteMessage(
                $"{updateableMods.Count} MOD RECORD(S) REQUIRE UPDATE",
                TerminalMessageKind.Warning);
            foreach (InstalledMod mod in updateableMods)
            {
                TerminalTheme.WriteMessage(
                    $"{mod.Name} // {mod.CommitsBehind} REVISIONS BEHIND",
                    TerminalMessageKind.Warning);
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
            TerminalTheme.WriteMessage("OPERATION CANCELLED", TerminalMessageKind.Muted);
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, TerminalTheme.ExceptionSettings);
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
