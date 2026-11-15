using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.ViewModels;

public sealed class MenuBranchViewModel(GitBranchInfo branch, bool isCurrent, Action<string> pick, bool isForGame = false)
{
    /// <summary>The rimworld-X.Y branch for the player's game version.</summary>
    public bool IsForGame { get; } = isForGame;
    public string Name { get; } = branch.Name;
    public string Display => $"origin/{Name}";
    public bool IsCurrent { get; } = isCurrent;
    public string Mark => IsCurrent ? "✓" : "";
    public string Age => branch.LastCommitDate is { } date ? $"updated {Humanize.Ago(date)}" : "";
    public IRelayCommand PickCommand { get; } = new RelayCommand(() => pick(branch.Name));
}

public enum CommitTag
{
    None,
    Latest,
    New,
    Head,
    Tip
}

public partial class MenuCommitViewModel(GitCommitInfo commit, CommitTag tag, Action<MenuCommitViewModel> pick)
    : ObservableObject
{
    public GitCommitInfo Commit { get; } = commit;
    public CommitTag Tag { get; } = tag;
    public string Hash => Commit.ShortHash;
    public string Message => Commit.Message;
    public string Meta => $"{Commit.Date.ToLocalTime():yyyy-MM-dd} · {Commit.Author}";
    public bool HasTag => Tag != CommitTag.None;
    public bool IsHeadTag => Tag == CommitTag.Head;
    public bool IsWarnTag => HasTag && !IsHeadTag;
    public string TagText => Tag switch
    {
        CommitTag.Latest => "latest",
        CommitTag.New => "new",
        CommitTag.Head => "HEAD",
        CommitTag.Tip => "tip",
        _ => ""
    };

    [ObservableProperty] private bool _isPicked;

    [RelayCommand]
    private void Pick() => pick(this);
}

/// <summary>
/// The branch and commit popovers on a table row: pick a branch, then a commit on it, then
/// fast-forward, reset or switch to it.
/// </summary>
public partial class VersionMenuViewModel : ObservableObject
{
    private readonly InstalledModsViewModel _page;
    private readonly IGitService _git;
    private readonly InstalledMod _mod;
    private CancellationTokenSource? _loadCts;
    private int _headIndex = -1;

    public VersionMenuViewModel(InstalledModsViewModel page, IGitService git, InstalledMod mod)
    {
        _page = page;
        _git = git;
        _mod = mod;
    }

    public ObservableCollection<MenuBranchViewModel> Branches { get; } = [];
    public ObservableCollection<MenuCommitViewModel> Commits { get; } = [];

    public string CurrentBranch => _mod.Branch ?? "HEAD";
    public bool IsDirty => _mod.HasLocalChanges;

    [ObservableProperty] private bool _isBranchOpen;
    [ObservableProperty] private bool _isCommitOpen;

    /// <summary>Branch whose commits are listed.</summary>
    [ObservableProperty] private string _branch = "";

