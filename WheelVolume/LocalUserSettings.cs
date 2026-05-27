using System.Text.Json;

namespace WheelVolume;

internal sealed class LocalUserSettings
{
    private const string AppFolderName = "WheelVolume";
    private const string SettingsFileName = "settings.json";
    private const long MaxSettingsFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public bool Enabled { get; set; } = true;
    public float VolumeStep { get; set; } = 0.02f;
    public int OsdTimeoutMs { get; set; } = 700;
    public string OsdScreenMode { get; set; } = "Cursor";
    public string ModifierKey { get; set; } = "LeftAlt";

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName,
        SettingsFileName
    );

    public static LocalUserSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new LocalUserSettings();

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > MaxSettingsFileBytes)
                return new LocalUserSettings();

            var settings = JsonSerializer.Deserialize<LocalUserSettings>(
                File.ReadAllText(path),
                SerializerOptions
            );

            return settings ?? new LocalUserSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LocalUserSettings();
        }
    }

    public void Save(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(this, SerializerOptions));

                MoveTempFileIntoPlace(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void MoveTempFileIntoPlace(string tempPath, string path)
    {
        if (!File.Exists(path))
        {
            File.Move(tempPath, path);
            return;
        }

        try
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        catch (PlatformNotSupportedException)
        {
            File.Delete(path);
            File.Move(tempPath, path);
        }
    }
}
