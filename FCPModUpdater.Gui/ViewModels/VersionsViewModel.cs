using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.ViewModels;

public enum VersionTab
{
    Branches,
    History,
    Manual
}

public sealed class VersionModViewModel(InstalledMod mod)
{
    public InstalledMod Mod { get; } = mod;
    public string Name => Mod.Name;
    public string Path => Mod.Path;
    public string Ref => $"{Mod.Branch ?? "detached"} @ {Mod.CurrentCommit?.ShortHash ?? "unknown"}";
    public bool IsDirty => Mod.HasLocalChanges;
}

public sealed class BranchViewModel(GitBranchInfo branch, bool isCurrent)
{
    public string Name { get; } = branch.Name;
    public string Display => $"origin/{Name}";
    public bool IsCurrent { get; } = isCurrent;
    public string Age => branch.LastCommitDate is { } date ? $"updated {Humanize.Ago(date)}" : "";
}

public sealed class HistoryCommitViewModel(GitCommitInfo commit, bool isHead)
{
    public GitCommitInfo Commit { get; } = commit;
    public string Hash => Commit.ShortHash;
    public string Date => Commit.Date.ToLocalTime().ToString("yyyy-MM-dd");
    public string Message => Commit.Message;
    public bool IsHead { get; } = isHead;
}

public partial class TabViewModel(string label, VersionTab tab, Action<VersionTab> pick) : ObservableObject
{
    public string Label { get; } = label;
    public VersionTab Tab { get; } = tab;

    [ObservableProperty] private bool _isActive;

    [RelayCommand]
    private void Pick() => pick(Tab);
}

