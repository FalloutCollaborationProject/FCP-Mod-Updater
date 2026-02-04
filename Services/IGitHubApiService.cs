using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public interface IGitHubApiService
{
    Task<IReadOnlyList<RemoteRepo>> GetOrganizationReposAsync(CancellationToken ct = default);
    Task<RemoteRepo?> GetRepoByNameAsync(string repoName, CancellationToken ct = default);
    int? RemainingRateLimit { get; }
    DateTimeOffset? RateLimitReset { get; }
}
