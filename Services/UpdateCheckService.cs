using System.Net.Http.Json;
using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public class UpdateCheckService
{
    private const string RepoOwner = "FalloutCollaborationProject";
    private const string RepoName = "FCP-Mod-Updater";
    private const string BaseUrl = "https://api.github.com";

    private readonly HttpClient _httpClient;

    public UpdateCheckService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Check if a newer version is available. Returns null if check fails or no update needed.
    /// Checks both stable and prerelease versions.
    /// </summary>
    public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/repos/{RepoOwner}/{RepoName}/releases?per_page=15";
            var releases = await _httpClient.GetFromJsonAsync<List<ReleaseInfo>>(url, ct);

            if (releases is null || releases.Count == 0)
                return null;

            // Find the release with the highest semver (stable or prerelease)
            ReleaseInfo? newest = null;
            Version? newestVersion = null;

            foreach (var release in releases)
            {
                if (release.Draft)
                    continue;

                var tagBase = GetBaseVersion(release.TagName.TrimStart('v'));
                if (!Version.TryParse(tagBase, out var ver))
                    continue;

                if (newestVersion is null || ver > newestVersion)
                {
                    newestVersion = ver;
                    newest = release;
                }
            }

            if (newest is null || newestVersion is null)
                return null;

            var currentBase = GetBaseVersion(AppVersion.SemanticVersion);
            if (!Version.TryParse(currentBase, out var currentVer))
                return null;

            if (newestVersion > currentVer)
            {
                var latestTag = newest.TagName.TrimStart('v');
                return new UpdateCheckResult(
                    CurrentVersion: AppVersion.InformationalVersion,
                    LatestVersion: latestTag,
                    ReleaseUrl: newest.HtmlUrl,
                    ReleaseName: newest.Name,
                    PublishedAt: newest.PublishedAt,
                    IsPrerelease: newest.Prerelease
                );
            }

            return null;
        }
        catch
        {
            // Update check is non-critical — fail silently
            return null;
        }
    }

    private static string GetBaseVersion(string version)
    {
        var dashIndex = version.IndexOf('-');
        return dashIndex >= 0 ? version[..dashIndex] : version;
    }
}

public record UpdateCheckResult(
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string ReleaseName,
    DateTimeOffset PublishedAt,
    bool IsPrerelease
);
