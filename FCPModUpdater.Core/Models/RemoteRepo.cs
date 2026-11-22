using System.Text.Json.Serialization;

namespace FCPModUpdater.Models;

public record RemoteRepo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("clone_url")] string CloneUrl,
    [property: JsonPropertyName("default_branch")] string DefaultBranch,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("topics")] IReadOnlyList<string> Topics
)
{
    [JsonPropertyName("pushed_at")] public DateTimeOffset? PushedAt { get; init; }

    /// <summary>Repository size as reported by GitHub, in kilobytes.</summary>
    [JsonPropertyName("size")] public long? SizeKb { get; init; }

    [JsonPropertyName("archived")] public bool Archived { get; init; }

    [JsonPropertyName("fork")] public bool Fork { get; init; }

    /// <summary>Id of the <see cref="ModCategory"/> this repository was listed under.</summary>
    [JsonIgnore] public string CategoryId { get; init; } = "";
}
