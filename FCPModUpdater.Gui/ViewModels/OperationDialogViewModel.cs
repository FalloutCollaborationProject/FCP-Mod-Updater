using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.ViewModels;

public enum OperationKind
{
    Update,
    Install,
    Convert,
    /// <summary>Put mods on the rimworld-X.Y branch for the player's game version.</summary>
    SwitchBranch
}

public enum OperationStep
{
    Review,
    Running,
    Results
}

public enum ItemState
{
    Queued,
    Running,
    Done,
    Failed,
    Skipped
}

public partial class StepViewModel(int number, string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Number))]
    private bool _isPast;

    [ObservableProperty] private bool _isActive;

    public string Number => IsPast ? "✓" : number.ToString();
}

public partial class OperationItemViewModel(string name, string target) : ObservableObject
{
    public string Name { get; } = name;
    public string Target { get; } = target;
    public InstalledMod? Mod { get; init; }
    public RemoteRepo? Repo { get; init; }

    // Review step
    public ObservableCollection<CommitViewModel> Commits { get; } = [];
    public int Behind => Mod?.CommitsBehind ?? 0;
    public string BehindLabel => $"↓ {Behind} incoming";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    [NotifyPropertyChangedFor(nameof(MoreLabel))]
    private int _moreCount;

    public bool HasMore => MoreCount > 0;
    public string MoreLabel => $"+ {Humanize.Count(MoreCount, "older commit")}";

    // Running step
    internal double BasePercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private double _percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateBrush))]
    [NotifyPropertyChangedFor(nameof(BarBrush))]
    [NotifyPropertyChangedFor(nameof(NameBrush))]
    private ItemState _state;

    internal string RunningVerb = "Working";

    public string StateText => State switch
    {
        ItemState.Running => $"{RunningVerb} {Percent:0}%",
        ItemState.Done => "✓ Done",
        ItemState.Failed => "✗ Failed",
        ItemState.Skipped => "Skipped",
        _ => "Queued"
    };

    public IBrush StateBrush => State switch
    {
        ItemState.Running => Palette.AccentHover,
        ItemState.Done => Palette.Ok,
        ItemState.Failed => Palette.Error,
        _ => Palette.TextDim
    };

    public IBrush BarBrush => State switch
    {
        ItemState.Done => Palette.Ok,
        ItemState.Failed => Palette.Error,
        _ => Palette.Accent
    };

    public IBrush NameBrush => State == ItemState.Queued ? Palette.TextMuted : Palette.Text;

    // Results step
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultBrush))]
    private string _resultText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);
    public string? CommandLine { get; set; }
    public bool HasCommandLine => !string.IsNullOrEmpty(CommandLine);
    public bool CanResolve => Mod is { Source: ModSource.Git };

    public IBrush ResultBrush => State switch
    {
        ItemState.Done => Palette.Ok,
        ItemState.Failed => Palette.Error,
        _ => Palette.TextDim
    };
}

