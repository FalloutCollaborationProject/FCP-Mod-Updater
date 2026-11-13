using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

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
