using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;

namespace FCPModUpdater.Gui.ViewModels;

public enum StatusKind
{
    UpToDate,
    Behind,
    Ahead,
    Diverged,
    Modified,
    NonGit,
    Error,
    Detached
}

/// <summary>What a status pill shows: icon, label and its colour pair.</summary>
public sealed record StatusBadge(StatusKind Kind, string Icon, string Text, IBrush Foreground, IBrush Background)
{
    public string Label => $"{Icon} {Text}";

    public static StatusBadge For(InstalledMod mod)
    {
        if (mod.FetchError is not null)
            return new StatusBadge(StatusKind.Error, "✗", "Fetch failed", Palette.Error, Palette.ErrorSurface);

        return mod.Status switch
        {
            ModStatus.UpToDate => new(StatusKind.UpToDate, "✓", "Up to date", Palette.Ok, Palette.OkSurface),
            ModStatus.Behind => new(StatusKind.Behind, "↓", $"{mod.CommitsBehind} behind", Palette.Warn,
                Palette.WarnSurface),
            ModStatus.Ahead => new(StatusKind.Ahead, "↑", $"{mod.CommitsAhead} ahead", Palette.Info,
                Palette.InfoSurface),
            ModStatus.Diverged => new(StatusKind.Diverged, "⇅",
                $"Diverged ({mod.CommitsBehind}↓ {mod.CommitsAhead}↑)", Palette.Error, Palette.ErrorSurface),
            ModStatus.LocalChanges => new(StatusKind.Modified, "~", "Modified", Palette.Info, Palette.InfoSurface),
            ModStatus.NonGit => new(StatusKind.NonGit, "—", "Not Git", Palette.Neutral, Palette.NeutralSurface),
            ModStatus.Error => new(StatusKind.Error, "✗", "Error", Palette.Error, Palette.ErrorSurface),
            _ => new(StatusKind.Detached, "?", "Detached HEAD", Palette.Neutral, Palette.NeutralSurface)
        };
    }
}

public partial class ModRowViewModel : ObservableObject
{
    public ModRowViewModel(InstalledMod mod, string? gameVersion = null)
    {
        Mod = mod;
        GameVersion = gameVersion;
        Badge = StatusBadge.For(mod);
        // A mod on the wrong branch for the game is not fast-forwarded: that would pull a newer RimWorld's build.
        CanUpdate = mod is { Source: ModSource.Git, Status: ModStatus.Behind } && !ModCatalog.BlocksUpdate(mod.VersionMismatch);
        _isChecked = CanUpdate;
    }

    public InstalledMod Mod { get; }
    public StatusBadge Badge { get; }

    /// <summary>Only mods that are cleanly behind can be fast-forwarded.</summary>
    public bool CanUpdate { get; }

    [ObservableProperty] private bool _isChecked;

    public string Name => Mod.Name;
    public string Path => Mod.Path;
    public bool IsGit => Mod.Source == ModSource.Git;
    public string Source => IsGit ? "Git" : "Local";
    public string Branch => IsGit ? Mod.Branch ?? "detached" : "—";
    public string Commit => Mod.CurrentCommit?.ShortHash ?? "—";
    public bool IsDirty => Mod.HasLocalChanges;
    public string Folder => $"Mods/{System.IO.Path.GetFileName(Mod.Path)}";
    public bool CanConvert => !IsGit && !string.IsNullOrEmpty(Mod.MatchedRepoName);
    public bool IsLocal => !IsGit;
    public bool IsNotRecommended => ModCatalog.IsNotRecommended(Mod.Topics, Mod.CategoryId, Category is { } c ? [c] : null);

    /// <summary>The player's RimWorld version ("1.6"), when known.</summary>
    public string? GameVersion { get; }

    public bool IsWrongBranch => Mod.VersionMismatch != VersionMismatch.None && Mod.VersionBranch is not null;
    public string WrongBranchLabel => Mod.VersionMismatch == VersionMismatch.OnOlderVersionBranch
        ? $"Build for {GameVersion} available"
        : $"Wrong branch for {GameVersion}";

    public string WrongBranchTip => Mod.VersionMismatch switch
    {
        VersionMismatch.OnMainline =>
            $"On {Branch}, which may need a newer RimWorld than {GameVersion}. Switch to {Mod.VersionBranch}.",
        VersionMismatch.OnOlderVersionBranch =>
            $"On {Branch}, made for an older RimWorld. Switch to {Mod.VersionBranch} for {GameVersion}.",
        VersionMismatch.AheadOfVersionBranch =>
            $"Has commits newer than {Mod.VersionBranch}, which may need a newer RimWorld than {GameVersion}.",
        _ => ""
    };

    /// <summary>Branch and commit popovers; set by the page for Git rows.</summary>
    public VersionMenuViewModel? Menu { get; init; }

    /// <summary>The mod's category; set by the page, otherwise looked up among the built-in ones.</summary>
    public ModCategory? Category
    {
        get => _category ?? ModCategories.Find(ModCategories.BuiltIn, Mod.CategoryId);
        init => _category = value;
    }

    private readonly ModCategory? _category;

    public string CategoryLabel => Category?.DisplayName ?? "";

    /// <summary>The mod's GitHub page: from its remote, or the matched org repo for local copies.</summary>
    public string? RepoUrl => ToWebUrl(Mod.RemoteUrl)
        ?? (Mod.MatchedRepoName is { } repo ? $"{(Category ?? ModCategories.Fcp).OrgUrl}/{repo}" : null);

    public string NotRecommendedTip => $"Tagged {Category?.NotRecommendedTopic ?? ModCatalog.NotRecommendedTopic} on GitHub";

    public bool HasRepoUrl => RepoUrl is not null;

    internal static string? ToWebUrl(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
            return null;
        var url = remote.Trim();
        if (url.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            url = "https://github.com/" + url["git@github.com:".Length..];
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            url = url[..^4];
        return url.TrimEnd('/');
    }

    public IBrush BranchBrush => Branch switch
    {
        "main" or "master" => Palette.Ok,
        "—" => Palette.TextFaint,
        _ => Palette.Warn
    };

    public bool HasError => Mod.FetchError is not null || Mod.Status == ModStatus.Error;

    public string ErrorTitle => Mod.FetchError is { } fetch
        ? $"Last error · exit {fetch.ExitCode}"
        : "Last error";

    public string? ErrorCommand => Mod.FetchError is not null
        ? "$ git fetch --all --prune --progress --recurse-submodules"
        : null;

    public bool HasErrorCommand => ErrorCommand is not null;

    public string ErrorText => Mod.FetchError is { } fetch
        ? $"Fetch failed for {Name}: {fetch.Error ?? $"git exited with code {fetch.ExitCode}"}"
        : Mod.ErrorMessage ?? "";

    public bool Matches(string search) =>
        search.Length == 0
        || Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Branch.Contains(search, StringComparison.OrdinalIgnoreCase)
        || Commit.Contains(search, StringComparison.OrdinalIgnoreCase);
}

public sealed class CommitViewModel(GitCommitInfo commit)
{
    public GitCommitInfo Commit { get; } = commit;
    public string Hash => Commit.ShortHash;
    public string Message => Commit.Message;
    public string Author => Commit.Author;
    public string Date => Commit.Date.ToLocalTime().ToString("yyyy-MM-dd");
    public string DateTime => Commit.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Meta => $" · {Date} · {Author}";
    public string AuthorAndTime => $"{Author} · {DateTime}";
}
