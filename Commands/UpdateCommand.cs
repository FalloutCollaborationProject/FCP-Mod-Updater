using FCPModUpdater.Commands.Settings;
using FCPModUpdater.Models;
using FCPModUpdater.Services;
using FCPModUpdater.UI;
using JetBrains.Annotations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FCPModUpdater.Commands;

[UsedImplicitly]
public class UpdateCommand : AsyncCommand<ModPathSettings>
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

            // Initialize services
            var gitService = new GitService();
            var gitHubApiService = new GitHubApiService();
            var modDiscoveryService = new ModDiscoveryService(gitService, gitHubApiService);

            // Discover mods
            var mods = await ProgressReporter.WithStatusAsync(
                "Scanning mods directory...",
                async () => await modDiscoveryService.DiscoverModsAsync(modsDirectory, ct: cancellationToken));

            // Find updateable mods
            var updateableMods = mods
                .Where(m => m.Source == ModSource.Git && m.Status == ModStatus.Behind)
                .ToList();

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

            // Update all
            var results = await ProgressReporter.WithBatchProgressAsync(
                "Updating mods",
                updateableMods,
                m => m.Name,
                async (mod, progress) =>
                {
                    progress.Report(25);
                    var fetchOk = await gitService.FetchAsync(mod.Path, ct: cancellationToken);
                    if (!fetchOk)
                        return (false, "Fetch failed");

                    progress.Report(50);
                    var pullOk = await gitService.PullAsync(mod.Path, ct: cancellationToken);
                    progress.Report(100);

                    return (pullOk, pullOk ? null : "Pull failed");
                });

            ModTableRenderer.RenderUpdateSummary(results);

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

        // Multiple paths found - use first one for non-interactive mode
        AnsiConsole.MarkupLine($"[yellow]Multiple installations found, using: {paths[0]}[/]");
        AnsiConsole.MarkupLine("[grey]Use --directory to specify a different path[/]");
        return paths[0];
    }
}