    /// <summary>True when the commit list was reached from the branch list, so "← Branches" shows.</summary>
    [ObservableProperty] private bool _fromBranch;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _emptyText;
    [ObservableProperty] private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cta))]
    [NotifyPropertyChangedFor(nameof(Action))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private MenuCommitViewModel? _picked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _dirtyAcknowledged;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    public bool IsOtherBranch => Branch != CurrentBranch;

    /// <summary>Shown when the listed branch is not the one made for the player's game version.</summary>
    public string? GameWarning => _mod.VersionBranch is { } vb && Branch != vb
        ? $"{Branch} may need a newer RimWorld than {_page.Shell.GameVersion?.Short}. Use {vb} for your game."
        : null;

    /// <summary>What applying the picked commit would do; null when it is already checked out.</summary>
    public VersionAction? Action
    {
        get
        {
            if (Picked is null)
                return null;
            if (IsOtherBranch)
                return VersionAction.SwitchBranch;
            var index = Commits.IndexOf(Picked);
            if (index == _headIndex)
                return null;
            return _headIndex < 0 || index < _headIndex ? VersionAction.FastForward : VersionAction.Reset;
        }
    }

    public string Cta => Picked is null ? "Pick a commit"
        : Action switch
        {
            VersionAction.SwitchBranch => $"Switch to {Branch} @ {Picked.Hash}",
            VersionAction.FastForward => $"Update to {Picked.Hash}",
            VersionAction.Reset => $"Reset to {Picked.Hash}",
            _ => "Already checked out"
        };

    // ── Opening ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task OpenBranchesAsync()
    {
        IsCommitOpen = false;
        IsBranchOpen = true;
        Error = null;
        CancellationToken ct = Restart();

        Branches.Clear();
        IsLoading = true;
        EmptyText = null;
        try
        {
            IReadOnlyList<GitBranchInfo> branches = await _git.GetRemoteBranchInfoAsync(_mod.Path, ct);
            if (ct.IsCancellationRequested)
                return;
            foreach (GitBranchInfo branch in branches
                         .OrderByDescending(b => b.Name == _mod.VersionBranch)
                         .ThenByDescending(b => b.Name == _mod.Branch))
                Branches.Add(new MenuBranchViewModel(branch, branch.Name == _mod.Branch, b => _ = ShowCommitsAsync(b, true),
                    branch.Name == _mod.VersionBranch));
            if (Branches.Count == 0)
                EmptyText = "No remote branches found. Refresh to fetch them.";
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

    [RelayCommand]
    public Task OpenCommitsAsync() => ShowCommitsAsync(CurrentBranch, false);

    [RelayCommand]
    private Task Back() => OpenBranchesAsync();

    public async Task ShowCommitsAsync(string branch, bool fromBranch)
    {
        IsBranchOpen = false;
        IsCommitOpen = true;
        Branch = branch;
        FromBranch = fromBranch;
        Error = null;
        DirtyAcknowledged = false;
        OnPropertyChanged(nameof(IsOtherBranch));
        OnPropertyChanged(nameof(GameWarning));
        CancellationToken ct = Restart();

        Commits.Clear();
        Picked = null;
        _headIndex = -1;
        IsLoading = true;
        EmptyText = null;
        try
        {
            var list = new List<MenuCommitViewModel>();
            if (branch == CurrentBranch)
            {
                IReadOnlyList<GitCommitInfo> incoming = _mod.Branch is null
                    ? []
                    : await _git.GetIncomingCommitsAsync(_mod.Path, 20, ct);
                IReadOnlyList<GitCommitInfo> history = await _git.GetCommitHistoryAsync(_mod.Path, 20, ct);
                if (ct.IsCancellationRequested)
                    return;

                for (var i = 0; i < incoming.Count; i++)
                    list.Add(new MenuCommitViewModel(incoming[i], i == 0 ? CommitTag.Latest : CommitTag.New, Pick));
                _headIndex = list.Count;
                for (var i = 0; i < history.Count; i++)
                    list.Add(new MenuCommitViewModel(history[i], i == 0 ? CommitTag.Head : CommitTag.None, Pick));
                if (history.Count == 0)
                    _headIndex = -1;
            }
            else
            {
                IReadOnlyList<GitCommitInfo> commits = await _git.GetBranchCommitsAsync(_mod.Path, branch, 20, ct);
                if (ct.IsCancellationRequested)
                    return;
                for (var i = 0; i < commits.Count; i++)
                    list.Add(new MenuCommitViewModel(commits[i], i == 0 ? CommitTag.Tip : CommitTag.None, Pick));
            }

            foreach (MenuCommitViewModel commit in list)
                Commits.Add(commit);

            // On the current branch the checked-out commit starts picked; elsewhere the branch tip.
            Pick(_headIndex >= 0 && _headIndex < Commits.Count ? Commits[_headIndex] : Commits.FirstOrDefault());
            if (Commits.Count == 0)
                EmptyText = "No commits found.";
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

    private void Pick(MenuCommitViewModel? commit)
    {
        foreach (MenuCommitViewModel c in Commits)
            c.IsPicked = c == commit;
        Picked = commit;
    }

    private CancellationToken Restart()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        return _loadCts.Token;
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private bool CanApply() => !IsBusy && Action is not null && (!IsDirty || DirtyAcknowledged);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (Action is not { } action || Picked is null)
            return;

        IsBusy = true;
        Error = null;
        try
        {
            var (success, error) = await ModOperations.ApplyVersionAsync(_git, _mod, action, Picked.Commit.Hash,
                IsOtherBranch ? Branch : null);
            if (!success)
            {
                Error = $"{error}. See Git output for details.";
                return;
            }

            Close();
            await _page.Shell.RescanAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void FullHistory()
    {
        Close();
        _page.Shell.OpenVersions(_mod.Path);
    }

    [RelayCommand]
    public void Close()
    {
        _loadCts?.Cancel();
        IsBranchOpen = false;
        IsCommitOpen = false;
        _page.RequestCloseMenus();
    }
}

/// <summary>Radio ring colour for the commit list: accent when picked.</summary>
public static class PickBrushes
{
    public static readonly Avalonia.Data.Converters.IValueConverter Ring =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(picked =>
            picked ? Palette.Accent : Avalonia.Media.Brush.Parse("#8A949E"));
}
