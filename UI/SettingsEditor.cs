using FCPModUpdater.Settings;

namespace FCPModUpdater.UI;

/// <summary>The keys the CLI accepts for <c>settings set/unset</c>, and how each maps onto <see cref="AppSettings"/>.</summary>
public static class SettingsEditor
{
    public const string ModsDirectory = "mods-directory";
    public const string GitHubToken = "github-token";
    public const string CheckForUpdates = "check-updates";
    public const string IncludePrereleases = "include-prereleases";
    public const string GameVersion = "game-version";

    public static readonly IReadOnlyList<string> Keys = [ModsDirectory, GitHubToken, CheckForUpdates, IncludePrereleases, GameVersion];

    /// <summary>Applies <paramref name="value"/> (null = reset to default) or returns an error message.</summary>
    public static (AppSettings? Result, string? Error) Apply(AppSettings settings, string key, string? value)
    {
        switch (key.ToLowerInvariant())
        {
            case ModsDirectory:
                if (value is null)
                    return (settings with { ModsDirectory = null }, null);
                var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ExpandHome(value)));
                return Directory.Exists(full)
                    ? (settings with { ModsDirectory = full }, null)
                    : (null, $"Directory does not exist: {full}");

            case GitHubToken:
                return (settings with { GitHubToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim() }, null);

            case CheckForUpdates:
                if (value is null)
                    return (settings with { CheckForUpdatesOnLaunch = true }, null);
                return ParseBool(value) is { } check
                    ? (settings with { CheckForUpdatesOnLaunch = check }, null)
                    : (null, $"Expected true or false, got '{value}'");

            case IncludePrereleases:
                if (value is null)
                    return (settings with { IncludePrereleases = null }, null);
                return ParseBool(value) is { } pre
                    ? (settings with { IncludePrereleases = pre }, null)
                    : (null, $"Expected true or false, got '{value}'");

            case GameVersion:
                if (string.IsNullOrWhiteSpace(value))
                    return (settings with { GameVersionOverride = null }, null);
                return Services.GameVersion.Parse(value) is { } v
                    ? (settings with { GameVersionOverride = v.Short }, null)
                    : (null, $"Expected a RimWorld version such as 1.6, got '{value}'");

            default:
                return (null, $"Unknown setting '{key}'. Known settings: {string.Join(", ", Keys)}");
        }
    }

    public static string MaskToken(string? token) =>
        string.IsNullOrEmpty(token) ? "(not set)"
        : token.Length <= 8 ? new string('•', token.Length)
        : token[..4] + new string('•', 8) + token[^4..];

    public static string DescribeSource(TokenSource source) => source switch
    {
        TokenSource.Argument => "--github-token argument",
        TokenSource.Environment => "GITHUB_TOKEN environment variable",
        TokenSource.Settings => "saved in settings",
        _ => "none (60 requests an hour)",
    };

    internal static bool? ParseBool(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" => true,
        "false" or "no" or "off" or "0" => false,
        _ => null,
    };

    private static string ExpandHome(string path) =>
        path == "~" || path.StartsWith("~/") || path.StartsWith("~\\")
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 1 ? path[2..] : "")
            : path;
}
