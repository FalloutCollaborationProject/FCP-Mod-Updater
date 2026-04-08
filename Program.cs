using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using FCPModUpdater;
using FCPModUpdater.Commands;
using FCPModUpdater.Infrastructure;
using FCPModUpdater.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

var services = new ServiceCollection();

services.AddHttpClient<IGitHubApiService, GitHubApiService>(ConfigureGitHubClient);
services.AddHttpClient<UpdateCheckService>(ConfigureGitHubClient);

services.AddSingleton<IGitService, GitService>();
services.AddSingleton<IModDiscoveryService, ModDiscoveryService>();

var app = new CommandApp(new TypeRegistrar(services));

app.Configure(config =>
{
    config.SetApplicationName("fcp-mod-manager");
    config.SetApplicationVersion(AppVersion.InformationalVersion);
    config.SetInterceptor(new GitRequiredInterceptor());

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Scan mods and show interactive menu (DEFAULT)")
        .WithExample("scan")
        .WithExample("scan", "--directory", "/path/to/RimWorld/Mods");

    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Update all FCP mods (non-interactive)")
        .WithExample("update")
        .WithExample("update", "--directory", "/path/to/RimWorld/Mods");
});

app.SetDefaultCommand<ScanCommand>();

var result = await app.RunAsync(args);

// On Windows, wait for key press before closing so the window doesn't vanish
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Press any key to exit...[/]");
    Console.ReadKey(true);
}

return result;

void ConfigureGitHubClient(HttpClient client)
{
    client.BaseAddress = new Uri("https://api.github.com");
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"FCPModUpdater/{AppVersion.SemanticVersion}");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrEmpty(token))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
