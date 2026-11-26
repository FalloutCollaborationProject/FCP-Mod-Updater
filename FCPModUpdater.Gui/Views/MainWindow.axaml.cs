using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Gui.ViewModels;

namespace FCPModUpdater.Gui.Views;

public partial class MainWindow : Window, IPlatformServices
{
    private readonly DispatcherTimer _relativeTimeTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel shell)
                shell.Platform = this;
        };

        _relativeTimeTimer.Tick += (_, _) => (DataContext as MainWindowViewModel)?.RefreshRelativeTimes();
        Opened += (_, _) => _relativeTimeTimer.Start();
        Closed += (_, _) => _relativeTimeTimer.Stop();
    }

    public async Task<string?> PickGitAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose git.exe",
            AllowMultiple = false,
            FileTypeFilter = OperatingSystem.IsWindows()
                ? [new FilePickerFileType("git.exe") { Patterns = ["git.exe"] }, FilePickerFileTypes.All]
                : null,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string? startIn)
    {
        IStorageFolder? start = startIn is not null && Directory.Exists(startIn)
            ? await StorageProvider.TryGetFolderFromPathAsync(startIn)
            : null;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose your RimWorld Mods folder",
            AllowMultiple = false,
            SuggestedStartLocation = start
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is not null)
            await Clipboard.SetTextAsync(text);
    }

    public void OpenFolder(string path) => Launch(path);

    public void OpenUrl(string url) => Launch(url);

    private static void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // No handler registered for folders/URLs; nothing useful to do.
        }
    }

}
