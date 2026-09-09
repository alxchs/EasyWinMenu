using System.IO;

namespace EasyWinMenu.App.Services;

internal static class ApplicationPaths
{
    private const string ProductFolderName = "EasyWinMenu";

    public static string ConfigFilePath { get; } = BuildConfigFilePath();

    private static string BuildConfigFilePath()
    {
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataFolder, ProductFolderName, "config.json");
    }
}
