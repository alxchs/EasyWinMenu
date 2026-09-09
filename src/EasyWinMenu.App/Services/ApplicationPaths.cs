using System.IO;

namespace EasyWinMenu.App.Services;

internal static class ApplicationPaths
{
    private const string ProductFolderName = "EasyWinMenu";

    private static string ProductDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ProductFolderName);

    public static string ConfigFilePath { get; } = Path.Combine(ProductDataFolder, "config.json");
    public static string IconCacheDirectory { get; } = Path.Combine(ProductDataFolder, "IconCache");
}
