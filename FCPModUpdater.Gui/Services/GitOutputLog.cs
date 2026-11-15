using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.Services;

public enum LogKind
{
    Command,
    Output,
    Ok,
    Error,
    Warning,
    Section
}

public partial class LogLine(DateTimeOffset time, string target, string mod, string text, LogKind kind) : ObservableObject
{
    public string Time { get; } = time.ToLocalTime().ToString("HH:mm:ss");
    public string Target { get; } = target;
    public string Mod { get; } = mod;
    public LogKind Kind { get; } = kind;

    [ObservableProperty] private string _text = text;

    public bool IsSection => Kind == LogKind.Section;
    public string Bracketed => Mod.Length == 0 ? "" : $"[{Mod}]";

    public IBrush Brush => Kind switch
    {
        LogKind.Command => Palette.AccentHover,
        LogKind.Ok => Palette.Ok,
        LogKind.Error => Palette.Error,
        LogKind.Warning => Palette.Warn,
        LogKind.Section => Palette.TextDim,
        _ => Palette.TextMuted
    };

    public IBrush RowBackground => Kind == LogKind.Error ? Palette.ErrorBlock : Palette.Transparent;
    public FontStyle FontStyle => Kind == LogKind.Section ? FontStyle.Italic : FontStyle.Normal;
}

public sealed class Problem
{
    public required string Target { get; init; }
    public required string Mod { get; init; }
    public required bool IsError { get; init; }
    public required string Summary { get; init; }
    public required string When { get; init; }
    public required string Command { get; init; }
    public required string ExitLabel { get; init; }
    public required string Detail { get; init; }

    /// <summary>Found while scanning (replaced by the next scan) rather than caused by a user action.</summary>
    public bool FromScan { get; init; }

    /// <summary>Set for "wrong branch for the game" warnings: the action switches the mod to this branch.</summary>
    public string? SwitchBranch { get; init; }

    public string Folder => $"Mods/{Path.GetFileName(Target)}";
    public string CommandLine => $"$ git {Command} · in {Folder} · {ExitLabel}";
    public string Icon => IsError ? "✗" : "⚠";
    public string ActionLabel => SwitchBranch is not null ? $"Switch to {SwitchBranch}"
        : IsError ? "Open folder" : "Open version selector";
    public string CopyText => $"{CommandLine}\n{Detail}";

    public IBrush IconForeground => IsError ? Palette.Error : Palette.Warn;
    public IBrush IconBackground => IsError ? Palette.ErrorSurface : Palette.WarnSurface;
    public IBrush Stripe => IsError ? Palette.ErrorStrong : Palette.Warn;
    public IBrush BlockBackground => IsError ? Palette.ErrorBlock : Palette.WarnBlock;
    public IBrush BlockBorder => IsError ? Palette.ErrorBlockBorder : Palette.WarnBlockBorder;
    public IBrush BlockForeground => IsError ? Palette.ErrorBlockText : Palette.WarnText;
}

/// <summary>
/// Collects the transcript of git commands for the output dock. Git runs on worker threads,
/// so every mutation is posted to the UI thread through <paramref name="post"/>.
/// </summary>
public sealed partial class GitOutputLog(Action<Action> post) : IGitOutputSink
{
    private const int MaxLines = 3000;

    // Read-only probes that run for every folder in the Mods directory; too noisy to show.
    private static readonly string[] HiddenCommands =
        ["rev-parse", "remote get-url", "log ", "branch -r", "for-each-ref", "submodule update"];

    // Failures of these are worth a Problems entry; the rest (e.g. rev-list with no upstream) are expected.
    private static readonly string[] ReportedCommands = ["fetch", "pull", "clone", "checkout", "reset"];

    [GeneratedRegex(@"^(?<phase>[A-Za-z][A-Za-z ]+):\s+(?<percent>\d+)%")]
    private static partial Regex ProgressRegex();

    private readonly Dictionary<string, (string Phase, LogLine Line)> _progressLines = new();

    public ObservableCollection<LogLine> Lines { get; } = [];
    public ObservableCollection<Problem> Problems { get; } = [];

    public event Action<LogLine>? LineAdded;

    /// <summary>Raised with (target, percent) when git reports transfer progress.</summary>
    public event Action<string, double>? ProgressReported;

    public event Action? Cleared;

    public static bool IsHidden(string arguments) =>
        HiddenCommands.Any(c => arguments.StartsWith(c, StringComparison.Ordinal));

