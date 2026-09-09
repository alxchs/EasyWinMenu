using System.IO;
using System.Security.Cryptography;
using System.Text;
using DrawingIcon = System.Drawing.Icon;

namespace EasyWinMenu.App.Services;

internal sealed class IconCacheService : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, DrawingIcon> _memoryCache = new();

    public IconCacheService(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(_cacheDirectory);
    }

    public DrawingIcon? GetIcon(string targetPath)
    {
        if (_memoryCache.TryGetValue(targetPath, out var cached))
        {
            return cached;
        }

        var resolvedPath = ResolveFullPath(targetPath);
        if (resolvedPath is null)
        {
            return null;
        }

        var cacheFilePath = Path.Combine(_cacheDirectory, BuildCacheKey(resolvedPath) + ".ico");
        var icon = LoadFromDisk(cacheFilePath) ?? ExtractAndPersist(resolvedPath, cacheFilePath);

        if (icon is not null)
        {
            _memoryCache[targetPath] = icon;
        }

        return icon;
    }

    private static string? ResolveFullPath(string targetPath)
    {
        if (File.Exists(targetPath))
        {
            return Path.GetFullPath(targetPath);
        }

        if (Path.IsPathRooted(targetPath))
        {
            return null;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, targetPath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var icon in _memoryCache.Values)
        {
            icon.Dispose();
        }

        _memoryCache.Clear();
    }

    private static string BuildCacheKey(string targetPath)
    {
        var lastWriteTicks = File.GetLastWriteTimeUtc(targetPath).Ticks;
        var identity = $"{targetPath.ToLowerInvariant()}|{lastWriteTicks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash);
    }

    private static DrawingIcon? LoadFromDisk(string cacheFilePath)
    {
        if (!File.Exists(cacheFilePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(cacheFilePath);
            return new DrawingIcon(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static DrawingIcon? ExtractAndPersist(string targetPath, string cacheFilePath)
    {
        DrawingIcon? extracted;
        try
        {
            extracted = DrawingIcon.ExtractAssociatedIcon(targetPath);
        }
        catch (IOException)
        {
            return null;
        }

        if (extracted is null)
        {
            return null;
        }

        using var stream = File.Create(cacheFilePath);
        extracted.Save(stream);

        return extracted;
    }
}
