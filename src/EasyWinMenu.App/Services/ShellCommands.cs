using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Services;

/// <summary>
/// The file commands people expect on an icon: reveal it, run it elevated, copy its
/// path, open its properties sheet. Each one is best-effort — a target that has moved
/// or a prompt the user declines is a normal outcome here, not an error to report.
/// </summary>
internal static class ShellCommands
{
    private const int SeeMaskInvokeIdList = 0x0000000C;

    /// <summary>True when the item points at something on disk that these verbs can act on.</summary>
    public static bool HasFileTarget(LaunchItem item)
    {
        return TryResolveTarget(item, out _);
    }

    /// <summary>The full path the item launches, when it is a real file or folder.</summary>
    public static bool TryResolveTarget(LaunchItem item, out string path)
    {
        path = string.Empty;
        if (item.Type is LaunchItemType.Command or LaunchItemType.Url)
        {
            return false;
        }

        var resolved = ResolveTarget(item);
        if (resolved is null)
        {
            return false;
        }

        path = resolved;
        return true;
    }

    public static void RevealInExplorer(LaunchItem item)
    {
        var path = ResolveTarget(item);
        if (path is null)
        {
            return;
        }

        // Folders open themselves; anything else opens its parent with the file picked out.
        var arguments = Directory.Exists(path) ? $"\"{path}\"" : $"/select,\"{path}\"";
        TryStart(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    public static void RunAsAdministrator(LaunchItem item)
    {
        var path = ResolveTarget(item) ?? item.Target;
        TryStart(new ProcessStartInfo(path)
        {
            Arguments = item.Arguments ?? string.Empty,
            WorkingDirectory = item.WorkingDirectory ?? string.Empty,
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    public static void CopyPath(LaunchItem item)
    {
        var path = ResolveTarget(item) ?? item.Target;
        try
        {
            System.Windows.Clipboard.SetText(path);
        }
        catch (ExternalException)
        {
            // Another process owns the clipboard right now; nothing to recover.
        }
    }

    public static void ShowProperties(LaunchItem item)
    {
        var path = ResolveTarget(item);
        if (path is null)
        {
            return;
        }

        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskInvokeIdList,
            lpVerb = "properties",
            lpFile = path,
            nShow = 1
        };

        ShellExecuteEx(ref info);
    }

    private static string? ResolveTarget(LaunchItem item)
    {
        // Always the launch target: an icon override only changes what is drawn.
        var target = item.Target;
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            return Path.GetFullPath(target);
        }

        // Bare executable names such as "notepad.exe" still resolve through PATH.
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, target);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // Includes the user dismissing the elevation prompt.
        }
        catch (FileNotFoundException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo lpExecInfo);
}
