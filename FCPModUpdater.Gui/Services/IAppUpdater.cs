namespace FCPModUpdater.Gui.Services;

/// <summary>
/// Updates the app itself. Backed by Velopack when the app was installed with Setup.exe or the Velopack AppImage;
/// a portable (zip/tar.gz) copy is not <see cref="IsInstalled"/> and keeps the updater-script notice.
/// </summary>
public interface IAppUpdater
{
    /// <summary>True when Velopack installed this copy, so it can update itself.</summary>
    bool IsInstalled { get; }

    /// <summary>Looks for a newer release Velopack can apply. Returns its version, or null when there is none or the check failed.</summary>
    Task<string?> PrepareAsync(bool includePrereleases, CancellationToken ct = default);

    /// <summary>Downloads the release found by <see cref="PrepareAsync"/>, reporting 0–100. Throws on failure.</summary>
    Task DownloadAsync(Action<int> progress, CancellationToken ct = default);

    /// <summary>Closes the app, applies the downloaded release and starts the new version.</summary>
    void ApplyAndRestart();

    /// <summary>Downloads the Windows installer of release <paramref name="tag"/> to a temp file and starts it. Throws on failure.</summary>
    Task RunInstallerAsync(string tag, CancellationToken ct = default);
}

/// <summary>Used when no updater is supplied (tests, design time): behaves like a portable copy.</summary>
internal sealed class NullAppUpdater : IAppUpdater
{
    public bool IsInstalled => false;
    public Task<string?> PrepareAsync(bool includePrereleases, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task DownloadAsync(Action<int> progress, CancellationToken ct = default) => Task.CompletedTask;
    public void ApplyAndRestart() { }
    public Task RunInstallerAsync(string tag, CancellationToken ct = default) => Task.CompletedTask;
}
