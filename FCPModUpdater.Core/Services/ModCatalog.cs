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

    public static IReadOnlyList<InstalledMod> GetUpdateableMods(IReadOnlyList<InstalledMod> mods)
        => mods.Where(m => m.Source == ModSource.Git && m.Status == ModStatus.Behind).ToList();

    /// <summary>Local (non-git) folders that match an FCP repository and can be replaced by a clone.</summary>
    public static IReadOnlyList<InstalledMod> GetConvertibleMods(IReadOnlyList<InstalledMod> mods)
        => mods.Where(m => m.Source != ModSource.Git && !string.IsNullOrEmpty(m.MatchedRepoName)).ToList();
}
