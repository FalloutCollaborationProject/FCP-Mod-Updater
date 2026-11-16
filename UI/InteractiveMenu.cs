using FCPModUpdater.Models;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;
using Spectre.Console;

namespace FCPModUpdater.UI;

public class InteractiveMenu
{
    private readonly IGitService _gitService;
    private readonly IGitHubApiService _gitHubApiService;
    private readonly IModDiscoveryService _modDiscoveryService;
    private readonly string _modsDirectory;
    private readonly Task<UpdateCheckResult?> _updateCheckTask;
    private readonly SettingsMenu? _settingsMenu;
    private readonly SettingsStore? _settingsStore;

    private IReadOnlyList<InstalledMod> _mods = [];
    private bool _updateNotificationShown;

    public InteractiveMenu(
        IGitService gitService,
        IGitHubApiService gitHubApiService,
        IModDiscoveryService modDiscoveryService,
        string modsDirectory,
        Task<UpdateCheckResult?> updateCheckTask,
        SettingsMenu? settingsMenu = null,
        SettingsStore? settingsStore = null)
    {
        _settingsStore = settingsStore;
        _settingsMenu = settingsMenu;
        _gitService = gitService;
        _gitHubApiService = gitHubApiService;
        _modDiscoveryService = modDiscoveryService;
        _modsDirectory = modsDirectory;
        _updateCheckTask = updateCheckTask;
    }

    private GameVersion? GameVersion => GameVersionService.Resolve(_modsDirectory, _settingsStore?.Current).Version;

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Initial scan
        await RefreshModsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            AnsiConsole.Clear();
            ModTableRenderer.RenderModTable(_mods, _gitHubApiService.RemainingRateLimit,
                _gitHubApiService.RateLimitReset, GameVersion);

            // Show update notification once, non-blocking
            if (!_updateNotificationShown && _updateCheckTask.IsCompleted)
            {
                _updateNotificationShown = true;
                var updateResult = await _updateCheckTask;
                if (updateResult != null)
                {
                    AppUpdateNoticeRenderer.Render(updateResult);
                }
            }

            GuiNoticeRenderer.RenderOnce(_settingsStore);

            AnsiConsole.WriteLine();

            var mismatched = ModCatalog.GetVersionMismatchedMods(_mods);
            var switchChoice = $"Switch {mismatched.Count} Mod(s) to the Branch for RimWorld {GameVersion?.Short}";
            var choices = new List<string>();
            if (mismatched.Count > 0)
                choices.Add(switchChoice);

