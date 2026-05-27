using WheelVolume;

var tests = new (string Name, Action Run)[]
{
    ("Wheel delta accumulates partial positive deltas", WheelDeltaAccumulatesPositivePartials),
    ("Wheel delta accumulates partial negative deltas", WheelDeltaAccumulatesNegativePartials),
    ("Wheel delta preserves remainder after whole steps", WheelDeltaPreservesRemainder),
    ("Wheel delta reset clears remainder", WheelDeltaResetClearsRemainder),
    ("Startup registry value quotes executable path", StartupRegistryValueQuotesPath),
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

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
