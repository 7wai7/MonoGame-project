using System.Text.Json;

namespace MonoGame_project.Configuration;

/// <summary>Loads and atomically saves one local settings file per OS user.</summary>
public sealed class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public UserSettingsStore()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        _path = Path.Combine(localData, "MonoGame-project", "settings.json");
        Current = Load();
    }

    public UserSettings Current { get; private set; }
    public string FilePath => _path;

    public void Save(UserSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var valid = UserSettings.Sanitize(value);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var tempPath = _path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(valid, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
            Current = valid;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return UserSettings.Default;
            }

            var saved = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path));
            return saved is null ? UserSettings.Default : UserSettings.Sanitize(saved);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"[settings] Could not load {_path}: {error.Message}");
            return UserSettings.Default;
        }
    }
}
