using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Gui.Services;
using FCPModUpdater.Models;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;

namespace FCPModUpdater.Gui.ViewModels;

public enum Page
{
    Installed,
    Install,
    Convert,
    Versions,
    Settings
}

public sealed record StartupOptions(string? ModsDirectory);

public partial class NavItemViewModel(Page page, string label, string iconKey, Action<Page> navigate) : ObservableObject
{
    public Page Page { get; } = page;
    public string Label { get; } = label;

    /// <summary>Resource key of the icon geometry in Styles/Tokens.axaml.</summary>
    public string IconKey { get; } = iconKey;

    [ObservableProperty] private string _count = "";
    [ObservableProperty] private bool _isActive;

    [RelayCommand]
    private void Go() => navigate(Page);
}

/// <summary>
/// The shell: owns the scanned mod list, navigation, the modal overlay and the sidebar status.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly IGitService _git;
    private readonly IGitHubApiService _gitHub;
    private readonly IModDiscoveryService _discovery;
    private readonly UpdateCheckService _updateCheck;
    private readonly PathDiscoveryService _paths;
    private readonly GitOutputLog _log;
    private readonly StartupOptions _options;
    private readonly Func<Task<bool>> _isGitInstalled;
    private readonly Func<string, Task<string?>> _probeGit;
    private readonly SettingsStore _settings;
    private readonly GitHubTokenProvider _tokens;
    private readonly Func<Task<(string? Version, string? ExecutablePath)>> _gitInfo;

    public MainWindowViewModel(
        IGitService git,
        IGitHubApiService gitHub,
        IModDiscoveryService discovery,
        UpdateCheckService updateCheck,
        PathDiscoveryService paths,
        GitOutputLog log,
        StartupOptions options,
        Action<Action> post,
        Func<Task<bool>>? isGitInstalled = null,
        SettingsStore? settings = null,
        GitHubTokenProvider? tokens = null,
        Func<Task<(string? Version, string? ExecutablePath)>>? gitInfo = null,
        Func<string, Task<string?>>? probeGit = null,
        IAppUpdater? appUpdater = null)
    {
        AppUpdater = appUpdater ?? new NullAppUpdater();
        _probeGit = probeGit ?? GitService.ProbeAsync;
        _settings = settings ?? SettingsStore.InMemory();
        _tokens = tokens ?? new GitHubTokenProvider(_settings, environment: () => null);
        _gitInfo = gitInfo ?? GitService.GetGitInfoAsync;
        _git = git;
        _gitHub = gitHub;
        _discovery = discovery;
        _updateCheck = updateCheck;
        _paths = paths;
        _log = log;
        _options = options;
        _isGitInstalled = isGitInstalled ?? GitService.IsGitInstalledAsync;
        Post = post;

        Installed = new InstalledModsViewModel(this, git, log);
        Install = new InstallViewModel(this);
        Versions = new VersionsViewModel(this, git);
        Settings = new SettingsViewModel(this, _settings, _tokens, gitHub, updateCheck, paths.DiscoverModPaths);

        NavItems =
        [
            new NavItemViewModel(Page.Installed, "Installed mods", "IconInstalled", Navigate) { IsActive = true },
            new NavItemViewModel(Page.Install, "Install new", "IconDownload", Navigate),
            new NavItemViewModel(Page.Convert, "Migrate local mods", "IconConvert", Navigate),
            new NavItemViewModel(Page.Versions, "Version selector", "IconBranch", Navigate)
        ];
        SettingsNav = new NavItemViewModel(Page.Settings, "Settings", "IconSettings", Navigate);

        _currentPage = Installed;
    }

    /// <summary>Queues work on the UI thread; shared with <see cref="GitOutputLog"/> so ordering is preserved.</summary>
    public Action<Action> Post { get; }

    public IPlatformServices Platform { get; set; } = new NullPlatformServices();

    /// <summary>Updates the app itself (Velopack); a portable copy is not installed and only gets the notice.</summary>
    public IAppUpdater AppUpdater { get; }

    /// <summary>True while a scan or an operation dialog is running, when the app must not restart.</summary>
    public bool IsBusy => IsScanning || HasDialog;

    public InstalledModsViewModel Installed { get; }
    public InstallViewModel Install { get; }
    public VersionsViewModel Versions { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>How many mods an update batch runs at once (see <see cref="ParallelRunner"/>).</summary>
    public int UpdateParallelism => _settings.Current.EffectiveUpdateParallelism;
    public IReadOnlyList<NavItemViewModel> NavItems { get; }
    public NavItemViewModel SettingsNav { get; }

    public string VersionLabel => $"v{AppVersion.SemanticVersion}";

    /// <summary>Pre-release builds (anything after "-") get a BETA tag, as the release workflow marks them.</summary>
    public bool IsBeta => AppVersion.IsDevBuild;

    /// <summary>Where <see cref="ModsDirectory"/> came from, shown on the Settings page.</summary>
    public FolderOrigin FolderOrigin { get; private set; }

    internal Task<(string? Version, string? ExecutablePath)> GetGitInfo() => _gitInfo();

    public IReadOnlyList<InstalledMod> Mods { get; private set; } = [];

    // ── Game version ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameVersionLabel))]
    [NotifyPropertyChangedFor(nameof(HasGameVersion))]
    private GameVersion? _gameVersion;

    public GameVersionSource GameVersionSource { get; private set; }
    public bool HasGameVersion => GameVersion is not null;
    public string GameVersionLabel => GameVersion is null ? "RimWorld version unknown" : $"RimWorld {GameVersion.Short}";

    /// <summary>Re-reads Version.txt and the saved override.</summary>
    public void RefreshGameVersion()
    {
        var (version, source) = GameVersionService.Resolve(ModsDirectory, _settings.Current);
        GameVersionSource = source;
        GameVersion = version;
    }
    public IReadOnlyList<RemoteRepo> OrgRepos { get; private set; } = [];

    /// <summary>The enabled mod categories (GitHub orgs) the repo list is built from.</summary>
    public IReadOnlyList<ModCategory> Categories => _gitHub.Categories is { Count: > 0 } c ? c : ModCategories.BuiltIn;

    [ObservableProperty] private object _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    private object? _dialog;

    public bool HasDialog => Dialog is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModsDirectory))]
    [NotifyPropertyChangedFor(nameof(ShowChooseFolder))]
    [NotifyPropertyChangedFor(nameof(ShowPage))]
    private string? _modsDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChooseFolder))]
    [NotifyPropertyChangedFor(nameof(ShowPage))]
    private bool _gitMissing;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _scanError;

    public bool HasModsDirectory => ModsDirectory is not null;
    public bool IsOnSettings => CurrentPage == Settings;
    public bool ShowPage => !GitMissing && (HasModsDirectory || IsOnSettings);
    public bool ShowChooseFolder => !GitMissing && ModsDirectory is null && !IsOnSettings;

    partial void OnCurrentPageChanged(object value)
    {
        OnPropertyChanged(nameof(IsOnSettings));
        OnPropertyChanged(nameof(ShowPage));
        OnPropertyChanged(nameof(ShowChooseFolder));
    }
    public DateTimeOffset? LastScanned { get; private set; }

    // ── Sidebar: GitHub rate limit ───────────────────────────────────────────

    [ObservableProperty] private string _rateLimitText = "—";
    [ObservableProperty] private double _rateLimitPercent;
    [ObservableProperty] private string _rateLimitHint = "";
    [ObservableProperty] private IBrush _rateLimitBrush = Palette.TextFaint;
    [ObservableProperty] private bool _showAddToken;

    public void RefreshRateLimit()
    {
        var hasToken = _tokens.ActiveSource != TokenSource.None;
        ShowAddToken = !hasToken;

        if (_gitHub.RemainingRateLimit is not { } remaining)
        {
            RateLimitText = "—";
            RateLimitPercent = 0;
            RateLimitHint = "";
            return;
        }

        var total = _gitHub.RateLimitTotal ?? (hasToken ? 5000 : 60);
        RateLimitText = $"{remaining:N0} / {total:N0}";
        RateLimitPercent = total == 0 ? 0 : 100.0 * remaining / total;
        RateLimitBrush = remaining == 0 ? Palette.Error : remaining * 5 < total ? Palette.Warn : Palette.TextFaint;
        RateLimitHint = _gitHub.RateLimitReset is { } reset ? $"Resets {reset.ToLocalTime():HH:mm}" : "";
    }

    [RelayCommand]
    private void GoToSettings() => Navigate(Page.Settings);

    [RelayCommand]
    private void OpenModsFolder()
    {
        if (ModsDirectory is { } dir && Directory.Exists(dir))
            Platform.OpenFolder(dir);
    }

    // ── Startup and scanning ─────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        if (!await CheckGitAsync())
        {
            GitMissing = true;
            Dialog = new GitMissingDialogViewModel(this);
            return;
        }

        await ContinueStartupAsync();
    }

    // ── Git location ─────────────────────────────────────────────────────────

    /// <summary>The git path saved in settings, or null when git comes from PATH.</summary>
    public string? SavedGitPath => _settings.Current.GitPath;

    /// <summary>Where RimWorld was looked for; Lutris only exists on Linux.</summary>
    public static string SearchedLocations => OperatingSystem.IsWindows() ? "Steam or GOG" : "Steam, GOG or Lutris";

    public static string NoModsFolderText =>
        $"RimWorld wasn't found in the usual {SearchedLocations} locations. Pick the Mods folder to continue, or start the app with --directory to skip this step.";

    /// <summary>Applies the saved git location, then checks that git runs.</summary>
    internal Task<bool> CheckGitAsync()
    {
        GitService.UseExecutable(_settings.Current.GitPath);
        return _isGitInstalled();
    }

    /// <summary>
    /// Saves <paramref name="input"/> (a git executable or its install folder; empty = git from PATH) once it
    /// checks out, and switches to it. Returns an error message, or null on success.
    /// </summary>
    internal async Task<string?> SetGitPathAsync(string? input)
    {
        string? path = null;
        if (!string.IsNullOrWhiteSpace(input))
        {
            path = GitService.NormalizeGitPath(input);
            if (path is null)
                return $"No git executable found at {input.Trim()}";
            if (await _probeGit(path) is null)
                return $"{path} isn't a working git executable.";
        }

        try
        {
            _settings.Update(s => s with { GitPath = path });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not save settings: {ex.Message}";
        }

        GitService.UseExecutable(path);
        _ = Settings.RefreshGitAsync();
        return null;
    }

    /// <summary>Called by the git popup once git works: closes it and finishes starting up.</summary>
    internal async Task OnGitFoundAsync()
    {
        GitMissing = false;
        Dialog = null;
        await ContinueStartupAsync();
    }

    private async Task ContinueStartupAsync()
    {
        if (_settings.Current.CheckForUpdatesOnLaunch)
            _ = CheckForAppUpdateAsync();

        RefreshRateLimit();
        (ModsDirectory, FolderOrigin) = ResolveFolder();
        Settings.RefreshFolder();
        if (ModsDirectory is not null)
            await RescanAsync();
    }

    /// <summary>--directory, then the saved folder (if it still exists), then auto-discovery.</summary>
    private (string?, FolderOrigin) ResolveFolder()
    {
        if (_options.ModsDirectory is { } arg)
            return (arg, FolderOrigin.Argument);
        if (_settings.Current.ModsDirectory is { } saved && Directory.Exists(saved))
            return (saved, FolderOrigin.Saved);
        return _paths.DiscoverModPaths().FirstOrDefault() is { } found
            ? (found, FolderOrigin.Detected)
            : (null, FolderOrigin.None);
    }

    /// <summary>Switches to another Mods folder, remembers it in settings and rescans.</summary>
    public async Task UseFolderAsync(string path, FolderOrigin origin)
    {
        var full = Path.GetFullPath(path);
        try
        {
            _settings.Update(s => s with { ModsDirectory = full });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ScanError = $"Could not save settings: {ex.Message}";
        }

        FolderOrigin = origin;
        if (full == ModsDirectory)
        {
            Settings.RefreshFolder();
            return;
        }

        ModsDirectory = full;
        LastScanned = null;
        Settings.RefreshFolder();
        await RescanAsync();
    }

    private async Task CheckForAppUpdateAsync()
    {
        Installed.AppUpdate = await _updateCheck.CheckAndRecordAsync(_settings);
        await Installed.PrepareInAppUpdateAsync(_settings.Current.EffectiveIncludePrereleases);
    }

    public async Task RescanAsync(bool clearCache = false)
    {
        if (IsScanning || ModsDirectory is not { } directory)
            return;

        IsScanning = true;
        ScanError = null;
        Installed.RefreshSummary();

        try
        {
            if (!Directory.Exists(directory))
            {
                ScanError = $"Folder not found: {directory}";
                Mods = [];
                return;
            }

            if (clearCache)
                _gitHub.ClearCache();

            RefreshGameVersion();
            _log.BeginScan(directory);
            var watch = Stopwatch.StartNew();

            // Discovery walks the folder synchronously before its first await; keep that off the UI thread.
            Mods = await Task.Run(() => _discovery.DiscoverModsAsync(directory));
            OrgRepos = await _gitHub.GetOrganizationReposAsync();

            await _log.FlushAsync();
            _log.EndScan(Mods, watch.Elapsed, GameVersion?.Short);
            LastScanned = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            ScanError = ex.Message;
        }
        finally
        {
            IsScanning = false;
            NotifyModsChanged();
        }
    }

    private void NotifyModsChanged()
    {
        Installed.OnModsChanged();
        Install.OnModsChanged();
        Versions.OnModsChanged();

        NavItems[0].Count = Mods.Count.ToString();
        NavItems[1].Count = ModCatalog.GetAvailableInstallRepos(OrgRepos, Mods.Select(m => m.Name)).Count.ToString();
        NavItems[2].Count = ModCatalog.GetConvertibleMods(Mods).Count.ToString();

        RefreshRateLimit();
        Settings.RefreshFolder();
        Settings.RefreshGameVersion();
    }

    /// <summary>Re-renders "2 minutes ago" style text; driven by a timer in the window.</summary>
    public void RefreshRelativeTimes() => Installed.RefreshSummary();

    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        var picked = await Platform.PickFolderAsync(ModsDirectory);
        if (picked is not null)
            await UseFolderAsync(picked, FolderOrigin.Picked);
    }

    // ── Navigation and overlays ──────────────────────────────────────────────

    public void Navigate(Page page)
    {
        // Leaving Settings after changing the token: rescan so the repo list reflects it.
        if (page != Page.Settings && CurrentPage == Settings)
            _ = Settings.ApplyTokenChangeAsync();

        switch (page)
        {
            case Page.Install:
                Install.IsConvert = false;
                CurrentPage = Install;
                break;
            case Page.Convert:
                Install.IsConvert = true;
                CurrentPage = Install;
                break;
            case Page.Versions:
                CurrentPage = Versions;
                break;
            case Page.Settings:
                CurrentPage = Settings;
                _ = Settings.OnShownAsync();
                break;
            default:
                CurrentPage = Installed;
                break;
        }

        Versions.IsActive = CurrentPage == Versions;
        SyncNav();
    }

    private void SyncNav()
    {
        Page active = CurrentPage == Installed ? Page.Installed
            : CurrentPage == Versions ? Page.Versions
            : CurrentPage == Settings ? Page.Settings
            : Install.IsConvert ? Page.Convert
            : Page.Install;

        foreach (NavItemViewModel item in NavItems)
            item.IsActive = item.Page == active;
        SettingsNav.IsActive = active == Page.Settings;
    }

    /// <summary>Keeps the sidebar in step when the Install page flips between its two tabs.</summary>
    public void OnInstallTabChanged() => SyncNav();

    public void OpenVersions(string modPath)
    {
        // Select first: activating the page starts a fetch for whichever mod is selected.
        Versions.Select(modPath);
        Navigate(Page.Versions);
    }

    public void ShowUpdateDialog(IReadOnlyList<InstalledMod> mods)
    {
        if (mods.Count == 0)
            return;

        var dialog = new OperationDialogViewModel(this, _git, _gitHub, _log, OperationKind.Update,
            mods.Select(m => new OperationItemViewModel(m.Name, m.Path) { Mod = m }));
        Dialog = dialog;
        _ = dialog.LoadReviewAsync();
    }

    public void ShowInstallDialog(IReadOnlyList<RemoteRepo> repos)
    {
        if (repos.Count == 0 || ModsDirectory is null)
            return;

        var dialog = new OperationDialogViewModel(this, _git, _gitHub, _log, OperationKind.Install,
            repos.Select(r => new OperationItemViewModel(r.Name, Path.Combine(ModsDirectory, r.Name)) { Repo = r }));
        Dialog = dialog;
        _ = dialog.StartAsync();
    }

    public void ShowConvertDialog(IReadOnlyList<InstalledMod> mods)
    {
        if (mods.Count == 0)
            return;

        var dialog = new OperationDialogViewModel(this, _git, _gitHub, _log, OperationKind.Convert,
            mods.Select(m => new OperationItemViewModel(m.Name, m.Path) { Mod = m }));
        Dialog = dialog;
        _ = dialog.StartAsync();
    }

    public void ShowSwitchBranchDialog(IReadOnlyList<InstalledMod> mods)
    {
        mods = mods.Where(m => m.VersionBranch is not null).ToList();
        if (mods.Count == 0)
            return;

        var dialog = new OperationDialogViewModel(this, _git, _gitHub, _log, OperationKind.SwitchBranch,
            mods.Select(m => new OperationItemViewModel(m.Name, m.Path) { Mod = m }));
        Dialog = dialog;
        _ = dialog.StartAsync();
    }

    public void ShowUninstallDialog(IReadOnlyList<InstalledMod> mods)
    {
        if (mods.Count > 0)
            Dialog = new UninstallDialogViewModel(this, mods);
    }

    public void CloseDialog(bool rescan)
    {
        Dialog = null;
        if (rescan)
            _ = RescanAsync();
    }
}
