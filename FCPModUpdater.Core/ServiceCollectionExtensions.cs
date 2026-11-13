using FCPModUpdater.Services;
using FCPModUpdater.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FCPModUpdater;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services every front end shares. Register an <see cref="IGitOutputSink"/>
    /// as well to receive a transcript of the git commands that run.
    /// </summary>
    /// <param name="githubToken">Token from a <c>--github-token</c> argument; it takes priority over every other source.</param>
    /// <param name="settingsStore">Settings to use; defaults to the platform settings file.</param>
    public static IServiceCollection AddFcpCore(
        this IServiceCollection services,
        string? githubToken = null,
        SettingsStore? settingsStore = null)
    {
        services.AddSingleton(settingsStore ?? new SettingsStore());
        services.AddSingleton(sp => new GitHubTokenProvider(sp.GetRequiredService<SettingsStore>(), githubToken));
        services.AddTransient<GitHubAuthHandler>();

        services.AddHttpClient<IGitHubApiService, GitHubApiService>(ConfigureGitHubClient)
            .AddHttpMessageHandler<GitHubAuthHandler>();
        services.AddHttpClient<UpdateCheckService>(ConfigureGitHubClient)
            .AddHttpMessageHandler<GitHubAuthHandler>();

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
    }
}