    public void CommandStarted(string target, string arguments)
    {
        if (IsHidden(arguments))
            return;

        DateTimeOffset now = DateTimeOffset.Now;
        post(() => Add(new LogLine(now, target, ModName(target), $"$ git {arguments}", LogKind.Command)));
    }

    public void OutputLine(string target, string arguments, string line)
    {
        if (IsHidden(arguments) || string.IsNullOrWhiteSpace(line))
            return;

        DateTimeOffset now = DateTimeOffset.Now;
        post(() =>
        {
            Match match = ProgressRegex().Match(line.TrimStart());
            if (!match.Success)
            {
                _progressLines.Remove(target);
                Add(new LogLine(now, target, ModName(target), line.TrimEnd(), LogKind.Output));
                return;
            }

            // Git rewrites progress in place; keep one line per phase instead of one per percent.
            var phase = match.Groups["phase"].Value;
            if (_progressLines.TryGetValue(target, out var current) && current.Phase == phase)
            {
                current.Line.Text = line.Trim();
            }
            else
            {
                var logLine = new LogLine(now, target, ModName(target), line.Trim(), LogKind.Output);
                _progressLines[target] = (phase, logLine);
                Add(logLine);
            }

            ProgressReported?.Invoke(target, double.Parse(match.Groups["percent"].Value));
        });
    }

    public void CommandFinished(string target, string arguments, int exitCode, TimeSpan duration, string? error)
    {
        if (IsHidden(arguments))
            return;

        DateTimeOffset now = DateTimeOffset.Now;
        post(() =>
        {
            _progressLines.Remove(target);

            if (error is null)
            {
                Add(new LogLine(now, target, ModName(target), $"✓ exit {exitCode} · {duration.TotalSeconds:0.0}s",
                    LogKind.Ok));
                return;
            }

            // Superseded by the user (e.g. picking another mod mid-load): worth a line, not a problem.
            if (error == GitService.CancelledError)
            {
                Add(new LogLine(now, target, ModName(target), "cancelled", LogKind.Section));
                return;
            }

            Add(new LogLine(now, target, ModName(target), $"✗ exit {exitCode} · {duration.TotalSeconds:0.0}s",
                LogKind.Error));

            var verb = ReportedCommands.FirstOrDefault(c => arguments.StartsWith(c, StringComparison.Ordinal));
            if (verb is null)
                return;

            RemoveProblems(p => p.Target == target && p.Command == arguments);
            Problems.Add(new Problem
            {
                Target = target,
                Mod = ModName(target),
                IsError = true,
                Summary = $"{char.ToUpperInvariant(verb[0])}{verb[1..]} failed",
                When = now.ToLocalTime().ToString("HH:mm:ss"),
                Command = arguments,
                ExitLabel = $"exit {exitCode} after {duration.TotalSeconds:0.0}s",
                Detail = string.IsNullOrWhiteSpace(error) ? $"git exited with code {exitCode}" : error.Trim(),
                FromScan = _scanning
            });
        });
    }

    private bool _scanning;

    /// <summary>Call on the UI thread when a scan starts.</summary>
    public void BeginScan(string modsDirectory)
    {
        _scanning = true;
        RemoveProblems(p => p.FromScan);
        Add(new LogLine(DateTimeOffset.Now, "", "", $"── Refresh started · {modsDirectory}", LogKind.Section));
    }

