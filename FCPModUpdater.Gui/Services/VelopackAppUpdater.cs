using System.Diagnostics;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;
using Velopack;
using Velopack.Sources;

namespace FCPModUpdater.Gui.Services;

/// <summary>Velopack-backed <see cref="IAppUpdater"/> reading the GitHub releases of FCP-Mod-Updater.</summary>
public sealed class VelopackAppUpdater(GitHubTokenProvider tokens, HttpClient http) : IAppUpdater
{
    /// <summary>
    /// Velopack channel per OS, matching <c>vpk pack --channel</c> in the release workflow. Explicit (not vpk's
    /// default win/linux) so the two platforms' <c>-full.nupkg</c> files get distinct names in one GitHub release.
    /// </summary>
    public static string Channel => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    /// <summary>Release asset name of the Windows installer (vpk: <c>&lt;packId&gt;-&lt;channel&gt;-Setup.exe</c>).</summary>
    public const string WindowsInstallerAsset = "CollabProjectUpdater-win-x64-Setup.exe";

    private static UpdateManager CreateManager(string? token, bool prerelease) =>
        new(new GithubSource(UpdateCheckService.RepoUrl, token, prerelease),
            new UpdateOptions { ExplicitChannel = Channel });

    private UpdateManager? _manager;
    private UpdateInfo? _update;

    private readonly Lazy<bool> _isInstalled = new(() =>
    {
        try
        {
            return CreateManager(null, false).IsInstalled;
        }
        catch
        {
            return false;
        }
    });

    public bool IsInstalled => _isInstalled.Value;

    public async Task<string?> PrepareAsync(bool includePrereleases, CancellationToken ct = default)
    {
        try
        {
            var manager = CreateManager(tokens.Token, includePrereleases);
            if (!manager.IsInstalled)
                return null;

            UpdateInfo? update = await manager.CheckForUpdatesAsync();
            _manager = manager;
            _update = update;
            return update?.TargetFullRelease.Version.ToString();
        }
        catch
        {
            // Like the release check, this is best effort; the banner then falls back to the release page.
            _manager = null;
            _update = null;
            return null;
        }
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken ct = default)
    {
        if (_manager is null || _update is null)
            throw new InvalidOperationException("No update to download.");
        return _manager.DownloadUpdatesAsync(_update, progress, ct);
    }

    public void ApplyAndRestart()
    {
        if (_manager is null || _update is null)
            throw new InvalidOperationException("No update to apply.");
        _manager.ApplyUpdatesAndRestart(_update.TargetFullRelease);
    }

    public async Task RunInstallerAsync(string tag, CancellationToken ct = default)
    {
        var url = UpdateCheckService.ReleaseAssetUrl(tag, WindowsInstallerAsset);
        var dir = Path.Combine(Path.GetTempPath(), "CollabProjectUpdater-setup");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, WindowsInstallerAsset);

        using (HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using FileStream output = File.Create(file);
            await response.Content.CopyToAsync(output, ct);
        }

        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }
}
