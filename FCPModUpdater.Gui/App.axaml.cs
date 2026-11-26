using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Gui.ViewModels;
using FCPModUpdater.Gui.Views;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace FCPModUpdater.Gui;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ServiceProvider services = BuildServices(desktop.Args ?? []);
            var shell = services.GetRequiredService<MainWindowViewModel>();

            desktop.MainWindow = new MainWindow { DataContext = shell };
            desktop.Exit += (_, _) => services.Dispose();

            _ = shell.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServices(string[] args)
    {
        Action<Action> post = action => Dispatcher.UIThread.Post(action);

        var services = new ServiceCollection();
        services.AddSingleton(new GitOutputLog(post));
        services.AddSingleton<IGitOutputSink>(sp => sp.GetRequiredService<GitOutputLog>());
        services.AddFcpCore(CommandLine.FindOption(args, "--github-token"));
        services.AddSingleton(ParseArgs(args));
        services.AddHttpClient<IAppUpdater, VelopackAppUpdater>(client =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"CollabProjectUpdater/{AppVersion.SemanticVersion}"));
        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<IGitService>(),
            sp.GetRequiredService<IGitHubApiService>(),
            sp.GetRequiredService<IModDiscoveryService>(),
            sp.GetRequiredService<UpdateCheckService>(),
            sp.GetRequiredService<PathDiscoveryService>(),
            sp.GetRequiredService<GitOutputLog>(),
            sp.GetRequiredService<StartupOptions>(),
            post,
            settings: sp.GetRequiredService<SettingsStore>(),
            tokens: sp.GetRequiredService<GitHubTokenProvider>(),
            appUpdater: sp.GetRequiredService<IAppUpdater>()));

        return services.BuildServiceProvider();
    }

    /// <summary>Accepts the same <c>-d|--directory &lt;path&gt;</c> option as the CLI (<c>--github-token</c> is read in <see cref="BuildServices"/>).</summary>
    internal static StartupOptions ParseArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "-d" or "--directory" && i + 1 < args.Length)
                return new StartupOptions(Path.GetFullPath(args[i + 1]));

            if (args[i].StartsWith("--directory=", StringComparison.Ordinal))
                return new StartupOptions(Path.GetFullPath(args[i]["--directory=".Length..]));
        }

        return new StartupOptions(null);
    }
}
