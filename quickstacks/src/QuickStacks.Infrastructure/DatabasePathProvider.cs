namespace QuickStacks.Infrastructure;

public static class DatabasePathProvider
{
    public static string GetDatabaseFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, "QuickStacks");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "quickstacks.db");
    }

    public static string GetConnectionString() => $"Data Source={GetDatabaseFilePath()}";
}