            var choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .Title("[bold]What would you like to do?[/]")
                    .PageSize(11)
                    .AddCancelResult("Exit")
                    .AddChoices(choices)
                    .AddChoices(
                        "Update Git Mods",
                        "Install New Mods",
                        "Uninstall Mods",
                        "Convert Local to Git",
                        "Mod Version Selector",
                        "Clear Cache & Refresh",
                        "Settings",
                        "Exit"
                    ), ct);

            try
            {
                if (choice == switchChoice)
                {
                    await HandleSwitchToVersionBranchAsync(mismatched, ct);
                    continue;
                }

                var shouldExit = choice switch
                {
                    "Update Git Mods" => await HandleUpdateAsync(ct),
                    "Install New Mods" => await HandleInstallAsync(ct),
                    "Uninstall Mods" => await HandleUninstallAsync(ct),
                    "Convert Local to Git" => await HandleConvertAsync(ct),
                    "Mod Version Selector" => await HandleVersionSelectorAsync(ct),
                    "Clear Cache & Refresh" => await HandleClearCacheAndRefreshAsync(ct),
                    "Settings" => await HandleSettingsAsync(ct),
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
        var updateableMods = ModCatalog.GetUpdateableMods(_mods);
        var skipped = ModCatalog.GetUpdateableMods(_mods, includeMismatched: true).Except(updateableMods).ToList();
        if (skipped.Count > 0)
            AnsiConsole.MarkupLine($"[yellow]⚠ Not offering {skipped.Count} mod(s) on the wrong branch for RimWorld {GameVersion?.Short}; use \"Switch ... Branch\" from the main menu.[/]");

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
            .AddCancelResult()
            .UseConverter(mod => $"{mod.Name} [grey]({mod.CommitsBehind} commits behind)[/]");

        foreach (InstalledMod mod in updateableMods)
        {
            prompt.AddChoice(mod).Select();
        }

        List<InstalledMod> selected = await AnsiConsole.PromptAsync(prompt, ct);

        if (selected.Count == 0)
            return false;

        // Show incoming commits for each selected mod
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Incoming commits:[/]");
        AnsiConsole.WriteLine();

        foreach (InstalledMod mod in selected)
        {
            IReadOnlyList<GitCommitInfo> commits = await _gitService.GetIncomingCommitsAsync(mod.Path, 5, ct);
            ModTableRenderer.RenderIncomingCommits(mod, commits);
        }

        if (!await AnsiConsole.ConfirmAsync("Proceed with update?", true, ct))
            return false;

        var results = await ProgressReporter.WithBatchProgressAsync(
            "Updating mods",
            selected.ToList(),
            mod => mod.Name,
            async (mod, progress) => await GitModUpdater.UpdateAsync(_gitService, mod, progress, ct),
            ParallelRunner.ResolveParallelism(selected.Count(),
                _settingsStore?.Current.EffectiveUpdateParallelism ?? AppSettings.DefaultUpdateParallelism));

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task<bool> HandleInstallAsync(CancellationToken ct)
    {
        IReadOnlyList<RemoteRepo> orgRepos = await ProgressReporter.WithStatusAsync(
            "Fetching available mods...",
            async () => await _gitHubApiService.GetOrganizationReposAsync(ct));

        List<RemoteRepo> availableRepos = ModCatalog.GetAvailableInstallRepos(orgRepos, _mods.Select(m => m.Name));

        if (availableRepos.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]All FCP mods are already installed![/]");
            WaitForKey();
            return false;
        }

        List<RemoteRepo> selectedRepos = await AnsiConsole.PromptAsync(
            new MultiSelectionPrompt<RemoteRepo>()
                .Title("[bold]Select mods to install:[/]")
                .InstructionsText("[grey](Press <space> to mark a mod for install, <enter> to confirm)[/]")
                .PageSize(15)
                .NotRequired()
                .WrapAround() // Pressing down on last item goes to first
                .AddCancelResult()
                .UseConverter(FormatInstallRepoChoice)
                .AddChoices(availableRepos), ct);

        if (selectedRepos.Count == 0)
            return false;

        var results = await ProgressReporter.WithBatchProgressAsync(
            "Installing mods",
            selectedRepos.ToList(),
            repo => repo.Name,
            async (repo, progress) =>
                await ModOperations.InstallAsync(_gitService, repo, _modsDirectory, progress, GameVersion, ct));

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task<bool> HandleUninstallAsync(CancellationToken ct)
    {
        List<InstalledMod> installedMods = _mods.ToList();

        if (installedMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No FCP mods installed.[/]");
            WaitForKey();
            return false;
        }

        List<InstalledMod> selected = await AnsiConsole.PromptAsync(
            new MultiSelectionPrompt<InstalledMod>()
                .Title("[bold red]Select mods to uninstall:[/]")
                .InstructionsText("[grey](Press <space> to mark a mod for uninstallation, <enter> to confirm)[/]")
                .PageSize(15)
                .NotRequired()
                .WrapAround()
                .AddCancelResult()
                .UseConverter(m => m.Name)
                .AddChoices(installedMods), ct);

        if (selected.Count == 0)
            return false;

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
        var confirmText = await AnsiConsole.AskAsync<string>("Type [red]DELETE[/] to confirm:", ct);
        if (confirmText != "DELETE")
        {
            AnsiConsole.MarkupLine("[grey]Uninstall cancelled.[/]");
            WaitForKey();
            return false;
        }

        var results = new List<(string Name, bool Success, string? Error)>();

        foreach (InstalledMod mod in selected)
        {
            var (success, error) = ModOperations.Uninstall(mod);
            results.Add((mod.Name, success, error));
        }

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task<bool> HandleConvertAsync(CancellationToken ct)
    {
        var nonGitMods = ModCatalog.GetConvertibleMods(_mods);

        if (nonGitMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No local mods found that match FCP repositories.[/]");
            WaitForKey();
            return false;
        }

        var selected = await AnsiConsole.PromptAsync(
            new MultiSelectionPrompt<InstalledMod>()
                .Title("[bold]Select mods to convert to Git:[/]")
                .PageSize(15)
                .Required(false)
                .AddCancelResult()
                .UseConverter(m => $"{m.Name} [grey]→ will clone from {m.MatchedRepoName}[/]")
                .AddChoices(nonGitMods), ct);

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
                await ModOperations.ConvertToGitAsync(_gitService, _gitHubApiService, mod, progress, GameVersion, ct));

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();

        await RefreshModsAsync(ct);
        return false;
    }

    private async Task<bool> HandleVersionSelectorAsync(CancellationToken ct)
    {
        List<InstalledMod> gitMods = _mods
            .Where(m => m.Source == ModSource.Git)
            .ToList();

        if (gitMods.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No git-based mods found.[/]");
            WaitForKey();
            return false;
        }

        InstalledMod? mod = await AnsiConsole.PromptAsync(
            new SelectionPrompt<InstalledMod>()
                .Title("[bold]Select a mod to manage:[/]")
                .PageSize(15)
                .AddCancelResult(InstalledMod.Invalid)
                .UseConverter(m =>
                    $"{m.Name} [grey]({m.Branch ?? "detached"} @ {m.CurrentCommit?.ShortHash ?? "unknown"})[/]")
                .AddChoices(gitMods), ct);

        if (mod == InstalledMod.Invalid)
            return false;

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

        var action = await AnsiConsole.PromptAsync(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddCancelResult("Back to Main Menu")
                .AddChoices(
                    "Switch Branch",
                    "Checkout Specific Commit",
                    "Back to Main Menu"
                ), ct);

        if (action == "Back to Main Menu")
            return false;

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

    private async Task HandleSwitchToVersionBranchAsync(IReadOnlyList<InstalledMod> mismatched, CancellationToken ct)
    {
        AnsiConsole.MarkupLine($"[bold]These mods are not on the branch for RimWorld {Markup.Escape(GameVersion?.Short ?? "")}:[/]");

        var prompt = new MultiSelectionPrompt<InstalledMod>()
            .Title("[bold]Select mods to switch:[/]")
            .PageSize(15)
            .Required(false)
            .AddCancelResult()
            .UseConverter(m => $"{Markup.Escape(ModTableRenderer.DescribeVersionMismatch(m, GameVersion))}"
                               + (m.HasLocalChanges ? " [yellow](local changes will be lost)[/]" : "")
                               + (m.CommitsAhead > 0 ? $" [yellow]({m.CommitsAhead} local commit(s) will be dropped)[/]" : ""));
        foreach (InstalledMod mod in mismatched)
            prompt.AddChoice(mod).Select();

        List<InstalledMod> selected = await AnsiConsole.PromptAsync(prompt, ct);
        if (selected.Count == 0)
            return;
        if (selected.Any(m => m.HasLocalChanges || m.CommitsAhead > 0)
            && !await AnsiConsole.ConfirmAsync("[yellow]Some mods have local changes or commits that will be lost. Continue?[/]", false, ct))
            return;

        var results = await ProgressReporter.WithBatchProgressAsync(
            "Switching branches",
            selected,
            mod => mod.Name,
            async (mod, _) => await ModOperations.SwitchToVersionBranchAsync(_gitService, mod, ct));

        ModTableRenderer.RenderUpdateSummary(results);
        WaitForKey();
        await RefreshModsAsync(ct);
    }

    private async Task<bool> HandleSettingsAsync(CancellationToken ct)
    {
        if (_settingsMenu != null)
            await _settingsMenu.RunAsync(ct);
        return false;
    }

    private async Task HandleBranchSwitchAsync(InstalledMod mod, CancellationToken ct)
    {
        IReadOnlyList<string> branches = await ProgressReporter.WithStatusAsync(
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

        var branch = await AnsiConsole.PromptAsync(
            new SelectionPrompt<string>()
                .Title("Select branch:")
                .PageSize(15)
                .AddCancelResult("Exit")
                .AddChoices(branches), ct);

        if (branch == "Exit")
            return;

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

        var method = await AnsiConsole.PromptAsync(
            new SelectionPrompt<string>()
                .Title("How would you like to select a commit?")
                .AddCancelResult("Exit")
                .AddChoices(
                    "Pick from history",
                    "Enter commit hash manually"
                ), ct);

        switch (method)
        {
            case "Exit":
            {
                return;
            }
            case "Enter commit hash manually":
            {
                var manualHash = (await AnsiConsole.AskAsync<string>("Enter commit hash:", ct)).Trim();
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

                break;
            }
            default:
            {
                GitCommitInfo commit = await AnsiConsole.PromptAsync(
                    new SelectionPrompt<GitCommitInfo>()
                        .Title("Select commit:")
                        .PageSize(15)
                        .AddCancelResult(GitCommitInfo.Invalid)
                        .UseConverter(c =>
                            $"[yellow]{c.ShortHash}[/] [grey]{c.Date.ToLocalTime():yyyy-MM-dd}[/] {Markup.Escape(Truncate(c.Message, 50))}")
                        .AddChoices(commits), ct);

                if (commit == GitCommitInfo.Invalid)
                    return;

                var currentBranch = mod.Branch ?? "HEAD";
                var success = await ProgressReporter.WithStatusAsync(
                    $"Resetting {currentBranch} to {commit.ShortHash}...",
                    async () => await _gitService.ResetToCommitAsync(mod.Path, commit.Hash, ct));

                AnsiConsole.MarkupLine(success
                    ? $"[green]Reset branch '{currentBranch}' to commit {commit.ShortHash}[/]"
                    : $"[red]Failed to reset to commit {commit.ShortHash}[/]");
                break;
            }
        }

        WaitForKey();
    }

    private async Task<bool> HandleClearCacheAndRefreshAsync(CancellationToken ct)
    {
        _gitHubApiService.ClearCache();
        await RefreshModsAsync(ct);
        return false;
    }

    private static void WaitForKey()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    private static string FormatInstallRepoChoice(RemoteRepo repo)
    {
        var warning = ModCatalog.IsNotRecommended(repo) ? " [yellow](not recommended for use)[/]" : string.Empty;

        return string.IsNullOrEmpty(repo.Description)
            ? $"{repo.Name}{warning}"
            : $"{repo.Name}{warning} [grey]— {Truncate(repo.Description, 50)}[/]";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..(maxLength - 3)] + "...";
    }
}
