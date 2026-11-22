using System.Text.Json.Serialization;
using FCPModUpdater.Settings;

namespace FCPModUpdater.Models;

/// <summary>
/// A family of mods published by one GitHub organization, with the rules for which of its repositories count as mods.
/// </summary>
public sealed record ModCategory(string Id, string DisplayName, string Organization)
{
    /// <summary>Only repos with this topic are mods. Null takes every repository that is not archived or a fork.</summary>
    public string? RequiredTopic { get; init; }

    /// <summary>Repos with this topic sort last and carry a warning.</summary>
    public string? NotRecommendedTopic { get; init; }

    public bool Enabled { get; init; } = true;

    [JsonIgnore]
    public string OrgUrlFragment => $"github.com/{Organization}/";

    [JsonIgnore]
    public string OrgUrl => $"https://github.com/{Organization}";

    public bool OwnsRemoteUrl(string? remoteUrl)
        => remoteUrl is not null && remoteUrl.Contains(OrgUrlFragment, StringComparison.OrdinalIgnoreCase);
}

public static class ModCategories
{
    public const string FcpId = "fcp";
    public const string EscpId = "escp";

    public static readonly ModCategory Fcp = new(FcpId, "FCP", "FalloutCollaborationProject")
    {
        RequiredTopic = "rimworld-mod",
        NotRecommendedTopic = "fcp-not-recommended",
    };

    public static readonly ModCategory Escp = new(EscpId, "ESCP", "ElderScrollsCollaborationProject");

    public static IReadOnlyList<ModCategory> BuiltIn { get; } = [Fcp, Escp];

    /// <summary>
    /// Built-in categories merged with the saved ones by <see cref="ModCategory.Id"/>: a saved entry replaces the
    /// built-in one (empty name or organization fall back to the built-in), an unknown id adds a category.
    /// Disabled categories are included; see <see cref="Effective"/>.
    /// </summary>
    public static IReadOnlyList<ModCategory> Resolve(AppSettings? settings)
    {
        var result = BuiltIn.ToList();
        foreach (ModCategory saved in settings?.Categories ?? [])
        {
            if (string.IsNullOrWhiteSpace(saved.Id))
                continue;
            var index = result.FindIndex(c => c.Id.Equals(saved.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                ModCategory builtIn = result[index];
                result[index] = saved with
                {
                    Id = builtIn.Id,
                    DisplayName = string.IsNullOrWhiteSpace(saved.DisplayName) ? builtIn.DisplayName : saved.DisplayName,
                    Organization = string.IsNullOrWhiteSpace(saved.Organization) ? builtIn.Organization : saved.Organization,
                };
            }
            else if (!string.IsNullOrWhiteSpace(saved.Organization))
            {
                result.Add(string.IsNullOrWhiteSpace(saved.DisplayName) ? saved with { DisplayName = saved.Id } : saved);
            }
        }
        return result;
    }

    /// <summary>The enabled categories, in order.</summary>
    public static IReadOnlyList<ModCategory> Effective(AppSettings? settings)
        => Resolve(settings).Where(c => c.Enabled).ToList();

    public static ModCategory? Find(IEnumerable<ModCategory> categories, string? id)
        => id is null ? null : categories.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static ModCategory? FindByRemoteUrl(IEnumerable<ModCategory> categories, string? remoteUrl)
        => categories.FirstOrDefault(c => c.OwnsRemoteUrl(remoteUrl));

    /// <summary>Settings with one category switched on or off, kept as a saved override.</summary>
    public static AppSettings WithEnabled(AppSettings settings, string id, bool enabled)
    {
        ModCategory current = Find(Resolve(settings), id) ?? throw new ArgumentException($"Unknown category '{id}'.", nameof(id));
        var saved = (settings.Categories ?? []).Where(c => !c.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        saved.Add(current with { Enabled = enabled });
        return settings with { Categories = saved };
    }
}