/// <summary>
/// The Review → Running → Results overlay shared by update, install and convert.
/// </summary>
public partial class OperationDialogViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly IGitService _git;
    private readonly IGitHubApiService _gitHub;
    private readonly GitOutputLog _log;
    private bool _cancelRemaining;

    public OperationDialogViewModel(
        MainWindowViewModel shell,
        IGitService git,
        IGitHubApiService gitHub,
        GitOutputLog log,
        OperationKind kind,
        IEnumerable<OperationItemViewModel> items)
    {
        _shell = shell;
        _git = git;
        _gitHub = gitHub;
        _log = log;
        Kind = kind;
        Items = new ObservableCollection<OperationItemViewModel>(items);

        var verb = kind switch
        {
            OperationKind.Update => "Updating",
            OperationKind.Install => "Installing",
            OperationKind.SwitchBranch => "Switching",
            _ => "Migrating"
        };

        foreach (OperationItemViewModel item in Items)
            item.RunningVerb = kind switch
            {
                OperationKind.Update => "Pulling",
                OperationKind.SwitchBranch => "Checking out",
                _ => "Cloning"
            };

        Steps = kind == OperationKind.Update
            ? [new StepViewModel(1, "Review changes"), new StepViewModel(2, verb), new StepViewModel(3, "Results")]
            : [new StepViewModel(1, verb), new StepViewModel(2, "Results")];

        RunningTitle = $"{verb} mods";
        _step = kind == OperationKind.Update ? OperationStep.Review : OperationStep.Running;
        SyncSteps();
    }

    public OperationKind Kind { get; }
    public ObservableCollection<OperationItemViewModel> Items { get; }
    public IReadOnlyList<StepViewModel> Steps { get; }
    public ObservableCollection<LogLine> LogLines { get; } = [];
    public string RunningTitle { get; }

    public string Title => Kind switch
    {
        OperationKind.Update => "Update Git mods",
        OperationKind.Install => "Install mods",
        OperationKind.SwitchBranch => $"Switch to the RimWorld {_shell.GameVersion?.Short} build",
        _ => "Migrate local mods to Git"
    };

    private string Noun => "mod";

    public string ReviewIntro => $"{Humanize.Count(Items.Count, "mod")} selected. These commits will be pulled.";
    public string StartLabel => $"Update {Humanize.Count(Items.Count, "mod")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReview))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsResults))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    private OperationStep _step;

    public bool IsReview => Step == OperationStep.Review;
    public bool IsRunning => Step == OperationStep.Running;
    public bool IsResults => Step == OperationStep.Results;
    public bool CanClose => !IsRunning;

    [ObservableProperty] private double _overallPercent;
    [ObservableProperty] private string _doneLabel = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _allSucceeded;
    [ObservableProperty] private bool _cancelRequested;

    partial void OnStepChanged(OperationStep value) => SyncSteps();

    private void SyncSteps()
    {
        var index = Kind == OperationKind.Update ? (int)Step : (int)Step - 1;
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].IsActive = i == index;
            Steps[i].IsPast = i < index;
        }
    }

    /// <summary>Loads what the review step shows; a no-op for install/convert.</summary>
    public async Task LoadReviewAsync()
    {
        if (Kind != OperationKind.Update)
            return;

        foreach (OperationItemViewModel item in Items)
        {
            IReadOnlyList<GitCommitInfo> commits = await _git.GetIncomingCommitsAsync(item.Target, 5);
            item.Commits.Clear();
            foreach (GitCommitInfo commit in commits)
                item.Commits.Add(new CommitViewModel(commit));

            item.MoreCount = Math.Max(0, item.Behind - commits.Count);
        }
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (Step == OperationStep.Running && Items.Any(i => i.State != ItemState.Queued))
            return;

        Step = OperationStep.Running;
        _log.LineAdded += OnLogLine;
        _log.ProgressReported += OnGitProgress;
        UpdateOverall();

        try
        {
            // Only updates run side by side: installs and migrations reshape the Mods folder.
            var parallelism = Kind == OperationKind.Update
                ? ParallelRunner.ResolveParallelism(Items.Count, _shell.UpdateParallelism)
                : 1;

            // Continuations resume on the UI thread, so item state is only touched there.
            await ParallelRunner.RunAsync(Items.ToList(), parallelism, async (item, _, _) =>
            {
                if (_cancelRemaining)
                {
                    item.State = ItemState.Skipped;
                    item.ResultText = "Skipped";
                    return true;
                }

                await RunItemAsync(item);
                UpdateOverall();
                return true;
            });
        }
        finally
        {
            _log.LineAdded -= OnLogLine;
            _log.ProgressReported -= OnGitProgress;
        }

        Summarize();
        Step = OperationStep.Results;
    }

    private async Task RunItemAsync(OperationItemViewModel item)
    {
        item.State = ItemState.Running;

        // Assigned directly rather than through Progress<T> so the bar cannot lag behind the result.
        var progress = new InlineProgress(value =>
        {
            item.BasePercent = value;
            item.Percent = Math.Max(item.Percent, value);
            UpdateOverall();
        }, _shell.Post);

        bool success;
        string? error;

        try
        {
            switch (Kind)
            {
                case OperationKind.Update:
                {
                    var before = item.Mod!.CurrentCommit?.ShortHash;
                    (success, error) = await Task.Run(() => GitModUpdater.UpdateAsync(_git, item.Mod, progress));
                    if (success)
                    {
                        GitCommitInfo? after = await _git.GetCurrentCommitAsync(item.Target);
                        item.ResultText = before is not null && after is not null
                            ? $"✓ Updated · {before} → {after.ShortHash}"
                            : "✓ Updated";
                    }
                    else
                    {
                        item.CommandLine = $"$ git pull --ff-only --progress --recurse-submodules · in {FolderOf(item)}";
                    }

                    break;
                }
                case OperationKind.Install:
                    (success, error) = await Task.Run(() =>
                        ModOperations.InstallAsync(_git, item.Repo!, _shell.ModsDirectory!, progress, _shell.GameVersion));
                    if (success) item.ResultText = "✓ Installed";
                    else item.CommandLine = $"$ git clone {item.Repo!.CloneUrl} · in Mods";
                    break;
                case OperationKind.SwitchBranch:
                    (success, error) = await Task.Run(() => ModOperations.SwitchToVersionBranchAsync(_git, item.Mod!));
                    if (success) item.ResultText = $"✓ On {item.Mod!.VersionBranch}";
                    else item.CommandLine = $"$ git checkout {item.Mod!.VersionBranch} · in {FolderOf(item)}";
                    break;
                default:
                    (success, error) = await Task.Run(() =>
                        ModOperations.ConvertToGitAsync(_git, _gitHub, item.Mod!, progress, _shell.GameVersion));
                    if (success) item.ResultText = "✓ Migrated to Git";
                    break;
            }
        }
        catch (Exception ex)
        {
            success = false;
            error = ex.Message;
        }

        // Let queued progress callbacks land before the final state is written.
        await _log.FlushAsync();

        if (success)
        {
            item.Percent = 100;
            item.State = ItemState.Done;
        }
        else
        {
            item.State = ItemState.Failed;
            item.ResultText = "✗ Failed";
            item.Error = error ?? "Unknown error";
        }
    }

    private static string FolderOf(OperationItemViewModel item) => $"Mods/{Path.GetFileName(item.Target)}";

    private void OnLogLine(LogLine line)
    {
        if (line.IsSection)
            return;

        LogLines.Add(line);
    }

    private void OnGitProgress(string target, double percent)
    {
        OperationItemViewModel? item = Items.FirstOrDefault(i => i.State == ItemState.Running && i.Target == target);
        if (item is null || Kind != OperationKind.Update)
            return;

        // GitModUpdater reports coarse checkpoints (25 fetch, 50 pull); fill in between from git's own percent.
        item.Percent = Math.Max(item.Percent, Math.Min(99, item.BasePercent + percent * 0.25));
        UpdateOverall();
    }

    private void UpdateOverall()
    {
        var finished = Items.Count(i => i.State is ItemState.Done or ItemState.Failed or ItemState.Skipped);
        DoneLabel = $"{finished} of {Items.Count} done";
        OverallPercent = Items.Count == 0
            ? 0
            : Items.Average(i => i.State is ItemState.Failed or ItemState.Skipped ? 100 : i.Percent);
    }

    private void Summarize()
    {
        var done = Items.Count(i => i.State == ItemState.Done);
        var failed = Items.Count(i => i.State == ItemState.Failed);
        var skipped = Items.Count(i => i.State == ItemState.Skipped);

        var verb = Kind switch
        {
            OperationKind.Update => "Updated",
            OperationKind.Install => "Installed",
            OperationKind.SwitchBranch => "Switched",
            _ => "Migrated"
        };

        var text = $"{verb} {Humanize.Count(done, Noun)}";
        if (failed > 0) text += $", {failed} failed";
        if (skipped > 0) text += $", {skipped} skipped";

        Summary = text + ".";
        AllSucceeded = failed == 0 && skipped == 0;
    }

    [RelayCommand]
    private void CancelRemaining()
    {
        // The running git command is left to finish so no repository is abandoned mid-pull.
        _cancelRemaining = true;
        CancelRequested = true;
    }

    [RelayCommand]
    private void Close()
    {
        if (IsRunning)
            return;

        _shell.CloseDialog(rescan: IsResults);
    }

    [RelayCommand]
    private Task CopyError(OperationItemViewModel item) =>
        _shell.Platform.CopyTextAsync($"{item.CommandLine}\n{item.Error}".Trim());

    [RelayCommand]
    private void OpenFolder(OperationItemViewModel item) =>
        _shell.Platform.OpenFolder(Directory.Exists(item.Target) ? item.Target : _shell.ModsDirectory ?? item.Target);

    [RelayCommand]
    private void Resolve(OperationItemViewModel item)
    {
        _shell.CloseDialog(rescan: true);
        _shell.OpenVersions(item.Target);
    }

    private sealed class InlineProgress(Action<double> apply, Action<Action> post) : IProgress<double>
    {
        public void Report(double value) => post(() => apply(value));
    }
}
