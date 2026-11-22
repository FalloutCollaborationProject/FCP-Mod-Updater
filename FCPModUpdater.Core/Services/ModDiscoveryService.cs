using System.Collections.Concurrent;
using FCPModUpdater.Models;
using FCPModUpdater.Settings;

namespace FCPModUpdater.Services;

public class ModDiscoveryService : IModDiscoveryService
{
    private static readonly string[] BranchSuffixes = ["-main", "-master", "-develop"];

    private readonly IGitService _gitService;
    private readonly IGitHubApiService _gitHubApiService;
    private readonly SettingsStore? _settings;

    public ModDiscoveryService(IGitService gitService, IGitHubApiService gitHubApiService, SettingsStore? settings = null)
    {
        _gitService = gitService;
        _gitHubApiService = gitHubApiService;
        _settings = settings;
    }

    public async Task<IReadOnlyList<InstalledMod>> DiscoverModsAsync(
        string modsDirectory,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(modsDirectory))
        {
            return [];
        }

        var orgRepos = await _gitHubApiService.GetOrganizationReposAsync(ct);
        IReadOnlyList<ModCategory> categories = _gitHubApiService.Categories ?? [];
        if (categories.Count == 0)
            categories = ModCategories.Effective(_settings?.Current);
        var orgRepoNames = orgRepos.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reposByName = orgRepos
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        GameVersion? gameVersion = GameVersionService.Resolve(modsDirectory, _settings?.Current).Version;
        var directories = Directory.GetDirectories(modsDirectory);
        var mods = new ConcurrentBag<InstalledMod>();

        await Parallel.ForEachAsync(directories,
            new ParallelOptions { MaxDegreeOfParallelism = 12, CancellationToken = ct },
            async (dir, token) =>
            {
                var folderName = Path.GetFileName(dir);
                progress?.Report($"Scanning: {folderName}");

                InstalledMod? mod = await AnalyzeModDirectoryAsync(dir, folderName, orgRepoNames, categories, gameVersion, token);
                if (mod != null)
                {
                    var urlCategory = ModCategories.FindByRemoteUrl(categories, mod.RemoteUrl)?.Id;
                    RemoteRepo? repo = FindRepo(mod, orgRepos, urlCategory, orgRepoNames, reposByName);
                    mods.Add(mod with
                    {
                        Topics = repo?.Topics ?? mod.Topics,
                        CategoryId = urlCategory ?? repo?.CategoryId ?? mod.CategoryId,
                    });
                }
            });

