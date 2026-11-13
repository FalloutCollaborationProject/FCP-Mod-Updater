using System.Text.Json;
using System.Text.Json.Serialization;

namespace FCPModUpdater.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the platform config folder.
/// A missing or unreadable file gives the defaults; saving is atomic.
/// </summary>
public class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _lock = new();
    private AppSettings? _current;

    private readonly bool _persist;

    public SettingsStore(string? filePath = null) : this(filePath ?? DefaultFilePath(), persist: true)
    {
    }

    private SettingsStore(string filePath, bool persist)
    {
        FilePath = filePath;
        _persist = persist;
    }

    /// <summary>A store that never reads or writes a file, for tests and previews.</summary>
    public static SettingsStore InMemory(AppSettings? initial = null)
    {
        var store = new SettingsStore("(in memory)", persist: false);
        store._current = initial ?? new AppSettings();
        return store;
    }

    public string FilePath { get; }

    /// <summary>Raised after <see cref="Save"/> or <see cref="Update"/> stores new values.</summary>
    public event Action<AppSettings>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_lock)
                return _current ??= Load();
        }
    }

    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings next;
        lock (_lock)
        {
            next = change(_current ??= Load());
            Write(next);
            _current = next;
        }

        Changed?.Invoke(next);
        return next;
    }

    public void Save(AppSettings settings) => Update(_ => settings);

    private AppSettings Load()
    {
        if (!_persist)
            return new AppSettings();
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    private void Write(AppSettings settings)
    {
        if (!_persist)
            return;

        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        var temp = FilePath + ".tmp";
        // Create the temp file owner-only before writing the token into it.
        using (var stream = OperatingSystem.IsWindows()
                   ? new FileStream(temp, FileMode.Create, FileAccess.Write)
                   : new FileStream(temp, new FileStreamOptions
                   {
                       Mode = FileMode.Create,
                       Access = FileAccess.Write,
                       UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                   }))
        {
            JsonSerializer.Serialize(stream, settings, JsonOptions);
        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(temp, FilePath, overwrite: true);
    }

    public static string DefaultFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "FCPModManager", "settings.json");
        }

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(configHome, "fcp-mod-manager", "settings.json");
    }
}
