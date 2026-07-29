using FCPModUpdater.Models;
using FCPModUpdater.Services;
using Spectre.Console;

namespace FCPModUpdater.UI;

public class InteractiveMenu
{
    internal const string NotRecommendedTopic = "fcp-not-recommended";

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
            TerminalTheme.WriteHeader();
            ModTableRenderer.RenderModTable(_mods, _gitHubApiService.RemainingRateLimit,
                _gitHubApiService.RateLimitReset);

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

            AnsiConsole.WriteLine();

            var choice = await AnsiConsole.PromptAsync(
                TerminalTheme.Selection<string>("Select operation")
                    .PageSize(10)
                    .AddCancelResult("Exit")
                    .AddChoices(
                        "Update Git Mods",
                        "Install New Mods",
                        "Uninstall Mods",
                        "Convert Local to Git",
                        "Mod Version Selector",
                        "Clear Cache & Refresh",
                        "Exit"
                    ), ct);

            try
            {
                var shouldExit = choice switch
                {
                    "Update Git Mods" => await HandleUpdateAsync(ct),
                    "Install New Mods" => await HandleInstallAsync(ct),
                    "Uninstall Mods" => await HandleUninstallAsync(ct),
                    "Convert Local to Git" => await HandleConvertAsync(ct),
                    "Mod Version Selector" => await HandleVersionSelectorAsync(ct),
                    "Clear Cache & Refresh" => await HandleClearCacheAndRefreshAsync(ct),
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
                AnsiConsole.WriteException(ex, TerminalTheme.ExceptionSettings);
                AnsiConsole.WriteLine();
                TerminalTheme.WriteMessage("PRESS ANY KEY TO CONTINUE", TerminalMessageKind.Muted);
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
            .Where(mod => mod.Source == ModSource.Git && mod.Status == ModStatus.Behind)
            .ToList();

        if (updateableMods.Count == 0)
        {
            TerminalTheme.WriteMessage("ALL MOD RECORDS CURRENT", TerminalMessageKind.Success);
            WaitForKey();
            return false;
        }

        var prompt = TerminalTheme.MultiSelection<InstalledMod>("Select mods to update")
            .PageSize(15)
            .Required(false)
            .AddCancelResult()
            .UseConverter(mod =>
                $"{Markup.Escape(mod.Name)} [{TerminalTheme.Dim.ToMarkup()}]" +
                $"({mod.CommitsBehind} REVISIONS BEHIND)[/]");

        foreach (InstalledMod mod in updateableMods)
        {
            prompt.AddChoice(mod).Select();
        }

        List<InstalledMod> selected = await AnsiConsole.PromptAsync(prompt, ct);

        if (selected.Count == 0)
            return false;

        // Show incoming commits for each selected mod
        AnsiConsole.WriteLine();
        TerminalTheme.WriteSection("Incoming revisions");
        AnsiConsole.WriteLine();

        foreach (InstalledMod mod in selected)
        {
            IReadOnlyList<GitCommitInfo> commits = await _gitService.GetIncomingCommitsAsync(mod.Path, 5, ct);
            ModTableRenderer.RenderIncomingCommits(mod, commits);
        }

        if (!await TerminalTheme.ConfirmAsync("Proceed with update?", true, ct))
            return false;

        var results = await ProgressReporter.WithBatchProgressAsync(
            "Updating mods",
            selected.ToList(),
            mod => mod.Name,
            async (mod, progress) => await GitModUpdater.UpdateAsync(_gitService, mod, progress, ct));

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

        List<RemoteRepo> availableRepos = GetAvailableInstallRepos(orgRepos, _mods.Select(m => m.Name));

        if (availableRepos.Count == 0)
        {
            TerminalTheme.WriteMessage("ALL AVAILABLE FCP MODS ALREADY INSTALLED",
                TerminalMessageKind.Success);
            WaitForKey();
            return false;
        }

        List<RemoteRepo> selectedRepos = await AnsiConsole.PromptAsync(
            TerminalTheme.MultiSelection<RemoteRepo>("Select mods to install")
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
            {
                var targetPath = Path.Combine(_modsDirectory, repo.Name);

                var result = await _gitService.CloneAsync(repo.CloneUrl, targetPath, percentProgress: progress, ct: ct);

                return (result.Success, result.Error);
            });

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
            TerminalTheme.WriteMessage("NO FCP MOD RECORDS INSTALLED", TerminalMessageKind.Warning);
            WaitForKey();
            return false;
        }

        List<InstalledMod> selected = await AnsiConsole.PromptAsync(
            TerminalTheme.MultiSelection<InstalledMod>("Select mods to uninstall")
                .PageSize(15)
                .NotRequired()
                .WrapAround()
                .AddCancelResult()
                .UseConverter(m => m.Name)
                .AddChoices(installedMods), ct);

        if (selected.Count == 0)
            return false;

        AnsiConsole.WriteLine();
        TerminalTheme.WriteMessage("PERMANENT DELETION AUTHORIZATION REQUIRED",
            TerminalMessageKind.Failure);
        foreach (InstalledMod mod in selected)
        {
            TerminalTheme.WriteMessage($"{mod.Name} // {mod.Path}", TerminalMessageKind.Failure);
        }

        AnsiConsole.WriteLine();

        if (!await TerminalTheme.ConfirmAsync("Are you sure you want to delete these mods?",
                defaultValue: false, cancellationToken: ct))
        {
            return false;
        }

        // Double confirmation
        var confirmText = await TerminalTheme.AskAsync("Type DELETE to confirm:", ct);
        if (confirmText != "DELETE")
        {
            TerminalTheme.WriteMessage("UNINSTALLATION CANCELLED", TerminalMessageKind.Muted);
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
            TerminalTheme.WriteMessage("NO LOCAL MODS MATCH FCP REPOSITORY RECORDS",
                TerminalMessageKind.Muted);
            WaitForKey();
            return false;
        }

        var selected = await AnsiConsole.PromptAsync(
            TerminalTheme.MultiSelection<InstalledMod>("Select mods to convert to Git")
                .PageSize(15)
                .Required(false)
                .AddCancelResult()
                .UseConverter(m =>
                    $"{Markup.Escape(m.Name)} [{TerminalTheme.Dim.ToMarkup()}]// CLONE FROM " +
                    $"{Markup.Escape(m.MatchedRepoName ?? "UNKNOWN")}[/]")
                .AddChoices(nonGitMods), ct);

        if (selected.Count == 0)
        {
            return false;
        }

        TerminalTheme.WriteMessage("LOCAL FOLDERS WILL BE REPLACED WITH FRESH GIT CLONES",
            TerminalMessageKind.Warning);
        TerminalTheme.WriteMessage("LOCAL MODIFICATIONS EXCEPT ABOUT.XML CHANGES WILL BE LOST",
            TerminalMessageKind.Warning);

        if (!await TerminalTheme.ConfirmAsync("Proceed with conversion?", cancellationToken: ct))
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
                    var result = await _gitService.CloneAsync(repo.CloneUrl, mod.Path, percentProgress: progress, ct: ct);  

                    return (result.Success, result.Error);        
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
        List<InstalledMod> gitMods = _mods
            .Where(m => m.Source == ModSource.Git)
            .ToList();

        if (gitMods.Count == 0)
        {
            TerminalTheme.WriteMessage("NO GIT-BASED MOD RECORDS FOUND", TerminalMessageKind.Warning);
            WaitForKey();
            return false;
        }

        InstalledMod? mod = await AnsiConsole.PromptAsync(
            TerminalTheme.Selection<InstalledMod>("Select a mod to manage")
                .PageSize(15)
                .AddCancelResult(InstalledMod.Invalid)
                .UseConverter(m =>
                    $"{Markup.Escape(m.Name)} [{TerminalTheme.Dim.ToMarkup()}]" +
                    $"({Markup.Escape(m.Branch ?? "DETACHED")} @ " +
                    $"{Markup.Escape(m.CurrentCommit?.ShortHash ?? "UNKNOWN")})[/]")
                .AddChoices(gitMods), ct);

        if (mod == InstalledMod.Invalid)
            return false;

        // Show current state
        AnsiConsole.WriteLine();
        TerminalTheme.WriteSection(mod.Name);
        TerminalTheme.WriteMessage($"BRANCH // {mod.Branch ?? "DETACHED HEAD"}");
        TerminalTheme.WriteMessage($"REVISION // {mod.CurrentCommit?.ShortHash ?? "UNKNOWN"}",
            TerminalMessageKind.Muted);

        if (mod.HasLocalChanges)
        {
            TerminalTheme.WriteMessage("LOCAL MODIFICATIONS DETECTED", TerminalMessageKind.Warning);
        }

        AnsiConsole.WriteLine();

        var action = await AnsiConsole.PromptAsync(
            TerminalTheme.Selection<string>("Select version operation")
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
            TerminalTheme.WriteMessage("LOCAL CHANGES MAY BE AFFECTED", TerminalMessageKind.Warning);
            if (!await TerminalTheme.ConfirmAsync("Continue anyway?", cancellationToken: ct))
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
        IReadOnlyList<string> branches = await ProgressReporter.WithStatusAsync(
            "Fetching branches...",
            async () =>
            {
                await _gitService.FetchAsync(mod.Path, ct: ct);
                return await _gitService.GetRemoteBranchesAsync(mod.Path, ct);
            });

        if (branches.Count == 0)
        {
            TerminalTheme.WriteMessage("NO REMOTE BRANCH RECORDS FOUND", TerminalMessageKind.Warning);
            WaitForKey();
            return;
        }

        var branch = await AnsiConsole.PromptAsync(
            TerminalTheme.Selection<string>("Select branch")
                .PageSize(15)
                .AddCancelResult("Exit")
                .AddChoices(branches), ct);

        if (branch == "Exit")
            return;

        var success = await ProgressReporter.WithStatusAsync(
            $"Switching to {branch}...",
            async () => await _gitService.CheckoutAsync(mod.Path, branch, ct));

        TerminalTheme.WriteMessage(
            success ? $"BRANCH ACTIVE // {branch}" : $"BRANCH SWITCH FAILED // {branch}",
            success ? TerminalMessageKind.Success : TerminalMessageKind.Failure);

        WaitForKey();
    }

    private async Task HandleCommitCheckoutAsync(InstalledMod mod, CancellationToken ct)
    {
        var commits = await ProgressReporter.WithStatusAsync(
            "Fetching commit history...",
            async () => await _gitService.GetCommitHistoryAsync(mod.Path, 20, ct));

        if (commits.Count == 0)
        {
            TerminalTheme.WriteMessage("NO REVISION RECORDS FOUND", TerminalMessageKind.Warning);
            WaitForKey();
            return;
        }

        var method = await AnsiConsole.PromptAsync(
            TerminalTheme.Selection<string>("Select revision method")
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
                var manualHash = (await TerminalTheme.AskAsync("Enter commit hash:", ct)).Trim();
                if (string.IsNullOrEmpty(manualHash))
                {
                    TerminalTheme.WriteMessage("NO REVISION HASH PROVIDED", TerminalMessageKind.Warning);
                    WaitForKey();
                    return;
                }

                var label = manualHash.Length > 7 ? manualHash[..7] : manualHash;

                var success = await ProgressReporter.WithStatusAsync(
                    $"Checking out {label}...",
                    async () => await _gitService.CheckoutAsync(mod.Path, manualHash, ct));

                if (success)
                {
                    TerminalTheme.WriteMessage($"REVISION ACTIVE // {label}",
                        TerminalMessageKind.Success);
                    TerminalTheme.WriteMessage("REPOSITORY NOW IN DETACHED HEAD STATE",
                        TerminalMessageKind.Warning);
                }
                else
                {
                    TerminalTheme.WriteMessage($"REVISION CHECKOUT FAILED // {label}",
                        TerminalMessageKind.Failure);
                }

                break;
            }
            default:
            {
                GitCommitInfo commit = await AnsiConsole.PromptAsync(
                    TerminalTheme.Selection<GitCommitInfo>("Select revision")
                        .PageSize(15)
                        .AddCancelResult(GitCommitInfo.Invalid)
                        .UseConverter(c =>
                            $"[{TerminalTheme.Warning.ToMarkup()}]{Markup.Escape(c.ShortHash)}[/] " +
                            $"[{TerminalTheme.Dim.ToMarkup()}]{c.Date.ToLocalTime():yyyy-MM-dd}[/] " +
                            $"{Markup.Escape(Truncate(c.Message, 50))}")
                        .AddChoices(commits), ct);

                if (commit == GitCommitInfo.Invalid)
                    return;

                var currentBranch = mod.Branch ?? "HEAD";
                var success = await ProgressReporter.WithStatusAsync(
                    $"Resetting {currentBranch} to {commit.ShortHash}...",
                    async () => await _gitService.ResetToCommitAsync(mod.Path, commit.Hash, ct));

                TerminalTheme.WriteMessage(
                    success
                        ? $"BRANCH {currentBranch} RESET TO {commit.ShortHash}"
                        : $"REVISION RESET FAILED // {commit.ShortHash}",
                    success ? TerminalMessageKind.Success : TerminalMessageKind.Failure);
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
        TerminalTheme.WriteMessage("PRESS ANY KEY TO CONTINUE", TerminalMessageKind.Muted);
        Console.ReadKey(true);
    }

    private static List<RemoteRepo> GetAvailableInstallRepos(
        IReadOnlyList<RemoteRepo> orgRepos,
        IEnumerable<string> installedNames)
    {
        var installedNameSet = new HashSet<string>(installedNames, StringComparer.OrdinalIgnoreCase);

        return orgRepos
            .Where(repo => !installedNameSet.Contains(repo.Name))
            .OrderBy(IsNotRecommended)
            .ThenBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsNotRecommended(RemoteRepo repo) =>
        repo.Topics.Contains(NotRecommendedTopic, StringComparer.OrdinalIgnoreCase);

    private static string FormatInstallRepoChoice(RemoteRepo repo)
    {
        var warning = IsNotRecommended(repo)
            ? $" [{TerminalTheme.Warning.ToMarkup()}](NOT RECOMMENDED FOR USE)[/]"
            : string.Empty;

        return string.IsNullOrEmpty(repo.Description)
            ? $"{Markup.Escape(repo.Name)}{warning}"
            : $"{Markup.Escape(repo.Name)}{warning} [{TerminalTheme.Dim.ToMarkup()}]// " +
              $"{Markup.Escape(Truncate(repo.Description, 50))}[/]";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..(maxLength - 3)] + "...";
    }
}