        return mods.OrderBy(m => m.Name).ToList();
    }

    private async Task<InstalledMod?> AnalyzeModDirectoryAsync(
        string path,
        string folderName,
        HashSet<string> orgRepoNames,
        IReadOnlyList<ModCategory> categories,
        GameVersion? gameVersion,
        CancellationToken ct)
    {
        var isGitRepo = await _gitService.IsGitRepositoryAsync(path, ct);

        if (!isGitRepo)
        {
            // Not a git repo - check if it matches an org repo by name
            var matchedRepo = MatchRepoName(folderName, orgRepoNames);
            if (matchedRepo != null)
            {
                return new InstalledMod
                {
                    Name = folderName,
                    Path = path,
                    Source = ModSource.Local,
                    Status = ModStatus.NonGit,
                    MatchedRepoName = matchedRepo
                };
            }

            return null;
        }

        // It's a git repo - check if it belongs to one of the category orgs
        var remoteUrl = await _gitService.GetRemoteUrlAsync(path, ct);

        if (string.IsNullOrEmpty(remoteUrl))
        {
            // Git repo without remote - check if folder matches org repo
            var matchedRepo = MatchRepoName(folderName, orgRepoNames);
            if (matchedRepo != null)
            {
                return new InstalledMod
                {
                    Name = folderName,
                    Path = path,
                    Source = ModSource.Git,
                    Status = ModStatus.Error,
                    ErrorMessage = "Git repository has no remote configured",
                    MatchedRepoName = matchedRepo
                };
            }

            return null;
        }

        ModCategory? urlCategory = ModCategories.FindByRemoteUrl(categories, remoteUrl);

        if (urlCategory is null)
        {
            // Check if folder name matches org repo (might be cloned from fork)
            if (MatchRepoName(folderName, orgRepoNames) == null)
            {
                return null;
            }
        }

        // It's a known mod - gather full status
        return await BuildInstalledModAsync(path, folderName, remoteUrl, urlCategory?.Id, gameVersion, ct);
    }

    private async Task<InstalledMod> BuildInstalledModAsync(
        string path,
        string folderName,
        string remoteUrl,
        string? categoryId,
        GameVersion? gameVersion,
        CancellationToken ct)
    {
        try
        {
            // Leftover checkouts of removed submodules would otherwise show the mod as modified.
            await _gitService.RemoveStaleSubmodulesAsync(path, ct);

            // Phase 1: Run all independent operations in parallel
            var branchTask = _gitService.GetCurrentBranchAsync(path, ct);
            var commitTask = _gitService.GetCurrentCommitAsync(path, ct);
            var changesTask = _gitService.HasLocalChangesAsync(path, ct);
            var fetchTask = _gitService.FetchAsync(path, ct: ct);

            await Task.WhenAll(branchTask, commitTask, changesTask, fetchTask);

            string? branch = branchTask.Result;
            GitCommitInfo? currentCommit = commitTask.Result;
            bool hasLocalChanges = changesTask.Result;
            GitOperationResult fetchResult = fetchTask.Result;

            // Phase 2: Get commit difference (needs fetch to complete first)
            (int behind, int ahead) = await _gitService.GetCommitDifferenceAsync(path, ct);

            ModStatus status = DetermineStatus(behind, ahead, hasLocalChanges, branch);

            var (versionBranch, mismatch) = gameVersion is null
                ? (null, VersionMismatch.None)
                : await CheckVersionBranchAsync(path, RepoNameFromUrl(remoteUrl) ?? folderName, categoryId, branch, gameVersion, ct);

            return new InstalledMod
            {
                Name = folderName,
                Path = path,
                Source = ModSource.Git,
                RemoteUrl = remoteUrl,
                Branch = branch,
                CurrentCommit = currentCommit,
                Status = status,
                CommitsBehind = behind,
                CommitsAhead = ahead,
                HasLocalChanges = hasLocalChanges,
                VersionBranch = versionBranch,
                VersionMismatch = mismatch,
                FetchError = fetchResult is { Success: false } ? fetchResult : null
            };
        }
        catch (Exception ex)
        {
            return new InstalledMod
            {
                Name = folderName,
                Path = path,
                Source = ModSource.Git,
                RemoteUrl = remoteUrl,
                Status = ModStatus.Error,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// When the repo has a branch for the player's game version, flags a checkout that is on another branch
    /// or has commits that branch doesn't contain. No such branch means main still targets this version.
    /// </summary>
    private async Task<(string? Branch, VersionMismatch Mismatch)> CheckVersionBranchAsync(
        string path, string repoName, string? categoryId, string? currentBranch, GameVersion gameVersion, CancellationToken ct)
    {
        var versionBranch = ModCatalog.GetVersionBranchName(repoName, gameVersion, categoryId);
        IReadOnlyList<string> remote = await _gitService.GetRemoteBranchesAsync(path, ct);
        var hasVersionBranch = remote.Contains(versionBranch, StringComparer.OrdinalIgnoreCase);

        // The game was updated past the branch this mod sits on: point at the newer build.
        if (ModCatalog.ParseVersionBranch(currentBranch) is { } onVersion
            && (onVersion.Major, onVersion.Minor).CompareTo((gameVersion.Major, gameVersion.Minor)) < 0)
        {
            var target = hasVersionBranch ? versionBranch
                : remote.FirstOrDefault(b => b.Equals("main", StringComparison.OrdinalIgnoreCase))
                  ?? remote.FirstOrDefault(b => b.Equals("master", StringComparison.OrdinalIgnoreCase));
            return target is null ? (null, VersionMismatch.None) : (target, VersionMismatch.OnOlderVersionBranch);
        }

        if (!hasVersionBranch)
            return (null, VersionMismatch.None);

        if (currentBranch is not null && !string.Equals(currentBranch, versionBranch, StringComparison.OrdinalIgnoreCase))
            return (versionBranch, VersionMismatch.OnMainline);

        // On the version branch, or detached: anything HEAD has that the branch doesn't is newer than this game.
        return await _gitService.IsAncestorAsync(path, "HEAD", $"origin/{versionBranch}", ct)
            ? (versionBranch, VersionMismatch.None)
            : (versionBranch, VersionMismatch.AheadOfVersionBranch);
    }

    /// <summary>
    /// The org repo a mod belongs to. Repos with the same name in several orgs are told apart by the remote URL's
    /// category; otherwise the first category in order wins.
    /// </summary>
    private static RemoteRepo? FindRepo(InstalledMod mod, IReadOnlyList<RemoteRepo> orgRepos, string? urlCategory,
        HashSet<string> orgRepoNames, Dictionary<string, RemoteRepo> reposByName)
    {
        var name = mod.MatchedRepoName
                   ?? RepoNameFromUrl(mod.RemoteUrl)
                   ?? MatchRepoName(System.IO.Path.GetFileName(mod.Path), orgRepoNames);
        if (name is not null && urlCategory is not null
            && orgRepos.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                                            && r.CategoryId.Equals(urlCategory, StringComparison.OrdinalIgnoreCase)) is { } exact)
            return exact;
        if (name is not null && reposByName.TryGetValue(name, out RemoteRepo? repo))
            return repo;
        var folderMatch = MatchRepoName(System.IO.Path.GetFileName(mod.Path), orgRepoNames);
        return folderMatch is not null ? reposByName.GetValueOrDefault(folderMatch) : null;
    }

    /// <summary>"https://github.com/Org/FCP-Core.git" → "FCP-Core".</summary>
    internal static string? RepoNameFromUrl(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return null;
        var trimmed = remoteUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        var slash = trimmed.LastIndexOfAny(['/', ':']);
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// Tries to match a folder name to an org repo, accounting for GitHub ZIP download suffixes like -main, -master.
    /// </summary>
    internal static string? MatchRepoName(string folderName, HashSet<string> orgRepoNames)
    {
        // Direct match
        if (orgRepoNames.Contains(folderName))
            return folderName;

        // Try stripping common branch suffixes (for ZIP downloads)
        foreach (var suffix in BranchSuffixes)
        {
            if (folderName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var baseName = folderName[..^suffix.Length];
                if (orgRepoNames.Contains(baseName))
                    return baseName;
            }
        }

        return null;
    }

    internal static ModStatus DetermineStatus(int behind, int ahead, bool hasLocalChanges, string? branch)
    {
        if (branch == null)
        {
            // Detached HEAD
            return ModStatus.Unknown;
        }

        if (hasLocalChanges)
        {
            return ModStatus.LocalChanges;
        }

        if (behind > 0 && ahead > 0)
        {
            return ModStatus.Diverged;
        }

        if (behind > 0)
        {
            return ModStatus.Behind;
        }

        if (ahead > 0)
        {
            return ModStatus.Ahead;
        }

        return ModStatus.UpToDate;
    }
}
