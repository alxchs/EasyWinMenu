using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using EasyWinMenu.Core.Models;

namespace EasyWinMenu.App.Services;

internal static class LaunchExecutor
{
    public static void Execute(LaunchItem item)
    {
        try
        {
            Process.Start(BuildStartInfo(item));
        }
        catch (Win32Exception)
        {
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static ProcessStartInfo BuildStartInfo(LaunchItem item)
    {
        var startInfo = item.Type == LaunchItemType.Command
            ? BuildCommandStartInfo(item)
            : new ProcessStartInfo(item.Target) { Arguments = item.Arguments ?? string.Empty };

        startInfo.UseShellExecute = true;

        if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
        {
            startInfo.WorkingDirectory = item.WorkingDirectory;
        }

        return startInfo;
    }

    private static ProcessStartInfo BuildCommandStartInfo(LaunchItem item)
    {
        var startInfo = new ProcessStartInfo("cmd.exe");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(item.Target);

        if (!string.IsNullOrWhiteSpace(item.Arguments))
        {
            startInfo.ArgumentList.Add(item.Arguments);
        }

        return startInfo;
    }
}
