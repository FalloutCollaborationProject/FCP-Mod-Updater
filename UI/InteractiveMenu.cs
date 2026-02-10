using FCPModUpdater.Models;
using FCPModUpdater.Services;
using Spectre.Console;

namespace FCPModUpdater.UI;

public class InteractiveMenu
{
    private readonly IGitService _gitService;
    private readonly IGitHubApiService _gitHubApiService;
    private readonly IModDiscoveryService _modDiscoveryService;
    private readonly string _modsDirectory;
    private readonly Task<UpdateCheckResult?> _updateCheckTask;

    private IReadOnlyList<InstalledMod> _mods = [];
    private bool _updateNotificationShown;

    public InteractiveMenu(
        IGitService gitService,
        IGitHubApiService gitHubApiService,
        IModDiscoveryService modDiscoveryService,
        string modsDirectory,
        Task<UpdateCheckResult?> updateCheckTask)
    {
        _gitService = gitService;
        _gitHubApiService = gitHubApiService;
        _modDiscoveryService = modDiscoveryService;
        _modsDirectory = modsDirectory;
        _updateCheckTask = updateCheckTask;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Initial scan
        await RefreshModsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            ModTableRenderer.RenderModTable(_mods, _gitHubApiService.RemainingRateLimit,
                _gitHubApiService.RateLimitReset);

            // Show update notification once, non-blocking
            if (!_updateNotificationShown && _updateCheckTask.IsCompleted)
            {
                _updateNotificationShown = true;
                var updateResult = await _updateCheckTask;
                if (updateResult != null)
                {
                    var label = updateResult.IsPrerelease ? "Pre-release available" : "Update available";
                    AnsiConsole.MarkupLine(
                        $"[yellow bold]{label}: v{updateResult.LatestVersion}[/] [grey](current: {updateResult.CurrentVersion})[/]");
                    AnsiConsole.MarkupLine($"[grey]Download: {updateResult.ReleaseUrl}[/]");
                }
            }

            AnsiConsole.WriteLine();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(
                        "Update Git Mods",
                        "Install New Mods",
                        "Uninstall Mods",
                        "Convert Local to Git",
                        "Mod Version Selector",
                        "Refresh Status",
                        "Exit"
                    ));

