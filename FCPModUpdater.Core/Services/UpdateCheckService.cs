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
            string? newestVersion = null;

            foreach (ReleaseInfo release in releases)
            {
                if (release.Draft || (release.Prerelease && !includePrereleases))
                    continue;

                var ver = release.TagName.TrimStart('v');
                if (!IsValidVersion(ver))
                    continue;

                if (newestVersion is null || CompareVersions(ver, newestVersion) > 0)
                {
                    newestVersion = ver;
                    newest = release;
                }
            }

            if (newest is null || newestVersion is null)
                return null;

            var current = BuiltFromTag(AppVersion.SemanticVersion);
            if (!IsValidVersion(current))
                return null;

            if (CompareVersions(newestVersion, current) > 0)
            {
                var latestTag = newest.TagName.TrimStart('v');
                return new UpdateCheckResult(
                    CurrentVersion: AppVersion.InformationalVersion,
                    LatestVersion: latestTag,
                    ReleaseUrl: newest.HtmlUrl,
                    ReleaseName: newest.Name,
                    PublishedAt: newest.PublishedAt,
                    IsPrerelease: newest.Prerelease,
                    ReleaseNotes: ChangelogPart(newest.Body)
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
    /// <summary>
    /// The updater script to run, when it sits next to the app. The scripts are separate release downloads (not in the
    /// archives), so a folder without one gets null and the caller points at the release instead.
    /// </summary>
    public static string? GetUpdaterCommand() =>
        GetUpdaterCommand(IsAppImage) is { } command
        && File.Exists(Path.Combine(AppContext.BaseDirectory, command.TrimStart('.', '/')))
            ? command
            : null;

    // An AppImage is one read-only file with no script beside it; it is updated by replacing the file.
    internal static string? GetUpdaterCommand(bool isAppImage) =>
        isAppImage ? null
        : OperatingSystem.IsWindows() ? "update-collab-project-updater.bat"
        : OperatingSystem.IsLinux() ? "./update-collab-project-updater.sh"
        : null;

    /// <summary>The tag a dev build came from: "1.0.0-dev.4" → "1.0.0", "1.0.0-alpha.3.dev.4" → "1.0.0-alpha.3".</summary>
    internal static string BuiltFromTag(string version) =>
        System.Text.RegularExpressions.Regex.Replace(version, @"[-.]dev\.\d+$", "");

    private static bool IsValidVersion(string version) =>
        Version.TryParse(SplitVersion(version).Core, out _);

    private static (string Core, string? Label) SplitVersion(string version)
    {
        var plus = version.IndexOf('+');
        if (plus >= 0)
            version = version[..plus];
        var dash = version.IndexOf('-');
        return dash >= 0 ? (version[..dash], version[(dash + 1)..]) : (version, null);
    }

    /// <summary>SemVer order: core version, then a release beats any pre-release, then the labels by dot part.</summary>
    /// <summary>
    /// The changelog part of a release body: the text above the first <c>---</c> line (below it are the download
    /// instructions from <c>.github/release-downloads.md</c>). Null when there is none.
    /// </summary>
    internal static string? ChangelogPart(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var end = Array.FindIndex(lines, l => l.Trim() == "---");
        var notes = string.Join('\n', end < 0 ? lines : lines[..end]).Trim();
        return notes.Length == 0 ? null : notes;
    }

    internal static int CompareVersions(string a, string b)
    {
        var (coreA, labelA) = SplitVersion(a);
        var (coreB, labelB) = SplitVersion(b);
        var core = Version.Parse(coreA).CompareTo(Version.Parse(coreB));
        if (core != 0)
            return core;
        if (labelA is null || labelB is null)
            return labelA is null ? (labelB is null ? 0 : 1) : -1;

        var partsA = labelA.Split('.');
        var partsB = labelB.Split('.');
        for (var i = 0; i < Math.Min(partsA.Length, partsB.Length); i++)
        {
            var numA = long.TryParse(partsA[i], out var na);
            var numB = long.TryParse(partsB[i], out var nb);
            var c = numA && numB ? na.CompareTo(nb)
                : numA ? -1
                : numB ? 1
                : string.CompareOrdinal(partsA[i], partsB[i]);
            if (c != 0)
                return Math.Sign(c);
        }

        return partsA.Length.CompareTo(partsB.Length);
    }
}

public record UpdateCheckResult(
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string ReleaseName,
    DateTimeOffset PublishedAt,
    bool IsPrerelease,
    string? ReleaseNotes = null
);
