using FCPModUpdater.Models;

namespace FCPModUpdater.Services;

public interface IModDiscoveryService
{
    Task<IReadOnlyList<InstalledMod>> DiscoverModsAsync(
        string modsDirectory,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
