using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public static class GitModUpdater
{
    public static async Task<(bool Success, string? Error)> UpdateAsync(
        IGitService gitService,
        InstalledMod mod,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(25);
        GitOperationResult fetchResult = await gitService.FetchAsync(mod.Path, ct: ct);
        if (!fetchResult.Success)
        {
            return await VerifyOrFailAsync(gitService, mod, "Fetch", fetchResult, ct);
        }

        progress?.Report(50);
        GitOperationResult pullResult = await gitService.PullAsync(mod.Path, ct: ct);
        progress?.Report(100);

        (int behind, _) = await gitService.GetCommitDifferenceAsync(mod.Path, ct);
        if (behind == 0)
        {
            return (Success: true, Error: null);
        }

        if (pullResult.Success)
        {
            return (Success: false,
                Error: $"Pull completed for {mod.Name}, but it is still {behind} commit(s) behind");
        }

        return (Success: false, Error: $"Pull failed for {mod.Name}: {FormatGitFailure(pullResult)}");
    }

    private static async Task<(bool Success, string? Error)> VerifyOrFailAsync(
        IGitService gitService,
        InstalledMod mod,
        string operation,
        GitOperationResult result,
        CancellationToken ct)
    {
        (int behind, _) = await gitService.GetCommitDifferenceAsync(mod.Path, ct);
        if (behind == 0)
        {
            return (Success: true, Error: null);
        }

        return (Success: false, Error: $"{operation} failed for {mod.Name}: {FormatGitFailure(result)}");
    }

    private static string FormatGitFailure(GitOperationResult result)
    {
        return result.Error ?? $"git exited with code {result.ExitCode}";
    }
}
