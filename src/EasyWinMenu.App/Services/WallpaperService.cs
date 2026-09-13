using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EasyWinMenu.App.Services;

/// <summary>
/// Finds the picture Windows is currently showing on the desktop, so a group can wear
/// the same image as its background.
/// </summary>
internal static class WallpaperService
{
    private const uint SpiGetDeskWallpaper = 0x0073;
    private const int MaxPath = 260;

    /// <summary>
    /// The wallpaper file, or null when the desktop is a solid colour, a slideshow
    /// frame that has not been written out, or a path that no longer exists.
    /// </summary>
    public static string? TryGetCurrentWallpaper()
    {
        var buffer = new StringBuilder(MaxPath);
        if (SystemParametersInfo(SpiGetDeskWallpaper, (uint)buffer.Capacity, buffer, 0))
        {
            var path = buffer.ToString();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        // Windows keeps a copy of the picture actually on screen here, which is what a
        // slideshow or a "fit to screen" transform leaves behind.
        var cached = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");

        return File.Exists(cached) ? cached : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);
}
