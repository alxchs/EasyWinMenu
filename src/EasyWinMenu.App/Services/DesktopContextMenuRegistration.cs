using EasyWinMenu.App.Localization;
using Microsoft.Win32;

namespace EasyWinMenu.App.Services;

/// <summary>
/// Registers a small cascading submenu on the real Windows desktop background's own
/// right-click menu — the classic per-verb registry trick under
/// <c>DesktopBackground\Shell</c>, not a shell extension DLL. Each verb just relaunches
/// this executable with a "--desktop-action=" argument; <see cref="SingleInstanceCoordinator"/>
/// makes sure that either starts the app (when it was closed) or hands the action to the
/// one already running, so these verbs work whether or not the tray icon is up.
/// </summary>
internal static class DesktopContextMenuRegistration
{
    private const string RootKeyPath = @"Software\Classes\DesktopBackground\Shell\EasyWinMenu";
    private const string ArgumentPrefix = "--desktop-action=";

    public const string NewGroupAction = "new-group";
    public const string ToggleCollapseAllAction = "toggle-collapse-all";
    public const string AllAppFolderAction = "all-appfolder";
    public const string AllPanelAction = "all-panel";
    public const string OpenSettingsAction = "open-settings";

    /// <summary>
    /// Idempotent — safe to call on every startup. Labels always follow Windows' own
    /// display language, never the app's configured one: this menu is read by Explorer
    /// while the app may not even be running, so the app's internal language setting has
    /// no bearing on it — the same way every other item already on that menu (Refresh,
    /// New, Display settings…) follows Windows, not some per-app preference.
    /// </summary>
    public static void Register()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            return;
        }

        var lang = LocalizationService.DetectLanguage();
        string L(string key) => LocalizationService.GetForLanguage(lang, key);

        using var root = Registry.CurrentUser.CreateSubKey(RootKeyPath);
        root.SetValue("MUIVerb", L("desktop.contextMenuRoot"));
        root.SetValue("Icon", $"\"{executablePath}\",0");
        // An empty SubCommands value is what tells Explorer this verb is a submenu
        // whose items live under its own "shell" subkey, instead of a single command.
        root.SetValue("SubCommands", string.Empty);

        using var shell = root.CreateSubKey("shell");
        WriteVerb(shell, "01NewGroup", L("tray.newDesktopGroup"), executablePath, NewGroupAction);
        WriteVerb(shell, "02ToggleCollapseAll", L("desktop.toggleCollapseAll"), executablePath, ToggleCollapseAllAction);
        WriteVerb(shell, "03AllAppFolder", L("desktop.allAppFolder"), executablePath, AllAppFolderAction);
        WriteVerb(shell, "04AllPanel", L("desktop.allPanel"), executablePath, AllPanelAction);
        WriteVerb(shell, "05OpenSettings", L("tray.settings"), executablePath, OpenSettingsAction);
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RootKeyPath, throwOnMissingSubKey: false);
    }

    /// <summary>The action named by a launch argument, or null when there is none.</summary>
    public static string? ParseAction(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.Ordinal))
            {
                return arg[ArgumentPrefix.Length..];
            }
        }

        return null;
    }

    private static void WriteVerb(RegistryKey shellKey, string verbKeyName, string label, string executablePath, string action)
    {
        using var verb = shellKey.CreateSubKey(verbKeyName);
        verb.SetValue(string.Empty, label);

        using var command = verb.CreateSubKey("command");
        command.SetValue(string.Empty, $"\"{executablePath}\" {ArgumentPrefix}{action}");
    }
}
