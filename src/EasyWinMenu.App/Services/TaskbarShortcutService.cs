using System.Diagnostics;
using System.IO;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Services;

/// <summary>
/// Writes a <c>.lnk</c> that brings one desktop group to the front: it launches this same
/// executable with <c>--desktop-action=focus-group:&lt;id&gt;</c>, and the already-running instance
/// picks the action up through <see cref="SingleInstanceCoordinator"/>. Pinning the file to the
/// taskbar is left to the user (drag it from the folder that opens): a <c>.lnk</c> dropped in
/// the taskbar's own folder is not enough on its own, because Explorer draws the buttons from
/// the binary <c>Favorites</c> value under the Taskband registry key, not from that folder.
/// </summary>
internal static class TaskbarShortcutService
{
    /// <summary>Creates (or refreshes) the shortcut for the group and returns its path.</summary>
    public static string CreateGroupShortcut(MenuCategory group, string executablePath)
    {
        if (string.IsNullOrEmpty(group.Id))
        {
            throw new InvalidOperationException("The group has no Id yet.");
        }

        Directory.CreateDirectory(ApplicationPaths.GroupShortcutsDirectory);
        var path = Path.Combine(ApplicationPaths.GroupShortcutsDirectory, FileNameFor(group));

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = executablePath;
        link.Arguments = DesktopContextMenuRegistration.FocusGroupArguments(group.Id);
        link.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
        link.IconLocation = $"{executablePath},0";
        link.Description = group.Name;
        link.Save();

        return path;
    }

    /// <summary>Opens Explorer on the folder with the shortcut selected, ready to be dragged.</summary>
    public static void RevealInExplorer(string shortcutPath)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{shortcutPath}\"") { UseShellExecute = true });
    }

    private static string FileNameFor(MenuCategory group)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = string.Concat(group.Name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        if (name.Length == 0)
        {
            name = "Group";
        }

        // Two groups may share a name; the short id keeps their shortcuts from overwriting each other.
        return $"{name} ({group.Id![..6]}).lnk";
    }
}
