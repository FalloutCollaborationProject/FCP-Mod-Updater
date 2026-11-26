using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public enum VersionAction
{
    /// <summary>Newer commit on the current branch: <c>git merge --ff-only</c>.</summary>
    FastForward,
    /// <summary>Older commit on the current branch: <c>git reset --hard</c>.</summary>
    Reset,
    /// <summary>Commit on another branch: checkout the branch, then reset to the commit.</summary>
    SwitchBranch,
    /// <summary>Any commit or branch by name: <c>git checkout</c> (detached HEAD for a hash).</summary>
    Checkout,
}

/// <summary>
/// The file-system side of install / convert / uninstall, shared by every front end.
/// </summary>
public static class ModOperations
{
    public static async Task<(bool Success, string? Error)> InstallAsync(
        IGitService gitService,
        RemoteRepo repo,
        string modsDirectory,
        IProgress<double>? progress = null,
        GameVersion? gameVersion = null,
        CancellationToken ct = default)
    {
        var targetPath = Path.Combine(modsDirectory, repo.Name);
        var result = await gitService.CloneAsync(repo.CloneUrl, targetPath, percentProgress: progress, ct: ct);
        return result.Success ? await UseVersionBranchAsync(gitService, repo.Name, repo.CategoryId, targetPath, gameVersion, ct) : result;
    }

    /// <summary>After a clone: checks out the rimworld-X.Y branch for the game, when the repo has one.</summary>
    private static async Task<(bool Success, string? Error)> UseVersionBranchAsync(
        IGitService gitService, string repoName, string? categoryId, string path, GameVersion? gameVersion, CancellationToken ct)
    {
        if (gameVersion is null)
            return (true, null);
        var branch = ModCatalog.GetVersionBranchName(repoName, gameVersion, categoryId);
        IReadOnlyList<string> remote = await gitService.GetRemoteBranchesAsync(path, ct);
        if (!remote.Contains(branch, StringComparer.OrdinalIgnoreCase))
            return (true, null);
        return await gitService.CheckoutAsync(path, branch, ct)
            ? (true, null)
            : (false, $"Cloned {repoName}, but checking out {branch} for RimWorld {gameVersion.Short} failed");
    }

    /// <summary>Puts a mod on its version branch at the branch tip.</summary>
    public static async Task<(bool Success, string? Error)> SwitchToVersionBranchAsync(
        IGitService gitService, InstalledMod mod, CancellationToken ct = default)
    {
        if (mod.VersionBranch is not { } branch)
            return (false, $"{mod.Name} has no branch for this game version");
        if (!await gitService.CheckoutAsync(mod.Path, branch, ct))
            return (false, $"Checkout of {branch} failed for {mod.Name}");
        return await gitService.ResetToCommitAsync(mod.Path, $"origin/{branch}", ct)
            ? (true, null)
            : (false, $"Switched to {branch}, but reset to origin/{branch} failed for {mod.Name}");
    }

    /// <summary>Replaces a local folder with a fresh clone of its matching repository.</summary>
    public static async Task<(bool Success, string? Error)> ConvertToGitAsync(
        IGitService gitService,
        IGitHubApiService gitHubApiService,
        InstalledMod mod,
        IProgress<double>? progress = null,
        GameVersion? gameVersion = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(mod.MatchedRepoName))
            return (Success: false, Error: "Repository not found");

        RemoteRepo? repo = await gitHubApiService.GetRepoByNameAsync(mod.MatchedRepoName, mod.CategoryId, ct);
        if (repo == null)
            return (Success: false, Error: "Repository not found");

        try
        {
            GitService.DeleteDirectory(mod.Path);
            var result = await gitService.CloneAsync(repo.CloneUrl, mod.Path, percentProgress: progress, ct: ct);
            return result.Success ? await UseVersionBranchAsync(gitService, repo.Name, repo.CategoryId, mod.Path, gameVersion, ct) : result;
        }
        catch (Exception ex)
        {
            return (Success: false, Error: ex.Message);
        }
    }

    /// <summary>
    /// Moves a mod to <paramref name="commit"/>. On another branch the branch is checked out first
    /// (tracking <c>origin/&lt;branch&gt;</c>), then reset to the commit when it is not the tip.
    /// </summary>
    public static async Task<(bool Success, string? Error)> ApplyVersionAsync(
        IGitService gitService,
        InstalledMod mod,
        VersionAction action,
        string commit,
        string? branch = null,
        CancellationToken ct = default)
    {
        switch (action)
        {
            case VersionAction.FastForward:
                return await gitService.FastForwardToAsync(mod.Path, commit, ct)
                    ? (true, null)
                    : (false, $"Fast-forward to {commit} failed for {mod.Name}");

            case VersionAction.Reset:
                return await gitService.ResetToCommitAsync(mod.Path, commit, ct)
                    ? (true, null)
                    : (false, $"Reset to {commit} failed for {mod.Name}");

            case VersionAction.SwitchBranch:
                if (string.IsNullOrEmpty(branch))
                    return (false, "No branch given");
                if (!await gitService.CheckoutAsync(mod.Path, branch, ct))
                    return (false, $"Checkout of {branch} failed for {mod.Name}");
                GitCommitInfo? head = await gitService.GetCurrentCommitAsync(mod.Path, ct);
                if (head is not null && (head.Hash.StartsWith(commit, StringComparison.OrdinalIgnoreCase)
                                         || head.ShortHash.Equals(commit, StringComparison.OrdinalIgnoreCase)))
                    return (true, null);
                return await gitService.ResetToCommitAsync(mod.Path, commit, ct)
                    ? (true, null)
                    : (false, $"Switched to {branch}, but reset to {commit} failed for {mod.Name}");

            case VersionAction.Checkout:
                return await gitService.CheckoutAsync(mod.Path, commit, ct)
                    ? (true, null)
                    : (false, $"Checkout of {commit} failed for {mod.Name}");

            default:
                return (false, $"Unknown action {action}");
        }
    }

    public static (bool Success, string? Error) Uninstall(InstalledMod mod)
    {
        try
        {
            GitService.DeleteDirectory(mod.Path);
            return (Success: true, Error: null);
        }
        catch (Exception ex)
        {
            return (Success: false, Error: ex.Message);
        }
    }
}
