using Microsoft.Win32;

namespace EasyWinMenu.App.Services;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EasyWinMenu";

    public static bool IsEnabled()
    {
        var executablePath = GetExecutablePath();
        if (executablePath is null)
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string existingPath
            && string.Equals(existingPath, executablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = GetExecutablePath();
        if (executablePath is not null)
        {
            key.SetValue(ValueName, executablePath);
        }
    }

    private static string? GetExecutablePath()
    {
        return Environment.ProcessPath;
    }
}
