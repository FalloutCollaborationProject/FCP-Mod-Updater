using Avalonia;
using Velopack;

namespace FCPModUpdater.Gui;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Handles Velopack's install/update/uninstall hooks (it may exit here); does nothing in a portable copy.
        VelopackApp.Build().Run();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
