using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

/// <summary>
/// Selection rules shared by every front end.
/// </summary>
public static class ModCatalog
{
    public const string NotRecommendedTopic = "fcp-not-recommended";

    public static bool IsNotRecommended(RemoteRepo repo) => IsNotRecommended(repo.Topics);

    public static bool IsNotRecommended(IReadOnlyList<string> topics) =>
        topics.Contains(NotRecommendedTopic, StringComparer.OrdinalIgnoreCase);

    /// <summary>Org repos that are not installed yet; recommended ones first, then by name.</summary>
    public static List<RemoteRepo> GetAvailableInstallRepos(
        IReadOnlyList<RemoteRepo> orgRepos,
        IEnumerable<string> installedNames)
    {
        var installedNameSet = new HashSet<string>(installedNames, StringComparer.OrdinalIgnoreCase);

        return orgRepos
            .Where(repo => !installedNameSet.Contains(repo.Name))
            .OrderBy(IsNotRecommended)
            .ThenBy(repo => repo.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Behind mods. Mods on the wrong branch for the game are left out unless <paramref name="includeMismatched"/>.</summary>
    public static IReadOnlyList<InstalledMod> GetUpdateableMods(IReadOnlyList<InstalledMod> mods, bool includeMismatched = false)
        => mods.Where(m => m.Source == ModSource.Git && m.Status == ModStatus.Behind
                           && (includeMismatched || !BlocksUpdate(m.VersionMismatch))).ToList();

    /// <summary>Pulling would bring in a newer game's build. An older version branch is safe to update; it just isn't the latest.</summary>
    public static bool BlocksUpdate(VersionMismatch mismatch)
        => mismatch is VersionMismatch.OnMainline or VersionMismatch.AheadOfVersionBranch;

    /// <summary>"rimworld-1.6" or "rimworld-1.6-snapshot" → 1.6; anything else → null.</summary>
    public static GameVersion? ParseVersionBranch(string? branch)
    {
        if (branch is null)
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(branch, @"^rimworld-(\d+\.\d+)(?:-snapshot)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? GameVersion.Parse(m.Groups[1].Value) : null;
    }

    public static IReadOnlyList<InstalledMod> GetVersionMismatchedMods(IReadOnlyList<InstalledMod> mods)
        => mods.Where(m => m.VersionMismatch != VersionMismatch.None && m.VersionBranch is not null).ToList();

    public const string ToolsRepoName = "FCP-Tools";

    /// <summary>
    /// Branch holding the last build for an older game version: "rimworld-1.6", except FCP-Tools,
    /// whose 1.6 branch is "rimworld-1.6-snapshot".
    /// </summary>
    public static string GetVersionBranchName(string? repoName, GameVersion gameVersion)
        => string.Equals(repoName, ToolsRepoName, StringComparison.OrdinalIgnoreCase) && gameVersion.Short == "1.6"
            ? "rimworld-1.6-snapshot"
            : $"rimworld-{gameVersion.Short}";

    /// <summary>Local (non-git) folders that match an FCP repository and can be replaced by a clone.</summary>
    public static IReadOnlyList<InstalledMod> GetConvertibleMods(IReadOnlyList<InstalledMod> mods)
        => mods.Where(m => m.Source != ModSource.Git && !string.IsNullOrEmpty(m.MatchedRepoName)).ToList();
}
