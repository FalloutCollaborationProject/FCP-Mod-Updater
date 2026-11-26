using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.ViewModels;

public partial class FilterChipViewModel(string label, IBrush dot, Func<ModRowViewModel, bool> test, Action<FilterChipViewModel> pick)
    : ObservableObject
{
    public string Label { get; } = label;
    public IBrush Dot { get; } = dot;
    public Func<ModRowViewModel, bool> Test { get; } = test;

    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isActive;

    [RelayCommand]
    private void Pick() => pick(this);
}

/// <summary>
/// The main window's page: status table, detail pane and the git output dock.
/// </summary>
public partial class InstalledModsViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly IGitService _git;
    private List<ModRowViewModel> _allRows = [];
    private CancellationTokenSource? _detailCts;

    public InstalledModsViewModel(MainWindowViewModel shell, IGitService git, GitOutputLog log)
    {
        _shell = shell;
        _git = git;
        Log = log;

        Chips =
        [
            new("All", Palette.TextDim, _ => true, PickChip),
            new("Updates", Palette.Warn, r => r.Badge.Kind == StatusKind.Behind, PickChip),
            new("Local changes", Palette.Info,
                r => r.Badge.Kind is StatusKind.Modified or StatusKind.Ahead or StatusKind.Diverged, PickChip),
            new("Non-Git", Palette.TextFaint, r => r.Badge.Kind == StatusKind.NonGit, PickChip),
            new("Errors", Palette.Error, r => r.Badge.Kind == StatusKind.Error, PickChip)
        ];
        _activeChip = Chips[0];
        _activeChip.IsActive = true;

        log.LineAdded += OnLineAdded;
        log.Cleared += () => VisibleLines.Clear();
        log.Problems.CollectionChanged += (_, _) => RefreshProblems();
    }

    public MainWindowViewModel Shell => _shell;
    public GitOutputLog Log { get; }

    // ── Table ────────────────────────────────────────────────────────────────

    public IReadOnlyList<FilterChipViewModel> Chips { get; }
    public ObservableCollection<ModRowViewModel> Rows { get; } = [];

    /// <summary>
    /// What the table shows: a header per category (reverse alphabetical) followed by its rows while expanded.
    /// Without several categories this is just <see cref="Rows"/>. Collapsed rows stay checked and updateable.
    /// </summary>
    public ObservableCollection<object> TableItems { get; } = [];

    private readonly HashSet<string> _collapsedGroups = CategoryGroups.CollapsedSet();

    /// <summary>The table's selected item; header rows are ignored.</summary>
    public object? SelectedTableItem
    {
        get => SelectedRow;
        set
        {
            if (value is ModRowViewModel row)
                SelectedRow = row;
            else
                OnPropertyChanged();
        }
    }


    private FilterChipViewModel _activeChip;

    [ObservableProperty] private string _searchText = "";

    [ObservableProperty] private IReadOnlyList<CategoryOption> _categories = [CategoryOption.All];
    [ObservableProperty] private CategoryOption _selectedCategory = CategoryOption.All;

    public bool HasCategories => Categories.Count > 2;

    partial void OnSelectedCategoryChanged(CategoryOption value) => ApplyFilter();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(UninstallLabel))]
    private ModRowViewModel? _selectedRow;

    /// <summary>Every highlighted row (Ctrl/Shift-click); <see cref="SelectedRow"/> is the one shown in detail.</summary>
    public IReadOnlyList<ModRowViewModel> SelectedRows { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateLabel))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCheckedCommand))]
    private int _checkedCount;

    [ObservableProperty] private string _summary = "Not scanned yet";

    public bool HasSelection => SelectedRow is not null;
    public string UpdateLabel => $"Update {Humanize.Count(CheckedCount, "mod")}";

    public string UninstallLabel => SelectedRows.Count > 1 ? $"Uninstall {SelectedRows.Count}…" : "Uninstall…";

    public void SetSelection(IReadOnlyList<ModRowViewModel> rows)
    {
        SelectedRows = rows;
        OnPropertyChanged(nameof(UninstallLabel));
    }

    /// <summary>Called by the shell after every scan.</summary>
    public void OnModsChanged()
    {
        var selectedPath = SelectedRow?.Path;

        foreach (ModRowViewModel row in _allRows)
            row.PropertyChanged -= OnRowChanged;

        IReadOnlyList<ModCategory> categories = _shell.Categories;
        (Categories, SelectedCategory) = CategoryOption.Build(categories, SelectedCategory);
        OnPropertyChanged(nameof(HasCategories));

        _allRows = _shell.Mods
            .Select(m => new ModRowViewModel(m, _shell.GameVersion?.Short)
            {
                Menu = m.Source == ModSource.Git ? new VersionMenuViewModel(this, _git, m) : null,
                Category = ModCategories.Find(categories, m.CategoryId),
            })
            .ToList();

        foreach (ModRowViewModel row in _allRows)
            row.PropertyChanged += OnRowChanged;

        foreach (FilterChipViewModel chip in Chips)
            chip.Count = _allRows.Count(chip.Test);

        ApplyFilter();
        SelectedRow = Rows.FirstOrDefault(r => r.Path == selectedPath) ?? Rows.FirstOrDefault();
        UpdateCheckedCount();
        OnPropertyChanged(nameof(HasUpdateable));
        WrongBranchCount = _allRows.Count(r => r.IsWrongBranch);
        RefreshSummary();
        RefreshProblems();
    }

    public void RefreshSummary()
    {
        if (_shell.LastScanned is not { } scanned)
        {
            Summary = _shell.IsScanning ? "Scanning…" : "Not scanned yet";
            return;
        }

        var git = _allRows.Count(r => r.IsGit);
        Summary = $"Last scanned {Humanize.Ago(scanned)} · {git} Git · {_allRows.Count - git} local";
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModRowViewModel.IsChecked))
            UpdateCheckedCount();
    }

    private void UpdateCheckedCount()
    {
        CheckedCount = _allRows.Count(r => r is { CanUpdate: true, IsChecked: true });
        _allChecked = _allRows.Any(r => r.CanUpdate) && _allRows.Where(r => r.CanUpdate).All(r => r.IsChecked);
        OnPropertyChanged(nameof(AllChecked));
    }

    private bool _allChecked;

    /// <summary>Header checkbox: ticks or clears every row that can be updated.</summary>
    public bool AllChecked
    {
        get => _allChecked;
        set
        {
            foreach (ModRowViewModel row in _allRows.Where(r => r.CanUpdate))
                row.IsChecked = value;
            UpdateCheckedCount();
        }
    }

    public bool HasUpdateable => _allRows.Any(r => r.CanUpdate);

    /// <summary>Raised when a row popover asks to be closed (the view owns the flyouts).</summary>
    public event Action? CloseMenusRequested;

    public void RequestCloseMenus() => CloseMenusRequested?.Invoke();

    [RelayCommand]
    private void OpenRepo(ModRowViewModel? row)
    {
        if ((row ?? SelectedRow)?.RepoUrl is { } url)
            _shell.Platform.OpenUrl(url);
    }

    [RelayCommand]
    private void OpenSelectedFolder()
    {
        if (SelectedRow is { } row)
            _shell.Platform.OpenFolder(row.Path);
    }

    private void PickChip(FilterChipViewModel chip)
    {
        _activeChip.IsActive = false;
        _activeChip = chip;
        chip.IsActive = true;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        ModRowViewModel? selected = SelectedRow;
        var search = SearchText.Trim();

        Rows.Clear();
        foreach (ModRowViewModel row in _allRows.Where(r => _activeChip.Test(r) && r.Matches(search)
                     && (SelectedCategory.Id is not { } id || string.Equals(r.Mod.CategoryId, id, StringComparison.OrdinalIgnoreCase))))
            Rows.Add(row);

        RebuildTable();
        SelectedRow = selected is not null && Rows.Contains(selected) ? selected : Rows.FirstOrDefault();
    }

    private void RebuildTable()
    {
        TableItems.Clear();
        if (!HasCategories)
        {
            foreach (ModRowViewModel row in Rows)
                TableItems.Add(row);
            return;
        }

        foreach (var (category, rows) in CategoryGroups.Group(Rows, r => r.Mod.CategoryId, _shell.Categories))
        {
            var group = new ModGroupViewModel(category, rows, _collapsedGroups, OnGroupToggled);
            TableItems.Add(group);
            if (group.IsExpanded)
                foreach (ModRowViewModel row in rows)
                    TableItems.Add(row);
        }
    }

    private void OnGroupToggled()
    {
        ModRowViewModel? selected = SelectedRow;
        RebuildTable();
        // Keep the detail pane on the selected mod even when its group was collapsed.
        SelectedRow = selected;
        OnPropertyChanged(nameof(SelectedTableItem));
    }

    // ── Detail pane ──────────────────────────────────────────────────────────

    public ObservableCollection<CommitViewModel> Incoming { get; } = [];

    [ObservableProperty] private bool _hasIncoming;

    partial void OnSelectedRowChanged(ModRowViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedTableItem));
        UpdateSelectedCommand.NotifyCanExecuteChanged();
        SwitchVersionCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
        ConvertSelectedCommand.NotifyCanExecuteChanged();
        _ = LoadIncomingAsync(value);

        if (OnlySelected)
        {
            RebuildVisibleLines();
            RefreshProblems();
        }
    }

    private async Task LoadIncomingAsync(ModRowViewModel? row)
    {
        _detailCts?.Cancel();
        _detailCts = new CancellationTokenSource();
        CancellationToken ct = _detailCts.Token;

        Incoming.Clear();
        HasIncoming = false;

        if (row is null || !row.IsGit || row.Mod.CommitsBehind == 0)
            return;

        try
        {
            IReadOnlyList<GitCommitInfo> commits = await _git.GetIncomingCommitsAsync(row.Path, 5, ct);
            if (ct.IsCancellationRequested)
                return;

            foreach (GitCommitInfo commit in commits)
                Incoming.Add(new CommitViewModel(commit));

            HasIncoming = Incoming.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private bool CanUpdateChecked() => CheckedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUpdateChecked))]
    private void UpdateChecked() =>
        _shell.ShowUpdateDialog(_allRows.Where(r => r is { CanUpdate: true, IsChecked: true }).Select(r => r.Mod).ToList());

    private bool CanUpdateSelected() => SelectedRow is { CanUpdate: true };

    [RelayCommand(CanExecute = nameof(CanUpdateSelected))]
    private void UpdateSelected() => _shell.ShowUpdateDialog([SelectedRow!.Mod]);

    private bool CanSwitchVersion() => SelectedRow is { IsGit: true };

    [RelayCommand(CanExecute = nameof(CanSwitchVersion))]
    private void SwitchVersion() => _shell.OpenVersions(SelectedRow!.Path);

    private bool CanConvertSelected() => SelectedRow is { CanConvert: true };

    [RelayCommand(CanExecute = nameof(CanConvertSelected))]
    private void ConvertSelected() => _shell.Navigate(Page.Convert);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Uninstall()
    {
        var rows = SelectedRows.Count > 0 ? SelectedRows : [SelectedRow!];
        _shell.ShowUninstallDialog(rows.Select(r => r.Mod).ToList());
    }

    [RelayCommand]
    private Task Refresh() => _shell.RescanAsync(clearCache: true);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWrongBranch))]
    [NotifyPropertyChangedFor(nameof(SwitchAllLabel))]
    private int _wrongBranchCount;

    public bool HasWrongBranch => WrongBranchCount > 0;
    public string SwitchAllLabel => $"Switch {Humanize.Count(WrongBranchCount, "mod")} to {_shell.GameVersion?.Short} build";

    [RelayCommand]
    private void SwitchAllToVersionBranch() =>
        _shell.ShowSwitchBranchDialog(_allRows.Where(r => r.IsWrongBranch).Select(r => r.Mod).ToList());

    [RelayCommand]
    private void SwitchSelectedToVersionBranch()
    {
        if (SelectedRow is { IsWrongBranch: true } row)
            _shell.ShowSwitchBranchDialog([row.Mod]);
    }

    // ── Output dock ──────────────────────────────────────────────────────────

    public ObservableCollection<LogLine> VisibleLines { get; } = [];
    public ObservableCollection<Problem> VisibleProblems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLog))]
    [NotifyPropertyChangedFor(nameof(ShowProblems))]
    private bool _isDockOpen = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLog))]
    [NotifyPropertyChangedFor(nameof(ShowProblems))]
    [NotifyPropertyChangedFor(nameof(IsLogTab))]
    private bool _isProblemsTab;

    [ObservableProperty] private bool _onlySelected;
    [ObservableProperty] private int _problemCount;

    public bool IsLogTab => !IsProblemsTab;
    public bool ShowLog => IsDockOpen && !IsProblemsTab;
    public bool ShowProblems => IsDockOpen && IsProblemsTab;
    public bool HasProblems => ProblemCount > 0;

    partial void OnProblemCountChanged(int value) => OnPropertyChanged(nameof(HasProblems));

    partial void OnOnlySelectedChanged(bool value)
    {
        RebuildVisibleLines();
        RefreshProblems();
    }

    private bool PassesDockFilter(string target) =>
        !OnlySelected || target.Length == 0 || SelectedRow is null || target == SelectedRow.Path;

    private void OnLineAdded(LogLine line)
    {
        if (!PassesDockFilter(line.Target))
            return;

        // Mirror the cap in GitOutputLog so the filtered view cannot grow without bound.
        if (VisibleLines.Count >= 3000)
            VisibleLines.RemoveAt(0);

        VisibleLines.Add(line);
    }

    private void RebuildVisibleLines()
    {
        VisibleLines.Clear();
        foreach (LogLine line in Log.Lines.Where(l => PassesDockFilter(l.Target)))
            VisibleLines.Add(line);
    }

    private void RefreshProblems()
    {
        ProblemCount = Log.Problems.Count;
        VisibleProblems.Clear();
        foreach (Problem problem in Log.Problems.Where(p => PassesDockFilter(p.Target)))
            VisibleProblems.Add(problem);
    }

    [RelayCommand]
    private void ShowLogTab()
    {
        IsProblemsTab = false;
        IsDockOpen = true;
    }

    [RelayCommand]
    private void ShowProblemsTab()
    {
        IsProblemsTab = true;
        IsDockOpen = true;
    }

    [RelayCommand]
    private void ShowSelectedInOutput()
    {
        IsProblemsTab = true;
        IsDockOpen = true;
        OnlySelected = true;
    }

    [RelayCommand]
    private void ToggleDock() => IsDockOpen = !IsDockOpen;

    [RelayCommand]
    private Task CopyOutput()
    {
        var text = IsProblemsTab
            ? string.Join("\n\n", VisibleProblems.Select(p => $"{p.Mod}: {p.Summary}\n{p.CopyText}"))
            : string.Join("\n", VisibleLines.Select(l => $"{l.Time}  {l.Mod,-24}  {l.Text}"));
        return _shell.Platform.CopyTextAsync(text);
    }

    [RelayCommand]
    private void ClearOutput()
    {
        if (IsProblemsTab)
            Log.Problems.Clear();
        else
            Log.Clear();
    }

    [RelayCommand]
    private Task CopyProblem(Problem problem) => _shell.Platform.CopyTextAsync(problem.CopyText);

    [RelayCommand]
    private void ProblemAction(Problem problem)
    {
        if (problem.SwitchBranch is not null
            && _shell.Mods.FirstOrDefault(m => m.Path == problem.Target) is { } mod)
            _shell.ShowSwitchBranchDialog([mod]);
        else if (problem.IsError)
            _shell.Platform.OpenFolder(problem.Target);
        else
            _shell.OpenVersions(problem.Target);
    }

    // ── App update banner ────────────────────────────────────────────────────

    /// <summary>The shell's app-update state; the banner at the top of this page binds to it.</summary>
    public UpdatesViewModel Updates => _shell.Updates;
}
