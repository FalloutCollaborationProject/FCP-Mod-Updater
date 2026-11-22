using System.Runtime.InteropServices;
using FCPModUpdater;
using FCPModUpdater.Commands;
using FCPModUpdater.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

var services = new ServiceCollection();

services.AddFcpCore(CommandLine.FindOption(args, "--github-token"));

var app = new CommandApp(new TypeRegistrar(services));

app.Configure(config =>
{
    config.SetApplicationName("collab-project-updater");
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

    config.AddBranch("settings", settings =>
    {
        settings.SetDescription("Show or change saved settings");
        settings.SetDefaultCommand<SettingsShowCommand>();
        settings.AddCommand<SettingsSetCommand>("set")
            .WithDescription("Save a setting")
            .WithExample("settings", "set", "mods-directory", "/path/to/RimWorld/Mods")
            .WithExample("settings", "set", "include-prereleases", "false");
        settings.AddCommand<SettingsUnsetCommand>("unset")
            .WithDescription("Reset a setting to its default")
            .WithExample("settings", "unset", "github-token");
    });
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
