using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public class GitHubApiService(HttpClient httpClient) : IGitHubApiService
{
    private const string OrgName = "FalloutCollaborationProject";
    private const string RequiredTopic = "rimworld-mod";

    private readonly HttpClient _httpClient = httpClient;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

    private IReadOnlyList<RemoteRepo>? _cachedRepos;
    private DateTimeOffset _cacheTime = DateTimeOffset.MinValue;

    public int? RemainingRateLimit { get; private set; }
    public int? RateLimitTotal { get; private set; }
    public DateTimeOffset? RateLimitReset { get; private set; }

    public void ClearCache()
    {
        _cachedRepos = null;
        _cacheTime = DateTimeOffset.MinValue;
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
            var url = $"orgs/{OrgName}/repos?per_page={perPage}&page={page}";

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
            // Filter to only repos tagged as RimWorld mods
            _cachedRepos = allRepos
                .Where(r => r.Topics.Contains(RequiredTopic, StringComparer.OrdinalIgnoreCase))
                .ToList();
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
