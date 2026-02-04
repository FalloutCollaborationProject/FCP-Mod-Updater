using System.Text.Json.Serialization;

namespace FCPModUpdater.Models;

public record RemoteRepo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("clone_url")] string CloneUrl,
    [property: JsonPropertyName("default_branch")] string DefaultBranch,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("topics")] IReadOnlyList<string> Topics
);
