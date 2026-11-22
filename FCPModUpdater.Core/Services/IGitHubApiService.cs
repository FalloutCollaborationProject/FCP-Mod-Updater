using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public interface IGitHubApiService
{
    void ClearCache();
    Task<IReadOnlyList<RemoteRepo>> GetOrganizationReposAsync(CancellationToken ct = default);
    Task<RemoteRepo?> GetRepoByNameAsync(string repoName, CancellationToken ct = default);

    /// <summary>Like <see cref="GetRepoByNameAsync(string, CancellationToken)"/>, preferring the repo in <paramref name="categoryId"/>.</summary>
    Task<RemoteRepo?> GetRepoByNameAsync(string repoName, string? categoryId, CancellationToken ct = default);

    /// <summary>The enabled categories the repository list is built from.</summary>
    IReadOnlyList<ModCategory> Categories { get; }
    /// <summary>
    /// Asks GitHub for the current rate limit (this call does not count against it).
    /// With <paramref name="token"/>, checks that token instead of the one in use.
    /// </summary>
    Task<RateLimitCheck> GetRateLimitAsync(string? token = null, CancellationToken ct = default);

    int? RemainingRateLimit { get; }
    int? RateLimitTotal { get; }
    DateTimeOffset? RateLimitReset { get; }
}
