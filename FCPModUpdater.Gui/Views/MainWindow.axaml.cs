using System.Diagnostics;
using Avalonia;
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
        SizeToScreen();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel shell)
                shell.Platform = this;
        };

        _relativeTimeTimer.Tick += (_, _) => (DataContext as MainWindowViewModel)?.RefreshRelativeTimes();
        Opened += (_, _) => _relativeTimeTimer.Start();
        Closed += (_, _) => _relativeTimeTimer.Stop();
    }

    /// <summary>
    /// Opens bigger on bigger screens: 70% × 80% of the working area, between 1280×800 and 1800×1150,
    /// never larger than the screen. Runs before the window is shown and centres it on the primary monitor.
    /// </summary>
    private void SizeToScreen()
    {
        if (Screens.Primary is not { } screen)
            return;

        var areaWidth = screen.WorkingArea.Width / screen.Scaling;
        var areaHeight = screen.WorkingArea.Height / screen.Scaling;

        Width = Math.Min(Math.Clamp(areaWidth * 0.70, 1280, 1800), areaWidth);
        Height = Math.Min(Math.Clamp(areaHeight * 0.80, 800, 1150), areaHeight);

        // Centre on the primary monitor ourselves: CenterScreen without an owner picks the screen at
        // (0,0), which is a monitor left of the primary one in multi-monitor setups.
        var area = screen.WorkingArea;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(
            area.X + (int)((area.Width - Width * screen.Scaling) / 2),
            area.Y + (int)((area.Height - Height * screen.Scaling) / 2));
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
