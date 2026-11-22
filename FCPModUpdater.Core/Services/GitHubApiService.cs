using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FCPModUpdater.Models;
using FCPModUpdater.Settings;

namespace FCPModUpdater.Services;

public class GitHubApiService : IGitHubApiService
{
    private readonly HttpClient _httpClient;
    private readonly Func<IReadOnlyList<ModCategory>> _categories;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

    private readonly Dictionary<string, (IReadOnlyList<RemoteRepo> Repos, DateTimeOffset Time)> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _cacheLock = new();

    /// <param name="settings">Supplies the categories to list; without it the built-in categories are used.</param>
    public GitHubApiService(HttpClient httpClient, SettingsStore? settings = null)
        : this(httpClient, () => ModCategories.Effective(settings?.Current))
    {
    }

    internal GitHubApiService(HttpClient httpClient, Func<IReadOnlyList<ModCategory>> categories)
    {
        _httpClient = httpClient;
        _categories = categories;
    }

    public int? RemainingRateLimit { get; private set; }
    public int? RateLimitTotal { get; private set; }
    public DateTimeOffset? RateLimitReset { get; private set; }

    public IReadOnlyList<ModCategory> Categories => _categories();

    public void ClearCache()
    {
        lock (_cacheLock)
            _cache.Clear();
    }

    public async Task<IReadOnlyList<RemoteRepo>> GetOrganizationReposAsync(CancellationToken ct = default)
    {
        var all = new List<RemoteRepo>();
        foreach (ModCategory category in _categories())
            all.AddRange(await GetCategoryReposAsync(category, ct));
        return all;
    }

    private async Task<IReadOnlyList<RemoteRepo>> GetCategoryReposAsync(ModCategory category, CancellationToken ct)
    {
        var key = $"{category.Id}|{category.Organization}|{category.RequiredTopic}";
        (IReadOnlyList<RemoteRepo> Repos, DateTimeOffset Time) cached;
        bool hasCached;
        lock (_cacheLock)
            hasCached = _cache.TryGetValue(key, out cached);

        if (hasCached && DateTimeOffset.UtcNow - cached.Time < _cacheExpiry)
            return cached.Repos;

        var allRepos = new List<RemoteRepo>();
        var page = 1;
        const int perPage = 100;

        while (true)
        {
            var url = $"orgs/{category.Organization}/repos?type=all&per_page={perPage}&page={page}";

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
                UpdateRateLimitInfo(response);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && RemainingRateLimit == 0)
                    {
                        // Rate limited - return cached data if available
                        if (hasCached)
                            return cached.Repos;
                    }

                    break;
                }

                var repos = await response.Content.ReadFromJsonAsync<List<RemoteRepo>>(ct);
                if (repos == null || repos.Count == 0)
                    break;

                allRepos.AddRange(repos);

                if (repos.Count < perPage)
                    break;

                page++;
            }
            catch (HttpRequestException)
            {
                // Network error - return cached data if available
                if (hasCached)
                    return cached.Repos;

                break;
            }
        }

        if (allRepos.Count > 0)
        {
            var filtered = allRepos
                .Where(r => IsMod(category, r))
                .Select(r => r with { CategoryId = category.Id })
                .ToList();
            lock (_cacheLock)
                _cache[key] = (filtered, DateTimeOffset.UtcNow);
            return filtered;
        }

        return hasCached ? cached.Repos : [];
    }

    private static bool IsMod(ModCategory category, RemoteRepo repo)
        => category.RequiredTopic is { } topic
            ? (repo.Topics ?? []).Contains(topic, StringComparer.OrdinalIgnoreCase)
            : !repo.Archived && !repo.Fork;

    public async Task<RemoteRepo?> GetRepoByNameAsync(string repoName, CancellationToken ct = default)
        => await GetRepoByNameAsync(repoName, null, ct);

    public async Task<RemoteRepo?> GetRepoByNameAsync(string repoName, string? categoryId, CancellationToken ct = default)
    {
        var repos = await GetOrganizationReposAsync(ct);
        var matches = repos.Where(r => r.Name.Equals(repoName, StringComparison.OrdinalIgnoreCase));
        return (categoryId is null ? null : matches.FirstOrDefault(r => r.CategoryId.Equals(categoryId, StringComparison.OrdinalIgnoreCase)))
               ?? matches.FirstOrDefault();
    }

    public async Task<RateLimitCheck> GetRateLimitAsync(string? token = null, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "rate_limit");
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new RateLimitCheck(null, "GitHub rejected the token (401 Unauthorized).");
            if (!response.IsSuccessStatusCode)
                return new RateLimitCheck(null, $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            using JsonDocument doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            JsonElement core = doc.RootElement.GetProperty("resources").GetProperty("core");
            var info = new RateLimitInfo(
                core.GetProperty("remaining").GetInt32(),
                core.GetProperty("limit").GetInt32(),
                DateTimeOffset.FromUnixTimeSeconds(core.GetProperty("reset").GetInt64()));

            if (string.IsNullOrWhiteSpace(token))
            {
                RemainingRateLimit = info.Remaining;
                RateLimitTotal = info.Limit;
                RateLimitReset = info.Reset;
            }

            return new RateLimitCheck(info, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new RateLimitCheck(null, ex.Message);
        }
    }

    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining))
        {
            if (int.TryParse(remaining.FirstOrDefault(), out var value))
                RemainingRateLimit = value;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Limit", out var limit))
        {
            if (int.TryParse(limit.FirstOrDefault(), out var value))
                RateLimitTotal = value;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset))
        {
            if (long.TryParse(reset.FirstOrDefault(), out var timestamp))
                RateLimitReset = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
    }

}