/// <summary>
/// "Version selector" page: switch branch, reset to a commit from history, or check out a typed hash.
/// </summary>
public partial class VersionsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly IGitService _git;
    private CancellationTokenSource? _loadCts;

    public VersionsViewModel(MainWindowViewModel shell, IGitService git)
    {
        _shell = shell;
        _git = git;
        Tabs =
        [
            new TabViewModel("Switch branch", VersionTab.Branches, t => Tab = t) { IsActive = true },
            new TabViewModel("Pick from history", VersionTab.History, t => Tab = t),
            new TabViewModel("Enter commit hash", VersionTab.Manual, t => Tab = t)
        ];
    }

    public ObservableCollection<VersionModViewModel> Mods { get; } = [];
    public IReadOnlyList<TabViewModel> Tabs { get; }
    public ObservableCollection<BranchViewModel> Branches { get; } = [];
    public ObservableCollection<HistoryCommitViewModel> Commits { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMod))]
    [NotifyPropertyChangedFor(nameof(NeedsDirtyConfirmation))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private VersionModViewModel? _selectedMod;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBranches))]
    [NotifyPropertyChangedFor(nameof(IsHistory))]
    [NotifyPropertyChangedFor(nameof(IsManual))]
    [NotifyPropertyChangedFor(nameof(Cta))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private VersionTab _tab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cta))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private BranchViewModel? _selectedBranch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cta))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private HistoryCommitViewModel? _selectedCommit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _manualHash = "";

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _dirtyAcknowledged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultMessage;

    [ObservableProperty] private bool _resultIsError;
    [ObservableProperty] private string _emptyText = "";

    /// <summary>
    /// True while the page is on screen. Loading branches runs a fetch, so it is not done for a hidden page.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;

            _isActive = value;
            if (value)
                _ = LoadAsync();
        }
    }

    private bool _isActive;

    public bool HasMod => SelectedMod is not null;
    public bool HasResult => !string.IsNullOrEmpty(ResultMessage);
    public bool IsBranches => Tab == VersionTab.Branches;
    public bool IsHistory => Tab == VersionTab.History;
    public bool IsManual => Tab == VersionTab.Manual;
    public bool NeedsDirtyConfirmation => SelectedMod is { IsDirty: true };

    public string Cta => Tab switch
    {
        VersionTab.Branches => SelectedBranch is null ? "Switch branch" : $"Switch to {SelectedBranch.Name}",
        VersionTab.History => SelectedCommit is null ? "Reset to commit" : $"Reset to {SelectedCommit.Hash}",
        _ => "Check out commit"
    };

    public string Hint => Tab switch
    {
        VersionTab.Branches => "Runs git fetch, then git checkout on the selected branch.",
        VersionTab.History => "Resets the current branch to this commit (git reset --hard).",
        _ => "You will be in detached HEAD state afterwards."
    };

    /// <summary>Called by the shell after every scan.</summary>
    public void OnModsChanged()
    {
        var selectedPath = SelectedMod?.Path;

        Mods.Clear();
        foreach (InstalledMod mod in _shell.Mods.Where(m => m.Source == ModSource.Git))
            Mods.Add(new VersionModViewModel(mod));

        SelectedMod = Mods.FirstOrDefault(m => m.Path == selectedPath) ?? Mods.FirstOrDefault();
    }

    public void Select(string path)
    {
        VersionModViewModel? mod = Mods.FirstOrDefault(m => m.Path == path);
        if (mod is not null)
            SelectedMod = mod;
    }

    partial void OnSelectedModChanged(VersionModViewModel? oldValue, VersionModViewModel? newValue)
    {
        // A rescan replaces the item with a fresh one for the same folder; keep the outcome message then.
        if (oldValue?.Path != newValue?.Path)
        {
            ResultMessage = null;
            DirtyAcknowledged = false;
        }

        _ = LoadAsync();
    }

    partial void OnTabChanged(VersionTab value)
    {
        foreach (TabViewModel tab in Tabs)
            tab.IsActive = tab.Tab == value;

        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken ct = _loadCts.Token;

        Branches.Clear();
        Commits.Clear();
        SelectedBranch = null;
        SelectedCommit = null;
        EmptyText = "";

        if (!IsActive || SelectedMod is not { } mod || Tab == VersionTab.Manual)
            return;

        IsLoading = true;
        try
        {
            if (Tab == VersionTab.Branches)
            {
                await _git.FetchAsync(mod.Path, ct: ct);
                IReadOnlyList<GitBranchInfo> branches = await _git.GetRemoteBranchInfoAsync(mod.Path, ct);
                if (ct.IsCancellationRequested) return;

                foreach (GitBranchInfo branch in branches.OrderByDescending(b => b.Name == mod.Mod.Branch))
                    Branches.Add(new BranchViewModel(branch, branch.Name == mod.Mod.Branch));

                SelectedBranch = Branches.FirstOrDefault();
                if (Branches.Count == 0) EmptyText = "No remote branches found.";
            }
            else
            {
                IReadOnlyList<GitCommitInfo> commits = await _git.GetCommitHistoryAsync(mod.Path, 20, ct);
                if (ct.IsCancellationRequested) return;

                var head = mod.Mod.CurrentCommit?.Hash;
                foreach (GitCommitInfo commit in commits)
                    Commits.Add(new HistoryCommitViewModel(commit, commit.Hash == head));

                SelectedCommit = Commits.FirstOrDefault();
                if (Commits.Count == 0) EmptyText = "No commits found.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private bool CanApply()
    {
        if (SelectedMod is null || IsBusy)
            return false;

        if (NeedsDirtyConfirmation && !DirtyAcknowledged)
            return false;

        return Tab switch
        {
            VersionTab.Branches => SelectedBranch is not null,
            VersionTab.History => SelectedCommit is not null,
            _ => !string.IsNullOrWhiteSpace(ManualHash)
        };
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        VersionModViewModel mod = SelectedMod!;
        IsBusy = true;
        ResultMessage = null;

        bool success;
        string ok, failed;

        try
        {
            switch (Tab)
            {
                case VersionTab.Branches:
                {
                    var branch = SelectedBranch!.Name;
                    (success, _) = await ModOperations.ApplyVersionAsync(_git, mod.Mod, VersionAction.Checkout, branch);
                    ok = $"Switched to branch '{branch}'.";
                    failed = $"Failed to switch to branch '{branch}'. See Git output for details.";
                    break;
                }
                case VersionTab.History:
                {
                    HistoryCommitViewModel commit = SelectedCommit!;
                    var current = mod.Mod.Branch ?? "HEAD";
                    (success, _) = await ModOperations.ApplyVersionAsync(_git, mod.Mod, VersionAction.Reset, commit.Commit.Hash);
                    ok = $"Reset branch '{current}' to commit {commit.Hash}.";
                    failed = $"Failed to reset to commit {commit.Hash}. See Git output for details.";
                    break;
                }
                default:
                {
                    var hash = ManualHash.Trim();
                    var label = hash.Length > 7 ? hash[..7] : hash;
                    (success, _) = await ModOperations.ApplyVersionAsync(_git, mod.Mod, VersionAction.Checkout, hash);
                    ok = $"Checked out commit {label}. You are now in detached HEAD state.";
                    failed = $"Failed to check out commit {label}. See Git output for details.";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            success = false;
            ok = "";
            failed = ex.Message;
        }

        IsBusy = false;

        if (success)
        {
            ManualHash = "";
            await _shell.RescanAsync();
        }

        ResultIsError = !success;
        ResultMessage = success ? ok : failed;
    }

    [RelayCommand]
    private void Cancel() => _shell.Navigate(Page.Installed);
}
