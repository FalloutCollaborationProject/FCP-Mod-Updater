using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

/// <summary>
/// Selection rules shared by every front end.
/// </summary>
public static class ModCatalog
{
    /// <summary>The FCP not-recommended topic.</summary>
    public const string NotRecommendedTopic = "fcp-not-recommended";

    /// <summary>
    /// Whether the repo carries its category's not-recommended topic. Pass <paramref name="categories"/> to
    /// honour configured categories; otherwise the built-in ones are used.
    /// </summary>
    public static bool IsNotRecommended(RemoteRepo repo, IEnumerable<ModCategory>? categories = null)
        => IsNotRecommended(repo.Topics, repo.CategoryId, categories);

    /// <summary>
    /// Whether <paramref name="topics"/> contain the not-recommended topic of <paramref name="categoryId"/>.
    /// An unknown or missing category falls back to the FCP topic.
    /// </summary>
    public static bool IsNotRecommended(IReadOnlyList<string>? topics, string? categoryId = null,
        IEnumerable<ModCategory>? categories = null)
    {
        if (topics is null || topics.Count == 0)
            return false;
        ModCategory? category = ModCategories.Find(categories ?? ModCategories.BuiltIn, categoryId);
        var topic = category is null ? NotRecommendedTopic : category.NotRecommendedTopic;
        return topic is not null && topics.Contains(topic, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Org repos that are not installed yet, optionally only those of <paramref name="categoryId"/>;
    /// recommended ones first, then by name.
    /// </summary>
    public static List<RemoteRepo> GetAvailableInstallRepos(
        IReadOnlyList<RemoteRepo> orgRepos,
        IEnumerable<string> installedNames,
        string? categoryId = null,
        IEnumerable<ModCategory>? categories = null)
    {
        var installedNameSet = new HashSet<string>(installedNames, StringComparer.OrdinalIgnoreCase);

        return orgRepos
            .Where(repo => !installedNameSet.Contains(repo.Name))
            .Where(repo => categoryId is null || repo.CategoryId.Equals(categoryId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(repo => IsNotRecommended(repo, categories))
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
    /// whose 1.6 branch is "rimworld-1.6-snapshot" (FCP only; a null category is treated as FCP).
    /// </summary>
    public static string GetVersionBranchName(string? repoName, GameVersion gameVersion, string? categoryId = null)
        => (categoryId is null or "" || categoryId.Equals(ModCategories.FcpId, StringComparison.OrdinalIgnoreCase))
           && string.Equals(repoName, ToolsRepoName, StringComparison.OrdinalIgnoreCase) && gameVersion.Short == "1.6"
            ? "rimworld-1.6-snapshot"
            : $"rimworld-{gameVersion.Short}";

    /// <summary>Mods of one category, or all of them when <paramref name="categoryId"/> is null.</summary>
    public static IReadOnlyList<InstalledMod> InCategory(IReadOnlyList<InstalledMod> mods, string? categoryId)
        => categoryId is null
            ? mods
            : mods.Where(m => string.Equals(m.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Local (non-git) folders that match an org repository and can be replaced by a clone.</summary>
    public static IReadOnlyList<InstalledMod> GetConvertibleMods(IReadOnlyList<InstalledMod> mods)
        => mods.Where(m => m.Source != ModSource.Git && !string.IsNullOrEmpty(m.MatchedRepoName)).ToList();
}