            try
            {
                var shouldExit = choice switch
                {
                    "Update Git Mods" => await HandleUpdateAsync(ct),
                    "Install New Mods" => await HandleInstallAsync(ct),
                    "Uninstall Mods" => await HandleUninstallAsync(ct),
                    "Convert Local to Git" => await HandleConvertAsync(ct),
                    "Mod Version Selector" => await HandleVersionSelectorAsync(ct),
                    "Refresh Status" => await HandleRefreshAsync(ct),
                    "Exit" => true,
                    _ => false
                };

                if (shouldExit)
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                AnsiConsole.MarkupLine("\n[grey]Press any key to continue...[/]");
                Console.ReadKey(true);
            }
        }
    }

    private async Task RefreshModsAsync(CancellationToken ct)
    {
        _mods = await ProgressReporter.WithStatusAsync(
            "Scanning mods directory...",
            async () => await _modDiscoveryService.DiscoverModsAsync(_modsDirectory, null, ct));
    }

    private async Task<bool> HandleUpdateAsync(CancellationToken ct)
    {
        var updateableMods = _mods
            .Where(m => m.Source == ModSource.Git && m.Status == ModStatus.Behind)
            .ToList();

        if (updateableMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]All mods are up to date![/]");
            WaitForKey();
            return false;
        }

        var prompt = new MultiSelectionPrompt<InstalledMod>()
            .Title("[bold]Select mods to update:[/]")
            .PageSize(15)
            .Required(false)
            .UseConverter(m => $"{m.Name} [grey]({m.CommitsBehind} commits behind)[/]");

        foreach (var mod in updateableMods)
        {
            prompt.AddChoice(mod).Select();
        }

        var selected = AnsiConsole.Prompt(prompt);

        if (selected.Count == 0)
        {
            return false;
        }

        // Show incoming commits for each selected mod
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Incoming commits:[/]");
        AnsiConsole.WriteLine();

        foreach (var mod in selected)
        {
            var commits = await _gitService.GetIncomingCommitsAsync(mod.Path, 5, ct);
            ModTableRenderer.RenderIncomingCommits(mod, commits);
        }

        if (!AnsiConsole.Confirm("Proceed with update?"))
        {
            return false;
        }

        var results = await ProgressReporter.WithBatchProgressAsync(
            "Updating mods",
            selected.ToList(),
            m => m.Name,
            async (mod, progress) =>
            {
                progress.Report(25);
                var fetchOk = await _gitService.FetchAsync(mod.Path, ct: ct);
                if (!fetchOk)
                    return (false, "Fetch failed");

                progress.Report(50);
                var pullOk = await _gitService.PullAsync(mod.Path, ct: ct);
                progress.Report(100);

                return (pullOk, pullOk ? null : "Pull failed");
            });

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task<bool> HandleInstallAsync(CancellationToken ct)
    {
        var orgRepos = await ProgressReporter.WithStatusAsync(
            "Fetching available mods...",
            async () => await _gitHubApiService.GetOrganizationReposAsync(ct));

        var installedNames = _mods.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableRepos = orgRepos
            .Where(r => !installedNames.Contains(r.Name))
            .OrderBy(r => r.Name)
            .ToList();

        if (availableRepos.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]All FCP mods are already installed![/]");
            WaitForKey();
            return false;
        }

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<RemoteRepo>()
                .Title("[bold]Select mods to install:[/]")
                .PageSize(15)
                .Required(false)
                .UseConverter(r => string.IsNullOrEmpty(r.Description)
                    ? r.Name
                    : $"{r.Name} [grey]— {Truncate(r.Description, 50)}[/]")
                .AddChoices(availableRepos));

        if (selected.Count == 0)
        {
            return false;
        }

        var results = await ProgressReporter.WithBatchProgressAsync(
            "Installing mods",
            selected.ToList(),
            r => r.Name,
            async (repo, progress) =>
            {
                var targetPath = Path.Combine(_modsDirectory, repo.Name);

                var cloneOk = await _gitService.CloneAsync(repo.CloneUrl, targetPath, percentProgress: progress, ct: ct);

                return (Success: cloneOk, Error: cloneOk ? null : "Clone failed");
            });

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task<bool> HandleUninstallAsync(CancellationToken ct)
    {
        var installedMods = _mods.ToList();

        if (installedMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No FCP mods installed.[/]");
            WaitForKey();
            return false;
        }

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<InstalledMod>()
                .Title("[bold red]Select mods to uninstall:[/]")
                .PageSize(15)
                .Required(false)
                .UseConverter(m => m.Name)
                .AddChoices(installedMods));

        if (selected.Count == 0)
        {
            return false;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold red]WARNING: This will permanently delete the following mods:[/]");
        foreach (InstalledMod mod in selected)
        {
            AnsiConsole.MarkupLine($"  [red]• {mod.Name}[/] ({mod.Path})");
        }

        AnsiConsole.WriteLine();

        if (!await AnsiConsole.ConfirmAsync("[red]Are you sure you want to delete these mods?[/]", defaultValue: false,
                cancellationToken: ct))
        {
            return false;
        }

        // Double confirmation
        var confirmText = AnsiConsole.Ask<string>("Type [red]DELETE[/] to confirm:");
        if (confirmText != "DELETE")
        {
            AnsiConsole.MarkupLine("[grey]Uninstall cancelled.[/]");
            WaitForKey();
            return false;
        }

        var results = new List<(string Name, bool Success, string? Error)>();

        foreach (InstalledMod mod in selected)
        {
            try
            {
                Directory.Delete(mod.Path, recursive: true);
                results.Add((mod.Name, true, null));
            }
            catch (Exception ex)
            {
                results.Add((mod.Name, false, ex.Message));
            }
        }

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task<bool> HandleConvertAsync(CancellationToken ct)
    {
        var nonGitMods = _mods
            .Where(mod => mod.Source != ModSource.Git && !string.IsNullOrEmpty(mod.MatchedRepoName))
            .ToList();

        if (nonGitMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No local mods found that match FCP repositories.[/]");
            WaitForKey();
            return false;
        }

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<InstalledMod>()
                .Title("[bold]Select mods to convert to Git:[/]")
                .PageSize(15)
                .Required(false)
                .UseConverter(m => $"{m.Name} [grey]→ will clone from {m.MatchedRepoName}[/]")
                .AddChoices(nonGitMods));

        if (selected.Count == 0)
        {
            return false;
        }

        AnsiConsole.MarkupLine("[yellow]Warning: This will replace local folders with fresh git clones.[/]");
        AnsiConsole.MarkupLine("[yellow]Any local modifications (except About.xml changes) will be lost.[/]");

        if (!await AnsiConsole.ConfirmAsync("Proceed with conversion?", cancellationToken: ct))
        {
            return false;
        }

        var results = await ProgressReporter.WithBatchProgressAsync(
            description: "Converting mods to Git",
            items: selected.ToList(),
            nameSelector: mod => mod.Name,
            action: async (mod, progress) =>
            {
                RemoteRepo? repo = await _gitHubApiService.GetRepoByNameAsync(mod.MatchedRepoName!, ct);
                if (repo == null)
                    return (Success: false, Error: "Repository not found");

                try
                {
                    // Delete existing folder
                    Directory.Delete(mod.Path, recursive: true);

                    // Clone fresh with progress
                    var cloneOk = await _gitService.CloneAsync(repo.CloneUrl, mod.Path, percentProgress: progress, ct: ct);

                    return (Success: cloneOk, Error: cloneOk ? null : "Clone failed");
                }
                catch (Exception ex)
                {
                    return (Success: false, Error: ex.Message);
                }
            });

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task<bool> HandleVersionSelectorAsync(CancellationToken ct)
    {
        var gitMods = _mods.Where(m => m.Source == ModSource.Git).ToList();

        if (gitMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No git-based mods found.[/]");
            WaitForKey();
            return false;
        }

        InstalledMod mod = AnsiConsole.Prompt(
            new SelectionPrompt<InstalledMod>()
                .Title("[bold]Select a mod to manage:[/]")
                .PageSize(15)
                .UseConverter(m =>
                    $"{m.Name} [grey]({m.Branch ?? "detached"} @ {m.CurrentCommit?.ShortHash ?? "unknown"})[/]")
                .AddChoices(gitMods));

        // Show current state
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{mod.Name}[/]");
        AnsiConsole.MarkupLine($"  Branch: [cyan]{mod.Branch ?? "detached HEAD"}[/]");
        AnsiConsole.MarkupLine($"  Commit: [grey]{mod.CurrentCommit?.ShortHash ?? "unknown"}[/]");

        if (mod.HasLocalChanges)
        {
            AnsiConsole.MarkupLine("  [yellow]⚠ Has local modifications[/]");
        }

        AnsiConsole.WriteLine();

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices(
                    "Switch Branch",
                    "Checkout Specific Commit",
                    "Back to Main Menu"
                ));

        if (action == "Back to Main Menu")
        {
            return false;
        }

        if (mod.HasLocalChanges)
        {
            AnsiConsole.MarkupLine("[yellow]Warning: You have local changes that may be affected.[/]");
            if (!await AnsiConsole.ConfirmAsync("Continue anyway?", cancellationToken: ct))
            {
                return false;
            }
        }

        switch (action)
        {
            case "Switch Branch":
                await HandleBranchSwitchAsync(mod, ct);
                break;
            case "Checkout Specific Commit":
                await HandleCommitCheckoutAsync(mod, ct);
                break;
        }

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task HandleBranchSwitchAsync(InstalledMod mod, CancellationToken ct)
    {
        var branches = await ProgressReporter.WithStatusAsync(
            "Fetching branches...",
            async () =>
            {
                await _gitService.FetchAsync(mod.Path, ct: ct);
                return await _gitService.GetRemoteBranchesAsync(mod.Path, ct);
            });

        if (branches.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No remote branches found.[/]");
            WaitForKey();
            return;
        }

        var branch = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select branch:")
                .PageSize(15)
                .AddChoices(branches));

        var success = await ProgressReporter.WithStatusAsync(
            $"Switching to {branch}...",
            async () => await _gitService.CheckoutAsync(mod.Path, branch, ct));

        AnsiConsole.MarkupLine(success
            ? $"[green]Switched to branch '{branch}'[/]"
            : $"[red]Failed to switch to branch '{branch}'[/]");

        WaitForKey();
    }

    private async Task HandleCommitCheckoutAsync(InstalledMod mod, CancellationToken ct)
    {
        var commits = await ProgressReporter.WithStatusAsync(
            "Fetching commit history...",
            async () => await _gitService.GetCommitHistoryAsync(mod.Path, 20, ct));

        if (commits.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No commits found.[/]");
            WaitForKey();
            return;
        }

        var method = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How would you like to select a commit?")
                .AddChoices(
                    "Pick from history",
                    "Enter commit hash manually"
                ));

        if (method == "Enter commit hash manually")
        {
            var manualHash = AnsiConsole.Ask<string>("Enter commit hash:").Trim();
            if (string.IsNullOrEmpty(manualHash))
            {
                AnsiConsole.MarkupLine("[yellow]No commit hash provided.[/]");
                WaitForKey();
                return;
            }

            var label = manualHash.Length > 7 ? manualHash[..7] : manualHash;

            var success = await ProgressReporter.WithStatusAsync(
                $"Checking out {label}...",
                async () => await _gitService.CheckoutAsync(mod.Path, manualHash, ct));

            if (success)
            {
                AnsiConsole.MarkupLine($"[green]Checked out commit {label}[/]");
                AnsiConsole.MarkupLine("[yellow]Note: You are now in 'detached HEAD' state.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Failed to checkout commit {label}[/]");
            }
        }
        else
        {
            GitCommitInfo commit = AnsiConsole.Prompt(
                new SelectionPrompt<GitCommitInfo>()
                    .Title("Select commit:")
                    .PageSize(15)
                    .UseConverter(c =>
                        $"[yellow]{c.ShortHash}[/] [grey]{c.Date.ToLocalTime():yyyy-MM-dd}[/] {Markup.Escape(Truncate(c.Message, 50))}")
                    .AddChoices(commits));

            var currentBranch = mod.Branch ?? "HEAD";
            var success = await ProgressReporter.WithStatusAsync(
                $"Resetting {currentBranch} to {commit.ShortHash}...",
                async () => await _gitService.ResetToCommitAsync(mod.Path, commit.Hash, ct));

            if (success)
            {
                AnsiConsole.MarkupLine($"[green]Reset branch '{currentBranch}' to commit {commit.ShortHash}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Failed to reset to commit {commit.ShortHash}[/]");
            }
        }

        WaitForKey();
    }

    private async Task<bool> HandleRefreshAsync(CancellationToken ct)
    {
        await RefreshModsAsync(ct);
        return false;
    }

    private static void WaitForKey()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..(maxLength - 3)] + "...";
    }
}
