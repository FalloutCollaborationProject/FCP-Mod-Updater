using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FCPModUpdater.Models;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;

namespace FCPModUpdater.Gui.ViewModels;

public enum FolderOrigin
{
    None,
    Argument,
    Saved,
    Detected,
    Picked
}

/// <summary>
/// Settings page. Every change is saved immediately; there is no Save button.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    public const string CreateTokenUrl = "https://github.com/settings/personal-access-tokens/new";

    private readonly MainWindowViewModel _shell;
    private readonly SettingsStore _store;
    private readonly GitHubTokenProvider _tokens;
    private readonly IGitHubApiService _gitHub;
    private readonly UpdateCheckService _updateCheck;
    private readonly Func<IReadOnlyList<string>> _discoverPaths;
    private bool _loading;

    public SettingsViewModel(MainWindowViewModel shell, SettingsStore store, GitHubTokenProvider tokens,
        IGitHubApiService gitHub, UpdateCheckService updateCheck, Func<IReadOnlyList<string>> discoverPaths)
    {
        _shell = shell;
        _store = store;
        _tokens = tokens;
        _gitHub = gitHub;
        _updateCheck = updateCheck;
        _discoverPaths = discoverPaths;

        _loading = true;
        AppSettings s = store.Current;
        _token = s.GitHubToken ?? "";
        _checkOnLaunch = s.CheckForUpdatesOnLaunch;
        _includePrereleases = s.EffectiveIncludePrereleases;
        _gameVersionText = s.GameVersionOverride ?? "";
        _updateParallelism = s.EffectiveUpdateParallelism;
        CategoryToggles = ModCategories.Resolve(s)
            .Select(c => new CategoryToggleViewModel(c, OnCategoryToggled))
            .ToList();
        _loading = false;
    }

    // ── Mod categories ───────────────────────────────────────────────────────

    /// <summary>One switch per category (GitHub org); categories can be added in the settings file.</summary>
    public IReadOnlyList<CategoryToggleViewModel> CategoryToggles { get; }

    private void OnCategoryToggled(ModCategory category, bool enabled)
    {
        if (_loading)
            return;
        Save(s => ModCategories.WithEnabled(s, category.Id, enabled));
        _gitHub.ClearCache();
        _ = _shell.RescanAsync(clearCache: true);
    }

    public string Version => _shell.VersionLabel;
    public bool IsBeta => _shell.IsBeta;
    public string FilePath => _store.FilePath;

    // ── Mods folder ──────────────────────────────────────────────────────────

    [ObservableProperty] private string _folderText = "";
    [ObservableProperty] private string? _folderStatus;
    [ObservableProperty] private bool _folderOk;
    [ObservableProperty] private string _folderSource = "";

    /// <summary>Called by the shell whenever the folder in use or the scan result changes.</summary>
    public void RefreshFolder()
    {
        FolderText = _shell.ModsDirectory ?? "";
        FolderSource = _shell.FolderOrigin switch
        {
            ViewModels.FolderOrigin.Argument => "From --directory for this run",
            ViewModels.FolderOrigin.Saved => "Saved in settings",
            ViewModels.FolderOrigin.Detected => "Detected from your RimWorld install",
            ViewModels.FolderOrigin.Picked => "Chosen by you",
            _ => ""
        };

        if (_shell.ModsDirectory is null)
        {
            FolderOk = false;
            FolderStatus = "No Mods folder set";
        }
        else if (!Directory.Exists(_shell.ModsDirectory))
        {
            FolderOk = false;
            FolderStatus = "✗ Folder not found";
        }
        else
        {
            FolderOk = true;
            FolderStatus = _shell.LastScanned is null ? "✓ Folder found" : $"✓ Found {_shell.Mods.Count} mods";
        }
    }

    /// <summary>Applies a typed path (Enter or leaving the field).</summary>
    [RelayCommand]
    public Task CommitFolderAsync()
    {
        var path = FolderText.Trim();
        if (path.Length == 0 || path == _shell.ModsDirectory)
            return Task.CompletedTask;

        if (!Directory.Exists(path))
        {
            FolderOk = false;
            FolderStatus = "✗ That folder does not exist";
            return Task.CompletedTask;
        }

        return _shell.UseFolderAsync(path, ViewModels.FolderOrigin.Picked);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await _shell.Platform.PickFolderAsync(_shell.ModsDirectory);
        if (picked is not null)
            await _shell.UseFolderAsync(picked, ViewModels.FolderOrigin.Picked);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (_shell.ModsDirectory is { } dir && Directory.Exists(dir))
            _shell.Platform.OpenFolder(dir);
    }

    [RelayCommand]
    private async Task DetectAgainAsync()
    {
        IReadOnlyList<string> found = _discoverPaths();
        if (found.Count == 0)
        {
            FolderOk = false;
            FolderStatus = "✗ RimWorld wasn't found in the usual Steam, GOG or Lutris locations";
            return;
        }

        await _shell.UseFolderAsync(found[0], ViewModels.FolderOrigin.Detected);
    }

    // ── GitHub token ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _token;
    [ObservableProperty] private bool _showToken;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _testOk;
    [ObservableProperty] private bool _isTesting;

    public char PasswordChar => ShowToken ? '\0' : '•';
    public string ShowTokenLabel => ShowToken ? "Hide token" : "Show token";

    partial void OnShowTokenChanged(bool value)
    {
        OnPropertyChanged(nameof(PasswordChar));
        OnPropertyChanged(nameof(ShowTokenLabel));
    }

    partial void OnTokenChanged(string value)
    {
        TestResult = null;
        if (_loading)
            return;
        Save(s => s with { GitHubToken = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
        RefreshTokenSource();
        _shell.RefreshRateLimit();

        // The cached repo list was fetched with the old token; a token can see private org repos.
        _gitHub.ClearCache();
        HasPendingTokenChange = true;
    }

    /// <summary>Set when the token or game version changed since the last scan; the shell rescans when it is applied.</summary>
    public bool HasPendingTokenChange { get; private set; }

    /// <summary>Rescans with the new token, at most once per change.</summary>
    public Task ApplyTokenChangeAsync()
    {
        if (!HasPendingTokenChange)
            return Task.CompletedTask;
        HasPendingTokenChange = false;
        return _shell.RescanAsync(clearCache: true);
    }

    public TokenSource ActiveSource => _tokens.ActiveSource;

    public string ActiveSourceLabel => ActiveSource switch
    {
        TokenSource.Argument => "Launch argument",
        TokenSource.Environment => "Environment variable",
        TokenSource.Settings => "Saved in Settings",
        _ => "No token"
    };

    public string ActiveSourceDescription => ActiveSource switch
    {
        TokenSource.Argument => "--github-token passed on the command line",
        TokenSource.Environment => "GITHUB_TOKEN is set for this session",
        TokenSource.Settings => "The token above",
        _ => "Requests are unauthenticated: 60 an hour"
    };

    public bool HasActiveToken => ActiveSource != TokenSource.None;

    public void RefreshTokenSource()
    {
        OnPropertyChanged(nameof(ActiveSource));
        OnPropertyChanged(nameof(ActiveSourceLabel));
        OnPropertyChanged(nameof(ActiveSourceDescription));
        OnPropertyChanged(nameof(HasActiveToken));
    }

    [RelayCommand]
    private async Task TestTokenAsync()
    {
        IsTesting = true;
        TestResult = null;
        try
        {
            var typed = string.IsNullOrWhiteSpace(Token) ? null : Token.Trim();
            RateLimitCheck check = await _gitHub.GetRateLimitAsync(typed);
            TestOk = check.Success;
            if (check.Info is { } info)
            {
                var what = typed is not null || HasActiveToken ? "Token valid" : "No token";
                TestResult = $"✓ {what} · {info.Remaining:N0} of {info.Limit:N0} requests left · resets {info.Reset.ToLocalTime():HH:mm}";
            }
            else
            {
                TestResult = $"✗ {check.Error}";
            }

            // A check with an explicit token leaves the sidebar numbers alone (that token may not be the one
            // in use). When it is the token in use, re-read the limit for it so the counter updates now.
            if (check.Success && typed is not null && typed == _tokens.Token)
                await _gitHub.GetRateLimitAsync();

            _shell.RefreshRateLimit();
            if (check.Success)
                await ApplyTokenChangeAsync();
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void ToggleShow() => ShowToken = !ShowToken;

    [RelayCommand]
    private void RemoveToken()
    {
        Token = "";
        ShowToken = false;
        _ = ApplyTokenChangeAsync();
    }

    [RelayCommand]
    private void CreateToken() => _shell.Platform.OpenUrl(CreateTokenUrl);

    // ── Game version ─────────────────────────────────────────────────────────

    [ObservableProperty] private string _gameVersionText;
    [ObservableProperty] private string _gameVersionStatus = "";

    public void RefreshGameVersion()
    {
        GameVersionStatus = _shell.GameVersion is { } v
            ? $"RimWorld {v.Raw} · {(_shell.GameVersionSource == GameVersionSource.Settings ? "set here" : "detected")}"
            : "Not detected: Version.txt wasn't found next to the Mods folder";
    }

    partial void OnGameVersionTextChanged(string value)
    {
        if (_loading)
            return;
        var parsed = GameVersion.Parse(value);
        if (value.Trim().Length > 0 && parsed is null)
        {
            GameVersionStatus = "✗ Enter a version such as 1.6";
            return;
        }

        Save(s => s with { GameVersionOverride = parsed?.Short });
        _shell.RefreshGameVersion();
        RefreshGameVersion();
        HasPendingTokenChange = true; // reuse the rescan-on-leave path: the branch checks depend on it
    }

    [RelayCommand]
    private void ClearGameVersion() => GameVersionText = "";

    // ── App updates ──────────────────────────────────────────────────────────

    [ObservableProperty] private bool _checkOnLaunch;
    [ObservableProperty] private bool _includePrereleases;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _lastCheckText = "";

    // ── Updates ──────────────────────────────────────────────────────────────

    public static double MaxParallelism => ParallelRunner.MaxParallelism;

    [ObservableProperty] private int _updateParallelism;

    partial void OnUpdateParallelismChanged(int value)
    {
        if (!_loading)
            Save(s => s with { UpdateParallelism = value });
    }

    partial void OnCheckOnLaunchChanged(bool value)
    {
        if (!_loading)
            Save(s => s with { CheckForUpdatesOnLaunch = value });
    }

    partial void OnIncludePrereleasesChanged(bool value)
    {
        if (!_loading)
            Save(s => s with { IncludePrereleases = value });
    }

    public void RefreshLastCheck()
    {
        AppSettings s = _store.Current;
        if (s.LastUpdateCheck is not { } when)
        {
            LastCheckText = "Not checked yet";
            return;
        }

        var result = s.LastUpdateCheckResult is { } v ? $"v{v} is available" : "You have the latest version";
        LastCheckText = $"Last checked {FormatAgo(when)} · {result}";
    }

    private static string FormatAgo(DateTimeOffset when) => Services.Humanize.Ago(when);

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        IsChecking = true;
        try
        {
            UpdateCheckResult? result = await _updateCheck.CheckAndRecordAsync(_store);
            _shell.Installed.AppUpdate = result;
            _shell.Installed.BannerDismissed = false;
            RefreshLastCheck();
        }
        finally
        {
            IsChecking = false;
        }
    }

    // ── Git ──────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _gitVersion = "Checking…";
    [ObservableProperty] private string? _gitPath;
    [ObservableProperty] private bool _gitOk;

    /// <summary>The saved git location as typed in the box; empty means git from PATH.</summary>
    [ObservableProperty] private string _gitPathText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGitPathError))]
    private string? _gitPathError;

    public bool HasGitPathError => GitPathError is not null;

    private bool _gitLoaded;

    public async Task OnShownAsync()
    {
        RefreshFolder();
        RefreshTokenSource();
        RefreshLastCheck();
        RefreshGameVersion();

        if (_gitLoaded)
            return;
        _gitLoaded = true;
        await RefreshGitAsync();
    }

    public async Task RefreshGitAsync()
    {
        GitPathText = _store.Current.GitPath ?? "";
        var (version, path) = await _shell.GetGitInfo();
        GitOk = version is not null;
        GitVersion = version is not null ? $"✓ {version}"
            : _store.Current.GitPath is not null ? "✗ The saved git location doesn't work"
            : "✗ Git was not found on your PATH";
        GitPath = path;
    }

    /// <summary>Applies the git location box (Enter or leaving the box).</summary>
    public async Task CommitGitPathAsync()
    {
        var typed = GitPathText.Trim();
        if (typed == (_store.Current.GitPath ?? ""))
        {
            GitPathError = null;
            return;
        }
        GitPathError = await _shell.SetGitPathAsync(typed);
    }

    [RelayCommand]
    private async Task BrowseGitAsync()
    {
        var picked = await _shell.Platform.PickGitAsync();
        if (picked is null)
            return;
        GitPathText = picked;
        await CommitGitPathAsync();
    }

    [RelayCommand]
    private async Task UsePathGitAsync()
    {
        GitPathText = "";
        await CommitGitPathAsync();
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
}

/// <summary>Green for a passing check line, red otherwise.</summary>
public static class SettingsBrushes
{
    public static readonly Avalonia.Data.Converters.IValueConverter Status =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(ok =>
            ok ? Services.Palette.Ok : Services.Palette.Error);
}
