using System.Net.Http.Json;
using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public class GitHubApiService : IGitHubApiService, IDisposable
{
    private const string OrgName = "FalloutCollaborationProject";
    private const string BaseUrl = "https://api.github.com";

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

    private IReadOnlyList<RemoteRepo>? _cachedRepos;
    private DateTimeOffset _cacheTime = DateTimeOffset.MinValue;

    public int? RemainingRateLimit { get; private set; }
    public DateTimeOffset? RateLimitReset { get; private set; }

    public GitHubApiService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FCPModUpdater/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<IReadOnlyList<RemoteRepo>> GetOrganizationReposAsync(CancellationToken ct = default)
    {
        if (_cachedRepos != null && DateTimeOffset.UtcNow - _cacheTime < _cacheExpiry)
        {
            return _cachedRepos;
        }

        var allRepos = new List<RemoteRepo>();
        var page = 1;
        const int perPage = 100;

        while (true)
        {
            var url = $"{BaseUrl}/orgs/{OrgName}/repos?per_page={perPage}&page={page}";

            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
                UpdateRateLimitInfo(response);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && RemainingRateLimit == 0)
                    {
                        // Rate limited - return cached data if available
                        if (_cachedRepos != null)
                            return _cachedRepos;
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
                if (_cachedRepos != null)
                    return _cachedRepos;

                break;
            }
        }

        if (allRepos.Count > 0)
        {
            _cachedRepos = allRepos;
            _cacheTime = DateTimeOffset.UtcNow;
        }

        return _cachedRepos ?? [];
    }

    public async Task<RemoteRepo?> GetRepoByNameAsync(string repoName, CancellationToken ct = default)
    {
        var repos = await GetOrganizationReposAsync(ct);
        return repos.FirstOrDefault(r =>
            r.Name.Equals(repoName, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining))
        {
            if (int.TryParse(remaining.FirstOrDefault(), out var value))
                RemainingRateLimit = value;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset))
        {
            if (long.TryParse(reset.FirstOrDefault(), out var timestamp))
                RateLimitReset = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
