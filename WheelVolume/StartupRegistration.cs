using System.Security;
using Microsoft.Win32;

namespace WheelVolume;

internal static class StartupRegistration
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "WheelVolume";

    public static bool IsEnabled(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        string expectedValue = GetRegistryValue(executablePath);

        return string.Equals(
            key?.GetValue(RegistryValueName) as string,
            expectedValue,
            StringComparison.OrdinalIgnoreCase
        );
    }

    public static bool TrySetEnabled(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);

            if (key == null)
                return false;

            if (enabled)
                key.SetValue(RegistryValueName, GetRegistryValue(executablePath));
            else
                key.DeleteValue(RegistryValueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }

    public static string GetRegistryValue(string executablePath)
    {
        return $"\"{executablePath}\"";
    }
}
