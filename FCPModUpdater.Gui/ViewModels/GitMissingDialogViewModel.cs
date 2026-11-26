using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FCPModUpdater.Gui.ViewModels;

/// <summary>Shown at startup when git can't be run: install it, or point the app at an existing git.exe.</summary>
public partial class GitMissingDialogViewModel(MainWindowViewModel shell) : ObservableObject
{
    public const string DownloadUrl = "https://git-scm.com/downloads";

    public string Description => shell.SavedGitPath is { } saved
        ? $"The saved git location doesn't work anymore:\n{saved}\nInstall Git, or choose where it is."
        : "This application uses Git to manage mods, and it wasn't found on your PATH. Install Git, or if it's already installed, choose where it is.";

    public string PathExample => OperatingSystem.IsWindows() ? @"C:\Program Files\Git\cmd\git.exe" : "/usr/bin/git";
    public string PathLabel => OperatingSystem.IsWindows()
        ? "Location of git.exe (or the Git install folder)"
        : "Location of the git executable";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UsePathCommand))]
    private string _pathText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UsePathCommand), nameof(CheckAgainCommand))]
    private bool _isBusy;

    public bool HasError => !string.IsNullOrEmpty(Error);

    private bool CanUsePath => !IsBusy && !string.IsNullOrWhiteSpace(PathText);
    private bool NotBusy => !IsBusy;

    [RelayCommand]
    private void GetGit() => shell.Platform.OpenUrl(DownloadUrl);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await shell.Platform.PickGitAsync();
        if (picked is null)
            return;
        PathText = picked;
        await UsePathAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUsePath))]
    private async Task UsePathAsync()
    {
        IsBusy = true;
        try
        {
            Error = await shell.SetGitPathAsync(PathText);
            if (Error is null)
                await shell.OnGitFoundAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>For after installing Git: looks on PATH (or the saved location) again.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task CheckAgainAsync()
    {
        IsBusy = true;
        try
        {
            if (await shell.CheckGitAsync())
                await shell.OnGitFoundAsync();
            else
                Error = "Git still wasn't found. If you just installed it, restart the app (PATH changes reach new programs only), or choose git.exe above.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
