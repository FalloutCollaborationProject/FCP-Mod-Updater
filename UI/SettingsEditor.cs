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
    public const string UpdateParallelism = "update-parallelism";
    public const string GitPath = "git-path";

    /// <summary>Prefix of the per-category key <c>category.&lt;id&gt;.enabled</c>.</summary>
    public const string CategoryPrefix = "category.";
    public const string CategoryEnabledSuffix = ".enabled";

    public static readonly IReadOnlyList<string> Keys = [ModsDirectory, GitHubToken, CheckForUpdates, IncludePrereleases, GameVersion, UpdateParallelism, GitPath, "category.<id>.enabled"];

    public static string CategoryKey(string id) => $"{CategoryPrefix}{id}{CategoryEnabledSuffix}";

    /// <summary>Applies <paramref name="value"/> (null = reset to default) or returns an error message.</summary>
    public static (AppSettings? Result, string? Error) Apply(AppSettings settings, string key, string? value)
    {
        var lower = key.ToLowerInvariant();
        if (lower.StartsWith(CategoryPrefix) && lower.EndsWith(CategoryEnabledSuffix))
        {
            var id = key[CategoryPrefix.Length..^CategoryEnabledSuffix.Length];
            if (Models.ModCategories.Find(Models.ModCategories.Resolve(settings), id) is null)
                return (null, $"Unknown category '{id}'. Known categories: {string.Join(", ", Models.ModCategories.Resolve(settings).Select(c => c.Id))}");
            if (value is null)
                return (Models.ModCategories.WithEnabled(settings, id, true), null);
            return ParseBool(value) is { } enabled
                ? (Models.ModCategories.WithEnabled(settings, id, enabled), null)
                : (null, $"Expected true or false, got '{value}'");
        }

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

            case UpdateParallelism:
                if (string.IsNullOrWhiteSpace(value))
                    return (settings with { UpdateParallelism = AppSettings.DefaultUpdateParallelism }, null);
                return int.TryParse(value.Trim(), out var n) && n >= 1 && n <= Services.ParallelRunner.MaxParallelism
                    ? (settings with { UpdateParallelism = n }, null)
                    : (null, $"Expected a number from 1 to {Services.ParallelRunner.MaxParallelism}, got '{value}'");

            case GitPath:
                if (string.IsNullOrWhiteSpace(value))
                    return (settings with { GitPath = null }, null);
                return Services.GitService.NormalizeGitPath(ExpandHome(value.Trim().Trim('"'))) is { } git
                    ? (settings with { GitPath = git }, null)
                    : (null, $"No git executable found at '{value}'");

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
