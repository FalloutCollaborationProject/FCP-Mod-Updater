using System.Text.RegularExpressions;
using FCPModUpdater.Settings;

namespace FCPModUpdater.Services;

public enum GameVersionSource
{
    None,
    Detected,
    Settings
}

/// <summary>A RimWorld version such as "1.6.4630 rev467"; <see cref="Short"/> is "1.6".</summary>
public sealed record GameVersion(int Major, int Minor, int? Build, string Raw)
{
    public string Short => $"{Major}.{Minor}";

    public static GameVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        Match m = Regex.Match(text.Trim(), @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success)
            return null;
        int? build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : null;
        return new GameVersion(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), build, text.Trim());
    }
}

/// <summary>Finds the RimWorld version the Mods folder belongs to.</summary>
public static class GameVersionService
{
    /// <summary>Reads Version.txt from the install folder above <paramref name="modsDirectory"/>.</summary>
    public static GameVersion? Detect(string? modsDirectory)
    {
        if (string.IsNullOrEmpty(modsDirectory))
            return null;
        try
        {
            var install = Path.GetDirectoryName(Path.GetFullPath(modsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (install is null)
                return null;
            var file = Path.Combine(install, "Version.txt");
            return File.Exists(file) ? GameVersion.Parse(File.ReadLines(file).FirstOrDefault()) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The saved override wins over detection.</summary>
    public static (GameVersion? Version, GameVersionSource Source) Resolve(string? modsDirectory, AppSettings? settings)
    {
        if (GameVersion.Parse(settings?.GameVersionOverride) is { } saved)
            return (saved, GameVersionSource.Settings);
        return Detect(modsDirectory) is { } found ? (found, GameVersionSource.Detected) : (null, GameVersionSource.None);
    }
}
