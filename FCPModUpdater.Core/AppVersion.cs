using System.Reflection;

namespace FCPModUpdater;

public static class AppVersion
{
    private static readonly Lazy<string> InformationalVersionLazy = new(() =>
    {
        var attr = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? "0.0.0-unknown";
    });

    /// <summary>
    /// Full version string, e.g. "0.1.1" or "0.1.1-dev.4+g2d7e0c6".
    /// </summary>
    public static string InformationalVersion => InformationalVersionLazy.Value;

    /// <summary>
    /// Semantic version without +metadata suffix, for comparison.
    /// </summary>
    public static string SemanticVersion
    {
        get
        {
            var ver = InformationalVersion;
            var plusIndex = ver.IndexOf('+');
            return plusIndex >= 0 ? ver[..plusIndex] : ver;
        }
    }

    /// <summary>
    /// True if this is a dev/pre-release build.
    /// </summary>
    public static bool IsDevBuild => SemanticVersion.Contains('-');
}
