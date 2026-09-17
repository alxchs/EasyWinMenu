using Microsoft.Win32;

namespace QuickStacks.UI;

/// <summary>"Iniciar com o Windows" (Fase 13) - porta direta do StartupRegistration.cs do EasyWinMenu, mesma chave Run do usuario atual (nao precisa de administrador).</summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickStacks";

    public static bool IsEnabled()
    {
        var executablePath = Environment.ProcessPath;
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

        var executablePath = Environment.ProcessPath;
        if (executablePath is not null)
        {
            key.SetValue(ValueName, executablePath);
        }
    }
}
