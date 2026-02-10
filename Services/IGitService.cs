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
    Task<bool> FetchAsync(string path, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<bool> PullAsync(string path, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<bool> CloneAsync(string url, string targetPath, IProgress<string>? progress = null, IProgress<double>? percentProgress = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRemoteBranchesAsync(string path, CancellationToken ct = default);
    Task<bool> CheckoutAsync(string path, string branchOrCommit, CancellationToken ct = default);
    Task<bool> ResetToCommitAsync(string path, string commitHash, CancellationToken ct = default);
    Task<bool> HasLocalChangesAsync(string path, CancellationToken ct = default);
    Task<IReadOnlyList<GitCommitInfo>> GetCommitHistoryAsync(string path, int limit = 20, CancellationToken ct = default);
}
