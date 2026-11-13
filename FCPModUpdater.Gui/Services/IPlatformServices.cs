namespace FCPModUpdater.Gui.Services;

/// <summary>
/// Things only the windowing layer can do; implemented by the main window.
/// </summary>
public interface IPlatformServices
{
    Task<string?> PickFolderAsync(string? startIn);
    Task CopyTextAsync(string text);
    void OpenFolder(string path);
    void OpenUrl(string url);
}

/// <summary>Stand-in used until a window is attached (and by tests).</summary>
public sealed class NullPlatformServices : IPlatformServices
{
    public Task<string?> PickFolderAsync(string? startIn) => Task.FromResult<string?>(null);
    public Task CopyTextAsync(string text) => Task.CompletedTask;
    public void OpenFolder(string path) { }
    public void OpenUrl(string url) { }
}
