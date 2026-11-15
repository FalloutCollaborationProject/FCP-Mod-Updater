using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public interface IGitService
{
    Task<bool> IsGitRepositoryAsync(string path, CancellationToken ct = default);
    Task<string?> GetRemoteUrlAsync(string path, CancellationToken ct = default);
    Task<GitCommitInfo?> GetCurrentCommitAsync(string path, CancellationToken ct = default);
    Task<string?> GetCurrentBranchAsync(string path, CancellationToken ct = default);
    Task<(int Behind, int Ahead)> GetCommitDifferenceAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<GitCommitInfo>> GetIncomingCommitsAsync(string path, int limit = 10, CancellationToken ct = default);
    Task<GitOperationResult> FetchAsync(string path, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<GitOperationResult> PullAsync(string path, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<(bool Success, string? Error)> CloneAsync(string url, string targetPath, IProgress<string>? progress = null, IProgress<double>? percentProgress = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRemoteBranchesAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<GitBranchInfo>> GetRemoteBranchInfoAsync(string path, CancellationToken ct = default);
    Task<bool> CheckoutAsync(string path, string branchOrCommit, CancellationToken ct = default);
    Task<bool> ResetToCommitAsync(string path, string commitHash, CancellationToken ct = default);
    Task<bool> HasLocalChangesAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<GitCommitInfo>> GetCommitHistoryAsync(string path, int limit = 20, CancellationToken ct = default);

    /// <summary>Latest commits on <c>origin/&lt;branch&gt;</c>, newest first.</summary>
    Task<IReadOnlyList<GitCommitInfo>> GetBranchCommitsAsync(string path, string branch, int limit = 20, CancellationToken ct = default);

    /// <summary>Moves the current branch forward to <paramref name="commit"/> (<c>git merge --ff-only</c>).</summary>
    /// <summary><c>git merge-base --is-ancestor</c>: true when <paramref name="commit"/> is contained in <paramref name="of"/>.</summary>
    Task<bool> IsAncestorAsync(string path, string commit, string of, CancellationToken ct = default);
    Task<bool> FastForwardToAsync(string path, string commit, CancellationToken ct = default);
}
