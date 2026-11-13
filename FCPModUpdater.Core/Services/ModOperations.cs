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
        CancellationToken ct = default)
    {
        var targetPath = Path.Combine(modsDirectory, repo.Name);
        return await gitService.CloneAsync(repo.CloneUrl, targetPath, percentProgress: progress, ct: ct);
    }

    /// <summary>Replaces a local folder with a fresh clone of its matching FCP repository.</summary>
    public static async Task<(bool Success, string? Error)> ConvertToGitAsync(
        IGitService gitService,
        IGitHubApiService gitHubApiService,
        InstalledMod mod,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(mod.MatchedRepoName))
            return (Success: false, Error: "Repository not found");

        RemoteRepo? repo = await gitHubApiService.GetRepoByNameAsync(mod.MatchedRepoName, ct);
        if (repo == null)
            return (Success: false, Error: "Repository not found");

        try
        {
            Directory.Delete(mod.Path, recursive: true);
            return await gitService.CloneAsync(repo.CloneUrl, mod.Path, percentProgress: progress, ct: ct);
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
            Directory.Delete(mod.Path, recursive: true);
            return (Success: true, Error: null);
        }
        catch (Exception ex)
        {
            return (Success: false, Error: ex.Message);
        }
    }
}
