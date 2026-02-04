namespace FCPModUpdater.Services;

public class PathDiscoveryService
{
    public IReadOnlyList<string> DiscoverModPaths()
    {
        var paths = new List<string>();
        
        if (OperatingSystem.IsLinux())
        {
            paths.AddRange(GetLinuxPaths());
        }
        else if (OperatingSystem.IsWindows())
        {
            paths.AddRange(GetWindowsPaths());
        }
        else if (OperatingSystem.IsMacOS())
        {
            paths.AddRange(GetMacPaths());
        }

        return paths.Where(Directory.Exists).Distinct().ToList();
    }

    private static List<string> GetLinuxPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return
        [
            // Steam (standard)
            Path.Combine(home, ".steam/steam/steamapps/common/RimWorld/Mods"),
            Path.Combine(home, ".local/share/Steam/steamapps/common/RimWorld/Mods"),

            // Steam (flatpak)
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.steam/steam/steamapps/common/RimWorld/Mods"),
            Path.Combine(home,
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/RimWorld/Mods"),

            // GOG
            Path.Combine(home, "Games/RimWorld/Mods"),
            Path.Combine(home, "GOG Games/RimWorld/Mods"),

            // Lutris
            Path.Combine(home, "Games/rimworld/drive_c/GOG Games/RimWorld/Mods")
        ];
    }

    private static List<string> GetWindowsPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var paths = new List<string>
        {
            // Steam default locations
            Path.Combine(programFilesX86, "Steam/steamapps/common/RimWorld/Mods"),
            Path.Combine(programFiles, "Steam/steamapps/common/RimWorld/Mods"),

            // GOG default location
            Path.Combine(programFilesX86, "GOG Galaxy/Games/RimWorld/Mods"),
            Path.Combine(programFiles, "GOG Galaxy/Games/RimWorld/Mods"),
            @"C:\GOG Games\RimWorld\Mods",
        };

        // Check common Steam library locations on other drives
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            paths.Add(Path.Combine(drive.Name, "SteamLibrary/steamapps/common/RimWorld/Mods"));
            paths.Add(Path.Combine(drive.Name, "Steam/steamapps/common/RimWorld/Mods"));
            paths.Add(Path.Combine(drive.Name, "Games/RimWorld/Mods"));
        }

        return paths;
    }

    private static List<string> GetMacPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return
        [
            // Steam
            Path.Combine(home, "Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app/Mods"),

            // GOG
            Path.Combine(home, "Applications/RimWorld.app/Mods"),
            "/Applications/RimWorld.app/Mods"
        ];
    }
}
