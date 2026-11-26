using System.Net.Http.Json;
using FCPModUpdater.Models;
using FCPModUpdater.Settings;

namespace FCPModUpdater.Services;

public class UpdateCheckService(HttpClient httpClient)
{
    private const string RepoOwner = "FalloutCollaborationProject";
    private const string RepoName = "FCP-Mod-Updater";

    /// <summary>The app's GitHub repository, whose releases carry the downloads.</summary>
    public const string RepoUrl = $"https://github.com/{RepoOwner}/{RepoName}";

    /// <summary>Direct download link of one asset of release <paramref name="tag"/> (with or without the leading v).</summary>
    public static string ReleaseAssetUrl(string tag, string asset) =>
        $"{RepoUrl}/releases/download/v{tag.TrimStart('v')}/{asset}";

    /// <summary>
    /// Check if a newer version is available. Returns null if check fails or no update needed.
    /// Pre-releases are considered only when <paramref name="includePrereleases"/> is true.
    /// </summary>
    public async Task<UpdateCheckResult?> CheckForUpdateAsync(bool includePrereleases = true, CancellationToken ct = default)
    {
        try
        {
            var url = $"repos/{RepoOwner}/{RepoName}/releases?per_page=15";
            var releases = await httpClient.GetFromJsonAsync<List<ReleaseInfo>>(url, ct);

            if (releases is null || releases.Count == 0)
                return null;

            // Find the release with the highest semver (stable or prerelease)
            ReleaseInfo? newest = null;
            Version? newestVersion = null;

            foreach (ReleaseInfo release in releases)
            {
                if (release.Draft || (release.Prerelease && !includePrereleases))
                    continue;

                var tagBase = GetBaseVersion(release.TagName.TrimStart('v'));
                if (!Version.TryParse(tagBase, out Version? ver))
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
            if (!Version.TryParse(currentBase, out Version? currentVer))
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

    /// <summary>
    /// Checks using the saved pre-release preference and records when the check ran and what it found.
    /// </summary>
    public async Task<UpdateCheckResult?> CheckAndRecordAsync(SettingsStore settings, CancellationToken ct = default)
    {
        UpdateCheckResult? result = await CheckForUpdateAsync(settings.Current.EffectiveIncludePrereleases, ct);
        try
        {
            settings.Update(s => s with
            {
                LastUpdateCheck = DateTimeOffset.Now,
                LastUpdateCheckResult = result?.LatestVersion,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recording the check is best effort
        }

        return result;
    }

    /// <summary>True when the app runs from an AppImage (its runtime sets <c>APPIMAGE</c> to the file's path).</summary>
    public static bool IsAppImage => IsAppImagePath(Environment.GetEnvironmentVariable("APPIMAGE"));

    internal static bool IsAppImagePath(string? appImage) => !string.IsNullOrWhiteSpace(appImage);

    /// <summary>The updater script shipped next to the app for this platform, or null when there is none.</summary>
    public static string? GetUpdaterCommand() => GetUpdaterCommand(IsAppImage);

    // An AppImage is one read-only file with no script beside it; it is updated by replacing the file.
    internal static string? GetUpdaterCommand(bool isAppImage) =>
        isAppImage ? null
        : OperatingSystem.IsWindows() ? "update-collab-project-updater.bat"
        : OperatingSystem.IsLinux() ? "./update-collab-project-updater.sh"
        : null;

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
