using System.Runtime.InteropServices;
using FCPModUpdater.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("fcp-mod-manager");
    config.SetApplicationVersion("1.0.0");

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
