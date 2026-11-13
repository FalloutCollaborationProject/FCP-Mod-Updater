using System.Net.Http.Headers;

namespace FCPModUpdater.Settings;

/// <summary>Adds the current GitHub token to each request, unless the request sets its own.</summary>
public class GitHubAuthHandler(GitHubTokenProvider tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Authorization is null && tokens.Token is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, ct);
    }
}
