using FCPModUpdater.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("fcp-mod-manager");
    config.SetApplicationVersion("1.0.0");

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Scan mods and show interactive menu")
        .WithExample("scan")
        .WithExample("scan", "--directory", "/path/to/RimWorld/Mods");

    config.AddCommand<UpdateCommand>("update")
        .WithDescription("Update all FCP mods (non-interactive)")
        .WithExample("update")
        .WithExample("update", "--directory", "/path/to/RimWorld/Mods");
});

return await app.RunAsync(args);
