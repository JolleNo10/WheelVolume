using WheelVolume;

var tests = new (string Name, Action Run)[]
{
    ("Wheel delta accumulates partial positive deltas", WheelDeltaAccumulatesPositivePartials),
    ("Wheel delta accumulates partial negative deltas", WheelDeltaAccumulatesNegativePartials),
    ("Wheel delta preserves remainder after whole steps", WheelDeltaPreservesRemainder),
    ("Wheel delta reset clears remainder", WheelDeltaResetClearsRemainder),
    ("Startup registry value quotes executable path", StartupRegistryValueQuotesPath),
    ("Local user settings round trip to disk", LocalUserSettingsRoundTripToDisk),
    ("Local user settings ignore corrupt file", LocalUserSettingsIgnoreCorruptFile),
    ("Local user settings ignore oversized file", LocalUserSettingsIgnoreOversizedFile),
    ("Local user settings save cleans up temp files", LocalUserSettingsSaveCleansUpTempFiles),
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"{tests.Length} test(s) passed.");
return 0;

static void WheelDeltaAccumulatesPositivePartials()
{
    var accumulator = new WheelDeltaAccumulator();

    AssertEqual(0, accumulator.AddDelta(60));
    AssertEqual(1, accumulator.AddDelta(60));
}

static void WheelDeltaAccumulatesNegativePartials()
{
    var accumulator = new WheelDeltaAccumulator();

    AssertEqual(0, accumulator.AddDelta(-60));
    AssertEqual(-1, accumulator.AddDelta(-60));
}

static void WheelDeltaPreservesRemainder()
{
    var accumulator = new WheelDeltaAccumulator();

    AssertEqual(1, accumulator.AddDelta(180));
    AssertEqual(1, accumulator.AddDelta(60));
}

static void WheelDeltaResetClearsRemainder()
{
    var accumulator = new WheelDeltaAccumulator();

    AssertEqual(0, accumulator.AddDelta(60));
    accumulator.Reset();
    AssertEqual(0, accumulator.AddDelta(60));
}

static void StartupRegistryValueQuotesPath()
{
    AssertEqual(
        "\"C:\\Tools\\WheelVolume\\WheelVolume.exe\"",
        StartupRegistration.GetRegistryValue("C:\\Tools\\WheelVolume\\WheelVolume.exe")
    );
}

static void LocalUserSettingsRoundTripToDisk()
{
    string path = GetTempSettingsPath();

    try
    {
        var settings = new LocalUserSettings
        {
            Enabled = false,
            VolumeStep = 0.05f,
            OsdTimeoutMs = 2000,
            OsdScreenMode = "Primary",
            ModifierKey = "Ctrl"
        };

        settings.Save(path);
        var loaded = LocalUserSettings.Load(path);

        AssertEqual(false, loaded.Enabled);
        AssertEqual(0.05f, loaded.VolumeStep);
        AssertEqual(2000, loaded.OsdTimeoutMs);
        AssertEqual("Primary", loaded.OsdScreenMode);
        AssertEqual("Ctrl", loaded.ModifierKey);
    }
    finally
    {
        DeleteTempSettings(path);
    }
}

static void LocalUserSettingsIgnoreCorruptFile()
{
    string path = GetTempSettingsPath();

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var loaded = LocalUserSettings.Load(path);

        AssertDefaultSettings(loaded);
    }
    finally
    {
        DeleteTempSettings(path);
    }
}

static void LocalUserSettingsIgnoreOversizedFile()
{
    string path = GetTempSettingsPath();

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', 64 * 1024 + 1));

        var loaded = LocalUserSettings.Load(path);

        AssertDefaultSettings(loaded);
    }
    finally
    {
        DeleteTempSettings(path);
    }
}

static void LocalUserSettingsSaveCleansUpTempFiles()
{
    string path = GetTempSettingsPath();

    try
    {
        var settings = new LocalUserSettings
        {
            VolumeStep = 0.05f
        };

        settings.Save(path);

        string directory = Path.GetDirectoryName(path)!;
        AssertEqual(0, Directory.GetFiles(directory, "*.tmp").Length);
    }
    finally
    {
        DeleteTempSettings(path);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void AssertDefaultSettings(LocalUserSettings loaded)
{
    AssertEqual(true, loaded.Enabled);
    AssertEqual(0.02f, loaded.VolumeStep);
    AssertEqual(700, loaded.OsdTimeoutMs);
    AssertEqual("Cursor", loaded.OsdScreenMode);
    AssertEqual("LeftAlt", loaded.ModifierKey);
}

static string GetTempSettingsPath()
{
    return Path.Combine(
        Path.GetTempPath(),
        "WheelVolume.Tests",
        Guid.NewGuid().ToString("N"),
        "settings.json"
    );
}

static void DeleteTempSettings(string path)
{
    string? directory = Path.GetDirectoryName(path);

    if (directory != null && Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
}
