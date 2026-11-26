using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;

namespace FCPModUpdater.Gui.ViewModels;

/// <summary>
/// Updates page and the update banner on the Installed page: app version, release check, in-app update
/// (Velopack) or, for a portable copy, the updater script and "Get the installer".
/// </summary>
public partial class UpdatesViewModel : ObservableObject
{
    private readonly MainWindowViewModel _shell;
    private readonly SettingsStore _store;
    private readonly UpdateCheckService _updateCheck;
    private readonly bool _loading;

    public UpdatesViewModel(MainWindowViewModel shell, SettingsStore store, UpdateCheckService updateCheck)
    {
        _shell = shell;
        _store = store;
        _updateCheck = updateCheck;

        _loading = true;
        _checkOnLaunch = store.Current.CheckForUpdatesOnLaunch;
        _includePrereleases = store.Current.EffectiveIncludePrereleases;
        _loading = false;
        RefreshLastCheck();
    }

    private IAppUpdater Updater => _shell.AppUpdater;
    private bool IsInstalledApp => Updater.IsInstalled;

    public string CurrentVersion => AppVersion.InformationalVersion;
    public bool IsBeta => _shell.IsBeta;

    public string InstallKind =>
        IsInstalledApp ? "Installed: updates itself"
        : UpdateCheckService.IsAppImage ? "Older AppImage: download the new one once to get automatic updates"
        : "Portable folder: Get the installer (or run the updater script) to switch to the version that updates itself";

    // ── Release check ────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBanner), nameof(HasUpdate), nameof(StatusTitle), nameof(BannerTitle),
        nameof(BannerCurrent), nameof(ReleaseNotes), nameof(HasReleaseNotes), nameof(ShowGetInstaller),
        nameof(HasBannerCommand), nameof(ShowAppImageHint))]
    private UpdateCheckResult? _appUpdate;

    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _lastCheckText = "";

    public bool HasUpdate => AppUpdate is not null;

    public string StatusTitle => AppUpdate is { } u
        ? $"{(u.IsPrerelease ? "Pre-release available" : "Update available")}: v{u.LatestVersion}"
        : _store.Current.LastUpdateCheck is null ? "Not checked yet" : "You have the latest version";

    public string? ReleaseNotes => string.IsNullOrWhiteSpace(AppUpdate?.ReleaseNotes) ? null : AppUpdate.ReleaseNotes.Trim();
    public bool HasReleaseNotes => ReleaseNotes is not null;

    partial void OnAppUpdateChanged(UpdateCheckResult? value)
    {
        _shell.UpdatesNav.Count = value is null ? "" : "1";
        if (value is null)
            CanUpdateInApp = false;
    }

    /// <summary>Checks GitHub (recording the check), then asks Velopack whether it can apply the result.</summary>
    public async Task CheckAsync()
    {
        IsChecking = true;
        try
        {
            AppUpdate = await _updateCheck.CheckAndRecordAsync(_store);
            BannerDismissed = false;
            await PrepareInAppUpdateAsync(_store.Current.EffectiveIncludePrereleases);
            RefreshLastCheck();
            OnPropertyChanged(nameof(StatusTitle));
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private Task CheckNowAsync() => CheckAsync();

    public void RefreshLastCheck()
    {
        AppSettings s = _store.Current;
        if (s.LastUpdateCheck is not { } when)
        {
            LastCheckText = "Not checked yet";
            return;
        }

        var result = s.LastUpdateCheckResult is { } v ? $"v{v} is available" : "You have the latest version";
        LastCheckText = $"Last checked {Humanize.Ago(when)} · {result}";
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _checkOnLaunch;
    [ObservableProperty] private bool _includePrereleases;

    partial void OnCheckOnLaunchChanged(bool value)
    {
        if (!_loading)
            Save(s => s with { CheckForUpdatesOnLaunch = value });
    }

    partial void OnIncludePrereleasesChanged(bool value)
    {
        if (_loading)
            return;
        Save(s => s with { IncludePrereleases = value });
        // Which release counts as newest depends on this.
        _ = CheckAsync();
    }

    private void Save(Func<AppSettings, AppSettings> change)
    {
        try
        {
            _store.Update(change);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shell.ScanError = $"Could not save settings: {ex.Message}";
        }
    }

    // ── Banner ───────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBanner))]
    private bool _bannerDismissed;

    public bool ShowBanner => AppUpdate is not null && !BannerDismissed;

    public string BannerTitle => StatusTitle;

    public string BannerCurrent => AppUpdate is null ? "" : $"(current {AppUpdate.CurrentVersion})";

    [RelayCommand]
    private void DismissBanner() => BannerDismissed = true;

    [RelayCommand]
    private void ShowDetails() => _shell.Navigate(Page.Updates);

    // ── Updating ─────────────────────────────────────────────────────────────

    /// <summary>The updater script to run; only for a portable copy (an installed app updates itself).</summary>
    public string BannerCommand => IsInstalledApp ? "" : UpdateCheckService.GetUpdaterCommand() ?? "";

    public bool HasBannerCommand => BannerCommand.Length > 0 && !CanUpdateInApp;

    /// <summary>The old (non-Velopack) AppImage is replaced by downloading the new one once.</summary>
    public bool ShowAppImageHint => UpdateCheckService.IsAppImage && !IsInstalledApp;

    /// <summary>A portable copy can switch to the self-updating install (Setup.exe on Windows, the new AppImage on Linux).</summary>
    public bool ShowGetInstaller => HasUpdate && !IsInstalledApp && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux());

    /// <summary>True when Velopack found an update it can download and apply.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBannerCommand), nameof(ShowUpdateNow))]
    private bool _canUpdateInApp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdateNow), nameof(UpdateProgressLabel))]
    private bool _isUpdating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateProgressLabel))]
    private int _updateProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateError))]
    private string? _updateError;

    public bool HasUpdateError => UpdateError is not null;

    public bool ShowUpdateNow => CanUpdateInApp && !IsUpdating;

    public string UpdateProgressLabel => $"Downloading… {UpdateProgress}%";

    /// <summary>Asks the installed app's updater whether it can apply the newest release itself.</summary>
    internal async Task PrepareInAppUpdateAsync(bool includePrereleases)
    {
        if (!IsInstalledApp || AppUpdate is null || IsUpdating)
        {
            CanUpdateInApp = false;
            return;
        }

        CanUpdateInApp = await Updater.PrepareAsync(includePrereleases) is not null;
    }

    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        if (!CanUpdateInApp || IsUpdating)
            return;
        if (_shell.IsBusy)
        {
            UpdateError = "Wait for the current scan or operation to finish, then update.";
            return;
        }

        UpdateError = null;
        UpdateProgress = 0;
        IsUpdating = true;
        try
        {
            await Updater.DownloadAsync(p => _shell.Post(() => UpdateProgress = p));
            Updater.ApplyAndRestart();
        }
        catch (Exception ex)
        {
            UpdateError = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task GetInstallerAsync()
    {
        if (AppUpdate is null)
            return;
        if (!OperatingSystem.IsWindows())
        {
            _shell.Platform.OpenUrl(AppUpdate.ReleaseUrl);
            return;
        }

        UpdateError = null;
        IsUpdating = true;
        try
        {
            await Updater.RunInstallerAsync(AppUpdate.LatestVersion);
        }
        catch (Exception ex)
        {
            UpdateError = $"Could not download the installer: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (AppUpdate is not null)
            _shell.Platform.OpenUrl(AppUpdate.ReleaseUrl);
    }
}
