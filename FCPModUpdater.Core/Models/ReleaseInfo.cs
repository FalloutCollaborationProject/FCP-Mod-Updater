using System.Text.Json.Serialization;

namespace FCPModUpdater.Models;

public record ReleaseInfo(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("draft")] bool Draft
);
