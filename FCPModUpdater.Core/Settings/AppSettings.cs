using System.Text.Json.Serialization;

namespace FCPModUpdater.Settings;

/// <summary>Values saved between runs, shared by the CLI and the GUI.</summary>
public sealed record AppSettings
{
    /// <summary>Saved RimWorld Mods folder. A <c>--directory</c> argument overrides it for that run.</summary>
    public string? ModsDirectory { get; init; }

    /// <summary>GitHub personal access token. Overridden by <c>--github-token</c> and <c>GITHUB_TOKEN</c>.</summary>
    public string? GitHubToken { get; init; }

    public bool CheckForUpdatesOnLaunch { get; init; } = true;

    /// <summary>Null means "not chosen yet": follow whether the running build is itself a pre-release.</summary>
    public bool? IncludePrereleases { get; init; }

    /// <summary>RimWorld version such as "1.6", used instead of the one read from the install's Version.txt.</summary>
    public string? GameVersionOverride { get; init; }

    public DateTimeOffset? LastUpdateCheck { get; init; }

    /// <summary>Latest version found by the last check, or null when the app was up to date.</summary>
    public string? LastUpdateCheckResult { get; init; }

    [JsonIgnore]
    public bool EffectiveIncludePrereleases => IncludePrereleases ?? AppVersion.IsDevBuild;
}
