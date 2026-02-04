using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public class ModDiscoveryService : IModDiscoveryService
{
    private const string FcpOrgUrl = "github.com/FalloutCollaborationProject/";

    private readonly IGitService _gitService;
    private readonly IGitHubApiService _gitHubApiService;

    public ModDiscoveryService(IGitService gitService, IGitHubApiService gitHubApiService)
    {
        _gitService = gitService;
        _gitHubApiService = gitHubApiService;
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

        var gitInstalled = await _gitService.IsGitInstalledAsync(ct);
        var orgRepos = await _gitHubApiService.GetOrganizationReposAsync(ct);
        var orgRepoNames = orgRepos.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var directories = Directory.GetDirectories(modsDirectory);
        var mods = new List<InstalledMod>();

        foreach (var dir in directories)
        {
            ct.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(dir);
            progress?.Report($"Scanning: {folderName}");

            InstalledMod? mod = await AnalyzeModDirectoryAsync(dir, folderName, gitInstalled, orgRepoNames, ct);
            if (mod != null)
            {
                mods.Add(mod);
            }
        }

        return mods.OrderBy(m => m.Name).ToList();
    }

    private async Task<InstalledMod?> AnalyzeModDirectoryAsync(
        string path,
        string folderName,
        bool gitInstalled,
        HashSet<string> orgRepoNames,
        CancellationToken ct)
    {
        // Check if it's a Workshop mod (has PublishedFileId.txt)
        var isWorkshop = File.Exists(Path.Combine(path, "About", "PublishedFileId.txt"));

        if (!gitInstalled)
        {
            // Can't check git status without git installed
            // Check if folder name matches an org repo
            if (orgRepoNames.Contains(folderName))
            {
                return new InstalledMod
                {
                    Name = folderName,
                    Path = path,
                    Source = isWorkshop ? ModSource.Workshop : ModSource.Local,
                    Status = ModStatus.NonGit,
                    MatchedRepoName = folderName
                };
            }

            return null;
        }

        var isGitRepo = await _gitService.IsGitRepositoryAsync(path, ct);

        if (!isGitRepo)
        {
            // Not a git repo - check if it matches an org repo by name
            if (orgRepoNames.Contains(folderName))
            {
                return new InstalledMod
                {
                    Name = folderName,
                    Path = path,
                    Source = isWorkshop ? ModSource.Workshop : ModSource.Local,
                    Status = ModStatus.NonGit,
                    MatchedRepoName = folderName
                };
            }

            return null;
        }

        // It's a git repo - check if it's an FCP mod
        var remoteUrl = await _gitService.GetRemoteUrlAsync(path, ct);

        if (string.IsNullOrEmpty(remoteUrl))
        {
            // Git repo without remote - check if folder matches org repo
            if (orgRepoNames.Contains(folderName))
            {
                return new InstalledMod
                {
                    Name = folderName,
                    Path = path,
                    Source = ModSource.Git,
                    Status = ModStatus.Error,
                    ErrorMessage = "Git repository has no remote configured",
                    MatchedRepoName = folderName
                };
            }

            return null;
        }

        var isFcpMod = remoteUrl.Contains(FcpOrgUrl, StringComparison.OrdinalIgnoreCase);

        if (!isFcpMod)
        {
            // Check if folder name matches org repo (might be cloned from fork)
            if (!orgRepoNames.Contains(folderName))
            {
                return null;
            }
        }

        // It's an FCP mod - gather full status
        return await BuildInstalledModAsync(path, folderName, remoteUrl, ct);
    }

    private async Task<InstalledMod> BuildInstalledModAsync(
        string path,
        string folderName,
        string remoteUrl,
        CancellationToken ct)
    {
        try
        {
            string? branch = await _gitService.GetCurrentBranchAsync(path, ct);
            GitCommitInfo? currentCommit = await _gitService.GetCurrentCommitAsync(path, ct);
            bool hasLocalChanges = await _gitService.HasLocalChangesAsync(path, ct);

            // Fetch to get accurate behind/ahead counts
            await _gitService.FetchAsync(path, ct: ct);

            (int behind, int ahead) = await _gitService.GetCommitDifferenceAsync(path, ct);

            ModStatus status = DetermineStatus(behind, ahead, hasLocalChanges, branch);

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
                HasLocalChanges = hasLocalChanges
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

    private static ModStatus DetermineStatus(int behind, int ahead, bool hasLocalChanges, string? branch)
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
