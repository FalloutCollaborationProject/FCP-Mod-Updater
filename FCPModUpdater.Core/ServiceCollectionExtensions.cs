using System.Net.Http.Headers;
using FCPModUpdater.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FCPModUpdater;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services every front end shares. Register an <see cref="IGitOutputSink"/>
    /// as well to receive a transcript of the git commands that run.
    /// </summary>
    public static IServiceCollection AddFcpCore(this IServiceCollection services)
    {
        services.AddHttpClient<IGitHubApiService, GitHubApiService>(ConfigureGitHubClient);
        services.AddHttpClient<UpdateCheckService>(ConfigureGitHubClient);

        services.AddSingleton<IGitService>(sp => new GitService(sp.GetService<IGitOutputSink>()));
        services.AddSingleton<IModDiscoveryService, ModDiscoveryService>();
        services.AddSingleton<PathDiscoveryService>();

        return services;
    }

    private static void ConfigureGitHubClient(HttpClient client)
    {
        client.BaseAddress = new Uri("https://api.github.com");
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FCPModUpdater/{AppVersion.SemanticVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