    /// <summary>Call on the UI thread when a scan ends; adds warnings git itself does not report.</summary>
    public void EndScan(IReadOnlyList<InstalledMod> mods, TimeSpan elapsed, string? gameVersion = null)
    {
        // Sink callbacks posted during the scan are already queued ahead of this call.
        _scanning = false;
        DateTimeOffset now = DateTimeOffset.Now;

        foreach (InstalledMod mod in mods.Where(m => m.Status == ModStatus.Diverged))
        {
            Add(new LogLine(now, mod.Path, mod.Name,
                $"⚠ diverged from origin/{mod.Branch} — fast-forward pull will fail", LogKind.Warning));

            Problems.Add(new Problem
            {
                Target = mod.Path,
                Mod = mod.Name,
                IsError = false,
                Summary = "Branch has diverged — updates will not apply",
                When = now.ToString("HH:mm:ss"),
                Command = $"rev-list --left-right --count origin/{mod.Branch}...HEAD",
                ExitLabel = "exit 0",
                Detail =
                    $"Local {mod.Branch} is {Humanize.Count(mod.CommitsBehind, "commit")} behind and {Humanize.Count(mod.CommitsAhead, "commit")} ahead of origin/{mod.Branch}.\n" +
                    "A fast-forward pull (git pull --ff-only) cannot apply these updates.\n" +
                    $"Reset to origin/{mod.Branch} to discard the local commits, or keep them and update manually.",
                FromScan = true
            });
        }

        foreach (InstalledMod mod in ModCatalog.GetVersionMismatchedMods(mods))
        {
            if (mod.VersionMismatch == VersionMismatch.OnOlderVersionBranch)
            {
                var older = $"On {mod.Branch}, made for an older RimWorld — {mod.VersionBranch} has the build for {gameVersion}";
                Add(new LogLine(now, mod.Path, mod.Name, $"⚠ {older}", LogKind.Warning));
                Problems.Add(new Problem
                {
                    Target = mod.Path,
                    Mod = mod.Name,
                    IsError = false,
                    Summary = older,
                    When = now.ToString("HH:mm:ss"),
                    Command = "rev-parse --abbrev-ref HEAD",
                    ExitLabel = "exit 0",
                    Detail =
                        $"{mod.Branch} holds the last build for an older RimWorld, and you're now on {gameVersion}.\n" +
                        $"Switch to {mod.VersionBranch} to get the build for {gameVersion}." +
                        (mod.HasLocalChanges ? "\nUncommitted local changes will be discarded." : ""),
                    FromScan = true,
                    SwitchBranch = mod.VersionBranch
                });
                continue;
            }

            var onMain = mod.VersionMismatch == VersionMismatch.OnMainline;
            var summary = onMain
                ? $"On {mod.Branch ?? "detached HEAD"} — {mod.VersionBranch} is the build for RimWorld {gameVersion}"
                : $"Newer than {mod.VersionBranch} — may need a newer RimWorld than {gameVersion}";
            Add(new LogLine(now, mod.Path, mod.Name, $"⚠ {summary}", LogKind.Warning));

            Problems.Add(new Problem
            {
                Target = mod.Path,
                Mod = mod.Name,
                IsError = false,
                Summary = summary,
                When = now.ToString("HH:mm:ss"),
                Command = $"merge-base --is-ancestor HEAD origin/{mod.VersionBranch}",
                ExitLabel = onMain ? "exit 0" : "exit 1",
                Detail =
                    $"This repo keeps its RimWorld {gameVersion} build on {mod.VersionBranch}; main has moved on to a newer game version.\n" +
                    (onMain
                        ? $"Updating from {mod.Branch ?? "this commit"} can pull changes your game can't load, so Update skips this mod.\n"
                        : $"The checked-out commit is not on origin/{mod.VersionBranch}.\n") +
                    $"Switch to {mod.VersionBranch} to get the last build made for {gameVersion}." +
                    (mod.HasLocalChanges ? "\nUncommitted local changes will be discarded." : ""),
                FromScan = true,
                SwitchBranch = mod.VersionBranch
            });
        }

        var errors = Problems.Count(p => p is { FromScan: true, IsError: true });
        var warnings = Problems.Count(p => p is { FromScan: true, IsError: false });
        Add(new LogLine(now, "", "",
            $"── Refresh finished · {Humanize.Count(mods.Count, "mod")} · {Humanize.Count(errors, "error")} · {Humanize.Count(warnings, "warning")} · {elapsed.TotalSeconds:0.0}s",
            LogKind.Section));
    }

    /// <summary>Completes once everything posted so far has been applied.</summary>
    public Task FlushAsync()
    {
        var done = new TaskCompletionSource();
        post(() => done.SetResult());
        return done.Task;
    }

    public void Clear()
    {
        Lines.Clear();
        _progressLines.Clear();
        Cleared?.Invoke();
    }

    private void Add(LogLine line)
    {
        if (Lines.Count >= MaxLines)
            Lines.RemoveAt(0);

        Lines.Add(line);
        LineAdded?.Invoke(line);
    }

    private void RemoveProblems(Func<Problem, bool> predicate)
    {
        for (var i = Problems.Count - 1; i >= 0; i--)
        {
            if (predicate(Problems[i]))
                Problems.RemoveAt(i);
        }
    }

    private static string ModName(string target) => Path.GetFileName(target.TrimEnd('/', '\\'));
}
